using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Security.Claims;
using NotificationClient = Gateway.Protos.Notification.NotificationService.NotificationServiceClient;
using Gateway.Protos.Notification;

namespace Gateway.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly NotificationClient _notifications;

    public NotificationController(NotificationClient notifications) => _notifications = notifications;

    private long GetUserId()
    {
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long.TryParse(value, out var id);
        return id;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var resp = await _notifications.GetNotificationsAsync(new GetNotificationsRequest
            {
                UserId   = GetUserId(),
                Page     = page,
                PageSize = pageSize
            });
            return Ok(new
            {
                notifications = resp.Notifications.Select(MapNotification),
                totalCount    = resp.TotalCount
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("unread")]
    public async Task<IActionResult> GetUnread([FromQuery] int limit = 20)
    {
        try
        {
            var resp = await _notifications.GetNotificationsAsync(new GetNotificationsRequest
            {
                UserId     = GetUserId(),
                UnreadOnly = true,
                Page       = 1,
                PageSize   = limit
            });
            return Ok(resp.Notifications.Select(MapNotification));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        try
        {
            var resp = await _notifications.GetUnreadCountAsync(
                new GetUnreadCountRequest { UserId = GetUserId() });
            return Ok(new { count = resp.Count });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("{id:long}/read")]
    [HttpPut("{id:long}/read")]
    public async Task<IActionResult> MarkAsRead(long id)
    {
        try
        {
            var resp = await _notifications.MarkAsReadAsync(
                new MarkAsReadRequest { NotificationId = id });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        try
        {
            var resp = await _notifications.MarkAllAsReadAsync(
                new MarkAllAsReadRequest { UserId = GetUserId() });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try
        {
            var resp = await _notifications.DeleteNotificationAsync(
                new DeleteNotificationRequest { NotificationId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static object MapNotification(Notification n) => new
    {
        id       = n.Id,
        userId   = n.UserId,
        type     = n.Type,
        title    = n.Title,
        message  = n.Message,
        isRead   = n.IsRead,
        sentAt   = n.SentAt,
        channel  = n.Channel
    };
}
