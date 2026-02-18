using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using ActivityService.Services;
using Grpc.Core;
using Grpc.Core.Testing;
using MassTransit;
using Moq;

namespace ActivityService.Tests
{
    public class ActivityServiceImplTests
    {
        private readonly AppDbContext _dbContext;
        private readonly Mock<ILogger<ActivityServiceImpl>> _loggerMock;
        private readonly Mock<IPublishEndpoint> _publishEndpointMock;
        private readonly Mock<IAnomalyDetectionService> _anomalyDetectionMock;
        private readonly ActivityServiceImpl _service;

        public ActivityServiceImplTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemory(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new AppDbContext(options);
            _loggerMock = new Mock<ILogger<ActivityServiceImpl>>();
            _publishEndpointMock = new Mock<IPublishEndpoint>();
            _anomalyDetectionMock = new Mock<IAnomalyDetectionService>();

            _service = new ActivityServiceImpl(
                _dbContext,
                _loggerMock.Object,
                _publishEndpointMock.Object,
                _anomalyDetectionMock.Object);
        }

        [Fact]
        public async Task CreateActivity_ValidActivity_ReturnsActivityReply()
        {
            // Arrange
            var request = new CreateActivityRequest
            {
                Activity = new ActivityReply
                {
                    ComputerId = 1,
                    ActivityType = "WEB_BROWSING",
                    RiskScore = 10.5f,
                    IsBlocked = false,
                    Synced = true
                }
            };

            _anomalyDetectionMock
                .Setup(x => x.DetectAnomalies(It.IsAny<Activity>()))
                .ReturnsAsync(new List<Anomaly>());

            var context = TestServerCallContext.Create(
                method: nameof(ActivityServiceImpl.CreateActivity),
                host: "localhost",
                deadline: DateTime.UtcNow.AddMinutes(1),
                requestHeaders: new Metadata(),
                cancellationToken: CancellationToken.None,
                peer: "ipv4:127.0.0.1",
                authContext: null,
                contextPropagationToken: null,
                writeHeadersFunc: _ => Task.CompletedTask,
                writeOptionsGetter: () => new WriteOptions(),
                writeOptionsSetter: _ => { });

            // Act
            var result = await _service.CreateActivity(request, context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.ComputerId);
            Assert.Equal("WEB_BROWSING", result.ActivityType);
            Assert.Equal(10.5f, result.RiskScore);
            Assert.False(result.IsBlocked);
            Assert.True(result.Synced);

            _publishEndpointMock.Verify(x => x.Publish(
                It.IsAny<ActivityCreatedEvent>(),
                It.IsAny<CancellationToken>()), Times.Once);

            _anomalyDetectionMock.Verify(x => x.DetectAnomalies(It.IsAny<Activity>()), Times.Once);
        }

        [Fact]
        public async Task CreateActivity_InvalidComputerId_ThrowsRpcException()
        {
            // Arrange
            var request = new CreateActivityRequest
            {
                Activity = new ActivityReply
                {
                    ComputerId = -1, // Invalid
                    ActivityType = "WEB_BROWSING"
                }
            };

            var context = TestServerCallContext.Create(
                method: nameof(ActivityServiceImpl.CreateActivity),
                host: "localhost",
                deadline: DateTime.UtcNow.AddMinutes(1),
                requestHeaders: new Metadata(),
                cancellationToken: CancellationToken.None,
                peer: "ipv4:127.0.0.1",
                authContext: null,
                contextPropagationToken: null,
                writeHeadersFunc: _ => Task.CompletedTask,
                writeOptionsGetter: () => new WriteOptions(),
                writeOptionsSetter: _ => { });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RpcException>(
                () => _service.CreateActivity(request, context));

            Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
            Assert.Contains("ComputerId must be positive", exception.Status.Detail);
        }

        [Fact]
        public async Task GetActivityById_ExistingId_ReturnsActivity()
        {
            // Arrange
            var activity = new Activity
            {
                ComputerId = 1,
                ActivityType = "WEB_BROWSING",
                RiskScore = 10.5m
            };
            _dbContext.Activities.Add(activity);
            await _dbContext.SaveChangesAsync();

            var request = new GetActivityByIdRequest { Id = activity.Id };
            var context = TestServerCallContext.Create(
                method: nameof(ActivityServiceImpl.GetActivityById),
                host: "localhost",
                deadline: DateTime.UtcNow.AddMinutes(1),
                requestHeaders: new Metadata(),
                cancellationToken: CancellationToken.None,
                peer: "ipv4:127.0.0.1",
                authContext: null,
                contextPropagationToken: null,
                writeHeadersFunc: _ => Task.CompletedTask,
                writeOptionsGetter: () => new WriteOptions(),
                writeOptionsSetter: _ => { });

            // Act
            var result = await _service.GetActivityById(request, context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(activity.Id, result.Id);
            Assert.Equal(1, result.ComputerId);
            Assert.Equal("WEB_BROWSING", result.ActivityType);
            Assert.Equal(10.5f, result.RiskScore);
        }

        [Fact]
        public async Task GetActivityById_NonExistingId_ThrowsRpcException()
        {
            // Arrange
            var request = new GetActivityByIdRequest { Id = 999 };
            var context = TestServerCallContext.Create(
                method: nameof(ActivityServiceImpl.GetActivityById),
                host: "localhost",
                deadline: DateTime.UtcNow.AddMinutes(1),
                requestHeaders: new Metadata(),
                cancellationToken: CancellationToken.None,
                peer: "ipv4:127.0.0.1",
                authContext: null,
                contextPropagationToken: null,
                writeHeadersFunc: _ => Task.CompletedTask,
                writeOptionsGetter: () => new WriteOptions(),
                writeOptionsSetter: _ => { });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RpcException>(
                () => _service.GetActivityById(request, context));

            Assert.Equal(StatusCode.NotFound, exception.StatusCode);
            Assert.Contains("not found", exception.Status.Detail);
        }
    }
}