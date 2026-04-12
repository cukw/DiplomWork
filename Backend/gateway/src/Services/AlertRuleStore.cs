using Gateway.Data;
using Gateway.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Services;

public sealed class AlertRuleStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDbContextFactory<GatewayRuntimeDbContext> _dbFactory;

    public AlertRuleStore(IDbContextFactory<GatewayRuntimeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<AlertRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = await db.AlertRules
                .AsNoTracking()
                .OrderByDescending(r => r.UpdatedAt)
                .ThenBy(r => r.Name)
                .ToListAsync(cancellationToken);

            return rules.Select(MapToModel).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var rule = await db.AlertRules
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return rule is null ? null : MapToModel(rule);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertRule> CreateAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var sanitized = Sanitize(rule);
            sanitized.Id = sanitized.Id == Guid.Empty ? Guid.NewGuid() : sanitized.Id;
            sanitized.CreatedAt = now;
            sanitized.UpdatedAt = now;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            db.AlertRules.Add(MapToEntity(sanitized));
            await db.SaveChangesAsync(cancellationToken);

            return sanitized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertRule?> UpdateAsync(
        Guid id,
        Action<AlertRule> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (entity is null)
                return null;

            var current = MapToModel(entity);
            mutate(current);

            var sanitized = Sanitize(current);
            sanitized.Id = id;
            sanitized.CreatedAt = entity.CreatedAt;
            sanitized.UpdatedAt = DateTime.UtcNow;

            MapToExistingEntity(sanitized, entity);
            await db.SaveChangesAsync(cancellationToken);
            return sanitized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await db.AlertRules
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync(cancellationToken);
            return deleted > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AlertRule Sanitize(AlertRule source)
    {
        return new AlertRule
        {
            Id = source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Alert rule" : source.Name.Trim(),
            Enabled = source.Enabled,
            Severity = string.IsNullOrWhiteSpace(source.Severity) ? "medium" : source.Severity.Trim().ToLowerInvariant(),
            Metric = string.IsNullOrWhiteSpace(source.Metric) ? "anomaly_count" : source.Metric.Trim().ToLowerInvariant(),
            Operator = string.IsNullOrWhiteSpace(source.Operator) ? "gte" : source.Operator.Trim().ToLowerInvariant(),
            Threshold = source.Threshold,
            WindowMinutes = Math.Clamp(source.WindowMinutes, 1, 1440),
            ActivityType = string.IsNullOrWhiteSpace(source.ActivityType) ? null : source.ActivityType.Trim().ToUpperInvariant(),
            UserId = source.UserId is > 0 ? source.UserId : null,
            ComputerId = source.ComputerId is > 0 ? source.ComputerId : null,
            NotifyInApp = source.NotifyInApp,
            NotifyEmail = source.NotifyEmail,
            CooldownMinutes = Math.Clamp(source.CooldownMinutes, 0, 1440),
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static AlertRule MapToModel(AlertRuleEntity entity)
    {
        return new AlertRule
        {
            Id = entity.Id,
            Name = entity.Name,
            Enabled = entity.Enabled,
            Severity = entity.Severity,
            Metric = entity.Metric,
            Operator = entity.Operator,
            Threshold = entity.Threshold,
            WindowMinutes = entity.WindowMinutes,
            ActivityType = entity.ActivityType,
            UserId = entity.UserId,
            ComputerId = entity.ComputerId,
            NotifyInApp = entity.NotifyInApp,
            NotifyEmail = entity.NotifyEmail,
            CooldownMinutes = entity.CooldownMinutes,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static AlertRuleEntity MapToEntity(AlertRule model)
    {
        return new AlertRuleEntity
        {
            Id = model.Id,
            Name = model.Name,
            Enabled = model.Enabled,
            Severity = model.Severity,
            Metric = model.Metric,
            Operator = model.Operator,
            Threshold = model.Threshold,
            WindowMinutes = model.WindowMinutes,
            ActivityType = model.ActivityType,
            UserId = model.UserId,
            ComputerId = model.ComputerId,
            NotifyInApp = model.NotifyInApp,
            NotifyEmail = model.NotifyEmail,
            CooldownMinutes = model.CooldownMinutes,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private static void MapToExistingEntity(AlertRule model, AlertRuleEntity entity)
    {
        entity.Name = model.Name;
        entity.Enabled = model.Enabled;
        entity.Severity = model.Severity;
        entity.Metric = model.Metric;
        entity.Operator = model.Operator;
        entity.Threshold = model.Threshold;
        entity.WindowMinutes = model.WindowMinutes;
        entity.ActivityType = model.ActivityType;
        entity.UserId = model.UserId;
        entity.ComputerId = model.ComputerId;
        entity.NotifyInApp = model.NotifyInApp;
        entity.NotifyEmail = model.NotifyEmail;
        entity.CooldownMinutes = model.CooldownMinutes;
        entity.UpdatedAt = model.UpdatedAt;
    }
}
