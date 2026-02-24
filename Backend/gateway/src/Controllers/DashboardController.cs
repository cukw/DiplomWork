using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using ActivityClient = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using Gateway.Protos.Activity;

namespace Gateway.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ActivityClient _activity;

    public DashboardController(ActivityClient activity) => _activity = activity;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var resp = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest());
            return Ok(new
            {
                totalActivities    = resp.TotalActivities,
                blockedActivities  = resp.BlockedActivities,
                anomalyCount       = resp.AnomalyCount,
                activityTypeCounts = resp.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value),
                averageRiskScore   = resp.AverageRiskScore
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("activities")]
    public async Task<IActionResult> GetActivities([FromQuery] int limit = 10)
    {
        try
        {
            var resp = await _activity.GetActivitiesAsync(new GetActivitiesRequest { Limit = limit });
            return Ok(resp.Activities.Select(MapActivity));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> GetAnomalies([FromQuery] int limit = 10)
    {
        try
        {
            var resp = await _activity.GetAnomaliesAsync(new GetAnomaliesRequest { Limit = limit });
            return Ok(resp.Anomalies.Select(MapAnomaly));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static object MapActivity(ActivityReply a) => new
    {
        id           = a.Id,
        computerId   = a.ComputerId,
        timestamp    = a.Timestamp,
        activityType = a.ActivityType,
        details      = a.Details,
        durationMs   = a.DurationMs,
        url          = a.Url,
        processName  = a.ProcessName,
        isBlocked    = a.IsBlocked,
        riskScore    = a.RiskScore,
        synced       = a.Synced
    };

    private static object MapAnomaly(AnomalyReply a) => new
    {
        id          = a.Id,
        activityId  = a.ActivityId,
        type        = a.Type,
        description = a.Description,
        detectedAt  = a.DetectedAt
    };
}
