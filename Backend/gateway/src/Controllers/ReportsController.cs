using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using ActivityClient = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using Gateway.Protos.Activity;

namespace Gateway.Controllers;

/// <summary>
/// Отчёты на основе данных ActivityService (через gRPC).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ActivityClient _activity;

    public ReportsController(ActivityClient activity) => _activity = activity;

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] string? date = null)
    {
        try
        {
            var day   = date != null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.UtcNow);
            var from  = ToUtc(day, TimeOnly.MinValue).ToString("O");
            var to    = ToUtc(day, TimeOnly.MaxValue).ToString("O");

            var stats = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to
            });

            var acts = await _activity.GetActivitiesAsync(new GetActivitiesRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to,
                Limit = 1000
            });

            return Ok(new
            {
                date             = day.ToString("yyyy-MM-dd"),
                totalActivities  = stats.TotalActivities,
                blockedActivities= stats.BlockedActivities,
                anomalyCount     = stats.AnomalyCount,
                averageRiskScore = stats.AverageRiskScore,
                activityTypeCounts = stats.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value),
                activities       = acts.Activities.Select(MapActivity)
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("weekly")]
    public async Task<IActionResult> Weekly([FromQuery] string? startDate = null)
    {
        try
        {
            var start = startDate != null
                ? DateOnly.Parse(startDate)
                : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6));
            var end   = start.AddDays(6);
            var from  = ToUtc(start, TimeOnly.MinValue).ToString("O");
            var to    = ToUtc(end, TimeOnly.MaxValue).ToString("O");

            var stats = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to
            });

            return Ok(new
            {
                startDate        = start.ToString("yyyy-MM-dd"),
                endDate          = end.ToString("yyyy-MM-dd"),
                totalActivities  = stats.TotalActivities,
                blockedActivities= stats.BlockedActivities,
                anomalyCount     = stats.AnomalyCount,
                averageRiskScore = stats.AverageRiskScore,
                activityTypeCounts = stats.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value)
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(
        [FromQuery] int? year  = null,
        [FromQuery] int? month = null)
    {
        try
        {
            var now   = DateTime.UtcNow;
            var y     = year  ?? now.Year;
            var m     = month ?? now.Month;
            var start = new DateOnly(y, m, 1);
            var end   = start.AddMonths(1).AddDays(-1);
            var from  = ToUtc(start, TimeOnly.MinValue).ToString("O");
            var to    = ToUtc(end, TimeOnly.MaxValue).ToString("O");

            var stats = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to
            });

            return Ok(new
            {
                year             = y,
                month            = m,
                totalActivities  = stats.TotalActivities,
                blockedActivities= stats.BlockedActivities,
                anomalyCount     = stats.AnomalyCount,
                averageRiskScore = stats.AverageRiskScore,
                activityTypeCounts = stats.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value)
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("custom")]
    public async Task<IActionResult> Custom(
        [FromQuery] string  startDate,
        [FromQuery] string  endDate,
        [FromQuery] string? groupBy = "day")
    {
        try
        {
            var start = DateOnly.Parse(startDate);
            var end   = DateOnly.Parse(endDate);
            var from  = ToUtc(start, TimeOnly.MinValue).ToString("O");
            var to    = ToUtc(end, TimeOnly.MaxValue).ToString("O");

            var stats = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to
            });

            var acts = await _activity.GetActivitiesAsync(new GetActivitiesRequest
            {
                FromTimestamp = from,
                ToTimestamp   = to,
                Limit = 5000
            });

            return Ok(new
            {
                startDate        = start.ToString("yyyy-MM-dd"),
                endDate          = end.ToString("yyyy-MM-dd"),
                groupBy,
                totalActivities  = stats.TotalActivities,
                blockedActivities= stats.BlockedActivities,
                anomalyCount     = stats.AnomalyCount,
                averageRiskScore = stats.AverageRiskScore,
                activityTypeCounts = stats.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value),
                activities       = acts.Activities.Select(MapActivity)
            });
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
        isBlocked    = a.IsBlocked,
        riskScore    = a.RiskScore
    };

    private static DateTime ToUtc(DateOnly date, TimeOnly time)
        => DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
}
