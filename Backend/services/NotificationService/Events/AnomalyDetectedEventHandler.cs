using MassTransit;
using NotificationService.Data;
using NotificationService.Models;
using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Events;

namespace NotificationService.Events;

public class AnomalyDetectedEventHandler : IConsumer<AnomalyDetectedEvent>
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<AnomalyDetectedEventHandler> _logger;

    public AnomalyDetectedEventHandler(
        NotificationDbContext db,
        ILogger<AnomalyDetectedEventHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetectedEvent> context)
    {
        var @event = context.Message;
        _logger.LogInformation("Processing AnomalyDetectedEvent for activity {ActivityId}, anomaly type {AnomalyType}",
            @event.ActivityId, @event.AnomalyType);

        try
        {
            // Get user associated with this computer
            // In a real implementation, you would call UserService to get the user
            // For now, we'll use a default admin user ID (1)
            var userId = 1; // Default admin user

            // Determine notification priority based on anomaly type
            var priority = GetNotificationPriority(@event.AnomalyType);
            var channel = priority == "HIGH" ? "email" : "in_app";
            var consumerName = nameof(AnomalyDetectedEventHandler);
            var eventKey = EventProcessingHelper.AnomalyDetectedKey(@event);

            var notification = new Notification
            {
                UserId = userId,
                Type = "ANOMALY_DETECTED",
                Title = $"Anomaly Detected: {@event.AnomalyType}",
                Message = $"Anomaly '{@event.AnomalyType}' detected for activity '{@event.ActivityType}' on computer {@event.ComputerId}. {@event.Description}",
                Channel = channel,
                SentAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IsRead = false
            };

            _db.ProcessedEventInboxEntries.Add(new ProcessedEventInboxEntry
            {
                Consumer = consumerName,
                EventKey = eventKey,
                MessageId = context.MessageId?.ToString(),
                ProcessedAt = DateTime.UtcNow
            });
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Created anomaly notification for activity {ActivityId}, anomaly type {AnomalyType}",
                @event.ActivityId, @event.AnomalyType);
        }
        catch (DbUpdateException ex) when (EventProcessingHelper.IsDuplicateProcessing(ex))
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation("Skipping duplicate AnomalyDetectedEvent for activity {ActivityId}, anomaly {AnomalyType} (MessageId={MessageId})",
                @event.ActivityId, @event.AnomalyType, context.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AnomalyDetectedEvent for activity {ActivityId}", @event.ActivityId);
            throw;
        }
    }

    private string GetNotificationPriority(string anomalyType)
    {
        return anomalyType.ToUpper() switch
        {
            "HIGH_RISK" => "HIGH",
            "SUSPICIOUS_TYPE" => "HIGH",
            "BLOCKED_ACTIVITY" => "HIGH",
            "UNUSUAL_DURATION" => "MEDIUM",
            "REPEATED_ACTIVITY" => "MEDIUM",
            _ => "LOW"
        };
    }
}
