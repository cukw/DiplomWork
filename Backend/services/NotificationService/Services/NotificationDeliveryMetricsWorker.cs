using Microsoft.EntityFrameworkCore;
using NotificationService.Data;

namespace NotificationService.Services;

public sealed class NotificationDeliveryMetricsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDeliveryMetricsWorker> _logger;

    public NotificationDeliveryMetricsWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDeliveryMetricsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification delivery metrics worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var now = DateTime.UtcNow;

                var queueDepthTask = db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.DeliveryStatus == "pending" || n.DeliveryStatus == "failed", stoppingToken);

                var retryDueDepthTask = db.Notifications
                    .AsNoTracking()
                    .CountAsync(n =>
                        (n.DeliveryStatus == "pending" || n.DeliveryStatus == "failed")
                        && n.NextRetryAt != null
                        && n.NextRetryAt <= now, stoppingToken);

                var dlqDepthTask = db.NotificationDeliveryDeadLetters
                    .AsNoTracking()
                    .CountAsync(stoppingToken);

                await Task.WhenAll(queueDepthTask, retryDueDepthTask, dlqDepthTask);
                NotificationDeliveryMetrics.Update(
                    queueDepthTask.Result,
                    retryDueDepthTask.Result,
                    dlqDepthTask.Result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh notification delivery gauges.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }

        _logger.LogInformation("Notification delivery metrics worker stopped.");
    }
}
