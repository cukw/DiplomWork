using ActivityService.Services.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ReportService.Data;

namespace ReportService.Events;

public class AnomalyDetectedReportProjectionConsumer : IConsumer<AnomalyDetectedEvent>
{
    private readonly ReportDbContext _db;
    private readonly ILogger<AnomalyDetectedReportProjectionConsumer> _logger;

    public AnomalyDetectedReportProjectionConsumer(ReportDbContext db, ILogger<AnomalyDetectedReportProjectionConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetectedEvent> context)
    {
        var e = context.Message;
        var occurredAt = ReportEventProcessingHelper.NormalizeUtc(e.OccurredAtUtc);
        var eventKey = ReportEventProcessingHelper.AnomalyKey(e);

        await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO processed_event_inbox (consumer, event_key, message_id, processed_at)
            VALUES ({nameof(AnomalyDetectedReportProjectionConsumer)}, {eventKey}, {context.MessageId?.ToString()}, {DateTime.UtcNow})
            ON CONFLICT (consumer, event_key) DO NOTHING;
        ", context.CancellationToken);

        if (inserted == 0)
        {
            await tx.RollbackAsync(context.CancellationToken);
            _logger.LogInformation("Skipping duplicate AnomalyDetectedEvent in ReportService for activity {ActivityId}", e.ActivityId);
            return;
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO daily_reports
                (report_date, computer_id, user_id, total_activities, blocked_actions, avg_risk_score, risk_score_samples, anomaly_count, created_at)
            VALUES
                ({occurredAt.Date}, {e.ComputerId}, NULL, 0, 0, NULL, 0, 1, {DateTime.UtcNow})
            ON CONFLICT (report_date, computer_id)
            DO UPDATE SET
                anomaly_count = COALESCE(daily_reports.anomaly_count, 0) + 1,
                blocked_actions = daily_reports.blocked_actions + {(IsBlockedActivityAnomaly(e.AnomalyType) ? 1 : 0)};
        ", context.CancellationToken);

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO report_daily_anomaly_rollups
                (bucket_date, computer_id, anomaly_type, total_count, last_event_at)
            VALUES
                ({occurredAt.Date}, {e.ComputerId}, {e.AnomalyType}, 1, {occurredAt})
            ON CONFLICT (bucket_date, computer_id, anomaly_type)
            DO UPDATE SET
                total_count = report_daily_anomaly_rollups.total_count + 1,
                last_event_at = GREATEST(report_daily_anomaly_rollups.last_event_at, EXCLUDED.last_event_at);
        ", context.CancellationToken);

        await tx.CommitAsync(context.CancellationToken);
    }

    private static bool IsBlockedActivityAnomaly(string anomalyType) =>
        (anomalyType ?? string.Empty).Trim().ToUpperInvariant() == "BLOCKED_ACTIVITY";
}
