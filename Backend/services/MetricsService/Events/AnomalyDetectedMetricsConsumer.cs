using ActivityService.Services.Events;
using MassTransit;
using MetricsService.Data;
using Microsoft.EntityFrameworkCore;

namespace MetricsService.Events;

public class AnomalyDetectedMetricsConsumer : IConsumer<AnomalyDetectedEvent>
{
    private readonly MetricsDbContext _db;
    private readonly ILogger<AnomalyDetectedMetricsConsumer> _logger;

    public AnomalyDetectedMetricsConsumer(MetricsDbContext db, ILogger<AnomalyDetectedMetricsConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetectedEvent> context)
    {
        var e = context.Message;
        var occurredAt = MetricsEventProcessingHelper.NormalizeUtc(e.OccurredAtUtc);
        var eventKey = MetricsEventProcessingHelper.AnomalyKey(e);

        await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO processed_event_inbox (consumer, event_key, message_id, processed_at)
            VALUES ({nameof(AnomalyDetectedMetricsConsumer)}, {eventKey}, {context.MessageId?.ToString()}, {DateTime.UtcNow})
            ON CONFLICT (consumer, event_key) DO NOTHING;
        ", context.CancellationToken);

        if (inserted == 0)
        {
            await tx.RollbackAsync(context.CancellationToken);
            _logger.LogInformation("Skipping duplicate AnomalyDetectedEvent in MetricsService for activity {ActivityId}", e.ActivityId);
            return;
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO anomaly_event_rollups
                (bucket_date, computer_id, anomaly_type, total_count, high_priority_count, last_event_at)
            VALUES
                ({occurredAt.Date}, {e.ComputerId}, {e.AnomalyType}, 1,
                 {(IsHighPriority(e.AnomalyType) ? 1 : 0)}, {occurredAt})
            ON CONFLICT (bucket_date, computer_id, anomaly_type)
            DO UPDATE SET
                total_count = anomaly_event_rollups.total_count + 1,
                high_priority_count = anomaly_event_rollups.high_priority_count + EXCLUDED.high_priority_count,
                last_event_at = GREATEST(anomaly_event_rollups.last_event_at, EXCLUDED.last_event_at);
        ", context.CancellationToken);

        await tx.CommitAsync(context.CancellationToken);
    }

    private static bool IsHighPriority(string anomalyType) =>
        (anomalyType ?? string.Empty).Trim().ToUpperInvariant() is "HIGH_RISK" or "SUSPICIOUS_TYPE" or "BLOCKED_ACTIVITY";
}
