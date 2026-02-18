using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityService.Services
{
    public interface IAnomalyDetectionService
    {
        Task<List<Anomaly>> DetectAnomalies(Activity activity);
    }

    public class AnomalyDetectionService : IAnomalyDetectionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AnomalyDetectionService> _logger;

        public AnomalyDetectionService(AppDbContext db, ILogger<AnomalyDetectionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<Anomaly>> DetectAnomalies(Activity activity)
        {
            var anomalies = new List<Anomaly>();

            // Rule 1: High risk score
            if (activity.RiskScore.HasValue && activity.RiskScore >= 80)
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "HIGH_RISK",
                    Description = $"Activity has high risk score: {activity.RiskScore}",
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Rule 2: Suspicious activity types
            var suspiciousTypes = new[] { "MALWARE", "DATA_EXFILTRATION", "UNAUTHORIZED_ACCESS" };
            if (suspiciousTypes.Contains(activity.ActivityType.ToUpper()))
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "SUSPICIOUS_TYPE",
                    Description = $"Suspicious activity type detected: {activity.ActivityType}",
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Rule 3: Unusual duration (activities longer than 24 hours)
            if (activity.DurationMs.HasValue && activity.DurationMs > 24 * 60 * 60 * 1000)
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "UNUSUAL_DURATION",
                    Description = $"Activity duration is unusually long: {activity.DurationMs}ms",
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Rule 4: Blocked activities
            if (activity.IsBlocked)
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "BLOCKED_ACTIVITY",
                    Description = "Activity was blocked by security system",
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Rule 5: Check for repeated similar activities from same computer
            await CheckForRepeatedActivities(activity, anomalies);

            // Rule 6: Check for suspicious URLs
            await CheckForSuspiciousUrls(activity, anomalies);

            // Rule 7: Check for unusual time patterns
            await CheckForUnusualTimePatterns(activity, anomalies);

            // Rule 8: Check for high-risk processes
            await CheckForHighRiskProcesses(activity, anomalies);

            // Rule 9: Check for data access patterns
            await CheckForDataAccessPatterns(activity, anomalies);

            // Rule 10: Check for network anomalies
            await CheckForNetworkAnomalies(activity, anomalies);

            if (anomalies.Any())
            {
                _logger.LogWarning("Detected {Count} anomalies for activity {Id}", anomalies.Count, activity.Id);
            }

            return anomalies;
        }

        private async Task CheckForRepeatedActivities(Activity activity, List<Anomaly> anomalies)
        {
            // Check for more than 10 similar activities in the last hour from the same computer
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var similarActivities = await _db.Activities
                .Where(a => a.ComputerId == activity.ComputerId &&
                           a.ActivityType == activity.ActivityType &&
                           a.Timestamp >= oneHourAgo &&
                           a.Id != activity.Id)
                .CountAsync();

            if (similarActivities >= 10)
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "REPEATED_ACTIVITY",
                    Description = $"High frequency of {activity.ActivityType} activities detected: {similarActivities + 1} in the last hour",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }

    private async Task CheckForSuspiciousUrls(Activity activity, List<Anomaly> anomalies)
    {
        if (string.IsNullOrEmpty(activity.Url))
            return;

        var suspiciousDomains = new[] {
            "malware.com", "phishing.site", "suspicious.net", "hacktool.org",
            "darkweb.onion", "illegal.download", "crypto-miner.net"
        };

        try
        {
            var uri = new Uri(activity.Url);
            var domain = uri.Host.ToLower();

            if (suspiciousDomains.Any(suspicious => domain.Contains(suspicious)))
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "SUSPICIOUS_URL",
                    Description = $"Access to suspicious URL detected: {activity.Url}",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing URL: {Url}", activity.Url);
        }
    }

    private async Task CheckForUnusualTimePatterns(Activity activity, List<Anomaly> anomalies)
    {
        // Check for activities outside normal working hours (9 AM - 6 PM)
        var hour = activity.Timestamp.Hour;
        if (hour < 9 || hour > 18)
        {
            // Check if this computer has activity during normal hours
            var hasNormalHoursActivity = await _db.Activities
                .Where(a => a.ComputerId == activity.ComputerId &&
                           a.Timestamp.Date == activity.Timestamp.Date &&
                           a.Timestamp.Hour >= 9 && a.Timestamp.Hour <= 18)
                .AnyAsync();

            if (hasNormalHoursActivity)
            {
                anomalies.Add(new Anomaly
                {
                    ActivityId = activity.Id,
                    Type = "UNUSUAL_TIME",
                    Description = $"Activity detected outside normal working hours: {activity.Timestamp:HH:mm}",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }
    }

    private async Task CheckForHighRiskProcesses(Activity activity, List<Anomaly> anomalies)
    {
        if (string.IsNullOrEmpty(activity.ProcessName))
            return;

        var highRiskProcesses = new[] {
            "hacktool.exe", "keylogger.exe", "malware.exe", "cryptominer.exe",
            "trojan.exe", "backdoor.exe", "rootkit.exe", "spyware.exe",
            "ransomware.exe", "worm.exe", "virus.exe", "botnet.exe"
        };

        if (highRiskProcesses.Contains(activity.ProcessName.ToLower()))
        {
            anomalies.Add(new Anomaly
            {
                ActivityId = activity.Id,
                Type = "HIGH_RISK_PROCESS",
                Description = $"High-risk process detected: {activity.ProcessName}",
                DetectedAt = DateTime.UtcNow
            });
        }
    }

    private async Task CheckForDataAccessPatterns(Activity activity, List<Anomaly> anomalies)
    {
        if (activity.ActivityType != "FILE_ACCESS" || string.IsNullOrEmpty(activity.Details))
            return;

        try
        {
            // Parse details to extract file path
            var details = System.Text.Json.JsonDocument.Parse(activity.Details);
            if (details.RootElement.TryGetProperty("filePath", out var filePathElement))
            {
                var filePath = filePathElement.GetString()?.ToLower() ?? "";
                
                // Check for access to sensitive files
                var sensitivePaths = new[] {
                    "password", "credential", "secret", "key", "certificate",
                    "private", "confidential", "classified", "restricted"
                };

                if (sensitivePaths.Any(sensitive => filePath.Contains(sensitive)))
                {
                    anomalies.Add(new Anomaly
                    {
                        ActivityId = activity.Id,
                        Type = "SENSITIVE_FILE_ACCESS",
                        Description = $"Access to sensitive file detected: {filePath}",
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing activity details for file access pattern check");
        }
    }

    private async Task CheckForNetworkAnomalies(Activity activity, List<Anomaly> anomalies)
    {
        if (activity.ActivityType != "NETWORK_ACCESS")
            return;

        // Check for multiple network connections in a short time
        var fiveMinutesAgo = activity.Timestamp.AddMinutes(-5);
        var recentNetworkActivities = await _db.Activities
            .Where(a => a.ComputerId == activity.ComputerId &&
                       a.ActivityType == "NETWORK_ACCESS" &&
                       a.Timestamp >= fiveMinutesAgo &&
                       a.Id != activity.Id)
            .CountAsync();

        if (recentNetworkActivities >= 20) // More than 20 network connections in 5 minutes
        {
            anomalies.Add(new Anomaly
            {
                ActivityId = activity.Id,
                Type = "EXCESSIVE_NETWORK_ACTIVITY",
                Description = $"Excessive network activity detected: {recentNetworkActivities + 1} connections in 5 minutes",
                DetectedAt = DateTime.UtcNow
            });
        }
    }
}
}