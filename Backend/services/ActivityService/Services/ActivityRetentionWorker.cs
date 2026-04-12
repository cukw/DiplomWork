using ActivityService.Services.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ActivityService.Services;

public sealed class ActivityRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ActivityRetentionOptions> _optionsMonitor;
    private readonly ILogger<ActivityRetentionWorker> _logger;

    public ActivityRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ActivityRetentionOptions> optionsMonitor,
        ILogger<ActivityRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue ?? new ActivityRetentionOptions();
            var interval = TimeSpan.FromMinutes(Math.Max(1, options.SweepIntervalMinutes));

            if (options.Enabled && options.RetentionDays > 0)
            {
                try
                {
                    var moved = await RunSweepAsync(options, stoppingToken);
                    if (moved > 0)
                    {
                        _logger.LogInformation(
                            "Activity retention sweep archived {Moved} records (retentionDays={RetentionDays}, batchSize={BatchSize}).",
                            moved,
                            options.RetentionDays,
                            options.BatchSize);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Activity retention sweep failed.");
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<int> RunSweepAsync(ActivityRetentionOptions options, CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, options.RetentionDays);
        var batchSize = Math.Clamp(options.BatchSize, 50, 10000);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoffParameter = new NpgsqlParameter("cutoff", cutoff);
        var batchParameter = new NpgsqlParameter("batch_size", batchSize);

        return await db.Database.ExecuteSqlRawAsync(
            """
            WITH moved AS (
                DELETE FROM activities
                 WHERE id IN (
                    SELECT id
                      FROM activities
                     WHERE "timestamp" < @cutoff
                     ORDER BY id
                     LIMIT @batch_size
                 )
                RETURNING id, computer_id, "timestamp", activity_type, details, duration_ms, url, process_name, is_blocked, risk_score, synced,
                          user_id, agent_id, agent_version, device_name, collector, event_id, sequence, batch_id, source_platform
            )
            INSERT INTO activities_archive (
                original_activity_id,
                computer_id,
                "timestamp",
                activity_type,
                details,
                duration_ms,
                url,
                process_name,
                is_blocked,
                risk_score,
                synced,
                user_id,
                agent_id,
                agent_version,
                device_name,
                collector,
                event_id,
                sequence,
                batch_id,
                source_platform,
                archived_at
            )
            SELECT
                id,
                computer_id,
                "timestamp",
                activity_type,
                details,
                duration_ms,
                url,
                process_name,
                is_blocked,
                risk_score,
                synced,
                user_id,
                agent_id,
                agent_version,
                device_name,
                collector,
                event_id,
                sequence,
                batch_id,
                source_platform,
                NOW()
            FROM moved
            ON CONFLICT (original_activity_id) DO NOTHING;
            """,
            [cutoffParameter, batchParameter],
            cancellationToken);
    }
}
