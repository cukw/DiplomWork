using Microsoft.Extensions.Options;

namespace NotificationService.Services;

public sealed class NotificationDeliveryRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDeliveryRetryWorker> _logger;
    private readonly NotificationDeliveryOptions _options;

    public NotificationDeliveryRetryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDeliveryRetryWorker> logger,
        IOptions<NotificationDeliveryOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options?.Value ?? new NotificationDeliveryOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Retry.Enabled)
        {
            _logger.LogInformation("Notification delivery retry worker is disabled by configuration.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Clamp(_options.Retry.PollIntervalSeconds, 1, 300));
        _logger.LogInformation("Notification delivery retry worker started. PollIntervalSeconds={PollInterval}", pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<INotificationDeliveryProcessor>();
                var processedCount = await processor.ProcessDueNotificationsAsync(stoppingToken);
                if (processedCount > 0)
                {
                    _logger.LogInformation("Notification delivery retry worker processed {ProcessedCount} due notifications.", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification delivery retry worker iteration failed.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
