using ActivityService.Services.Data;
using ActivityService.Services.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services;

public class ActivityOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityOutboxDispatcher> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;
    private readonly int _maxAttempts;

    public ActivityOutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<ActivityOutboxDispatcher> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _pollInterval = TimeSpan.FromMilliseconds(
            int.TryParse(configuration["RabbitMQ:DispatcherPollMs"], out var pollMs) ? Math.Max(pollMs, 200) : 1000);
        _batchSize = int.TryParse(configuration["RabbitMQ:DispatcherBatchSize"], out var batchSize) ? Math.Clamp(batchSize, 1, 500) : 100;
        _maxAttempts = int.TryParse(configuration["RabbitMQ:PublishRetryLimit"], out var maxAttempts) ? Math.Max(maxAttempts, 1) : 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Activity outbox dispatcher started (poll={PollMs}ms, batch={Batch}, maxAttempts={MaxAttempts})",
            (int)_pollInterval.TotalMilliseconds, _batchSize, _maxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(stoppingToken);

                if (processed == 0)
                    await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in ActivityOutboxDispatcher loop");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;
        var messages = await db.Set<ActivityService.Services.Models.OutboxMessage>()
            .Where(x => x.ProcessedAt == null && x.AvailableAt <= now)
            .OrderBy(x => x.Id)
            .Take(_batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var payload = OutboxEventEnvelopeFactory.DeserializePayload(message.EventType, message.Payload);
                if (payload == null)
                    throw new InvalidOperationException($"Unknown outbox event type: {message.EventType}");

                var headers = OutboxEventEnvelopeFactory.DeserializeHeaders(message.Headers);

                await publishEndpoint.Publish(payload, publishContext =>
                {
                    foreach (var (key, value) in headers)
                        publishContext.Headers.Set(key, value);

                    publishContext.Headers.Set("x-outbox-message-id", message.Id);
                    publishContext.Headers.Set("x-outbox-attempt", message.AttemptCount + 1);
                    publishContext.Headers.Set("x-outbox-dispatched-at-utc", DateTime.UtcNow.ToString("O"));
                }, cancellationToken);

                message.AttemptCount += 1;
                message.ProcessedAt = DateTime.UtcNow;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.AttemptCount += 1;
                message.LastError = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;

                var delaySeconds = Math.Min(60, (int)Math.Pow(2, Math.Min(message.AttemptCount, 6)));
                message.AvailableAt = DateTime.UtcNow.AddSeconds(delaySeconds);

                _logger.LogWarning(ex,
                    "Failed to dispatch outbox message {OutboxId} ({EventType}) for activity {ActivityId}; attempt {Attempt}/{MaxAttempts}, next retry in {DelaySeconds}s",
                    message.Id, message.EventType, message.ActivityId, message.AttemptCount, _maxAttempts, delaySeconds);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}
