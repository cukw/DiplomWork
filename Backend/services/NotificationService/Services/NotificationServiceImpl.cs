using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using Grpc.Core;
using NotificationService.Data;
using Google.Protobuf.WellKnownTypes;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotificationDeliveryOptions _deliveryOptions;

    public NotificationServiceImpl(
        NotificationDbContext db,
        ILogger<NotificationServiceImpl> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<NotificationDeliveryOptions> deliveryOptions)
    {
        _db = db;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _deliveryOptions = deliveryOptions?.Value ?? new NotificationDeliveryOptions();
    }

    public override async Task<SendNotificationResponse> SendNotification(SendNotificationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Send notification request for user ID: {UserId}, type: {Type}", request.UserId, request.Type);

        try
        {
            var notification = new NotificationEntity
            {
                UserId = (int)request.UserId,
                Type = request.Type,
                Title = request.Title,
                Message = request.Message,
                Channel = NormalizeChannel(request.Channel),
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            // Here you would typically implement the actual notification sending logic
            // (email, SMS, push notification, etc.)
            await SendNotificationAsync(notification, context.CancellationToken);

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
            Channel = notification.Channel
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

    private async Task SendNotificationAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var channel = NormalizeChannel(notification.Channel);

        switch (channel)
        {
            case "in_app":
                _logger.LogInformation("In-app notification stored for user {UserId}: {Title}", notification.UserId, notification.Title);
                return;
            case "email":
                await TrySendEmailAsync(notification, cancellationToken);
                return;
            case "webhook":
                await TrySendWebhookAsync(notification, cancellationToken);
                return;
            default:
                _logger.LogWarning("Unsupported notification channel '{Channel}', fallback to in_app for notification {Id}", channel, notification.Id);
                return;
        }
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

    private async Task TrySendEmailAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var smtp = _deliveryOptions.Smtp;
        if (!smtp.Enabled || string.IsNullOrWhiteSpace(smtp.Host))
        {
            _logger.LogWarning(
                "Email delivery skipped for notification {Id}: SMTP is disabled or not configured. Stored as in-app only.",
                notification.Id);
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                Subject = notification.Title ?? string.Empty,
                Body = notification.Message ?? string.Empty,
                IsBodyHtml = false,
                From = new MailAddress(smtp.FromAddress, smtp.FromName)
            };

            // UserId is internal system id; direct e-mail lookup is not implemented in this service.
            // Route all operational emails to configured mailbox for now.
            message.To.Add(smtp.FromAddress);

            using var smtpClient = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(smtp.Username, smtp.Password),
                Timeout = Math.Clamp(smtp.TimeoutSeconds, 1, 60) * 1000
            };

            cancellationToken.ThrowIfCancellationRequested();
            await smtpClient.SendMailAsync(message, cancellationToken);

            _logger.LogInformation("Email notification sent for user {UserId}: {Title}", notification.UserId, notification.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email delivery failed for notification {Id}. Notification remains stored in-app.", notification.Id);
        }
    }

    private async Task TrySendWebhookAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var webhook = _deliveryOptions.Webhook;
        if (!webhook.Enabled || string.IsNullOrWhiteSpace(webhook.Endpoint))
        {
            _logger.LogWarning(
                "Webhook delivery skipped for notification {Id}: webhook is disabled or endpoint is missing. Stored as in-app only.",
                notification.Id);
            return;
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Clamp(webhook.TimeoutSeconds, 1, 60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            var client = _httpClientFactory.CreateClient("NotificationDelivery");
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Endpoint)
            {
                Content = JsonContent.Create(new
                {
                    notification.Id,
                    notification.UserId,
                    notification.Type,
                    notification.Title,
                    notification.Message,
                    notification.Channel,
                    sentAt = notification.SentAt?.ToUniversalTime().ToString("o")
                })
            };

            if (!string.IsNullOrWhiteSpace(webhook.AuthHeaderName) && !string.IsNullOrWhiteSpace(webhook.AuthHeaderValue))
            {
                request.Headers.TryAddWithoutValidation(webhook.AuthHeaderName, webhook.AuthHeaderValue);
            }

            using var response = await client.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Webhook delivery returned non-success status {StatusCode} for notification {Id}",
                    (int)response.StatusCode,
                    notification.Id);
                return;
            }

            _logger.LogInformation("Webhook notification sent for user {UserId}: {Title}", notification.UserId, notification.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook delivery failed for notification {Id}. Notification remains stored in-app.", notification.Id);
        }
    }
}
