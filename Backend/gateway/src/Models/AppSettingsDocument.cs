namespace Gateway.Models;

public sealed class AppSettingsDocument
{
    public GeneralSettingsModel GeneralSettings { get; set; } = new();
    public SecuritySettingsModel SecuritySettings { get; set; } = new();
    public NotificationSettingsModel NotificationSettings { get; set; } = new();
    public MonitoringSettingsModel MonitoringSettings { get; set; } = new();
    public List<ApplicationListEntryModel> WhitelistEntries { get; set; } = SeedWhitelist();
    public List<ApplicationListEntryModel> BlacklistEntries { get; set; } = SeedBlacklist();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private static List<ApplicationListEntryModel> SeedWhitelist() =>
    [
        new() { Id = 1, Application = "chrome.exe", Description = "Google Chrome Browser" },
        new() { Id = 2, Application = "explorer.exe", Description = "Windows Explorer" },
        new() { Id = 3, Application = "winword.exe", Description = "Microsoft Word" }
    ];

    private static List<ApplicationListEntryModel> SeedBlacklist() =>
    [
        new() { Id = 1, Application = "torrent.exe", Description = "Torrent Client" },
        new() { Id = 2, Application = "game.exe", Description = "Gaming Application" }
    ];
}

public sealed class GeneralSettingsModel
{
    public string SystemName { get; set; } = "Activity Monitoring System";
    public string LogLevel { get; set; } = "Info";
    public string MaxLogRetention { get; set; } = "30";
    public string SessionTimeout { get; set; } = "60";
    public bool EnableAuditLog { get; set; } = true;
}

public sealed class SecuritySettingsModel
{
    public string PasswordMinLength { get; set; } = "8";
    public bool PasswordRequireSpecialChars { get; set; } = true;
    public string SessionTimeoutMinutes { get; set; } = "30";
    public string MaxLoginAttempts { get; set; } = "5";
    public string LockoutDurationMinutes { get; set; } = "15";
    public bool EnableTwoFactor { get; set; }
    public string JwtExpirationHours { get; set; } = "24";
}

public sealed class NotificationSettingsModel
{
    public bool EmailNotifications { get; set; } = true;
    public bool SmsNotifications { get; set; }
    public bool PushNotifications { get; set; } = true;
    public string AlertThreshold { get; set; } = "5";
    public string NotificationEmail { get; set; } = "admin@company.com";
    public string SmtpServer { get; set; } = "smtp.company.com";
    public string SmtpPort { get; set; } = "587";
}

public sealed class MonitoringSettingsModel
{
    public string DataRetentionDays { get; set; } = "90";
    public bool RealTimeMonitoring { get; set; } = true;
    public bool AnomalyDetection { get; set; } = true;
    public string MonitoringInterval { get; set; } = "5";
    public bool EnableWhitelist { get; set; } = true;
    public bool EnableBlacklist { get; set; } = true;
}

public sealed class ApplicationListEntryModel
{
    public long Id { get; set; }
    public string Application { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
