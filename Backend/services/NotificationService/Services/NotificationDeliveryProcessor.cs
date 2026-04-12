using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationEntity = NotificationService.Models.Notification;
using NotificationDeliveryDeadLetterEntity = NotificationService.Models.NotificationDeliveryDeadLetter;

namespace NotificationService.Services;

public interface INotificationDeliveryProcessor
{
    Task ProcessNewNotificationAsync(NotificationEntity notification, CancellationToken cancellationToken);
    Task<int> ProcessDueNotificationsAsync(CancellationToken cancellationToken);
}

public sealed class NotificationDeliveryProcessor : INotificationDeliveryProcessor
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<NotificationDeliveryProcessor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly NotificationDeliveryOptions _deliveryOptions;

    public NotificationDeliveryProcessor(
        NotificationDbContext db,
        ILogger<NotificationDeliveryProcessor> logger,
        IHttpClientFactory httpClientFactory,
        INotificationRecipientResolver recipientResolver,
        IOptions<NotificationDeliveryOptions> deliveryOptions)
    {
        _db = db;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _recipientResolver = recipientResolver;
        _deliveryOptions = deliveryOptions?.Value ?? new NotificationDeliveryOptions();
    }

    public async Task ProcessNewNotificationAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        await ProcessSingleAsync(notification, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessDueNotificationsAsync(CancellationToken cancellationToken)
    {
        if (!_deliveryOptions.Retry.Enabled)
            return 0;

        var now = DateTime.UtcNow;
        var batchSize = Math.Clamp(_deliveryOptions.Retry.BatchSize, 1, 500);
        var due = await _db.Notifications
            .Where(n =>
                (n.DeliveryStatus == "pending" || n.DeliveryStatus == "failed") &&
                n.NextRetryAt != null &&
                n.NextRetryAt <= now)
            .OrderBy(n => n.NextRetryAt)
            .ThenBy(n => n.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return 0;

        foreach (var notification in due)
        {
            await ProcessSingleAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    private async Task ProcessSingleAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var channel = NormalizeChannel(notification.Channel);
        notification.Channel = channel;
        notification.MaxDeliveryAttempts = NormalizeMaxAttempts(notification.MaxDeliveryAttempts);

        if (channel == "in_app")
        {
            MarkDelivered(notification);
            return;
        }

        notification.DeliveryAttempts = Math.Max(0, notification.DeliveryAttempts) + 1;
        var attemptResult = channel switch
        {
            "email" => await TrySendEmailAsync(notification, cancellationToken),
            "webhook" => await TrySendWebhookAsync(notification, cancellationToken),
            _ => DeliveryAttemptResult.Succeeded("Unsupported channel; stored as in-app")
        };

        if (attemptResult.Success)
        {
            MarkDelivered(notification);
            return;
        }

        notification.LastDeliveryError = Truncate(attemptResult.Error, 4000);
        var exhaustedAttempts = notification.DeliveryAttempts >= notification.MaxDeliveryAttempts;
        if (!attemptResult.Retryable || exhaustedAttempts)
        {
            notification.DeliveryStatus = "deadletter";
            notification.NextRetryAt = null;
            await PersistDeadLetterAsync(notification, notification.LastDeliveryError ?? "Delivery failed", cancellationToken);
            return;
        }

        notification.DeliveryStatus = "failed";
        notification.DeliveredAt = null;
        notification.NextRetryAt = DateTime.UtcNow.Add(CalculateBackoff(notification.DeliveryAttempts));
    }

    private async Task PersistDeadLetterAsync(NotificationEntity notification, string reason, CancellationToken cancellationToken)
    {
        var exists = await _db.NotificationDeliveryDeadLetters
            .AnyAsync(dlq => dlq.NotificationId == notification.Id, cancellationToken);
        if (exists)
            return;

        _db.NotificationDeliveryDeadLetters.Add(new NotificationDeliveryDeadLetterEntity
        {
            NotificationId = notification.Id,
            Channel = notification.Channel,
            RecipientEmail = notification.RecipientEmail,
            Attempts = notification.DeliveryAttempts,
            Reason = Truncate(reason, 4000) ?? string.Empty,
            FailedAt = DateTime.UtcNow
        });
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var initialSeconds = Math.Clamp(_deliveryOptions.Retry.InitialDelaySeconds, 1, 3600);
        var maxSeconds = Math.Clamp(_deliveryOptions.Retry.MaxDelaySeconds, initialSeconds, 86400);
        var exponent = Math.Max(attempt - 1, 0);
        var delaySeconds = initialSeconds * Math.Pow(2, exponent);
        var clampedSeconds = Math.Min(delaySeconds, maxSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    private int NormalizeMaxAttempts(int persistedValue)
    {
        if (persistedValue > 0)
            return persistedValue;

        return Math.Max(1, _deliveryOptions.Retry.MaxAttempts);
    }

    private static void MarkDelivered(NotificationEntity notification)
    {
        notification.DeliveryStatus = "delivered";
        notification.NextRetryAt = null;
        notification.DeliveredAt = DateTime.UtcNow;
        notification.LastDeliveryError = null;
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

    private async Task<DeliveryAttemptResult> TrySendEmailAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var smtp = _deliveryOptions.Smtp;
        if (!smtp.Enabled || string.IsNullOrWhiteSpace(smtp.Host))
        {
            return DeliveryAttemptResult.NonRetryable("SMTP disabled or host missing");
        }

        try
        {
            var recipientAddress = NormalizeEmail(notification.RecipientEmail);
            if (recipientAddress is null && notification.UserId is > 0)
            {
                var resolvedRecipient = await _recipientResolver.ResolveByUserIdAsync(notification.UserId.Value, cancellationToken);
                recipientAddress = resolvedRecipient.Email;

                if (!string.IsNullOrWhiteSpace(recipientAddress))
                    notification.RecipientEmail = recipientAddress;
            }

            if (recipientAddress is null)
                recipientAddress = NormalizeEmail(smtp.FallbackRecipientAddress);

            if (recipientAddress is null)
                return DeliveryAttemptResult.NonRetryable("Recipient email is missing");

            using var message = new MailMessage
            {
                Subject = notification.Title ?? string.Empty,
                Body = notification.Message ?? string.Empty,
                IsBodyHtml = false,
                From = new MailAddress(smtp.FromAddress, smtp.FromName)
            };

            message.To.Add(recipientAddress);

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

            _logger.LogInformation("Notification email delivered. NotificationId={NotificationId}, UserId={UserId}", notification.Id, notification.UserId);
            return DeliveryAttemptResult.Succeeded();
        }
        catch (SmtpException ex)
        {
            _logger.LogWarning(ex, "SMTP delivery failed for notification {NotificationId}", notification.Id);
            return DeliveryAttemptResult.RetryableFailure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email delivery failed for notification {NotificationId}", notification.Id);
            return DeliveryAttemptResult.RetryableFailure(ex.Message);
        }
    }

    private async Task<DeliveryAttemptResult> TrySendWebhookAsync(NotificationEntity notification, CancellationToken cancellationToken)
    {
        var webhook = _deliveryOptions.Webhook;
        if (!webhook.Enabled || string.IsNullOrWhiteSpace(webhook.Endpoint))
        {
            return DeliveryAttemptResult.NonRetryable("Webhook disabled or endpoint missing");
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
                request.Headers.TryAddWithoutValidation(webhook.AuthHeaderName, webhook.AuthHeaderValue);

            using var response = await client.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var retryable = statusCode is >= 500 or 408 or 429;
                return retryable
                    ? DeliveryAttemptResult.RetryableFailure($"Webhook responded with status {statusCode}")
                    : DeliveryAttemptResult.NonRetryable($"Webhook responded with status {statusCode}");
            }

            _logger.LogInformation("Notification webhook delivered. NotificationId={NotificationId}, UserId={UserId}", notification.Id, notification.UserId);
            return DeliveryAttemptResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook delivery failed for notification {NotificationId}", notification.Id);
            return DeliveryAttemptResult.RetryableFailure(ex.Message);
        }
    }

    private static string? NormalizeEmail(string? rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
            return null;

        var normalized = rawEmail.Trim();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record DeliveryAttemptResult(bool Success, bool Retryable, string? Error)
    {
        public static DeliveryAttemptResult Succeeded(string? warning = null) => new(true, false, warning);
        public static DeliveryAttemptResult RetryableFailure(string error) => new(false, true, error);
        public static DeliveryAttemptResult NonRetryable(string error) => new(false, false, error);
    }
}
