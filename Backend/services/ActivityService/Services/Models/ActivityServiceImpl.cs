using Grpc.Core;
using ActivityService.Services.Data;
using ActivityService.Services.Models;
using ActivityService.Services.Events;
using Microsoft.EntityFrameworkCore;
using Google.Protobuf.Collections;
using System.Globalization;

namespace ActivityService.Services
{
    public class ActivityServiceImpl : ActivityService.ActivityGrpcService.ActivityGrpcServiceBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ActivityServiceImpl> _logger;
        private readonly IAnomalyDetectionService _anomalyDetectionService;

        public ActivityServiceImpl(
            AppDbContext db,
            ILogger<ActivityServiceImpl> logger,
            IAnomalyDetectionService anomalyDetectionService)
        {
            _db = db;
            _logger = logger;
            _anomalyDetectionService = anomalyDetectionService;
        }

        public override async Task<GetActivitiesReply> GetActivities(GetActivitiesRequest request, ServerCallContext context)
        {
            _logger.LogInformation(
                "GetActivities: computer={ComputerId}, type={Type}, from={FromTimestamp}, to={ToTimestamp}, limit={Limit}, blocked={OnlyBlocked}",
                request.ComputerId, request.ActivityType, request.FromTimestamp, request.ToTimestamp, request.Limit, request.OnlyBlocked);

            var query = _db.Activities.AsQueryable();

            if (request.ComputerId != 0)
                query = query.Where(a => a.ComputerId == request.ComputerId);

            if (!string.IsNullOrEmpty(request.ActivityType))
            {
                var normalizedType = NormalizeActivityType(request.ActivityType);
                var rawType = request.ActivityType.Trim();
                query = query.Where(a => a.ActivityType == normalizedType || a.ActivityType == rawType);
            }

            if (request.OnlyBlocked)
                query = query.Where(a => a.IsBlocked);

            if (!string.IsNullOrEmpty(request.FromTimestamp))
            {
                if (TryParseUtcTimestamp(request.FromTimestamp, out var from))
                    query = query.Where(a => a.Timestamp >= from);
                else
                    _logger.LogWarning("Invalid FromTimestamp format: {FromTimestamp}", request.FromTimestamp);
            }

            if (!string.IsNullOrEmpty(request.ToTimestamp))
            {
                if (TryParseUtcTimestamp(request.ToTimestamp, out var to))
                    query = query.Where(a => a.Timestamp <= to);
                else
                    _logger.LogWarning("Invalid ToTimestamp format: {ToTimestamp}", request.ToTimestamp);
            }

            var totalCount = await query.CountAsync(context.CancellationToken);

            var activities = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(request.Limit != 0 ? request.Limit : 50)
                .ToListAsync(context.CancellationToken);

            var reply = new GetActivitiesReply { TotalCount = totalCount };
            reply.Activities.AddRange(activities.Select(MapToReply));

            _logger.LogInformation("Returning {Count} activities out of {Total}", activities.Count, totalCount);
            return reply;
        }

        public override async Task<ActivityReply> CreateActivity(CreateActivityRequest request, ServerCallContext context)
        {
            if (request.Activity == null)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Activity data is required"));

            if (request.Activity.ComputerId <= 0)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "ComputerId must be positive"));

            if (string.IsNullOrWhiteSpace(request.Activity.ActivityType))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "ActivityType is required"));

            if (request.Activity.RiskScore < 0 || request.Activity.RiskScore > 100)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "RiskScore must be between 0 and 100"));

            var activity = new Activity
            {
                ComputerId   = request.Activity.ComputerId,
                ActivityType = NormalizeActivityType(request.Activity.ActivityType),
                Details      = request.Activity.Details,
                DurationMs   = request.Activity.DurationMs,
                Url          = request.Activity.Url,
                ProcessName  = request.Activity.ProcessName,
                IsBlocked    = request.Activity.IsBlocked,
                RiskScore    = (decimal?)request.Activity.RiskScore,
                Synced       = request.Activity.Synced
            };

            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

            _db.Activities.Add(activity);
            await _db.SaveChangesAsync(context.CancellationToken);

            // Detect and create anomalies
            var anomalies = await _anomalyDetectionService.DetectAnomalies(activity);
            if (anomalies.Any())
            {
                _db.Anomalies.AddRange(anomalies);
                await _db.SaveChangesAsync(context.CancellationToken);

                foreach (var anomaly in anomalies)
                    _db.OutboxMessages.Add(OutboxEventEnvelopeFactory.CreateAnomalyDetected(activity, anomaly));
            }

            _db.OutboxMessages.Add(OutboxEventEnvelopeFactory.CreateActivityCreated(activity));
            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);

            _logger.LogInformation("Created activity {Id} for computer {ComputerId} with type {ActivityType}, detected {AnomalyCount} anomalies",
                activity.Id, activity.ComputerId, activity.ActivityType, anomalies.Count);
            return MapToReply(activity);
        }

        public override async Task<ActivityReply> GetActivityById(GetActivityByIdRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetActivityById: id={Id}", request.Id);

            var activity = await _db.Activities
                .FirstOrDefaultAsync(a => a.Id == request.Id, context.CancellationToken);

            if (activity == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Activity with ID {request.Id} not found"));

            return MapToReply(activity);
        }

        public override async Task<GetAnomaliesReply> GetAnomalies(GetAnomaliesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetAnomalies: activityId={ActivityId}, limit={Limit}",
                request.ActivityId, request.Limit);

            var query = _db.Anomalies.AsQueryable();

            if (request.ActivityId != 0)
                query = query.Where(a => a.ActivityId == request.ActivityId);

            var anomalies = await query
                .OrderByDescending(a => a.DetectedAt)
                .Take(request.Limit != 0 ? request.Limit : 50)
                .ToListAsync(context.CancellationToken);

            var reply = new GetAnomaliesReply();
            reply.Anomalies.AddRange(anomalies.Select(MapToReply));
            return reply;
        }

        public override async Task<DeleteActivityReply> DeleteActivity(DeleteActivityRequest request, ServerCallContext context)
        {
            _logger.LogInformation("DeleteActivity: id={Id}", request.Id);

            var activity = await _db.Activities
                .FirstOrDefaultAsync(a => a.Id == request.Id, context.CancellationToken);

            if (activity == null)
                return new DeleteActivityReply { Success = false, Message = $"Activity with ID {request.Id} not found" };

            _db.Activities.Remove(activity);
            await _db.SaveChangesAsync(context.CancellationToken);

            return new DeleteActivityReply { Success = true, Message = $"Activity {request.Id} deleted successfully" };
        }

        public override async Task<ActivityReply> UpdateActivity(UpdateActivityRequest request, ServerCallContext context)
        {
            _logger.LogInformation("UpdateActivity: id={Id}", request.Id);

            if (request.Activity == null)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Activity data is required"));

            var activity = await _db.Activities
                .FirstOrDefaultAsync(a => a.Id == request.Id, context.CancellationToken);

            if (activity == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Activity with ID {request.Id} not found"));

            if (request.Activity.ComputerId > 0)
                activity.ComputerId = request.Activity.ComputerId;

            if (!string.IsNullOrWhiteSpace(request.Activity.ActivityType))
                activity.ActivityType = NormalizeActivityType(request.Activity.ActivityType);

            activity.Details     = request.Activity.Details;
            activity.DurationMs  = request.Activity.DurationMs;
            activity.Url         = request.Activity.Url;
            activity.ProcessName = request.Activity.ProcessName;
            activity.IsBlocked   = request.Activity.IsBlocked;
            activity.RiskScore   = (decimal?)request.Activity.RiskScore;
            activity.Synced      = request.Activity.Synced;

            if (activity.RiskScore < 0 || activity.RiskScore > 100)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "RiskScore must be between 0 and 100"));

            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

            await _db.SaveChangesAsync(context.CancellationToken);

            var anomalies = await _anomalyDetectionService.DetectAnomalies(activity);
            if (anomalies.Any())
            {
                _db.Anomalies.AddRange(anomalies);
                await _db.SaveChangesAsync(context.CancellationToken);

                foreach (var anomaly in anomalies)
                    _db.OutboxMessages.Add(OutboxEventEnvelopeFactory.CreateAnomalyDetected(activity, anomaly));
            }

            _db.OutboxMessages.Add(OutboxEventEnvelopeFactory.CreateActivityCreated(activity));
            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);

            _logger.LogInformation("Updated activity {Id}, detected {AnomalyCount} anomalies",
                activity.Id, anomalies.Count);
            return MapToReply(activity);
        }

        public override async Task<GetActivityStatisticsReply> GetActivityStatistics(GetActivityStatisticsRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetActivityStatistics: computer={ComputerId}, from={FromTimestamp}, to={ToTimestamp}",
                request.ComputerId, request.FromTimestamp, request.ToTimestamp);

            var query = _db.Activities.AsQueryable();

            if (request.ComputerId != 0)
                query = query.Where(a => a.ComputerId == request.ComputerId);

            if (!string.IsNullOrEmpty(request.FromTimestamp) && TryParseUtcTimestamp(request.FromTimestamp, out var from))
                query = query.Where(a => a.Timestamp >= from);

            if (!string.IsNullOrEmpty(request.ToTimestamp) && TryParseUtcTimestamp(request.ToTimestamp, out var to))
                query = query.Where(a => a.Timestamp <= to);

            var totalActivities   = await query.CountAsync(context.CancellationToken);
            var blockedActivities = await query.CountAsync(a => a.IsBlocked, context.CancellationToken);

            var activityIds  = await query.Select(a => a.Id).ToListAsync(context.CancellationToken);
            var anomalyCount = await _db.Anomalies
                .Where(a => activityIds.Contains(a.ActivityId))
                .CountAsync(context.CancellationToken);

            var activityTypeCounts = await query
                .GroupBy(a => a.ActivityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync(context.CancellationToken);

            var typeCountsMap = new MapField<string, int>();
            foreach (var typeCount in activityTypeCounts)
            {
                var normalizedType = NormalizeActivityType(typeCount.Type);
                typeCountsMap[normalizedType] = (typeCountsMap.TryGetValue(normalizedType, out var existing) ? existing : 0)
                    + typeCount.Count;
            }

            var avgRiskScore = (await query
                .Where(a => a.RiskScore.HasValue)
                .AverageAsync(a => (decimal?)a.RiskScore!.Value, context.CancellationToken))
                ?? 0m;

            var reply = new GetActivityStatisticsReply
            {
                TotalActivities   = totalActivities,
                BlockedActivities = blockedActivities,
                AnomalyCount      = anomalyCount,
                AverageRiskScore  = (float)avgRiskScore
            };
            reply.ActivityTypeCounts.Add(typeCountsMap);

            return reply;
        }

        private static bool TryParseUtcTimestamp(string value, out DateTime utcDateTime)
        {
            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                utcDateTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;
            }

            utcDateTime = default;
            return false;
        }

        private static string NormalizeActivityType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN";

            return value
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .ToUpperInvariant();
        }

        private static ActivityReply MapToReply(Activity activity) => new()
        {
            Id           = activity.Id,
            ComputerId   = activity.ComputerId,
            Timestamp    = activity.Timestamp.ToString("O"),
            ActivityType = NormalizeActivityType(activity.ActivityType),
            Details      = activity.Details ?? "",
            DurationMs   = activity.DurationMs ?? 0,
            Url          = activity.Url ?? "",
            ProcessName  = activity.ProcessName ?? "",
            IsBlocked    = activity.IsBlocked,
            RiskScore    = (float)(activity.RiskScore ?? 0),
            Synced       = activity.Synced
        };

        private static AnomalyReply MapToReply(Anomaly anomaly) => new()
        {
            Id          = anomaly.Id,
            ActivityId  = anomaly.ActivityId,
            Type        = anomaly.Type ?? "",
            Description = anomaly.Description ?? "",
            DetectedAt  = anomaly.DetectedAt.ToString("O")
        };
    }
}
