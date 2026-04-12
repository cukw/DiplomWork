using Microsoft.AspNetCore.Mvc;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;
using UserLookup = UserService;

namespace ActivityService.Controllers;

[ApiController]
[Route("dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DashboardController> _logger;
    private readonly UserLookup.UserService.UserServiceClient _userServiceClient;

    public DashboardController(
        AppDbContext db,
        ILogger<DashboardController> logger,
        UserLookup.UserService.UserServiceClient userServiceClient)
    {
        _db = db;
        _logger = logger;
        _userServiceClient = userServiceClient;
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
            var avgRiskScore = (await _db.Activities
                .Where(a => a.RiskScore.HasValue)
                .AverageAsync(a => (decimal?)a.RiskScore!.Value)) ?? 0m;

            var (totalUsers, activeUsers, totalComputers, activeComputers) =
                await GetUserAndComputerStatsAsync(HttpContext.RequestAborted);

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

    private async Task<(int TotalUsers, int ActiveUsers, int TotalComputers, int ActiveComputers)> GetUserAndComputerStatsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        var users = new List<UserLookup.UserProfile>();
        int? totalUsersFromService = null;

        try
        {
            while (true)
            {
                var response = await _userServiceClient.GetAllUsersAsync(
                    new UserLookup.GetAllUsersRequest { Page = page, PageSize = pageSize },
                    cancellationToken: cancellationToken);

                if (!response.Success)
                {
                    _logger.LogWarning("UserService returned unsuccessful response in GetAllUsers: {Message}", response.Message);
                    break;
                }

                if (totalUsersFromService is null && response.TotalCount > 0)
                    totalUsersFromService = response.TotalCount;

                if (response.Users.Count == 0)
                    break;

                users.AddRange(response.Users);

                if (response.Users.Count < pageSize)
                    break;

                if (totalUsersFromService.HasValue && users.Count >= totalUsersFromService.Value)
                    break;

                page++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch users from UserService for dashboard stats");
        }

        var totalUsers = totalUsersFromService.GetValueOrDefault(users.Count);
        if (totalUsers < users.Count)
            totalUsers = users.Count;

        var activeUsers = 0;
        var totalComputerIds = new HashSet<long>();
        var activeComputerIds = new HashSet<long>();
        var activeCutoffUtc = DateTime.UtcNow.AddHours(-24);

        foreach (var user in users)
        {
            if (user?.Computer is null || user.Computer.Id <= 0)
                continue;

            totalComputerIds.Add(user.Computer.Id);

            if (!IsActiveComputer(user.Computer, activeCutoffUtc))
                continue;

            activeUsers++;
            activeComputerIds.Add(user.Computer.Id);
        }

        return (totalUsers, activeUsers, totalComputerIds.Count, activeComputerIds.Count);
    }

    private static bool IsActiveComputer(UserLookup.ComputerInfo computer, DateTime cutoffUtc)
    {
        var status = (computer.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (status is "online" or "active")
            return true;

        if (string.IsNullOrWhiteSpace(computer.LastSeen))
            return false;

        if (!DateTime.TryParse(computer.LastSeen, out var parsed))
            return false;

        var asUtc = parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
        };

        return asUtc >= cutoffUtc;
    }
}
