using System.Collections.Concurrent;
using Gateway.Models;

namespace Gateway.Services;

public sealed class AlertRuleStore
{
    private readonly ConcurrentDictionary<Guid, AlertRule> _rules = new();

    public AlertRuleStore()
    {
        SeedDefaults();
    }

    public IReadOnlyList<AlertRule> GetAll()
    {
        return _rules.Values
            .OrderByDescending(r => r.UpdatedAt)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();
    }

    public AlertRule? Get(Guid id)
    {
        return _rules.TryGetValue(id, out var rule) ? Clone(rule) : null;
    }

    public AlertRule Create(AlertRule rule)
    {
        var now = DateTime.UtcNow;
        rule.Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        rule.CreatedAt = now;
        rule.UpdatedAt = now;

        var stored = Clone(rule);
        _rules[stored.Id] = stored;
        return Clone(stored);
    }

    public AlertRule? Update(Guid id, Action<AlertRule> mutate)
    {
        while (true)
        {
            if (!_rules.TryGetValue(id, out var current))
                return null;

            var updated = Clone(current);
            mutate(updated);
            updated.Id = id;
            updated.CreatedAt = current.CreatedAt;
            updated.UpdatedAt = DateTime.UtcNow;

            if (_rules.TryUpdate(id, updated, current))
                return Clone(updated);
        }
    }

    public bool Delete(Guid id)
    {
        return _rules.TryRemove(id, out _);
    }

    private void SeedDefaults()
    {
        var seeds = new[]
        {
            new AlertRule
            {
                Name = "Spike in anomalies (15m)",
                Severity = "high",
                Metric = "anomaly_count",
                Operator = "gte",
                Threshold = 10,
                WindowMinutes = 15,
                CooldownMinutes = 15,
                NotifyInApp = true,
                NotifyEmail = true
            },
            new AlertRule
            {
                Name = "Blocked activity threshold",
                Severity = "medium",
                Metric = "blocked_activities",
                Operator = "gte",
                Threshold = 5,
                WindowMinutes = 30,
                CooldownMinutes = 10,
                NotifyInApp = true,
                NotifyEmail = false
            },
            new AlertRule
            {
                Name = "High average risk score",
                Severity = "critical",
                Metric = "average_risk_score",
                Operator = "gte",
                Threshold = 65,
                WindowMinutes = 10,
                CooldownMinutes = 10,
                NotifyInApp = true,
                NotifyEmail = true
            }
        };

        foreach (var rule in seeds)
        {
            Create(rule);
        }
    }

    private static AlertRule Clone(AlertRule rule)
    {
        return new AlertRule
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            Severity = rule.Severity,
            Metric = rule.Metric,
            Operator = rule.Operator,
            Threshold = rule.Threshold,
            WindowMinutes = rule.WindowMinutes,
            ActivityType = rule.ActivityType,
            UserId = rule.UserId,
            ComputerId = rule.ComputerId,
            NotifyInApp = rule.NotifyInApp,
            NotifyEmail = rule.NotifyEmail,
            CooldownMinutes = rule.CooldownMinutes,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
        };
    }
}
