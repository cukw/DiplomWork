using Microsoft.AspNetCore.Mvc;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Controllers;

[ApiController]
[Route("search")]
public class ActivitySearchController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ActivitySearchController> _logger;

    public ActivitySearchController(AppDbContext db, ILogger<ActivitySearchController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("activities")]
    public async Task<IActionResult> SearchActivities(
        [FromQuery] string? query = null,
        [FromQuery] string? activityType = null,
        [FromQuery] int? computerId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool? isBlocked = null,
        [FromQuery] decimal? minRiskScore = null,
        [FromQuery] decimal? maxRiskScore = null,
        [FromQuery] string? processName = null,
        [FromQuery] string? url = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var activitiesQuery = _db.Activities.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(query))
            {
                activitiesQuery = activitiesQuery.Where(a => 
                    EF.Functions.ILike(a.ActivityType, $"%{query}%") ||
                    EF.Functions.ILike(a.ProcessName ?? "", $"%{query}%") ||
                    EF.Functions.ILike(a.Url ?? "", $"%{query}%") ||
                    EF.Functions.ILike(a.Details ?? "", $"%{query}%"));
            }

            if (!string.IsNullOrEmpty(activityType))
            {
                activitiesQuery = activitiesQuery.Where(a => a.ActivityType == activityType);
            }

            if (computerId.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.ComputerId == computerId.Value);
            }

            if (startDate.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.Timestamp <= endDate.Value);
            }

            if (isBlocked.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.IsBlocked == isBlocked.Value);
            }

            if (minRiskScore.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.RiskScore >= minRiskScore.Value);
            }

            if (maxRiskScore.HasValue)
            {
                activitiesQuery = activitiesQuery.Where(a => a.RiskScore <= maxRiskScore.Value);
            }

            if (!string.IsNullOrEmpty(processName))
            {
                activitiesQuery = activitiesQuery.Where(a => 
                    a.ProcessName != null && EF.Functions.ILike(a.ProcessName, $"%{processName}%"));
            }

            if (!string.IsNullOrEmpty(url))
            {
                activitiesQuery = activitiesQuery.Where(a => 
                    a.Url != null && EF.Functions.ILike(a.Url, $"%{url}%"));
            }

            // Get total count
            var totalCount = await activitiesQuery.CountAsync();

            // Apply pagination
            var activities = await activitiesQuery
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.Id,
                    computerId = a.ComputerId,
                    computerName = $"PC-{a.ComputerId:D3}",
                    timestamp = a.Timestamp,
                    activityType = a.ActivityType,
                    details = a.Details,
                    durationMs = a.DurationMs,
                    url = a.Url,
                    processName = a.ProcessName,
                    isBlocked = a.IsBlocked,
                    riskScore = a.RiskScore,
                    synced = a.Synced
                })
                .ToListAsync();

            var result = new
            {
                activities,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    hasNextPage = page * pageSize < totalCount,
                    hasPreviousPage = page > 1
                }
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching activities");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> SearchAnomalies(
        [FromQuery] string? query = null,
        [FromQuery] string? anomalyType = null,
        [FromQuery] int? computerId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var anomaliesQuery = _db.Anomalies.Include(a => a.Activity).AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(query))
            {
                anomaliesQuery = anomaliesQuery.Where(a => 
                    EF.Functions.ILike(a.Type, $"%{query}%") ||
                    EF.Functions.ILike(a.Description ?? "", $"%{query}%"));
            }

            if (!string.IsNullOrEmpty(anomalyType))
            {
                anomaliesQuery = anomaliesQuery.Where(a => a.Type == anomalyType);
            }

            if (computerId.HasValue)
            {
                anomaliesQuery = anomaliesQuery.Where(a => a.Activity.ComputerId == computerId.Value);
            }

            if (startDate.HasValue)
            {
                anomaliesQuery = anomaliesQuery.Where(a => a.DetectedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                anomaliesQuery = anomaliesQuery.Where(a => a.DetectedAt <= endDate.Value);
            }

            // Get total count
            var totalCount = await anomaliesQuery.CountAsync();

            // Apply pagination
            var anomalies = await anomaliesQuery
                .OrderByDescending(a => a.DetectedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.Id,
                    activityId = a.ActivityId,
                    computerId = a.Activity.ComputerId,
                    computerName = $"PC-{a.Activity.ComputerId:D3}",
                    activityType = a.Activity.ActivityType,
                    type = a.Type,
                    description = a.Description,
                    detectedAt = a.DetectedAt,
                    severity = GetSeverityFromType(a.Type)
                })
                .ToListAsync();

            var result = new
            {
                anomalies,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    hasNextPage = page * pageSize < totalCount,
                    hasPreviousPage = page > 1
                }
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching anomalies");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("filters")]
    public async Task<IActionResult> GetAvailableFilters()
    {
        try
        {
            var activityTypes = await _db.Activities
                .Select(a => a.ActivityType)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            var anomalyTypes = await _db.Anomalies
                .Select(a => a.Type)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            var computerIds = await _db.Activities
                .Select(a => a.ComputerId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();

            var computers = computerIds.Select(id => new
            {
                id,
                name = $"PC-{id:D3}"
            }).ToList();

            var result = new
            {
                activityTypes,
                anomalyTypes,
                computers
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available filters");
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
            "SUSPICIOUS_URL" => "High",
            "HIGH_RISK_PROCESS" => "High",
            "SENSITIVE_FILE_ACCESS" => "High",
            "UNUSUAL_DURATION" => "Medium",
            "UNUSUAL_TIME" => "Medium",
            "REPEATED_ACTIVITY" => "Medium",
            "EXCESSIVE_NETWORK_ACTIVITY" => "Medium",
            _ => "Low"
        };
    }
}