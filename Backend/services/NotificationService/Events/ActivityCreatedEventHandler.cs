using MassTransit;
using NotificationService.Data;
using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Events;
using NotificationService.Services;
using NotificationEntity = NotificationService.Models.Notification;
using ProcessedEventInboxEntryEntity = NotificationService.Models.ProcessedEventInboxEntry;

namespace NotificationService.Events;

public class ActivityCreatedEventHandler : IConsumer<ActivityCreatedEvent>
{
    private readonly NotificationDbContext _db;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly ILogger<ActivityCreatedEventHandler> _logger;

    public ActivityCreatedEventHandler(
        NotificationDbContext db,
        INotificationRecipientResolver recipientResolver,
        ILogger<ActivityCreatedEventHandler> logger)
    {
        _db = db;
        _recipientResolver = recipientResolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ActivityCreatedEvent> context)
    {
        var @event = context.Message;
        _logger.LogInformation("Processing ActivityCreatedEvent for activity {ActivityId}, computer {ComputerId}, type {ActivityType}",
            @event.ActivityId, @event.ComputerId, @event.ActivityType);

        try
        {
            var recipient = await _recipientResolver.ResolveByComputerIdAsync(@event.ComputerId, context.CancellationToken);
            if (recipient.UserId is null)
            {
                _logger.LogWarning(
                    "ActivityCreatedEvent recipient could not be mapped for computer {ComputerId}. Notification will be stored without user binding.",
                    @event.ComputerId);
            }

            // Check if this activity type requires notification
            var notificationTypes = new[] { "MALWARE", "DATA_EXFILTRATION", "UNAUTHORIZED_ACCESS", "SUSPICIOUS_ACTIVITY" };

            if (notificationTypes.Contains(@event.ActivityType.ToUpper()))
            {
                var consumerName = nameof(ActivityCreatedEventHandler);
                var eventKey = EventProcessingHelper.ActivityCreatedKey(@event);
                var notification = new NotificationEntity
                {
                    UserId = recipient.UserId,
                    Type = "SECURITY_ALERT",
                    Title = $"Security Alert: {@event.ActivityType}",
                    Message = $"Suspicious activity '{@event.ActivityType}' detected on computer {@event.ComputerId}. Activity ID: {@event.ActivityId}",
                    Channel = "email",
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

                _logger.LogInformation("Created security notification for activity {ActivityId}", @event.ActivityId);
            }
        }
        catch (DbUpdateException ex) when (EventProcessingHelper.IsDuplicateProcessing(ex))
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation("Skipping duplicate ActivityCreatedEvent for activity {ActivityId} (MessageId={MessageId})",
                @event.ActivityId, context.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ActivityCreatedEvent for activity {ActivityId}", @event.ActivityId);
            throw;
        }
    }
}
