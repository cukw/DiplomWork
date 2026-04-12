using Gateway.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private readonly IDbContextFactory<GatewayRuntimeDbContext> _dbContextFactory;

    public AuditController(IDbContextFactory<GatewayRuntimeDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? action = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? q = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page > 0 ? page : 1;
        var normalizedPageSize = pageSize > 0 ? Math.Min(pageSize, 200) : 50;

        if (!TryParseUtc(from, out var fromUtc))
            return BadRequest(new { message = "Invalid from date. Expected ISO-8601 date/time." });
        if (!TryParseUtc(to, out var toUtc))
            return BadRequest(new { message = "Invalid to date. Expected ISO-8601 date/time." });
        if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
            return BadRequest(new { message = "from must be less than or equal to to" });

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.AdminAuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var value = action.Trim();
            query = query.Where(x => x.Action == value);
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            var value = actor.Trim();
            query = query.Where(x => x.Actor == value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Action, pattern)
                || EF.Functions.ILike(x.Actor, pattern)
                || EF.Functions.ILike(x.TargetType, pattern)
                || EF.Functions.ILike(x.TargetId, pattern)
                || (x.DetailsJson != null && EF.Functions.ILike(x.DetailsJson, pattern)));
        }

        if (fromUtc is not null)
            query = query.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtc is not null)
            query = query.Where(x => x.CreatedAt <= toUtc.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var events = await query
            .OrderByDescending(x => x.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => new
            {
                id = x.Id,
                action = x.Action,
                actor = x.Actor,
                targetType = x.TargetType,
                targetId = x.TargetId,
                success = x.Success,
                statusCode = x.StatusCode,
                detailsJson = x.DetailsJson,
                createdAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            events,
            totalCount,
            page = normalizedPage,
            pageSize = normalizedPageSize
        });
    }

    private static bool TryParseUtc(string? value, out DateTime? parsedUtc)
    {
        parsedUtc = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!DateTimeOffset.TryParse(value, out var dto))
            return false;

        parsedUtc = dto.UtcDateTime;
        return true;
    }
}
