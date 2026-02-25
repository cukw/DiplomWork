using ActivityService.Services.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ReportService.Data;

namespace ReportService.Events;

public class ActivityCreatedReportProjectionConsumer : IConsumer<ActivityCreatedEvent>
{
    private readonly ReportDbContext _db;
    private readonly ILogger<ActivityCreatedReportProjectionConsumer> _logger;

    public ActivityCreatedReportProjectionConsumer(ReportDbContext db, ILogger<ActivityCreatedReportProjectionConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ActivityCreatedEvent> context)
    {
        var e = context.Message;
        var occurredAt = ReportEventProcessingHelper.NormalizeUtc(e.OccurredAtUtc);
        var eventKey = ReportEventProcessingHelper.ActivityKey(e);

        await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO processed_event_inbox (consumer, event_key, message_id, processed_at)
            VALUES ({nameof(ActivityCreatedReportProjectionConsumer)}, {eventKey}, {context.MessageId?.ToString()}, {DateTime.UtcNow})
            ON CONFLICT (consumer, event_key) DO NOTHING;
        ", context.CancellationToken);

        if (inserted == 0)
        {
            await tx.RollbackAsync(context.CancellationToken);
            _logger.LogInformation("Skipping duplicate ActivityCreatedEvent in ReportService for activity {ActivityId}", e.ActivityId);
            return;
        }

        var riskValue = e.RiskScore ?? 0m;
        var riskSamples = e.RiskScore.HasValue ? 1 : 0;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO daily_reports
                (report_date, computer_id, user_id, total_activities, blocked_actions, avg_risk_score, risk_score_samples, anomaly_count, created_at)
            VALUES
                ({occurredAt.Date}, {e.ComputerId}, NULL, 1, {(e.IsBlocked ? 1 : 0)}, {(e.RiskScore.HasValue ? riskValue : (decimal?)null)}, {riskSamples}, 0, {DateTime.UtcNow})
            ON CONFLICT (report_date, computer_id)
            DO UPDATE SET
                total_activities = daily_reports.total_activities + 1,
                blocked_actions = daily_reports.blocked_actions + EXCLUDED.blocked_actions,
                avg_risk_score = CASE
                    WHEN COALESCE(daily_reports.risk_score_samples, 0) + EXCLUDED.risk_score_samples = 0 THEN daily_reports.avg_risk_score
                    WHEN EXCLUDED.risk_score_samples = 0 THEN daily_reports.avg_risk_score
                    ELSE ROUND((
                        (COALESCE(daily_reports.avg_risk_score, 0) * COALESCE(daily_reports.risk_score_samples, 0))
                        + (COALESCE(EXCLUDED.avg_risk_score, 0) * EXCLUDED.risk_score_samples)
                    ) / (COALESCE(daily_reports.risk_score_samples, 0) + EXCLUDED.risk_score_samples), 2)
                END,
                risk_score_samples = COALESCE(daily_reports.risk_score_samples, 0) + EXCLUDED.risk_score_samples;
        ", context.CancellationToken);

        await tx.CommitAsync(context.CancellationToken);
    }
}
