using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using ActivityClient = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using Gateway.Protos.Activity;

namespace Gateway.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ActivityClient _activity;

    public SearchController(ActivityClient activity) => _activity = activity;

    [HttpGet("activities")]
    public async Task<IActionResult> SearchActivities(
        [FromQuery] string?  query        = null,
        [FromQuery] string?  activityType = null,
        [FromQuery] int?     computerId   = null,
        [FromQuery] string?  startDate    = null,
        [FromQuery] string?  endDate      = null,
        [FromQuery] bool?    isBlocked    = null,
        [FromQuery] int      page         = 1,
        [FromQuery] int      pageSize     = 20)
    {
        try
        {
            var req = new GetActivitiesRequest { Limit = pageSize * page };
            if (computerId.HasValue)   req.ComputerId   = computerId.Value;
            if (!string.IsNullOrEmpty(activityType)) req.ActivityType = activityType;
            if (!string.IsNullOrEmpty(startDate))    req.FromTimestamp = startDate;
            if (!string.IsNullOrEmpty(endDate))      req.ToTimestamp = endDate;
            if (isBlocked.HasValue)    req.OnlyBlocked  = isBlocked.Value;

            var resp = await _activity.GetActivitiesAsync(req);

            var items = resp.Activities
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapActivity);

            return Ok(new
            {
                items,
                totalCount  = resp.TotalCount,
                page,
                pageSize
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> SearchAnomalies(
        [FromQuery] int? activityId = null,
        [FromQuery] int  page       = 1,
        [FromQuery] int  pageSize   = 20)
    {
        try
        {
            var req = new GetAnomaliesRequest { Limit = pageSize * page };
            if (activityId.HasValue) req.ActivityId = activityId.Value;

            var resp = await _activity.GetAnomaliesAsync(req);

            var items = resp.Anomalies
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapAnomaly);

            return Ok(new { items, page, pageSize });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("filters")]
    public IActionResult GetFilters() => Ok(new
    {
        activityTypes = new[] { "process_open", "site_visit", "file_access", "network", "usb" },
        anomalyTypes  = new[] { "HIGH_RISK", "SUSPICIOUS_URL", "UNUSUAL_TIME", "BLOCKED", "REPETITIVE" },
        riskLevels    = new[] { "low", "medium", "high", "critical" }
    });

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
        riskScore    = a.RiskScore
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
