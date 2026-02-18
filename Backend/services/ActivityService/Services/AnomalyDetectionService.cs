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
    }
}