using ActivityService.Services.Events;
using MassTransit;
using MetricsService.Data;
using Microsoft.EntityFrameworkCore;

namespace MetricsService.Events;

public class ActivityCreatedMetricsConsumer : IConsumer<ActivityCreatedEvent>
{
    private readonly MetricsDbContext _db;
    private readonly ILogger<ActivityCreatedMetricsConsumer> _logger;

    public ActivityCreatedMetricsConsumer(MetricsDbContext db, ILogger<ActivityCreatedMetricsConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ActivityCreatedEvent> context)
    {
        var e = context.Message;
        var occurredAt = MetricsEventProcessingHelper.NormalizeUtc(e.OccurredAtUtc);
        var eventKey = MetricsEventProcessingHelper.ActivityKey(e);

        await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO processed_event_inbox (consumer, event_key, message_id, processed_at)
            VALUES ({nameof(ActivityCreatedMetricsConsumer)}, {eventKey}, {context.MessageId?.ToString()}, {DateTime.UtcNow})
            ON CONFLICT (consumer, event_key) DO NOTHING;
        ", context.CancellationToken);

        if (inserted == 0)
        {
            await tx.RollbackAsync(context.CancellationToken);
            _logger.LogInformation("Skipping duplicate ActivityCreatedEvent in MetricsService for activity {ActivityId}", e.ActivityId);
            return;
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO activity_event_rollups
                (bucket_date, computer_id, activity_type, total_count, blocked_count, risk_score_sum, risk_score_samples, last_event_at)
            VALUES
                ({occurredAt.Date}, {e.ComputerId}, {e.ActivityType}, 1, {(e.IsBlocked ? 1 : 0)}, {(e.RiskScore ?? 0m)}, {(e.RiskScore.HasValue ? 1 : 0)}, {occurredAt})
            ON CONFLICT (bucket_date, computer_id, activity_type)
            DO UPDATE SET
                total_count = activity_event_rollups.total_count + 1,
                blocked_count = activity_event_rollups.blocked_count + EXCLUDED.blocked_count,
                risk_score_sum = activity_event_rollups.risk_score_sum + EXCLUDED.risk_score_sum,
                risk_score_samples = activity_event_rollups.risk_score_samples + EXCLUDED.risk_score_samples,
                last_event_at = GREATEST(activity_event_rollups.last_event_at, EXCLUDED.last_event_at);
        ", context.CancellationToken);

        await tx.CommitAsync(context.CancellationToken);
    }
}
