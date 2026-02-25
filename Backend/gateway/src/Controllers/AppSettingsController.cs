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

    public AppSettingsController(AppSettingsStore store)
    {
        _store = store;
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
        var saved = await _store.SaveAsync(document, cancellationToken);
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
        var saved = await _store.ReplaceWhitelistEntriesAsync(entries ?? [], cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPost("whitelist")]
    public async Task<IActionResult> CreateWhitelistEntry([FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.Application))
            return BadRequest(new { message = "Application is required" });

        var saved = await _store.UpsertWhitelistEntryAsync(entry, cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPut("whitelist/{id:long}")]
    public async Task<IActionResult> UpdateWhitelistEntry(long id, [FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        entry.Id = id;
        if (string.IsNullOrWhiteSpace(entry.Application))
            return BadRequest(new { message = "Application is required" });

        var saved = await _store.UpsertWhitelistEntryAsync(entry, cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpDelete("whitelist/{id:long}")]
    public async Task<IActionResult> DeleteWhitelistEntry(long id, CancellationToken cancellationToken)
    {
        var saved = await _store.DeleteWhitelistEntryAsync(id, cancellationToken);
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
        var saved = await _store.ReplaceBlacklistEntriesAsync(entries ?? [], cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPost("blacklist")]
    public async Task<IActionResult> CreateBlacklistEntry([FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.Application))
            return BadRequest(new { message = "Application is required" });

        var saved = await _store.UpsertBlacklistEntryAsync(entry, cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpPut("blacklist/{id:long}")]
    public async Task<IActionResult> UpdateBlacklistEntry(long id, [FromBody] ApplicationListEntryModel entry, CancellationToken cancellationToken)
    {
        entry.Id = id;
        if (string.IsNullOrWhiteSpace(entry.Application))
            return BadRequest(new { message = "Application is required" });

        var saved = await _store.UpsertBlacklistEntryAsync(entry, cancellationToken);
        return Ok(new { entries = saved });
    }

    [HttpDelete("blacklist/{id:long}")]
    public async Task<IActionResult> DeleteBlacklistEntry(long id, CancellationToken cancellationToken)
    {
        var saved = await _store.DeleteBlacklistEntryAsync(id, cancellationToken);
        return Ok(new { entries = saved });
    }
}
