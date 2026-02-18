using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using ActivityService.Services;

namespace ActivityService.Tests
{
    public class AnomalyDetectionServiceTests
    {
        private readonly AppDbContext _dbContext;
        private readonly Mock<ILogger<AnomalyDetectionService>> _loggerMock;
        private readonly AnomalyDetectionService _service;

        public AnomalyDetectionServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemory(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new AppDbContext(options);
            _loggerMock = new Mock<ILogger<AnomalyDetectionService>>();
            _service = new AnomalyDetectionService(_dbContext, _loggerMock.Object);
        }

        [Fact]
        public async Task DetectAnomalies_HighRiskScore_ReturnsHighRiskAnomaly()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "WEB_BROWSING",
                RiskScore = 85m // High risk
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Single(anomalies);
            Assert.Equal("HIGH_RISK", anomalies[0].Type);
            Assert.Contains("high risk score: 85", anomalies[0].Description);
        }

        [Fact]
        public async Task DetectAnomalies_SuspiciousActivityType_ReturnsSuspiciousTypeAnomaly()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "MALWARE"
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Single(anomalies);
            Assert.Equal("SUSPICIOUS_TYPE", anomalies[0].Type);
            Assert.Contains("Suspicious activity type detected: MALWARE", anomalies[0].Description);
        }

        [Fact]
        public async Task DetectAnomalies_BlockedActivity_ReturnsBlockedActivityAnomaly()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "WEB_BROWSING",
                IsBlocked = true
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Single(anomalies);
            Assert.Equal("BLOCKED_ACTIVITY", anomalies[0].Type);
            Assert.Contains("Activity was blocked by security system", anomalies[0].Description);
        }

        [Fact]
        public async Task DetectAnomalies_UnusualDuration_ReturnsUnusualDurationAnomaly()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "WEB_BROWSING",
                DurationMs = 25 * 60 * 60 * 1000 // 25 hours in milliseconds
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Single(anomalies);
            Assert.Equal("UNUSUAL_DURATION", anomalies[0].Type);
            Assert.Contains("unusually long", anomalies[0].Description);
        }

        [Fact]
        public async Task DetectAnomalies_RepeatedActivities_ReturnsRepeatedActivityAnomaly()
        {
            // Arrange
            var computerId = 1;
            var activityType = "WEB_BROWSING";
            
            // Create 10 similar activities in the last hour
            for (int i = 0; i < 10; i++)
            {
                _dbContext.Activities.Add(new Activity
                {
                    ComputerId = computerId,
                    ActivityType = activityType,
                    Timestamp = DateTime.UtcNow.AddMinutes(-30) // 30 minutes ago
                });
            }
            await _dbContext.SaveChangesAsync();

            var newActivity = new Activity
            {
                Id = 11,
                ComputerId = computerId,
                ActivityType = activityType,
                Timestamp = DateTime.UtcNow
            };

            // Act
            var anomalies = await _service.DetectAnomalies(newActivity);

            // Assert
            Assert.Single(anomalies);
            Assert.Equal("REPEATED_ACTIVITY", anomalies[0].Type);
            Assert.Contains("High frequency", anomalies[0].Description);
        }

        [Fact]
        public async Task DetectAnomalies_NoAnomalies_ReturnsEmptyList()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "WEB_BROWSING",
                RiskScore = 10m, // Low risk
                IsBlocked = false,
                DurationMs = 5000 // 5 seconds
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Empty(anomalies);
        }

        [Fact]
        public async Task DetectAnomalies_MultipleAnomalies_ReturnsAllAnomalies()
        {
            // Arrange
            var activity = new Activity
            {
                Id = 1,
                ComputerId = 1,
                ActivityType = "MALWARE", // Suspicious type
                RiskScore = 90m, // High risk
                IsBlocked = true // Blocked
            };

            // Act
            var anomalies = await _service.DetectAnomalies(activity);

            // Assert
            Assert.Equal(3, anomalies.Count);
            Assert.Contains(anomalies, a => a.Type == "SUSPICIOUS_TYPE");
            Assert.Contains(anomalies, a => a.Type == "HIGH_RISK");
            Assert.Contains(anomalies, a => a.Type == "BLOCKED_ACTIVITY");
        }
    }
}