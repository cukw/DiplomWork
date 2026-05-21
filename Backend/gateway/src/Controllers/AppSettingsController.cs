using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

[ApiController]
[Route("api/app-settings")]
[Authorize]
public sealed class AppSettingsController : ControllerBase
{
    private readonly AppSettingsStore _store;
    private readonly PolicyAccessListSyncService _policySyncService;
    private readonly IAdminAuditLogger _auditLogger;
    private readonly ILogger<AppSettingsController> _logger;

    public AppSettingsController(
        AppSettingsStore store,
        PolicyAccessListSyncService policySyncService,
        IAdminAuditLogger auditLogger,
        ILogger<AppSettingsController> logger)
    {
        _store = store;
        _policySyncService = policySyncService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] AppSettingsDocument document, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var saved = await _store.SaveAsync(document, cancellationToken);
        var syncResult = await SyncAccessPoliciesAsync(saved, cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.save",
            actor,
            "app-settings",
            "global",
            true,
            200,
            new { syncResult.TotalAgents, syncResult.SyncedAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(saved);
    }

    [HttpGet("whitelist")]
    public async Task<IActionResult> GetWhitelist(CancellationToken cancellationToken)
    {
        var entries = await _store.GetWhitelistEntriesAsync(cancellationToken);
        return Ok(new { entries });
    }

    [HttpPut("whitelist")]
    public async Task<IActionResult> ReplaceWhitelist([FromBody] List<ApplicationListEntryModel> entries, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var saved = await _store.ReplaceWhitelistEntriesAsync(entries ?? [], cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.whitelist.replace",
            actor,
            "app-settings.whitelist",
            "global",
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPost("whitelist")]
    public async Task<IActionResult> CreateWhitelistEntry([FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        if (string.IsNullOrWhiteSpace(entry.Application))
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "app-settings.whitelist.create",
                actor,
                "app-settings.whitelist",
                "global",
                false,
                400,
                new { message = "Application is required" }), cancellationToken);
            return BadRequest(new { message = "Application is required" });
        }

        var saved = await _store.UpsertWhitelistEntryAsync(entry, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.whitelist.create",
            actor,
            "app-settings.whitelist",
            entry.Application.Trim(),
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPut("whitelist/{id:long}")]
    public async Task<IActionResult> UpdateWhitelistEntry(long id, [FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        entry.Id = id;
        if (string.IsNullOrWhiteSpace(entry.Application))
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "app-settings.whitelist.update",
                actor,
                "app-settings.whitelist",
                id.ToString(),
                false,
                400,
                new { message = "Application is required" }), cancellationToken);
            return BadRequest(new { message = "Application is required" });
        }

        var saved = await _store.UpsertWhitelistEntryAsync(entry, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.whitelist.update",
            actor,
            "app-settings.whitelist",
            id.ToString(),
            true,
            200,
            new { application = entry.Application.Trim(), count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpDelete("whitelist/{id:long}")]
    public async Task<IActionResult> DeleteWhitelistEntry(long id, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var saved = await _store.DeleteWhitelistEntryAsync(id, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.whitelist.delete",
            actor,
            "app-settings.whitelist",
            id.ToString(),
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpGet("blacklist")]
    public async Task<IActionResult> GetBlacklist(CancellationToken cancellationToken)
    {
        var entries = await _store.GetBlacklistEntriesAsync(cancellationToken);
        return Ok(new { entries });
    }

    [HttpPut("blacklist")]
    public async Task<IActionResult> ReplaceBlacklist([FromBody] List<ApplicationListEntryModel> entries, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var saved = await _store.ReplaceBlacklistEntriesAsync(entries ?? [], cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.blacklist.replace",
            actor,
            "app-settings.blacklist",
            "global",
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPost("blacklist")]
    public async Task<IActionResult> CreateBlacklistEntry([FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        if (string.IsNullOrWhiteSpace(entry.Application))
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "app-settings.blacklist.create",
                actor,
                "app-settings.blacklist",
                "global",
                false,
                400,
                new { message = "Application is required" }), cancellationToken);
            return BadRequest(new { message = "Application is required" });
        }

        var saved = await _store.UpsertBlacklistEntryAsync(entry, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.blacklist.create",
            actor,
            "app-settings.blacklist",
            entry.Application.Trim(),
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPut("blacklist/{id:long}")]
    public async Task<IActionResult> UpdateBlacklistEntry(long id, [FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        entry.Id = id;
        if (string.IsNullOrWhiteSpace(entry.Application))
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "app-settings.blacklist.update",
                actor,
                "app-settings.blacklist",
                id.ToString(),
                false,
                400,
                new { message = "Application is required" }), cancellationToken);
            return BadRequest(new { message = "Application is required" });
        }

        var saved = await _store.UpsertBlacklistEntryAsync(entry, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.blacklist.update",
            actor,
            "app-settings.blacklist",
            id.ToString(),
            true,
            200,
            new { application = entry.Application.Trim(), count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpDelete("blacklist/{id:long}")]
    public async Task<IActionResult> DeleteBlacklistEntry(long id, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var saved = await _store.DeleteBlacklistEntryAsync(id, cancellationToken);
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.blacklist.delete",
            actor,
            "app-settings.blacklist",
            id.ToString(),
            true,
            200,
            new { count = saved.Count, syncResult.TotalAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPost("sync/policies")]
    public async Task<IActionResult> SyncPolicies(CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "panel";
        var syncResult = await SyncAccessPoliciesFromStoreAsync(cancellationToken);
        await _auditLogger.LogAsync(new AdminAuditEvent(
            "app-settings.sync-policies",
            actor,
            "app-settings",
            "global",
            syncResult.Success,
            200,
            new { syncResult.TotalAgents, syncResult.SyncedAgents, syncResult.FailedAgents }), cancellationToken);
        return Ok(syncResult);
    }

    private async Task<PolicyAccessListSyncResult> SyncAccessPoliciesFromStoreAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return await SyncAccessPoliciesAsync(settings, cancellationToken);
    }

    private async Task<PolicyAccessListSyncResult> SyncAccessPoliciesAsync(
        AppSettingsDocument settings,
        CancellationToken cancellationToken)
    {
        PolicyAccessListSyncResult syncResult;
        try
        {
            syncResult = await _policySyncService.SyncFromSettingsAsync(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Access-list sync failed before agent policy updates could be completed.");
            syncResult = new PolicyAccessListSyncResult
            {
                TotalAgents = 0,
                SyncedAgents = 0,
                FailedAgents = 1,
                Errors = [$"Policy sync failed: {ex.Message}"]
            };
        }

        Response.Headers["X-Policy-Sync-Total-Agents"] = syncResult.TotalAgents.ToString();
        Response.Headers["X-Policy-Sync-Synced-Agents"] = syncResult.SyncedAgents.ToString();
        Response.Headers["X-Policy-Sync-Failed-Agents"] = syncResult.FailedAgents.ToString();
        Response.Headers["X-Policy-Sync-Status"] = syncResult.Success
            ? "ok"
            : syncResult.TotalAgents > 0 ? "partial" : "failed";

        if (!syncResult.Success)
        {
            _logger.LogWarning(
                "Access-list sync completed with failures. Total={Total}, Synced={Synced}, Failed={Failed}",
                syncResult.TotalAgents,
                syncResult.SyncedAgents,
                syncResult.FailedAgents);
        }

        return syncResult;
    }
}
