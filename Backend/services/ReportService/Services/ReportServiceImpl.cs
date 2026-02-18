using Grpc.Core;
using ReportService.Data;
using ReportService.Models;
using Microsoft.EntityFrameworkCore;
using ProtoDailyReport = global::ReportService.DailyReport;
using ProtoUserStats = global::ReportService.UserStats;
namespace ReportService.Services;

public class ReportServiceImpl : ReportService.ReportServiceBase
{
    private readonly ReportDbContext _db;
    private readonly ILogger<ReportServiceImpl> _logger;

    public ReportServiceImpl(
        ReportDbContext db,
        ILogger<ReportServiceImpl> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<GenerateDailyReportResponse> GenerateDailyReport(GenerateDailyReportRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Generate daily report request for date: {ReportDate}, computer ID: {ComputerId}", request.ReportDate, request.ComputerId);

        try
        {
            if (!DateTime.TryParse(request.ReportDate, out var reportDate))
            {
                return new GenerateDailyReportResponse
                {
                    Success = false,
                    Message = "Invalid report date format"
                };
            }

            var existingReport = await _db.DailyReports
                .FirstOrDefaultAsync(r => r.ReportDate.Date == reportDate.Date &&
                                          r.ComputerId == request.ComputerId);
            
            if (existingReport != null)
            {
                return new GenerateDailyReportResponse
                {
                    Success = false,
                    Message = "Daily report already exists for this date and computer"
                };
            }

            var report = new Models.DailyReport
            {
                ReportDate = reportDate.Date,
                ComputerId = (int)request.ComputerId,
                UserId = request.UserId > 0 ? (int?)request.UserId : null,
                TotalActivities = 0L,
                BlockedActions = 0L,
                AvgRiskScore = null
            };

            _db.DailyReports.Add(report);
            await _db.SaveChangesAsync();

            return new GenerateDailyReportResponse
            {
                Success = true,
                Message = "Daily report generated successfully",
                Report = MapDailyReportToProto(report)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating daily report for date: {ReportDate}, computer ID: {ComputerId}", request.ReportDate, request.ComputerId);
            return new GenerateDailyReportResponse
            {
                Success = false,
                Message = "An error occurred while generating daily report"
            };
        }
    }

    public override async Task<GetDailyReportResponse> GetDailyReport(GetDailyReportRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get daily report request for report ID: {ReportId}", request.ReportId);

        try
        {
            var report = await _db.DailyReports.FindAsync(request.ReportId);

            if (report == null)
            {
                return new GetDailyReportResponse
                {
                    Success = false,
                    Message = "Daily report not found"
                };
            }

            return new GetDailyReportResponse
            {
                Success = true,
                Message = "Daily report retrieved successfully",
                Report = MapDailyReportToProto(report)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving daily report for ID: {ReportId}", request.ReportId);
            return new GetDailyReportResponse
            {
                Success = false,
                Message = "An error occurred while retrieving daily report"
            };
        }
    }

    public override async Task<GetDailyReportsByDateRangeResponse> GetDailyReportsByDateRange(GetDailyReportsByDateRangeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get daily reports by date range request from {StartDate} to {EndDate}", request.StartDate, request.EndDate);

        try
        {
            if (!DateTime.TryParse(request.StartDate, out var startDate) || 
                !DateTime.TryParse(request.EndDate, out var endDate))
            {
                return new GetDailyReportsByDateRangeResponse
                {
                    Success = false,
                    Message = "Invalid date format"
                };
            }

            var page = Math.Max(1, request.Page);
            var pageSize = Math.Min(100, Math.Max(1, request.PageSize)); // Ограничение
            
            var query = _db.DailyReports
                .Where(r => r.ReportDate.Date >= startDate.Date &&
                           r.ReportDate.Date <= endDate.Date)
                .OrderByDescending(r => r.ReportDate);
            
            var totalCount = await query.CountAsync();
            var reports = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var reportProtos = reports.Select(MapDailyReportToProto).ToList();

            return new GetDailyReportsByDateRangeResponse
            {
                Success = true,
                Message = "Daily reports retrieved successfully",
                Reports = { reportProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving daily reports by date range from {StartDate} to {EndDate}", request.StartDate, request.EndDate);
            return new GetDailyReportsByDateRangeResponse
            {
                Success = false,
                Message = "An error occurred while retrieving daily reports"
            };
        }
    }

    public override async Task<GetDailyReportsByComputerResponse> GetDailyReportsByComputer(GetDailyReportsByComputerRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get daily reports by computer request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Min(100, Math.Max(1, request.PageSize));
            
            var query = _db.DailyReports.Where(r => r.ComputerId == request.ComputerId);
            
            if (!string.IsNullOrEmpty(request.StartDate) && DateTime.TryParse(request.StartDate, out var startDate))
            {
                query = query.Where(r => r.ReportDate.Date >= startDate.Date);
            }
            
            if (!string.IsNullOrEmpty(request.EndDate) && DateTime.TryParse(request.EndDate, out var endDate))
            {
                query = query.Where(r => r.ReportDate.Date <= endDate.Date);
            }
            
            query = query.OrderByDescending(r => r.ReportDate);
            
            var totalCount = await query.CountAsync();
            var reports = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var reportProtos = reports.Select(r => MapDailyReportToProto(r)).ToList();

            return new GetDailyReportsByComputerResponse
            {
                Success = true,
                Message = "Daily reports retrieved successfully",
                Reports = { reportProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving daily reports for computer ID: {ComputerId}", request.ComputerId);
            return new GetDailyReportsByComputerResponse
            {
                Success = false,
                Message = "An error occurred while retrieving daily reports"
            };
        }
    }

    public override async Task<GenerateUserStatsResponse> GenerateUserStats(GenerateUserStatsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Generate user stats request for user ID: {UserId}", request.UserId);

        try
        {
            if (!DateTime.TryParse(request.PeriodStart, out var periodStart) || 
                !DateTime.TryParse(request.PeriodEnd, out var periodEnd))
            {
                return new GenerateUserStatsResponse
                {
                    Success = false,
                    Message = "Invalid period date format"
                };
            }

            var userStats = new Models.UserStats
            {
                UserId = (int)request.UserId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalTimeMs = request.TotalTimeMs > 0 ? request.TotalTimeMs : null,
                RiskySitesList = request.RiskySites.ToList(),
                Violations = request.Violations
            };

            _db.UserStats.Add(userStats);
            await _db.SaveChangesAsync();

            return new GenerateUserStatsResponse
            {
                Success = true,
                Message = "User stats generated successfully",
                Stats = MapUserStatsToProto(userStats)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating user stats for user ID: {UserId}", request.UserId);
            return new GenerateUserStatsResponse
            {
                Success = false,
                Message = "An error occurred while generating user stats"
            };
        }
    }

    public override async Task<GetUserStatsResponse> GetUserStats(GetUserStatsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get user stats request for stats ID: {StatsId}", request.StatsId);

        try
        {
            var stats = await _db.UserStats.FindAsync(request.StatsId);

            if (stats == null)
            {
                return new GetUserStatsResponse
                {
                    Success = false,
                    Message = "User stats not found"
                };
            }

            return new GetUserStatsResponse
            {
                Success = true,
                Message = "User stats retrieved successfully",
                Stats = MapUserStatsToProto(stats)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user stats for ID: {StatsId}", request.StatsId);
            return new GetUserStatsResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user stats"
            };
        }
    }

    public override async Task<GetUserStatsByDateRangeResponse> GetUserStatsByDateRange(GetUserStatsByDateRangeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get user stats by date range request for user ID: {UserId}", request.UserId);

        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Min(100, Math.Max(1, request.PageSize));
            
            var query = _db.UserStats.Where(u => u.UserId == request.UserId);
            
            if (!string.IsNullOrEmpty(request.StartDate) && DateTime.TryParse(request.StartDate, out var startDate))
            {
                query = query.Where(u => u.PeriodStart.Date >= startDate.Date);
            }
            
            if (!string.IsNullOrEmpty(request.EndDate) && DateTime.TryParse(request.EndDate, out var endDate))
            {
                query = query.Where(u => u.PeriodEnd.Date <= endDate.Date);
            }
            
            query = query.OrderByDescending(u => u.PeriodStart);
            
            var totalCount = await query.CountAsync();
            var stats = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var statsProtos = stats.Select(s => MapUserStatsToProto(s)).ToList();

            return new GetUserStatsByDateRangeResponse
            {
                Success = true,
                Message = "User stats retrieved successfully",
                Stats = { statsProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user stats for user ID: {UserId}", request.UserId);
            return new GetUserStatsByDateRangeResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user stats"
            };
        }
    }

    public override async Task<GetSummaryReportResponse> GetSummaryReport(GetSummaryReportRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get summary report request");

        try
        {
            var query = _db.DailyReports.AsQueryable();
            
            if (!string.IsNullOrEmpty(request.StartDate) && DateTime.TryParse(request.StartDate, out var startDate))
            {
                query = query.Where(r => r.ReportDate.Date >= startDate.Date);
            }
            
            if (!string.IsNullOrEmpty(request.EndDate) && DateTime.TryParse(request.EndDate, out var endDate))
            {
                query = query.Where(r => r.ReportDate.Date <= endDate.Date);
            }
            
            if (request.UserId > 0)
            {
                query = query.Where(r => r.UserId.HasValue && r.UserId.Value == (int)request.UserId);
            }
            
            if (request.ComputerId > 0)
            {
                query = query.Where(r => r.ComputerId == (int)request.ComputerId);
            }

            var reports = await query.ToListAsync();
            
            var summary = new SummaryReport
            {
                TotalUsers = reports.Where(r => r.UserId.HasValue)
                                   .Select(r => r.UserId!.Value)
                                   .Distinct()
                                   .Count(),
                TotalComputers = reports.Select(r => r.ComputerId).Distinct().Count(),
                TotalActivities = reports.Sum(r => r.TotalActivities),
                TotalBlockedActions = reports.Sum(r => r.BlockedActions),
                AvgRiskScore = reports.Where(r => r.AvgRiskScore.HasValue)
                                     .Select(r => (double)r.AvgRiskScore!.Value)
                                     .DefaultIfEmpty(0.0)
                                     .Average()
            };

            return new GetSummaryReportResponse
            {
                Success = true,
                Message = "Summary report retrieved successfully",
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving summary report");
            return new GetSummaryReportResponse
            {
                Success = false,
                Message = "An error occurred while retrieving summary report"
            };
        }
    }

    public override async Task<ExportReportResponse> ExportReport(ExportReportRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Export report request for type: {ReportType}, format: {Format}", request.ReportType, request.Format);

        try
        {
            var safeType = System.Text.RegularExpressions.Regex.Replace(request.ReportType, "[^a-zA-Z0-9_-]", "");
            var safeFormat = System.Text.RegularExpressions.Regex.Replace(request.Format, "[^a-zA-Z0-9]", "");
            var fileName = $"report_{safeType}_{DateTime.UtcNow:yyyyMMddHHmmss}.{safeFormat}";
            var downloadUrl = $"/api/downloads/{fileName}";

            return new ExportReportResponse
            {
                Success = true,
                Message = "Report exported successfully",
                DownloadUrl = downloadUrl,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting report for type: {ReportType}, format: {Format}", request.ReportType, request.Format);
            return new ExportReportResponse
            {
                Success = false,
                Message = "An error occurred while exporting report"
            };
        }
    }

    private static ProtoDailyReport MapDailyReportToProto(Models.DailyReport report)
    {
        return new ProtoDailyReport
        {
            Id = report.Id,
            ReportDate = report.ReportDate.ToString("yyyy-MM-dd"),
            ComputerId = report.ComputerId,
            UserId = report.UserId ?? 0L,
            TotalActivities = report.TotalActivities,
            BlockedActions = report.BlockedActions,
            AvgRiskScore = report.AvgRiskScore.HasValue ? (double)report.AvgRiskScore.Value : 0.0,
            CreatedAt = report.CreatedAt.ToString("o")
        };
    }

    private static ProtoUserStats MapUserStatsToProto(Models.UserStats stats)
    {
        return new ProtoUserStats
        {
            Id = stats.Id,
            UserId = stats.UserId,
            PeriodStart = stats.PeriodStart.ToString("o"),
            PeriodEnd = stats.PeriodEnd.ToString("o"),
            TotalTimeMs = stats.TotalTimeMs ?? 0L,
            RiskySites = stats.RiskySites ?? "[]",
            Violations = stats.Violations,
            CreatedAt = stats.CreatedAt.ToString("o")
        };
    }
}
