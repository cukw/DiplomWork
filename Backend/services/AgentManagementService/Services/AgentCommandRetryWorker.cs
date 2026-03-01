using AgentManagementService.Data;
using AgentManagementService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentManagementService.Services;

public sealed class AgentCommandRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentCommandRetryWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _retryBaseDelay;
    private readonly TimeSpan _retryMaxDelay;
    private readonly int _batchSize;

    public AgentCommandRetryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CommandDeliveryOptions> options,
        ILogger<AgentCommandRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var value = options?.Value ?? new CommandDeliveryOptions();
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, value.PollIntervalSeconds));
        _retryBaseDelay = TimeSpan.FromSeconds(Math.Max(1, value.RetryBaseDelaySeconds));
        _retryMaxDelay = TimeSpan.FromSeconds(Math.Max(5, value.RetryMaxDelaySeconds));
        _batchSize = Math.Clamp(value.BatchSize, 1, 1000);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent command retry worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimedOutCommandsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent command retry worker failed on iteration");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Agent command retry worker stopped");
    }

    private async Task ProcessTimedOutCommandsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var now = DateTime.UtcNow;

        var timedOutCommands = await db.AgentCommands
            .Where(c => c.Status == "running" && c.TimeoutAt != null && c.TimeoutAt <= now)
            .OrderBy(c => c.Id)
            .Take(_batchSize)
            .ToListAsync(cancellationToken);

        if (timedOutCommands.Count == 0)
            return;

        foreach (var command in timedOutCommands)
        {
            if (command.DeliveryAttempts < command.MaxDeliveryAttempts)
            {
                var retryDelay = ComputeRetryDelay(command.DeliveryAttempts);
                command.Status = "pending";
                command.NextRetryAt = now.Add(retryDelay);
                command.TimeoutAt = null;
                command.ResultMessage = $"Dispatch timeout. Retry scheduled in {(int)retryDelay.TotalSeconds}s";
                continue;
            }

            command.Status = "deadletter";
            command.AcknowledgedAt ??= now;
            command.TimeoutAt = null;
            command.NextRetryAt = null;
            command.DeadLetterReason = $"Dispatch timeout after {command.DeliveryAttempts} attempts";
            command.ResultMessage = command.DeadLetterReason;

            await PersistDeadLetterAsync(db, command, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Processed timed out commands: {Count}",
            timedOutCommands.Count);
    }

    private async Task PersistDeadLetterAsync(AgentDbContext db, global::AgentManagementService.Models.AgentCommand command, CancellationToken cancellationToken)
    {
        var exists = await db.AgentCommandDeadLetters
            .AnyAsync(x => x.AgentCommandId == command.Id, cancellationToken);

        if (exists)
            return;

        db.AgentCommandDeadLetters.Add(new AgentCommandDeadLetter
        {
            AgentCommandId = command.Id,
            AgentId = command.AgentId,
            CommandKey = command.CommandKey,
            Type = command.Type,
            PayloadJson = command.PayloadJson,
            Reason = command.DeadLetterReason,
            DeliveryAttempts = command.DeliveryAttempts,
            FailedAt = DateTime.UtcNow
        });
    }

    private TimeSpan ComputeRetryDelay(int attempts)
    {
        var power = Math.Max(0, attempts - 1);
        var seconds = _retryBaseDelay.TotalSeconds * Math.Pow(2, power);
        var bounded = Math.Min(seconds, _retryMaxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Max(1, bounded));
    }
}
