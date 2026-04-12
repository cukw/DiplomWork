using Grpc.Core;
using NotificationService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationEntity = NotificationService.Models.Notification;
using NotificationTemplateEntity = NotificationService.Models.NotificationTemplate;
using ProtoNotification = global::NotificationService.Notification;
using ProtoNotificationTemplate = global::NotificationService.NotificationTemplate;

namespace NotificationService.Services;

public class NotificationServiceImpl : NotificationService.NotificationServiceBase
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<NotificationServiceImpl> _logger;
    private readonly NotificationDeliveryOptions _deliveryOptions;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly INotificationDeliveryProcessor _deliveryProcessor;

    public NotificationServiceImpl(
        NotificationDbContext db,
        ILogger<NotificationServiceImpl> logger,
        INotificationRecipientResolver recipientResolver,
        INotificationDeliveryProcessor deliveryProcessor,
        IOptions<NotificationDeliveryOptions> deliveryOptions)
    {
        _db = db;
        _logger = logger;
        _recipientResolver = recipientResolver;
        _deliveryProcessor = deliveryProcessor;
        _deliveryOptions = deliveryOptions?.Value ?? new NotificationDeliveryOptions();
    }

    public override async Task<SendNotificationResponse> SendNotification(SendNotificationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Send notification request for user ID: {UserId}, type: {Type}", request.UserId, request.Type);

        try
        {
            var requestedRecipientEmail = NormalizeEmail(request.RecipientEmail);
            if (requestedRecipientEmail is null && request.UserId > 0)
            {
                var resolvedRecipient = await _recipientResolver.ResolveByUserIdAsync((int)request.UserId, context.CancellationToken);
                requestedRecipientEmail = resolvedRecipient.Email;
            }

            var channel = NormalizeChannel(request.Channel);
            var maxAttempts = Math.Max(1, _deliveryOptions.Retry.MaxAttempts);

            var notification = new NotificationEntity
            {
                UserId = request.UserId > 0 ? (int)request.UserId : null,
                Type = request.Type,
                Title = request.Title,
                Message = request.Message,
                Channel = channel,
                RecipientEmail = requestedRecipientEmail,
                SentAt = DateTime.UtcNow,
                IsRead = false,
                DeliveryStatus = channel == "in_app" ? "delivered" : "pending",
                DeliveryAttempts = 0,
                MaxDeliveryAttempts = maxAttempts,
                NextRetryAt = channel == "in_app" ? null : DateTime.UtcNow,
                DeliveredAt = channel == "in_app" ? DateTime.UtcNow : null,
                LastDeliveryError = null
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync(context.CancellationToken);
            await _deliveryProcessor.ProcessNewNotificationAsync(notification, context.CancellationToken);

            return new SendNotificationResponse
            {
                Success = true,
                Message = notification.DeliveryStatus switch
                {
                    "delivered" => "Notification sent successfully",
                    "failed" => "Notification queued for retry",
                    "deadletter" => "Notification moved to dead letter queue",
                    _ => "Notification accepted"
                },
                Notification = MapNotificationToProto(notification)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification for user ID: {UserId}", request.UserId);
            return new SendNotificationResponse
            {
                Success = false,
                Message = "An error occurred while sending notification"
            };
        }
    }

    public override async Task<GetNotificationsResponse> GetNotifications(GetNotificationsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get notifications request for user ID: {UserId}, unread only: {UnreadOnly}", request.UserId, request.UnreadOnly);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.Notifications.AsQueryable();
            
            if (request.UserId > 0)
                query = query.Where(n => n.UserId == request.UserId);
            
            if (request.UnreadOnly)
                query = query.Where(n => !n.IsRead);
            
            query = query.OrderByDescending(n => n.SentAt);
            
            var totalCount = await query.CountAsync();
            var notifications = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var notificationProtos = notifications.Select(MapNotificationToProto).ToList();

            return new GetNotificationsResponse
            {
                Success = true,
                Message = "Notifications retrieved successfully",
                Notifications = { notificationProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notifications for user ID: {UserId}", request.UserId);
            return new GetNotificationsResponse
            {
                Success = false,
                Message = "An error occurred while retrieving notifications"
            };
        }
    }

    public override async Task<MarkAsReadResponse> MarkAsRead(MarkAsReadRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Mark as read request for notification ID: {NotificationId}", request.NotificationId);

        try
        {
            var notification = await _db.Notifications.FindAsync(request.NotificationId);

            if (notification == null)
            {
                return new MarkAsReadResponse
                {
                    Success = false,
                    Message = "Notification not found"
                };
            }

            notification.IsRead = true;
            await _db.SaveChangesAsync();

            return new MarkAsReadResponse
            {
                Success = true,
                Message = "Notification marked as read"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read for ID: {NotificationId}", request.NotificationId);
            return new MarkAsReadResponse
            {
                Success = false,
                Message = "An error occurred while marking notification as read"
            };
        }
    }

    public override async Task<MarkAllAsReadResponse> MarkAllAsRead(MarkAllAsReadRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Mark all as read request for user ID: {UserId}", request.UserId);

        try
        {
            var notifications = await _db.Notifications
                .Where(n => n.UserId == request.UserId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _db.SaveChangesAsync();

            return new MarkAllAsReadResponse
            {
                Success = true,
                Message = "All notifications marked as read"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read for user ID: {UserId}", request.UserId);
            return new MarkAllAsReadResponse
            {
                Success = false,
                Message = "An error occurred while marking all notifications as read"
            };
        }
    }

    public override async Task<DeleteNotificationResponse> DeleteNotification(DeleteNotificationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete notification request for notification ID: {NotificationId}", request.NotificationId);

        try
        {
            var notification = await _db.Notifications.FindAsync(request.NotificationId);

            if (notification == null)
            {
                return new DeleteNotificationResponse
                {
                    Success = false,
                    Message = "Notification not found"
                };
            }

            _db.Notifications.Remove(notification);
            await _db.SaveChangesAsync();

            return new DeleteNotificationResponse
            {
                Success = true,
                Message = "Notification deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification for ID: {NotificationId}", request.NotificationId);
            return new DeleteNotificationResponse
            {
                Success = false,
                Message = "An error occurred while deleting notification"
            };
        }
    }

    public override async Task<GetUnreadCountResponse> GetUnreadCount(GetUnreadCountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get unread count request for user ID: {UserId}", request.UserId);

        try
        {
            var count = await _db.Notifications
                .CountAsync(n => n.UserId == request.UserId && !n.IsRead);

            return new GetUnreadCountResponse
            {
                Success = true,
                Message = "Unread count retrieved successfully",
                Count = count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unread count for user ID: {UserId}", request.UserId);
            return new GetUnreadCountResponse
            {
                Success = false,
                Message = "An error occurred while retrieving unread count"
            };
        }
    }

    public override async Task<CreateNotificationTemplateResponse> CreateNotificationTemplate(CreateNotificationTemplateRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Create notification template request for type: {Type}", request.Type);

        try
        {
            var template = new NotificationTemplateEntity
            {
                Type = request.Type,
                Subject = request.Subject,
                BodyTemplate = request.BodyTemplate
            };

            _db.NotificationTemplates.Add(template);
            await _db.SaveChangesAsync();

            return new CreateNotificationTemplateResponse
            {
                Success = true,
                Message = "Notification template created successfully",
                Template = MapNotificationTemplateToProto(template)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating notification template for type: {Type}", request.Type);
            return new CreateNotificationTemplateResponse
            {
                Success = false,
                Message = "An error occurred while creating notification template"
            };
        }
    }

    public override async Task<UpdateNotificationTemplateResponse> UpdateNotificationTemplate(UpdateNotificationTemplateRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update notification template request for template ID: {TemplateId}", request.TemplateId);

        try
        {
            var template = await _db.NotificationTemplates.FindAsync(request.TemplateId);

            if (template == null)
            {
                return new UpdateNotificationTemplateResponse
                {
                    Success = false,
                    Message = "Notification template not found"
                };
            }

            if (!string.IsNullOrEmpty(request.Subject))
                template.Subject = request.Subject;
            
            if (!string.IsNullOrEmpty(request.BodyTemplate))
                template.BodyTemplate = request.BodyTemplate;

            await _db.SaveChangesAsync();

            return new UpdateNotificationTemplateResponse
            {
                Success = true,
                Message = "Notification template updated successfully",
                Template = MapNotificationTemplateToProto(template)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating notification template for ID: {TemplateId}", request.TemplateId);
            return new UpdateNotificationTemplateResponse
            {
                Success = false,
                Message = "An error occurred while updating notification template"
            };
        }
    }

    public override async Task<GetNotificationTemplateResponse> GetNotificationTemplate(GetNotificationTemplateRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get notification template request for type: {Type}", request.Type);

        try
        {
            var template = await _db.NotificationTemplates
                .FirstOrDefaultAsync(t => t.Type == request.Type);

            if (template == null)
            {
                return new GetNotificationTemplateResponse
                {
                    Success = false,
                    Message = "Notification template not found"
                };
            }

            return new GetNotificationTemplateResponse
            {
                Success = true,
                Message = "Notification template retrieved successfully",
                Template = MapNotificationTemplateToProto(template)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notification template for type: {Type}", request.Type);
            return new GetNotificationTemplateResponse
            {
                Success = false,
                Message = "An error occurred while retrieving notification template"
            };
        }
    }

    public override async Task<GetAllNotificationTemplatesResponse> GetAllNotificationTemplates(GetAllNotificationTemplatesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get all notification templates request");

        try
        {
            var templates = await _db.NotificationTemplates.ToListAsync();
            var templateProtos = templates.Select(MapNotificationTemplateToProto).ToList();

            return new GetAllNotificationTemplatesResponse
            {
                Success = true,
                Message = "Notification templates retrieved successfully",
                Templates = { templateProtos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all notification templates");
            return new GetAllNotificationTemplatesResponse
            {
                Success = false,
                Message = "An error occurred while retrieving notification templates"
            };
        }
    }

    public override async Task<DeleteNotificationTemplateResponse> DeleteNotificationTemplate(DeleteNotificationTemplateRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete notification template request for template ID: {TemplateId}", request.TemplateId);

        try
        {
            var template = await _db.NotificationTemplates.FindAsync(request.TemplateId);

            if (template == null)
            {
                return new DeleteNotificationTemplateResponse
                {
                    Success = false,
                    Message = "Notification template not found"
                };
            }

            _db.NotificationTemplates.Remove(template);
            await _db.SaveChangesAsync();

            return new DeleteNotificationTemplateResponse
            {
                Success = true,
                Message = "Notification template deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification template for ID: {TemplateId}", request.TemplateId);
            return new DeleteNotificationTemplateResponse
            {
                Success = false,
                Message = "An error occurred while deleting notification template"
            };
        }
    }

    private static ProtoNotification MapNotificationToProto(NotificationEntity notification)
    {
        return new ProtoNotification
        {
            Id = notification.Id,
            UserId = notification.UserId ?? 0,
            Type = notification.Type ?? "",
            Title = notification.Title ?? "",
            Message = notification.Message ?? "",
            IsRead = notification.IsRead,
            SentAt = notification.SentAt?.ToUniversalTime().ToString("o") ?? "",
            Channel = notification.Channel,
            RecipientEmail = notification.RecipientEmail ?? string.Empty,
            DeliveryStatus = notification.DeliveryStatus ?? string.Empty,
            DeliveryAttempts = notification.DeliveryAttempts,
            MaxDeliveryAttempts = notification.MaxDeliveryAttempts,
            NextRetryAt = notification.NextRetryAt?.ToUniversalTime().ToString("o") ?? string.Empty,
            DeliveredAt = notification.DeliveredAt?.ToUniversalTime().ToString("o") ?? string.Empty,
            LastDeliveryError = notification.LastDeliveryError ?? string.Empty
        };
    }

    private static ProtoNotificationTemplate MapNotificationTemplateToProto(NotificationTemplateEntity template)
    {
        return new ProtoNotificationTemplate
        {
            Id = template.Id,
            Type = template.Type,
            Subject = template.Subject ?? "",
            BodyTemplate = template.BodyTemplate ?? ""
        };
    }

    private string NormalizeChannel(string? rawChannel)
    {
        var fallback = string.IsNullOrWhiteSpace(_deliveryOptions.DefaultChannel)
            ? "in_app"
            : _deliveryOptions.DefaultChannel.Trim().ToLowerInvariant();
        var channel = string.IsNullOrWhiteSpace(rawChannel)
            ? fallback
            : rawChannel.Trim().ToLowerInvariant();

        return channel switch
        {
            "inapp" => "in_app",
            "ui" => "in_app",
            "app" => "in_app",
            "http" => "webhook",
            _ => channel
        };
    }

    private static string? NormalizeEmail(string? rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
            return null;

        var normalized = rawEmail.Trim();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }
}
