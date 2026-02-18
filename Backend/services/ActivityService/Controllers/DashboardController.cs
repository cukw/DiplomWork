using Microsoft.AspNetCore.Mvc;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Controllers;

[ApiController]
[Route("dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(AppDbContext db, ILogger<DashboardController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var totalActivities = await _db.Activities.CountAsync();
            var blockedActivities = await _db.Activities.CountAsync(a => a.IsBlocked);
            var anomalyCount = await _db.Anomalies.CountAsync();
            
            // Get activity type counts
            var activityTypeCounts = await _db.Activities
                .GroupBy(a => a.ActivityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync();

            // Average risk score
            var avgRiskScore = await _db.Activities
                .Where(a => a.RiskScore.HasValue)
                .AverageAsync(a => a.RiskScore!.Value);

            // Get unique computers count
            var totalComputers = await _db.Activities
                .Select(a => a.ComputerId)
                .Distinct()
                .CountAsync();

            // Get active computers (with activity in last 24 hours)
            var oneDayAgo = DateTime.UtcNow.AddDays(-1);
            var activeComputers = await _db.Activities
                .Where(a => a.Timestamp >= oneDayAgo)
                .Select(a => a.ComputerId)
                .Distinct()
                .CountAsync();

            // Mock user data (in real implementation, you'd get this from UserService)
            var totalUsers = 150;
            var activeUsers = 89;

            var stats = new
            {
                totalUsers,
                activeUsers,
                totalComputers,
                activeComputers,
                totalActivities,
                blockedActivities,
                anomalyCount,
                averageRiskScore = (float)avgRiskScore,
                activityTypeCounts = activityTypeCounts.ToDictionary(t => t.Type, t => t.Count)
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("activities")]
    public async Task<IActionResult> GetRecentActivities([FromQuery] int limit = 10)
    {
        try
        {
            var activities = await _db.Activities
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(a => new
                {
                    id = a.Id,
                    computer = $"PC-{a.ComputerId:D3}", // Format as PC-001, PC-002, etc.
                    activity = a.ActivityType,
                    timestamp = a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    status = a.IsBlocked ? "blocked" : (a.RiskScore > 50 ? "warning" : "normal"),
                    details = a.Details,
                    processName = a.ProcessName,
                    url = a.Url,
                    riskScore = a.RiskScore
                })
                .ToListAsync();

            return Ok(activities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activities");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> GetRecentAnomalies([FromQuery] int limit = 10)
    {
        try
        {
            var anomalies = await _db.Anomalies
                .Include(a => a.Activity)
                .OrderByDescending(a => a.DetectedAt)
                .Take(limit)
                .Select(a => new
                {
                    id = a.Id,
                    computer = $"PC-{a.Activity.ComputerId:D3}", // Format as PC-001, PC-002, etc.
                    type = a.Type,
                    description = a.Description,
                    timestamp = a.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    activityType = a.Activity.ActivityType,
                    severity = GetSeverityFromType(a.Type)
                })
                .ToListAsync();

            return Ok(anomalies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent anomalies");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private static string GetSeverityFromType(string anomalyType)
    {
        return anomalyType.ToUpper() switch
        {
            "HIGH_RISK" => "High",
            "SUSPICIOUS_TYPE" => "High",
            "BLOCKED_ACTIVITY" => "High",
            "UNUSUAL_DURATION" => "Medium",
            "REPEATED_ACTIVITY" => "Medium",
            _ => "Low"
        };
    }
}