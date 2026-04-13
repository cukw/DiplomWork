using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Globalization;
using System.Text.Json;
using ActivityClient = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using Gateway.Protos.Activity;

namespace Gateway.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ActivityClient _activity;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardController> _logger;
    private readonly string _activityHttpBaseUrl;

    public DashboardController(
        ActivityClient activity,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DashboardController> logger)
    {
        _activity = activity;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _activityHttpBaseUrl = (configuration["Services:ActivityHttp"]
                ?? configuration["Services:ActivityRest"]
                ?? "http://activityservice:5002")
            .TrimEnd('/');
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        try
        {
            var resp = await _activity.GetActivityStatisticsAsync(
                new GetActivityStatisticsRequest(),
                CreateDashboardCallOptions(cancellationToken));

            return Ok(new
            {
                totalUsers         = resp.TotalUsers,
                activeUsers        = resp.ActiveUsers,
                totalComputers     = resp.TotalComputers,
                activeComputers    = resp.ActiveComputers,
                totalActivities    = resp.TotalActivities,
                blockedActivities  = resp.BlockedActivities,
                anomalyCount       = resp.AnomalyCount,
                activityTypeCounts = resp.ActivityTypeCounts.ToDictionary(k => k.Key, v => v.Value),
                averageRiskScore   = resp.AverageRiskScore
            });
        }
        catch (RpcException ex) when (ShouldUseActivityRestFallback(ex))
        {
            return await GetStatsFromActivityRestAsync(ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "ActivityService gRPC dashboard stats request failed. Status={StatusCode}", ex.StatusCode);
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("activities")]
    public async Task<IActionResult> GetActivities([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _activity.GetActivitiesAsync(
                new GetActivitiesRequest { Limit = limit },
                CreateDashboardCallOptions(cancellationToken));

            return Ok(resp.Activities.Select(MapActivity));
        }
        catch (RpcException ex) when (ShouldUseActivityRestFallback(ex))
        {
            return await GetActivitiesFromActivityRestAsync(limit, ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "ActivityService gRPC dashboard activities request failed. Status={StatusCode}", ex.StatusCode);
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> GetAnomalies([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _activity.GetAnomaliesAsync(
                new GetAnomaliesRequest { Limit = limit },
                CreateDashboardCallOptions(cancellationToken));

            return Ok(resp.Anomalies.Select(MapAnomaly));
        }
        catch (RpcException ex) when (ShouldUseActivityRestFallback(ex))
        {
            return await GetAnomaliesFromActivityRestAsync(limit, ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "ActivityService gRPC dashboard anomalies request failed. Status={StatusCode}", ex.StatusCode);
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static CallOptions CreateDashboardCallOptions(CancellationToken cancellationToken) =>
        new CallOptions(
            deadline: DateTime.UtcNow.AddSeconds(8),
            cancellationToken: cancellationToken)
        .WithWaitForReady(true);

    private static bool ShouldUseActivityRestFallback(RpcException ex) =>
        ex.StatusCode is Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded;

    private async Task<IActionResult> GetStatsFromActivityRestAsync(RpcException grpcException, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                grpcException,
                "ActivityService gRPC dashboard stats request is unavailable. Falling back to REST dashboard endpoint.");

            var root = await GetActivityDashboardJsonAsync("stats", cancellationToken);
            return Ok(root);
        }
        catch (Exception fallbackException)
        {
            return ActivityServiceUnavailable(fallbackException, grpcException, "stats");
        }
    }

    private async Task<IActionResult> GetActivitiesFromActivityRestAsync(int limit, RpcException grpcException, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                grpcException,
                "ActivityService gRPC dashboard activities request is unavailable. Falling back to REST dashboard endpoint.");

            var root = await GetActivityDashboardJsonAsync($"activities?limit={Math.Max(1, limit)}", cancellationToken);
            if (root.ValueKind != JsonValueKind.Array)
                return Ok(Array.Empty<object>());

            return Ok(root.EnumerateArray().Select(MapRestActivity).ToArray());
        }
        catch (Exception fallbackException)
        {
            return ActivityServiceUnavailable(fallbackException, grpcException, "activities");
        }
    }

    private async Task<IActionResult> GetAnomaliesFromActivityRestAsync(int limit, RpcException grpcException, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                grpcException,
                "ActivityService gRPC dashboard anomalies request is unavailable. Falling back to REST dashboard endpoint.");

            var root = await GetActivityDashboardJsonAsync($"anomalies?limit={Math.Max(1, limit)}", cancellationToken);
            if (root.ValueKind != JsonValueKind.Array)
                return Ok(Array.Empty<object>());

            return Ok(root.EnumerateArray().Select(MapRestAnomaly).ToArray());
        }
        catch (Exception fallbackException)
        {
            return ActivityServiceUnavailable(fallbackException, grpcException, "anomalies");
        }
    }

    private async Task<JsonElement> GetActivityDashboardJsonAsync(string path, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var response = await client.GetAsync($"{_activityHttpBaseUrl}/dashboard/{path}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"ActivityService REST dashboard returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private IActionResult ActivityServiceUnavailable(Exception fallbackException, RpcException grpcException, string operation)
    {
        _logger.LogError(
            fallbackException,
            "ActivityService dashboard {Operation} is unavailable through both gRPC and REST. GrpcStatus={GrpcStatus}",
            operation,
            grpcException.StatusCode);

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            message = "ActivityService is unavailable",
            operation,
            grpcStatus = grpcException.StatusCode.ToString(),
            detail = string.IsNullOrWhiteSpace(grpcException.Status.Detail)
                ? fallbackException.Message
                : grpcException.Status.Detail
        });
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
        synced       = a.Synced,
        userId       = a.HasUserId ? (long?)a.UserId : null,
        agentId      = a.HasAgentId ? (long?)a.AgentId : null,
        agentVersion = a.AgentVersion,
        deviceName   = a.DeviceName,
        collector    = a.Collector,
        eventId      = a.EventId,
        sequence     = a.Sequence,
        batchId      = a.BatchId,
        sourcePlatform = a.SourcePlatform
    };

    private static object MapAnomaly(AnomalyReply a) => new
    {
        id          = a.Id,
        activityId  = a.ActivityId,
        type        = a.Type,
        description = a.Description,
        detectedAt  = a.DetectedAt
    };

    private static object MapRestActivity(JsonElement activity)
    {
        var status = GetString(activity, "status");

        return new
        {
            id = GetLong(activity, "id") ?? 0,
            computerId = GetLong(activity, "computerId", "computer_id")
                ?? ParseComputerId(GetString(activity, "computer")),
            timestamp = GetString(activity, "timestamp") ?? "",
            activityType = GetString(activity, "activityType", "activity_type", "activity") ?? "UNKNOWN",
            details = GetString(activity, "details") ?? "",
            durationMs = GetLong(activity, "durationMs", "duration_ms") ?? 0,
            url = GetString(activity, "url") ?? "",
            processName = GetString(activity, "processName", "process_name") ?? "",
            isBlocked = GetBool(activity, "isBlocked", "is_blocked")
                ?? string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase),
            riskScore = GetDouble(activity, "riskScore", "risk_score") ?? 0,
            synced = GetBool(activity, "synced") ?? true
        };
    }

    private static object MapRestAnomaly(JsonElement anomaly) => new
    {
        id = GetLong(anomaly, "id") ?? 0,
        activityId = GetLong(anomaly, "activityId", "activity_id") ?? 0,
        type = GetString(anomaly, "type") ?? "",
        description = GetString(anomaly, "description") ?? "",
        detectedAt = GetString(anomaly, "detectedAt", "detected_at", "timestamp") ?? "",
        severity = GetString(anomaly, "severity") ?? ""
    };

    private static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static long? GetLong(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static double? GetDouble(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static bool? GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var result) => result,
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement element, string[] names, out JsonElement value)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
                return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static long? ParseComputerId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
