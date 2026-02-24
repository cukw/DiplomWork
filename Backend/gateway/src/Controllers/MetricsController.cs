using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using MetricsClient = Gateway.Protos.Metrics.MetricsService.MetricsServiceClient;
using Gateway.Protos.Metrics;

namespace Gateway.Controllers;

[ApiController]
[Route("api/metrics")]
[Authorize]
public class MetricsController : ControllerBase
{
    private readonly MetricsClient _metrics;

    public MetricsController(MetricsClient metrics) => _metrics = metrics;

    // ─── Метрики ─────────────────────────────────────────────────────────────

    [HttpGet("metrics")]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _metrics.GetAllMetricsAsync(new GetAllMetricsRequest
            {
                Page     = page,
                PageSize = pageSize
            });
            return Ok(new
            {
                metrics    = resp.Metrics.Select(MapMetric),
                totalCount = resp.TotalCount
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("metrics/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            var resp = await _metrics.GetMetricAsync(new GetMetricRequest { MetricId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(MapMetric(resp.Metric));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("metrics")]
    public async Task<IActionResult> Create([FromBody] CreateMetricDto dto)
    {
        try
        {
            var resp = await _metrics.CreateMetricAsync(new CreateMetricRequest
            {
                UserId = dto.UserId,
                Type   = dto.Type   ?? "",
                Config = dto.Config ?? "{}"
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapMetric(resp.Metric));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPut("metrics/{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateMetricDto dto)
    {
        try
        {
            var resp = await _metrics.UpdateMetricAsync(new UpdateMetricRequest
            {
                MetricId = id,
                Type     = dto.Type     ?? "",
                Config   = dto.Config   ?? "{}",
                IsActive = dto.IsActive
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapMetric(resp.Metric));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpDelete("metrics/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try
        {
            var resp = await _metrics.DeleteMetricAsync(new DeleteMetricRequest { MetricId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    // ─── Whitelist ────────────────────────────────────────────────────────────

    [HttpGet("whitelist/{metricId:long}")]
    public async Task<IActionResult> GetWhitelist(long metricId)
    {
        try
        {
            var resp = await _metrics.GetWhitelistEntriesAsync(
                new GetWhitelistEntriesRequest { MetricId = metricId });
            return Ok(resp.Entries.Select(MapWhitelist));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("whitelist/{metricId:long}")]
    public async Task<IActionResult> AddWhitelist(long metricId, [FromBody] ListEntryDto dto)
    {
        try
        {
            var resp = await _metrics.AddWhitelistEntryAsync(new AddWhitelistEntryRequest
            {
                MetricId = metricId,
                Pattern  = dto.Pattern ?? "",
                Action   = dto.Action  ?? ""
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapWhitelist(resp.Entry));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpDelete("whitelist/entry/{entryId:long}")]
    public async Task<IActionResult> RemoveWhitelist(long entryId)
    {
        try
        {
            var resp = await _metrics.RemoveWhitelistEntryAsync(
                new RemoveWhitelistEntryRequest { EntryId = entryId });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    // ─── Blacklist ────────────────────────────────────────────────────────────

    [HttpGet("blacklist/{metricId:long}")]
    public async Task<IActionResult> GetBlacklist(long metricId)
    {
        try
        {
            var resp = await _metrics.GetBlacklistEntriesAsync(
                new GetBlacklistEntriesRequest { MetricId = metricId });
            return Ok(resp.Entries.Select(MapBlacklist));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("blacklist/{metricId:long}")]
    public async Task<IActionResult> AddBlacklist(long metricId, [FromBody] ListEntryDto dto)
    {
        try
        {
            var resp = await _metrics.AddBlacklistEntryAsync(new AddBlacklistEntryRequest
            {
                MetricId = metricId,
                Pattern  = dto.Pattern ?? "",
                Action   = dto.Action  ?? ""
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapBlacklist(resp.Entry));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpDelete("blacklist/entry/{entryId:long}")]
    public async Task<IActionResult> RemoveBlacklist(long entryId)
    {
        try
        {
            var resp = await _metrics.RemoveBlacklistEntryAsync(
                new RemoveBlacklistEntryRequest { EntryId = entryId });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    private static object? MapMetric(Metric? m) => m is null ? null : new
    {
        id        = m.Id,
        userId    = m.UserId,
        type      = m.Type,
        config    = m.Config,
        isActive  = m.IsActive,
        updatedAt = m.UpdatedAt
    };

    private static object? MapWhitelist(WhitelistEntry? e) => e is null ? null : new
    {
        id        = e.Id,
        metricId  = e.MetricId,
        pattern   = e.Pattern,
        action    = e.Action,
        createdAt = e.CreatedAt
    };

    private static object? MapBlacklist(BlacklistEntry? e) => e is null ? null : new
    {
        id        = e.Id,
        metricId  = e.MetricId,
        pattern   = e.Pattern,
        action    = e.Action,
        createdAt = e.CreatedAt
    };

    public record CreateMetricDto(long UserId, string? Type, string? Config);
    public record UpdateMetricDto(string? Type, string? Config, bool IsActive);
    public record ListEntryDto(string? Pattern, string? Action);
}
