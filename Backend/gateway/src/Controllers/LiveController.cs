using System.Security.Claims;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ActivityClient = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using NotificationClient = Gateway.Protos.Notification.NotificationService.NotificationServiceClient;
using Gateway.Protos.Activity;
using Gateway.Protos.Notification;

namespace Gateway.Controllers;

[ApiController]
[Route("api/live")]
[Authorize]
public class LiveController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ActivityClient _activity;
    private readonly NotificationClient _notifications;
    private readonly ILogger<LiveController> _logger;

    public LiveController(
        ActivityClient activity,
        NotificationClient notifications,
        ILogger<LiveController> logger)
    {
        _activity = activity;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await WriteEventAsync("ready", new
        {
            timestamp = DateTime.UtcNow,
            intervalMs = 5000
        }, cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = await BuildSnapshotAsync(cancellationToken);
                await WriteEventAsync("snapshot", snapshot, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live stream terminated unexpectedly");
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

    private async Task<object> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        int? unreadCount = null;
        object? dashboard = null;
        List<object> errors = [];

        try
        {
            var stats = await _activity.GetActivityStatisticsAsync(new GetActivityStatisticsRequest(), cancellationToken: cancellationToken);
            dashboard = new
            {
                totalActivities = stats.TotalActivities,
                blockedActivities = stats.BlockedActivities,
                anomalyCount = stats.AnomalyCount,
                averageRiskScore = stats.AverageRiskScore
            };
        }
        catch (RpcException ex)
        {
            errors.Add(new { source = "activity", message = ex.Status.Detail });
        }
        catch (Exception ex)
        {
            errors.Add(new { source = "activity", message = ex.Message });
        }

        var userId = GetUserId();
        if (userId > 0)
        {
            try
            {
                var unread = await _notifications.GetUnreadCountAsync(new GetUnreadCountRequest { UserId = userId }, cancellationToken: cancellationToken);
                unreadCount = unread.Count;
            }
            catch (RpcException ex)
            {
                errors.Add(new { source = "notifications", message = ex.Status.Detail });
            }
            catch (Exception ex)
            {
                errors.Add(new { source = "notifications", message = ex.Message });
            }
        }

        return new
        {
            timestamp = DateTime.UtcNow,
            dashboard,
            notifications = new
            {
                unreadCount
            },
            errors = errors.Count == 0 ? null : errors
        };
    }

    private long GetUserId()
    {
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(value, out var id) ? id : 0;
    }

    private async Task WriteEventAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
