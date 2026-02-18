using Microsoft.AspNetCore.Mvc;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Controllers;

[ApiController]
[Route("reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(AppDbContext db, ILogger<ReportsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyReport([FromQuery] DateTime? date = null)
    {
        try
        {
            var targetDate = date ?? DateTime.UtcNow.Date;
            var startDate = targetDate.Date;
            var endDate = targetDate.Date.AddDays(1);

            var activities = await _db.Activities
                .Where(a => a.Timestamp >= startDate && a.Timestamp < endDate)
                .ToListAsync();

            var anomalies = await _db.Anomalies
                .Include(a => a.Activity)
                .Where(a => a.DetectedAt >= startDate && a.DetectedAt < endDate)
                .ToListAsync();

            // Group activities by hour
            var hourlyActivities = activities
                .GroupBy(a => a.Timestamp.Hour)
                .Select(g => new
                {
                    hour = g.Key,
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderBy(x => x.hour)
                .ToList();

            // Group activities by type
            var activityTypes = activities
                .GroupBy(a => a.ActivityType)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Group anomalies by type
            var anomalyTypes = anomalies
                .GroupBy(a => a.Type)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Get top computers by activity count
            var topComputers = activities
                .GroupBy(a => a.ComputerId)
                .Select(g => new
                {
                    computerId = g.Key,
                    computerName = $"PC-{g.Key:D3}",
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            var report = new
            {
                date = targetDate.ToString("yyyy-MM-dd"),
                summary = new
                {
                    totalActivities = activities.Count,
                    totalAnomalies = anomalies.Count,
                    blockedActivities = activities.Count(a => a.IsBlocked),
                    averageRiskScore = activities.Any() ? activities.Average(a => a.RiskScore ?? 0) : 0,
                    uniqueComputers = activities.Select(a => a.ComputerId).Distinct().Count()
                },
                hourlyActivities,
                activityTypes,
                anomalyTypes,
                topComputers
            };

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating daily report for date {Date}", date);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("weekly")]
    public async Task<IActionResult> GetWeeklyReport([FromQuery] DateTime? startDate = null)
    {
        try
        {
            var start = startDate ?? DateTime.UtcNow.Date.AddDays(-7);
            var end = start.AddDays(7);

            var activities = await _db.Activities
                .Where(a => a.Timestamp >= start && a.Timestamp < end)
                .ToListAsync();

            var anomalies = await _db.Anomalies
                .Include(a => a.Activity)
                .Where(a => a.DetectedAt >= start && a.DetectedAt < end)
                .ToListAsync();

            // Group activities by day
            var dailyActivities = activities
                .GroupBy(a => a.Timestamp.Date)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0),
                    anomalies = anomalies.Count(a => a.Activity.Timestamp.Date == g.Key)
                })
                .OrderBy(x => x.date)
                .ToList();

            // Group activities by type
            var activityTypes = activities
                .GroupBy(a => a.ActivityType)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Group anomalies by type
            var anomalyTypes = anomalies
                .GroupBy(a => a.Type)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Get top computers by activity count
            var topComputers = activities
                .GroupBy(a => a.ComputerId)
                .Select(g => new
                {
                    computerId = g.Key,
                    computerName = $"PC-{g.Key:D3}",
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            var report = new
            {
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                summary = new
                {
                    totalActivities = activities.Count,
                    totalAnomalies = anomalies.Count,
                    blockedActivities = activities.Count(a => a.IsBlocked),
                    averageRiskScore = activities.Any() ? activities.Average(a => a.RiskScore ?? 0) : 0,
                    uniqueComputers = activities.Select(a => a.ComputerId).Distinct().Count()
                },
                dailyActivities,
                activityTypes,
                anomalyTypes,
                topComputers
            };

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating weekly report starting from {StartDate}", startDate);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthlyReport([FromQuery] int? year = null, [FromQuery] int? month = null)
    {
        try
        {
            var targetYear = year ?? DateTime.UtcNow.Year;
            var targetMonth = month ?? DateTime.UtcNow.Month;
            var start = new DateTime(targetYear, targetMonth, 1);
            var end = start.AddMonths(1);

            var activities = await _db.Activities
                .Where(a => a.Timestamp >= start && a.Timestamp < end)
                .ToListAsync();

            var anomalies = await _db.Anomalies
                .Include(a => a.Activity)
                .Where(a => a.DetectedAt >= start && a.DetectedAt < end)
                .ToListAsync();

            // Group activities by day
            var dailyActivities = activities
                .GroupBy(a => a.Timestamp.Date)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0),
                    anomalies = anomalies.Count(a => a.Activity.Timestamp.Date == g.Key)
                })
                .OrderBy(x => x.date)
                .ToList();

            // Group activities by type
            var activityTypes = activities
                .GroupBy(a => a.ActivityType)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Group anomalies by type
            var anomalyTypes = anomalies
                .GroupBy(a => a.Type)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Get top computers by activity count
            var topComputers = activities
                .GroupBy(a => a.ComputerId)
                .Select(g => new
                {
                    computerId = g.Key,
                    computerName = $"PC-{g.Key:D3}",
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            var report = new
            {
                year = targetYear,
                month = targetMonth,
                summary = new
                {
                    totalActivities = activities.Count,
                    totalAnomalies = anomalies.Count,
                    blockedActivities = activities.Count(a => a.IsBlocked),
                    averageRiskScore = activities.Any() ? activities.Average(a => a.RiskScore ?? 0) : 0,
                    uniqueComputers = activities.Select(a => a.ComputerId).Distinct().Count()
                },
                dailyActivities,
                activityTypes,
                anomalyTypes,
                topComputers
            };

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating monthly report for {Year}-{Month}", year, month);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("custom")]
    public async Task<IActionResult> GetCustomReport(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string? groupBy = "day")
    {
        try
        {
            var activities = await _db.Activities
                .Where(a => a.Timestamp >= startDate && a.Timestamp < endDate)
                .ToListAsync();

            var anomalies = await _db.Anomalies
                .Include(a => a.Activity)
                .Where(a => a.DetectedAt >= startDate && a.DetectedAt < endDate)
                .ToListAsync();

            // Group activities based on the groupBy parameter
            object groupedActivities = groupBy.ToLower() switch
            {
                "hour" => activities
                    .GroupBy(a => new { a.Timestamp.Date, a.Timestamp.Hour })
                    .Select(g => new
                    {
                        date = g.Key.Date.ToString("yyyy-MM-dd"),
                        hour = g.Key.Hour,
                        count = g.Count(),
                        riskScore = g.Average(a => a.RiskScore ?? 0)
                    })
                    .OrderBy(x => x.date).ThenBy(x => x.hour)
                    .ToList(),
                
                "day" => activities
                    .GroupBy(a => a.Timestamp.Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        count = g.Count(),
                        riskScore = g.Average(a => a.RiskScore ?? 0)
                    })
                    .OrderBy(x => x.date)
                    .ToList(),
                
                "week" => activities
                    .GroupBy(a => System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                        a.Timestamp, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Sunday))
                    .Select(g => new
                    {
                        week = g.Key,
                        count = g.Count(),
                        riskScore = g.Average(a => a.RiskScore ?? 0)
                    })
                    .OrderBy(x => x.week)
                    .ToList(),
                
                "month" => activities
                    .GroupBy(a => new { a.Timestamp.Year, a.Timestamp.Month })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,
                        count = g.Count(),
                        riskScore = g.Average(a => a.RiskScore ?? 0)
                    })
                    .OrderBy(x => x.year).ThenBy(x => x.month)
                    .ToList(),
                
                _ => activities
                    .GroupBy(a => a.Timestamp.Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        count = g.Count(),
                        riskScore = g.Average(a => a.RiskScore ?? 0)
                    })
                    .OrderBy(x => x.date)
                    .ToList()
            };

            // Group activities by type
            var activityTypes = activities
                .GroupBy(a => a.ActivityType)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Group anomalies by type
            var anomalyTypes = anomalies
                .GroupBy(a => a.Type)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToList();

            // Get top computers by activity count
            var topComputers = activities
                .GroupBy(a => a.ComputerId)
                .Select(g => new
                {
                    computerId = g.Key,
                    computerName = $"PC-{g.Key:D3}",
                    count = g.Count(),
                    riskScore = g.Average(a => a.RiskScore ?? 0)
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            var report = new
            {
                startDate = startDate.ToString("yyyy-MM-dd"),
                endDate = endDate.ToString("yyyy-MM-dd"),
                groupBy,
                summary = new
                {
                    totalActivities = activities.Count,
                    totalAnomalies = anomalies.Count,
                    blockedActivities = activities.Count(a => a.IsBlocked),
                    averageRiskScore = activities.Any() ? activities.Average(a => a.RiskScore ?? 0) : 0,
                    uniqueComputers = activities.Select(a => a.ComputerId).Distinct().Count()
                },
                groupedActivities,
                activityTypes,
                anomalyTypes,
                topComputers
            };

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating custom report from {StartDate} to {EndDate}", startDate, endDate);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}