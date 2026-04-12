using System.Text.Json;
using Gateway.Data;
using Gateway.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Services;

public sealed record AdminAuditEvent(
    string Action,
    string Actor,
    string TargetType,
    string TargetId,
    bool Success,
    int? StatusCode = null,
    object? Details = null,
    DateTime? CreatedAtUtc = null);

public interface IAdminAuditLogger
{
    Task LogAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class AdminAuditLogger : IAdminAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IDbContextFactory<GatewayRuntimeDbContext> _dbContextFactory;
    private readonly ILogger<AdminAuditLogger> _logger;

    public AdminAuditLogger(
        IDbContextFactory<GatewayRuntimeDbContext> dbContextFactory,
        ILogger<AdminAuditLogger> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task LogAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var createdAtUtc = auditEvent.CreatedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
            var normalizedAction = (auditEvent.Action ?? string.Empty).Trim();
            var normalizedActor = string.IsNullOrWhiteSpace(auditEvent.Actor) ? "panel" : auditEvent.Actor.Trim();
            var normalizedTargetType = (auditEvent.TargetType ?? string.Empty).Trim();
            var normalizedTargetId = (auditEvent.TargetId ?? string.Empty).Trim();
            var detailsJson = SerializeDetails(auditEvent.Details);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            db.AdminAuditEvents.Add(new AdminAuditEventEntity
            {
                Action = normalizedAction,
                Actor = normalizedActor,
                TargetType = normalizedTargetType,
                TargetId = normalizedTargetId,
                Success = auditEvent.Success,
                StatusCode = auditEvent.StatusCode,
                DetailsJson = detailsJson,
                CreatedAt = createdAtUtc
            });
            await db.SaveChangesAsync(cancellationToken);

            // Structured mirror record intended for SIEM forwarders from container stdout.
            _logger.LogInformation(
                "SIEM_AUDIT {AuditJson}",
                JsonSerializer.Serialize(new
                {
                    type = "admin_audit",
                    action = normalizedAction,
                    actor = normalizedActor,
                    targetType = normalizedTargetType,
                    targetId = normalizedTargetId,
                    success = auditEvent.Success,
                    statusCode = auditEvent.StatusCode,
                    createdAt = createdAtUtc,
                    details = detailsJson
                }, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist admin audit event. Action={Action}, Target={TargetType}:{TargetId}, Success={Success}",
                auditEvent.Action,
                auditEvent.TargetType,
                auditEvent.TargetId,
                auditEvent.Success);
        }
    }

    private static string SerializeDetails(object? details)
    {
        if (details is null)
            return "{}";

        try
        {
            return JsonSerializer.Serialize(details, JsonOptions);
        }
        catch
        {
            return "{}";
        }
    }
}
