using MassTransit;
using NotificationService.Data;
using NotificationService.Models;
using Microsoft.EntityFrameworkCore;

namespace NotificationService.Events;

public class ActivityCreatedEventHandler : IConsumer<ActivityCreatedEvent>
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<ActivityCreatedEventHandler> _logger;

    public ActivityCreatedEventHandler(
        NotificationDbContext db,
        ILogger<ActivityCreatedEventHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ActivityCreatedEvent> context)
    {
        var @event = context.Message;
        _logger.LogInformation("Processing ActivityCreatedEvent for activity {ActivityId}, computer {ComputerId}, type {ActivityType}", 
            @event.ActivityId, @event.ComputerId, @event.ActivityType);

        try
        {
            // Get user associated with this computer
            // In a real implementation, you would call UserService to get the user
            // For now, we'll use a default admin user ID (1)
            var userId = 1; // Default admin user

            // Check if this activity type requires notification
            var notificationTypes = new[] { "MALWARE", "DATA_EXFILTRATION", "UNAUTHORIZED_ACCESS", "SUSPICIOUS_ACTIVITY" };
            
            if (notificationTypes.Contains(@event.ActivityType.ToUpper()))
            {
                var notification = new Notification
                {
                    UserId = userId,
                    Type = "SECURITY_ALERT",
                    Title = $"Security Alert: {@event.ActivityType}",
                    Message = $"Suspicious activity '{@event.ActivityType}' detected on computer {@event.ComputerId}. Activity ID: {@event.ActivityId}",
                    Channel = "email",
                    SentAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    IsRead = false
                };

                _db.Notifications.Add(notification);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Created security notification for activity {ActivityId}", @event.ActivityId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ActivityCreatedEvent for activity {ActivityId}", @event.ActivityId);
            throw;
        }
    }
}