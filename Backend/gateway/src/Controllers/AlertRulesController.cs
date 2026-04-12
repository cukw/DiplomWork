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
    private readonly IAdminAuditLogger _auditLogger;

    public AlertRulesController(AlertRuleStore store, IAdminAuditLogger auditLogger)
    {
        _store = store;
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var rules = await _store.GetAllAsync(cancellationToken);
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
    public async Task<IActionResult> CreateRule([FromBody] AlertRuleUpsertRequest request, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "alert-rule.create",
                actor,
                "alert-rule",
                "new",
                false,
                400,
                new { message = validationError }), cancellationToken);
            return BadRequest(new { message = validationError });
        }

        var created = await _store.CreateAsync(MapNewRule(request), cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "alert-rule.create",
            actor,
            "alert-rule",
            created.Id.ToString(),
            true,
            201,
            new { created.Name, created.Metric, created.Threshold }), cancellationToken);
        return CreatedAtAction(nameof(GetRule), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRule(Guid id, CancellationToken cancellationToken)
    {
        var rule = await _store.GetAsync(id, cancellationToken);
        return rule is null ? NotFound(new { message = "Alert rule not found" }) : Ok(rule);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRule(Guid id, [FromBody] AlertRuleUpsertRequest request, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "alert-rule.update",
                actor,
                "alert-rule",
                id.ToString(),
                false,
                400,
                new { message = validationError }), cancellationToken);
            return BadRequest(new { message = validationError });
        }

        var updated = await _store.UpdateAsync(id, rule => ApplyRule(rule, request), cancellationToken);
        if (updated is null)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "alert-rule.update",
                actor,
                "alert-rule",
                id.ToString(),
                false,
                404,
                new { message = "Alert rule not found" }), cancellationToken);
            return NotFound(new { message = "Alert rule not found" });
        }

        await _auditLogger.LogAsync(new AdminAuditEvent(
            "alert-rule.update",
            actor,
            "alert-rule",
            id.ToString(),
            true,
            200,
            new { updated.Name, updated.Enabled, updated.Threshold }), cancellationToken);
        return Ok(updated);
    }

    [HttpPatch("{id:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(Guid id, [FromBody] ToggleAlertRuleRequest request, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var updated = await _store.UpdateAsync(id, rule => rule.Enabled = request.Enabled, cancellationToken);
        if (updated is null)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "alert-rule.toggle-enabled",
                actor,
                "alert-rule",
                id.ToString(),
                false,
                404,
                new { message = "Alert rule not found", enabled = request.Enabled }), cancellationToken);
            return NotFound(new { message = "Alert rule not found" });
        }

        await _auditLogger.LogAsync(new AdminAuditEvent(
            "alert-rule.toggle-enabled",
            actor,
            "alert-rule",
            id.ToString(),
            true,
            200,
            new { updated.Enabled }), cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var deleted = await _store.DeleteAsync(id, cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "alert-rule.delete",
            actor,
            "alert-rule",
            id.ToString(),
            deleted,
            deleted ? 200 : 404,
            new { deleted }), cancellationToken);

        return deleted
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
