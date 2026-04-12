namespace NotificationService.Services;

public sealed class NotificationDeliveryOptions
{
    public string DefaultChannel { get; set; } = "in_app";
    public RetryDeliveryOptions Retry { get; set; } = new();
    public SmtpDeliveryOptions Smtp { get; set; } = new();
    public WebhookDeliveryOptions Webhook { get; set; } = new();
}

public sealed class RetryDeliveryOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxAttempts { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 30;
    public int MaxDelaySeconds { get; set; } = 900;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
}

public sealed class SmtpDeliveryOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "noreply@localhost";
    public string FromName { get; set; } = "Activity Monitor";
    public string FallbackRecipientAddress { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class WebhookDeliveryOptions
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string AuthHeaderName { get; set; } = string.Empty;
    public string AuthHeaderValue { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
