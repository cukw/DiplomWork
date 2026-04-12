using MassTransit;
using NotificationService.Data;
using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Events;
using NotificationService.Services;
using NotificationEntity = NotificationService.Models.Notification;
using ProcessedEventInboxEntryEntity = NotificationService.Models.ProcessedEventInboxEntry;

namespace NotificationService.Events;

public class AnomalyDetectedEventHandler : IConsumer<AnomalyDetectedEvent>
{
    private readonly NotificationDbContext _db;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly ILogger<AnomalyDetectedEventHandler> _logger;

    public AnomalyDetectedEventHandler(
        NotificationDbContext db,
        INotificationRecipientResolver recipientResolver,
        ILogger<AnomalyDetectedEventHandler> logger)
    {
        _db = db;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetectedEvent> context)
    {
        var @event = context.Message;
        _logger.LogInformation("Processing AnomalyDetectedEvent for activity {ActivityId}, anomaly type {AnomalyType}",
            @event.ActivityId, @event.AnomalyType);

        try
        {
            var recipient = await _recipientResolver.ResolveByComputerIdAsync(@event.ComputerId, context.CancellationToken);
            if (recipient.UserId is null)
            {
                _logger.LogWarning(
                    "AnomalyDetectedEvent recipient could not be mapped for computer {ComputerId}. Notification will be stored without user binding.",
                    @event.ComputerId);
            }

            // Determine notification priority based on anomaly type
            var priority = GetNotificationPriority(@event.AnomalyType);
            var channel = priority == "HIGH" ? "email" : "in_app";
            var consumerName = nameof(AnomalyDetectedEventHandler);
            var eventKey = EventProcessingHelper.AnomalyDetectedKey(@event);

            var notification = new NotificationEntity
            {
                UserId = recipient.UserId,
                Type = "ANOMALY_DETECTED",
                Title = $"Anomaly Detected: {@event.AnomalyType}",
                Message = $"Anomaly '{@event.AnomalyType}' detected for activity '{@event.ActivityType}' on computer {@event.ComputerId}. {@event.Description}",
                Channel = channel,
                RecipientEmail = recipient.Email,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _db.ProcessedEventInboxEntries.Add(new ProcessedEventInboxEntryEntity
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
