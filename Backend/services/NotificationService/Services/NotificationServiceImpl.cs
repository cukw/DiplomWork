using Grpc.Core;
using NotificationService.Data;
using NotificationService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;

namespace NotificationService.Services;

public class NotificationServiceImpl : NotificationService.NotificationServiceBase
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<NotificationServiceImpl> _logger;

    public NotificationServiceImpl(
        NotificationDbContext db,
        ILogger<NotificationServiceImpl> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<SendNotificationResponse> SendNotification(SendNotificationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Send notification request for user ID: {UserId}, type: {Type}", request.UserId, request.Type);

        try
        {
            var notification = new Notification
            {
                UserId = (int)request.UserId,
                Type = request.Type,
                Title = request.Title,
                Message = request.Message,
                Channel = string.IsNullOrEmpty(request.Channel) ? "email" : request.Channel,
                SentAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IsRead = false
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            // Here you would typically implement the actual notification sending logic
            // (email, SMS, push notification, etc.)
            await SendNotificationAsync(notification);

            return new SendNotificationResponse
            {
                Success = true,
                Message = "Notification sent successfully",
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
            var template = new NotificationTemplate
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

    private static Notification MapNotificationToProto(Notification notification)
    {
        return new Notification
        {
            Id = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type ?? "",
            Title = notification.Title ?? "",
            Message = notification.Message ?? "",
            IsRead = notification.IsRead,
            SentAt = notification.SentAt?.ToString() ?? "",
            Channel = notification.Channel
        };
    }

    private static NotificationTemplate MapNotificationTemplateToProto(NotificationTemplate template)
    {
        return new NotificationTemplate
        {
            Id = template.Id,
            Type = template.Type,
            Subject = template.Subject ?? "",
            BodyTemplate = template.BodyTemplate ?? ""
        };
    }

    private async Task SendNotificationAsync(Notification notification)
    {
        // This is a placeholder for the actual notification sending logic
        // In a real implementation, you would:
        // - Send email notifications
        // - Send SMS notifications
        // - Send push notifications
        // - Send in-app notifications
        // - Send notifications to external services
        
        _logger.LogInformation("Notification sent via {Channel} to user {UserId}: {Title}", 
            notification.Channel, notification.UserId, notification.Title);
        
        // Simulate async operation
        await Task.Delay(100);
    }
}