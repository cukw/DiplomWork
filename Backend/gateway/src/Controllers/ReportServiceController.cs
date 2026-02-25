using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Text;
using System.Text.Json;
using Gateway.Services;
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
    private readonly DownloadFileStore _downloads;

    public ReportServiceController(ReportClient report, DownloadFileStore downloads)
    {
        _report = report;
        _downloads = downloads;
    }

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
            var format = NormalizeExportFormat(dto.Format);
            if (format is null)
            {
                return BadRequest(new
                {
                    message = "Unsupported export format. Supported formats: csv, json"
                });
            }

            var startDate = dto.StartDate ?? "";
            var endDate = dto.EndDate ?? "";
            if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            {
                return BadRequest(new { message = "startDate and endDate are required" });
            }

            var summaryResp = await _report.GetSummaryReportAsync(new GetSummaryReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                UserId = dto.UserId,
                ComputerId = dto.ComputerId
            });

            if (!summaryResp.Success)
                return BadRequest(new { message = summaryResp.Message });

            var dailyReports = await GetAllDailyReportsAsync(startDate, endDate);

            var safeType = SanitizeToken(dto.ReportType, "report");
            var fileBase = $"report_{safeType}_{DateTime.UtcNow:yyyyMMddHHmmss}";

            byte[] fileBytes;
            string fileName;
            string contentType;

            if (format == "json")
            {
                fileName = $"{fileBase}.json";
                var jsonPayload = BuildExportJson(dto, summaryResp.Summary, dailyReports);
                fileBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
                contentType = "application/json";
            }
            else
            {
                fileName = $"{fileBase}.csv";
                fileBytes = Encoding.UTF8.GetBytes(BuildExportCsv(dto, summaryResp.Summary, dailyReports));
                contentType = "text/csv";
            }

            var storedName = await _downloads.SaveAsync(fileName, fileBytes, HttpContext.RequestAborted);
            return Ok(new
            {
                downloadUrl = $"/api/downloads/{storedName}",
                fileName = storedName,
                contentType
            });
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

    private async Task<List<DailyReport>> GetAllDailyReportsAsync(string startDate, string endDate)
    {
        const int pageSize = 500;
        var page = 1;
        var all = new List<DailyReport>();

        while (true)
        {
            var resp = await _report.GetDailyReportsByDateRangeAsync(new GetDailyReportsByDateRangeRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                Page = page,
                PageSize = pageSize
            });

            if (!resp.Success)
                break;

            all.AddRange(resp.Reports);
            if (resp.Reports.Count < pageSize)
                break;

            page++;
            if (page > 100) // hard stop for safety
                break;
        }

        return all;
    }

    private static string? NormalizeExportFormat(string? format)
    {
        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        return normalized is "csv" or "json" ? normalized : null;
    }

    private static string SanitizeToken(string? input, string fallback)
    {
        var raw = string.IsNullOrWhiteSpace(input) ? fallback : input.Trim();
        var safe = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe.ToLowerInvariant();
    }

    private static object BuildExportJson(ExportDto dto, SummaryReport summary, IReadOnlyList<DailyReport> dailyReports)
    {
        return new
        {
            metadata = new
            {
                exportedAt = DateTime.UtcNow,
                reportType = dto.ReportType ?? "report",
                format = "json",
                period = new { startDate = dto.StartDate, endDate = dto.EndDate },
                filters = new { dto.UserId, dto.ComputerId }
            },
            summary = new
            {
                totalUsers = summary.TotalUsers,
                totalComputers = summary.TotalComputers,
                totalActivities = summary.TotalActivities,
                totalBlockedActions = summary.TotalBlockedActions,
                avgRiskScore = summary.AvgRiskScore,
                topUsers = summary.TopUsers.Select(u => new { u.UserId, u.UserName, u.Activities, u.RiskScore }),
                topComputers = summary.TopComputers.Select(c => new { c.ComputerId, c.Hostname, c.Activities, c.RiskScore })
            },
            dailyReports = dailyReports.Select(r => new
            {
                r.Id,
                r.ReportDate,
                r.ComputerId,
                r.UserId,
                r.TotalActivities,
                r.BlockedActions,
                r.AvgRiskScore,
                r.CreatedAt
            })
        };
    }

    private static string BuildExportCsv(ExportDto dto, SummaryReport summary, IReadOnlyList<DailyReport> dailyReports)
    {
        static string Csv(string? value)
        {
            var s = value ?? string.Empty;
            if (s.Contains('"')) s = s.Replace("\"", "\"\"");
            return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s}\"" : s;
        }

        var sb = new StringBuilder();
        sb.AppendLine("section,key,value");
        sb.AppendLine($"meta,exportedAt,{Csv(DateTime.UtcNow.ToString("O"))}");
        sb.AppendLine($"meta,reportType,{Csv(dto.ReportType ?? "report")}");
        sb.AppendLine($"meta,startDate,{Csv(dto.StartDate ?? "")}");
        sb.AppendLine($"meta,endDate,{Csv(dto.EndDate ?? "")}");
        sb.AppendLine($"summary,totalUsers,{summary.TotalUsers}");
        sb.AppendLine($"summary,totalComputers,{summary.TotalComputers}");
        sb.AppendLine($"summary,totalActivities,{summary.TotalActivities}");
        sb.AppendLine($"summary,totalBlockedActions,{summary.TotalBlockedActions}");
        sb.AppendLine($"summary,avgRiskScore,{summary.AvgRiskScore}");
        sb.AppendLine();
        sb.AppendLine("dailyReports,id,reportDate,computerId,userId,totalActivities,blockedActions,avgRiskScore,createdAt");

        foreach (var r in dailyReports)
        {
            sb.Append("dailyReports,")
              .Append(r.Id).Append(',')
              .Append(Csv(r.ReportDate)).Append(',')
              .Append(r.ComputerId).Append(',')
              .Append(r.UserId).Append(',')
              .Append(r.TotalActivities).Append(',')
              .Append(r.BlockedActions).Append(',')
              .Append(r.AvgRiskScore).Append(',')
              .Append(Csv(r.CreatedAt))
              .AppendLine();
        }

        return sb.ToString();
    }
}
