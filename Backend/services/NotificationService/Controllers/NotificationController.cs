using Microsoft.AspNetCore.Mvc;
using NotificationService.Data;
using NotificationService.Models;
using Microsoft.EntityFrameworkCore;

namespace NotificationService.Controllers;

[ApiController]
[Route("api")]
public class NotificationController : ControllerBase
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(NotificationDbContext db, ILogger<NotificationController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        try
        {
            var notifications = await _db.Notifications
                .OrderByDescending(n => n.SentAt)
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return Ok(notifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notifications");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("notifications/unread")]
    public async Task<IActionResult> GetUnreadNotifications([FromQuery] int limit = 20)
    {
        try
        {
            var unreadNotifications = await _db.Notifications
                .Where(n => !n.IsRead)
                .OrderByDescending(n => n.SentAt)
                .Take(limit)
                .ToListAsync();

            return Ok(unreadNotifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unread notifications");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("notifications/{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        try
        {
            var notification = await _db.Notifications.FindAsync(id);
            if (notification == null)
            {
                return NotFound(new { error = "Notification not found" });
            }

            notification.IsRead = true;
            
            await _db.SaveChangesAsync();

            return Ok(new { message = "Notification marked as read" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "Healthy", service = "NotificationService", timestamp = DateTime.UtcNow });
    }
}