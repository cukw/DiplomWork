using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using ReportClient = Gateway.Protos.Report.ReportService.ReportServiceClient;
using Gateway.Protos.Report;

namespace Gateway.Controllers;

/// <summary>
/// Управление отчётами через ReportService (gRPC).
/// Маршрут /api/report/... (не путать с /api/reports/... который на ActivityService).
/// </summary>
[ApiController]
[Route("api/report")]
[Authorize]
public class ReportServiceController : ControllerBase
{
    private readonly ReportClient _report;

    public ReportServiceController(ReportClient report) => _report = report;

    [HttpPost("daily")]
    public async Task<IActionResult> GenerateDaily([FromBody] GenerateDailyDto dto)
    {
        try
        {
            var resp = await _report.GenerateDailyReportAsync(new GenerateDailyReportRequest
            {
                ReportDate  = dto.ReportDate ?? "",
                ComputerId  = dto.ComputerId,
                UserId      = dto.UserId
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapReport(resp.Report));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("daily/{id:long}")]
    public async Task<IActionResult> GetDaily(long id)
    {
        try
        {
            var resp = await _report.GetDailyReportAsync(new GetDailyReportRequest { ReportId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(MapReport(resp.Report));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("daily/range")]
    public async Task<IActionResult> GetDailyRange(
        [FromQuery] string startDate,
        [FromQuery] string endDate,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _report.GetDailyReportsByDateRangeAsync(
                new GetDailyReportsByDateRangeRequest
                {
                    StartDate = startDate,
                    EndDate   = endDate,
                    Page      = page,
                    PageSize  = pageSize
                });
            return Ok(new
            {
                reports    = resp.Reports.Select(MapReport),
                totalCount = resp.TotalCount
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string  startDate,
        [FromQuery] string  endDate,
        [FromQuery] long?   userId     = null,
        [FromQuery] long?   computerId = null)
    {
        try
        {
            var resp = await _report.GetSummaryReportAsync(new GetSummaryReportRequest
            {
                StartDate  = startDate,
                EndDate    = endDate,
                UserId     = userId     ?? 0,
                ComputerId = computerId ?? 0
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            var s = resp.Summary;
            return Ok(new
            {
                totalUsers          = s.TotalUsers,
                totalComputers      = s.TotalComputers,
                totalActivities     = s.TotalActivities,
                totalBlockedActions = s.TotalBlockedActions,
                avgRiskScore        = s.AvgRiskScore,
                topUsers            = s.TopUsers.Select(u => new { u.UserId, u.UserName, u.Activities, u.RiskScore }),
                topComputers        = s.TopComputers.Select(c => new { c.ComputerId, c.Hostname, c.Activities, c.RiskScore })
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ExportDto dto)
    {
        try
        {
            var resp = await _report.ExportReportAsync(new ExportReportRequest
            {
                ReportType = dto.ReportType ?? "daily",
                Format     = dto.Format     ?? "pdf",
                StartDate  = dto.StartDate  ?? "",
                EndDate    = dto.EndDate    ?? "",
                UserId     = dto.UserId,
                ComputerId = dto.ComputerId
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(new { downloadUrl = resp.DownloadUrl, fileName = resp.FileName });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    private static object? MapReport(DailyReport? r) => r is null ? null : new
    {
        id              = r.Id,
        reportDate      = r.ReportDate,
        computerId      = r.ComputerId,
        userId          = r.UserId,
        totalActivities = r.TotalActivities,
        blockedActions  = r.BlockedActions,
        avgRiskScore    = r.AvgRiskScore,
        createdAt       = r.CreatedAt
    };

    public record GenerateDailyDto(string? ReportDate, long ComputerId, long UserId);
    public record ExportDto(string? ReportType, string? Format, string? StartDate, string? EndDate, long UserId, long ComputerId);
}
