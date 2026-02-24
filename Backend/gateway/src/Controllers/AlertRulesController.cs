using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gateway.Models;
using Gateway.Services;

namespace Gateway.Controllers;

[ApiController]
[Route("api/alert-rules")]
[Authorize]
public sealed class AlertRulesController : ControllerBase
{
    private static readonly HashSet<string> AllowedSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "medium", "high", "critical"
    };

    private static readonly HashSet<string> AllowedMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "anomaly_count",
        "blocked_activities",
        "average_risk_score",
        "total_activities"
    };

    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "gt", "gte", "lt", "lte", "eq"
    };

    private readonly AlertRuleStore _store;

    public AlertRulesController(AlertRuleStore store)
    {
        _store = store;
    }

    [HttpGet]
    public IActionResult GetRules()
    {
        var rules = _store.GetAll();
        return Ok(new { rules, totalCount = rules.Count, timestamp = DateTime.UtcNow });
    }

    [HttpGet("metadata")]
    public IActionResult GetMetadata()
    {
        return Ok(new
        {
            severities = AllowedSeverities.OrderBy(x => x).ToArray(),
            metrics = new[]
            {
                new { key = "anomaly_count", label = "Anomaly Count" },
                new { key = "blocked_activities", label = "Blocked Activities" },
                new { key = "average_risk_score", label = "Average Risk Score" },
                new { key = "total_activities", label = "Total Activities" }
            },
            operators = new[]
            {
                new { key = "gt", label = ">" },
                new { key = "gte", label = ">=" },
                new { key = "lt", label = "<" },
                new { key = "lte", label = "<=" },
                new { key = "eq", label = "=" }
            }
        });
    }

    [HttpPost]
    public IActionResult CreateRule([FromBody] AlertRuleUpsertRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var created = _store.Create(MapNewRule(request));
        return CreatedAtAction(nameof(GetRule), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetRule(Guid id)
    {
        var rule = _store.Get(id);
        return rule is null ? NotFound(new { message = "Alert rule not found" }) : Ok(rule);
    }

    [HttpPut("{id:guid}")]
    public IActionResult UpdateRule(Guid id, [FromBody] AlertRuleUpsertRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var updated = _store.Update(id, rule => ApplyRule(rule, request));
        return updated is null ? NotFound(new { message = "Alert rule not found" }) : Ok(updated);
    }

    [HttpPatch("{id:guid}/enabled")]
    public IActionResult SetEnabled(Guid id, [FromBody] ToggleAlertRuleRequest request)
    {
        var updated = _store.Update(id, rule => rule.Enabled = request.Enabled);
        return updated is null ? NotFound(new { message = "Alert rule not found" }) : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteRule(Guid id)
    {
        return _store.Delete(id)
            ? Ok(new { deleted = true, id })
            : NotFound(new { message = "Alert rule not found" });
    }

    private static AlertRule MapNewRule(AlertRuleUpsertRequest request)
    {
        var rule = new AlertRule();
        ApplyRule(rule, request);
        return rule;
    }

    private static void ApplyRule(AlertRule rule, AlertRuleUpsertRequest request)
    {
        rule.Name = request.Name.Trim();
        rule.Enabled = request.Enabled;
        rule.Severity = request.Severity.Trim().ToLowerInvariant();
        rule.Metric = request.Metric.Trim().ToLowerInvariant();
        rule.Operator = request.Operator.Trim().ToLowerInvariant();
        rule.Threshold = request.Threshold;
        rule.WindowMinutes = request.WindowMinutes;
        rule.ActivityType = string.IsNullOrWhiteSpace(request.ActivityType) ? null : request.ActivityType.Trim().ToUpperInvariant();
        rule.UserId = request.UserId;
        rule.ComputerId = request.ComputerId;
        rule.NotifyInApp = request.NotifyInApp;
        rule.NotifyEmail = request.NotifyEmail;
        rule.CooldownMinutes = request.CooldownMinutes;
    }

    private static string? ValidateRequest(AlertRuleUpsertRequest request)
    {
        if (request is null)
            return "Request body is required";

        if (string.IsNullOrWhiteSpace(request.Name))
            return "Rule name is required";

        if (!AllowedSeverities.Contains(request.Severity ?? string.Empty))
            return "Unsupported severity";

        if (!AllowedMetrics.Contains(request.Metric ?? string.Empty))
            return "Unsupported metric";

        if (!AllowedOperators.Contains(request.Operator ?? string.Empty))
            return "Unsupported operator";

        if (request.WindowMinutes is < 1 or > 1440)
            return "WindowMinutes must be between 1 and 1440";

        if (request.CooldownMinutes is < 0 or > 1440)
            return "CooldownMinutes must be between 0 and 1440";

        return null;
    }

    public sealed class AlertRuleUpsertRequest
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        [Required]
        public string Severity { get; set; } = "medium";

        [Required]
        public string Metric { get; set; } = "anomaly_count";

        [Required]
        public string Operator { get; set; } = "gte";

        public decimal Threshold { get; set; } = 1;
        public int WindowMinutes { get; set; } = 15;
        public string? ActivityType { get; set; }
        public int? UserId { get; set; }
        public int? ComputerId { get; set; }
        public bool NotifyInApp { get; set; } = true;
        public bool NotifyEmail { get; set; }
        public int CooldownMinutes { get; set; } = 10;
    }

    public sealed class ToggleAlertRuleRequest
    {
        public bool Enabled { get; set; }
    }
}
