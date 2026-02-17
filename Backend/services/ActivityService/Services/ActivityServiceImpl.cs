using Grpc.Core;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using ActivityService.Services.Events;
using ActivityService;
using MassTransit;  // Для IPublishEndpoint
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services
{
    public class ActivityServiceImpl : ActivityService.ActivityServiceBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ActivityServiceImpl> _logger;
        private readonly IPublishEndpoint _publishEndpoint;  // DI для RabbitMQ

        public ActivityServiceImpl(
            AppDbContext db, 
            ILogger<ActivityServiceImpl> logger,
            IPublishEndpoint publishEndpoint)  // Добавьте в ctor
        {
            _db = db;
            _logger = logger;
            _publishEndpoint = publishEndpoint;
        }

        public override async Task<GetActivitiesReply> GetActivities(GetActivitiesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetActivities: computer={ComputerId}, type={Type}", 
                request.ComputerId, request.ActivityType);

            var query = _db.Activities.AsQueryable();

            if (request.ComputerId != 0)
                query = query.Where(a => a.ComputerId == request.ComputerId);

            if (!string.IsNullOrEmpty(request.ActivityType))
                query = query.Where(a => a.ActivityType == request.ActivityType);

            if (request.OnlyBlocked)
                query = query.Where(a => a.IsBlocked);

            if (!string.IsNullOrEmpty(request.FromTimestamp))
            {
                if (DateTime.TryParse(request.FromTimestamp, out var from))
                    query = query.Where(a => a.Timestamp >= from);
            }

            var activities = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(request.Limit != 0 ? request.Limit : 50)
                .ToListAsync(context.CancellationToken);

            var reply = new GetActivitiesReply
            {
                TotalCount = activities.Count
            };
            reply.Activities.AddRange(activities.Select(MapToReply));

            return reply;
        }

        public override async Task<ActivityReply> CreateActivity(CreateActivityRequest request, ServerCallContext context)
        {
            var activity = new Activity
            {
                ComputerId = request.Activity.ComputerId,
                ActivityType = request.Activity.ActivityType,
                Details = request.Activity.Details,
                DurationMs = request.Activity.DurationMs,
                Url = request.Activity.Url,
                ProcessName = request.Activity.ProcessName,
                IsBlocked = request.Activity.IsBlocked,
                RiskScore = (decimal?)request.Activity.RiskScore,
                Synced = request.Activity.Synced
            };

            _db.Activities.Add(activity);
            await _db.SaveChangesAsync(context.CancellationToken);

            // ✅ Publish после сохранения
            await _publishEndpoint.Publish(new ActivityCreatedEvent(
                activity.Id, activity.ComputerId, activity.ActivityType),
                context.CancellationToken);

            _logger.LogInformation("Created activity {Id}", activity.Id);
            return MapToReply(activity);
        }

        private static ActivityReply MapToReply(Activity activity) => new()
        {
            Id = activity.Id,
            ComputerId = activity.ComputerId,
            Timestamp = activity.Timestamp.ToString("O"),
            ActivityType = activity.ActivityType,
            Details = activity.Details,
            DurationMs = activity.DurationMs ?? 0,
            Url = activity.Url,
            ProcessName = activity.ProcessName,
            IsBlocked = activity.IsBlocked,
            RiskScore = (float)(activity.RiskScore ?? 0),
            Synced = activity.Synced
        };
    }
}
