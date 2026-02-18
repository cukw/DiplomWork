using Grpc.Core;
using MetricsService.Data;
using MetricsService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;

namespace MetricsService.Services;

public class MetricsServiceImpl : MetricsService.MetricsServiceBase
{
    private readonly MetricsDbContext _db;
    private readonly ILogger<MetricsServiceImpl> _logger;

    public MetricsServiceImpl(
        MetricsDbContext db,
        ILogger<MetricsServiceImpl> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<CreateMetricResponse> CreateMetric(CreateMetricRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Create metric request for user ID: {UserId}, type: {Type}", request.UserId, request.Type);

        try
        {
            var metric = new Metric
            {
                UserId = (int)request.UserId,
                Type = request.Type,
                Config = request.Config,
                IsActive = true
            };

            _db.Metrics.Add(metric);
            await _db.SaveChangesAsync();

            return new CreateMetricResponse
            {
                Success = true,
                Message = "Metric created successfully",
                Metric = MapMetricToProto(metric)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating metric for user ID: {UserId}", request.UserId);
            return new CreateMetricResponse
            {
                Success = false,
                Message = "An error occurred while creating metric"
            };
        }
    }

    public override async Task<UpdateMetricResponse> UpdateMetric(UpdateMetricRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update metric request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var metric = await _db.Metrics
                .Include(m => m.WhitelistEntries)
                .Include(m => m.BlacklistEntries)
                .FirstOrDefaultAsync(m => m.Id == request.MetricId);

            if (metric == null)
            {
                return new UpdateMetricResponse
                {
                    Success = false,
                    Message = "Metric not found"
                };
            }

            // Update metric properties
            if (!string.IsNullOrEmpty(request.Type))
                metric.Type = request.Type;
            
            if (!string.IsNullOrEmpty(request.Config))
                metric.Config = request.Config;
            
            metric.IsActive = request.IsActive;
            metric.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return new UpdateMetricResponse
            {
                Success = true,
                Message = "Metric updated successfully",
                Metric = MapMetricToProto(metric)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating metric for ID: {MetricId}", request.MetricId);
            return new UpdateMetricResponse
            {
                Success = false,
                Message = "An error occurred while updating metric"
            };
        }
    }

    public override async Task<GetMetricResponse> GetMetric(GetMetricRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get metric request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var metric = await _db.Metrics
                .Include(m => m.WhitelistEntries)
                .Include(m => m.BlacklistEntries)
                .FirstOrDefaultAsync(m => m.Id == request.MetricId);

            if (metric == null)
            {
                return new GetMetricResponse
                {
                    Success = false,
                    Message = "Metric not found"
                };
            }

            return new GetMetricResponse
            {
                Success = true,
                Message = "Metric retrieved successfully",
                Metric = MapMetricToProto(metric)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metric for ID: {MetricId}", request.MetricId);
            return new GetMetricResponse
            {
                Success = false,
                Message = "An error occurred while retrieving metric"
            };
        }
    }

    public override async Task<GetMetricsByUserResponse> GetMetricsByUser(GetMetricsByUserRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get metrics by user request for user ID: {UserId}", request.UserId);

        try
        {
            var metrics = await _db.Metrics
                .Include(m => m.WhitelistEntries)
                .Include(m => m.BlacklistEntries)
                .Where(m => m.UserId == request.UserId)
                .ToListAsync();

            var metricProtos = metrics.Select(MapMetricToProto).ToList();

            return new GetMetricsByUserResponse
            {
                Success = true,
                Message = "Metrics retrieved successfully",
                Metrics = { metricProtos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metrics for user ID: {UserId}", request.UserId);
            return new GetMetricsByUserResponse
            {
                Success = false,
                Message = "An error occurred while retrieving metrics"
            };
        }
    }

    public override async Task<GetAllMetricsResponse> GetAllMetrics(GetAllMetricsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get all metrics request - Page: {Page}, PageSize: {PageSize}", request.Page, request.PageSize);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.Metrics
                .Include(m => m.WhitelistEntries)
                .Include(m => m.BlacklistEntries)
                .AsQueryable();
            
            var totalCount = await query.CountAsync();
            var metrics = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var metricProtos = metrics.Select(MapMetricToProto).ToList();

            return new GetAllMetricsResponse
            {
                Success = true,
                Message = "Metrics retrieved successfully",
                Metrics = { metricProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all metrics");
            return new GetAllMetricsResponse
            {
                Success = false,
                Message = "An error occurred while retrieving metrics"
            };
        }
    }

    public override async Task<DeleteMetricResponse> DeleteMetric(DeleteMetricRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete metric request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var metric = await _db.Metrics
                .Include(m => m.WhitelistEntries)
                .Include(m => m.BlacklistEntries)
                .FirstOrDefaultAsync(m => m.Id == request.MetricId);

            if (metric == null)
            {
                return new DeleteMetricResponse
                {
                    Success = false,
                    Message = "Metric not found"
                };
            }

            // Remove related entries
            _db.WhitelistEntries.RemoveRange(metric.WhitelistEntries);
            _db.BlacklistEntries.RemoveRange(metric.BlacklistEntries);
            
            // Remove metric
            _db.Metrics.Remove(metric);
            await _db.SaveChangesAsync();

            return new DeleteMetricResponse
            {
                Success = true,
                Message = "Metric deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting metric for ID: {MetricId}", request.MetricId);
            return new DeleteMetricResponse
            {
                Success = false,
                Message = "An error occurred while deleting metric"
            };
        }
    }

    public override async Task<AddWhitelistEntryResponse> AddWhitelistEntry(AddWhitelistEntryRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Add whitelist entry request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var entry = new WhitelistEntry
            {
                MetricId = (int)request.MetricId,
                Pattern = request.Pattern,
                Action = string.IsNullOrEmpty(request.Action) ? "allow" : request.Action
            };

            _db.WhitelistEntries.Add(entry);
            await _db.SaveChangesAsync();

            return new AddWhitelistEntryResponse
            {
                Success = true,
                Message = "Whitelist entry added successfully",
                Entry = MapWhitelistEntryToProto(entry)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding whitelist entry for metric ID: {MetricId}", request.MetricId);
            return new AddWhitelistEntryResponse
            {
                Success = false,
                Message = "An error occurred while adding whitelist entry"
            };
        }
    }

    public override async Task<RemoveWhitelistEntryResponse> RemoveWhitelistEntry(RemoveWhitelistEntryRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Remove whitelist entry request for entry ID: {EntryId}", request.EntryId);

        try
        {
            var entry = await _db.WhitelistEntries.FindAsync(request.EntryId);

            if (entry == null)
            {
                return new RemoveWhitelistEntryResponse
                {
                    Success = false,
                    Message = "Whitelist entry not found"
                };
            }

            _db.WhitelistEntries.Remove(entry);
            await _db.SaveChangesAsync();

            return new RemoveWhitelistEntryResponse
            {
                Success = true,
                Message = "Whitelist entry removed successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing whitelist entry for ID: {EntryId}", request.EntryId);
            return new RemoveWhitelistEntryResponse
            {
                Success = false,
                Message = "An error occurred while removing whitelist entry"
            };
        }
    }

    public override async Task<GetWhitelistEntriesResponse> GetWhitelistEntries(GetWhitelistEntriesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get whitelist entries request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var entries = await _db.WhitelistEntries
                .Where(e => e.MetricId == request.MetricId)
                .ToListAsync();

            var entryProtos = entries.Select(MapWhitelistEntryToProto).ToList();

            return new GetWhitelistEntriesResponse
            {
                Success = true,
                Message = "Whitelist entries retrieved successfully",
                Entries = { entryProtos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving whitelist entries for metric ID: {MetricId}", request.MetricId);
            return new GetWhitelistEntriesResponse
            {
                Success = false,
                Message = "An error occurred while retrieving whitelist entries"
            };
        }
    }

    public override async Task<AddBlacklistEntryResponse> AddBlacklistEntry(AddBlacklistEntryRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Add blacklist entry request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var entry = new BlacklistEntry
            {
                MetricId = (int)request.MetricId,
                Pattern = request.Pattern,
                Action = string.IsNullOrEmpty(request.Action) ? "block" : request.Action
            };

            _db.BlacklistEntries.Add(entry);
            await _db.SaveChangesAsync();

            return new AddBlacklistEntryResponse
            {
                Success = true,
                Message = "Blacklist entry added successfully",
                Entry = MapBlacklistEntryToProto(entry)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding blacklist entry for metric ID: {MetricId}", request.MetricId);
            return new AddBlacklistEntryResponse
            {
                Success = false,
                Message = "An error occurred while adding blacklist entry"
            };
        }
    }

    public override async Task<RemoveBlacklistEntryResponse> RemoveBlacklistEntry(RemoveBlacklistEntryRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Remove blacklist entry request for entry ID: {EntryId}", request.EntryId);

        try
        {
            var entry = await _db.BlacklistEntries.FindAsync(request.EntryId);

            if (entry == null)
            {
                return new RemoveBlacklistEntryResponse
                {
                    Success = false,
                    Message = "Blacklist entry not found"
                };
            }

            _db.BlacklistEntries.Remove(entry);
            await _db.SaveChangesAsync();

            return new RemoveBlacklistEntryResponse
            {
                Success = true,
                Message = "Blacklist entry removed successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing blacklist entry for ID: {EntryId}", request.EntryId);
            return new RemoveBlacklistEntryResponse
            {
                Success = false,
                Message = "An error occurred while removing blacklist entry"
            };
        }
    }

    public override async Task<GetBlacklistEntriesResponse> GetBlacklistEntries(GetBlacklistEntriesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get blacklist entries request for metric ID: {MetricId}", request.MetricId);

        try
        {
            var entries = await _db.BlacklistEntries
                .Where(e => e.MetricId == request.MetricId)
                .ToListAsync();

            var entryProtos = entries.Select(MapBlacklistEntryToProto).ToList();

            return new GetBlacklistEntriesResponse
            {
                Success = true,
                Message = "Blacklist entries retrieved successfully",
                Entries = { entryProtos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving blacklist entries for metric ID: {MetricId}", request.MetricId);
            return new GetBlacklistEntriesResponse
            {
                Success = false,
                Message = "An error occurred while retrieving blacklist entries"
            };
        }
    }

    private static Metric MapMetricToProto(MetricsService.Models.Metric metric)
    {
        return new Metric
        {
            Id = metric.Id,
            UserId = metric.UserId ?? 0,
            Type = metric.Type,
            Config = metric.Config,
            IsActive = metric.IsActive,
            UpdatedAt = metric.UpdatedAt.ToString("o")
        };
    }

    private static WhitelistEntry MapWhitelistEntryToProto(MetricsService.Models.WhitelistEntry entry)
    {
        return new WhitelistEntry
        {
            Id = entry.Id,
            MetricId = entry.MetricId,
            Pattern = entry.Pattern,
            Action = entry.Action,
            CreatedAt = entry.CreatedAt.ToString("o")
        };
    }

    private static BlacklistEntry MapBlacklistEntryToProto(MetricsService.Models.BlacklistEntry entry)
    {
        return new BlacklistEntry
        {
            Id = entry.Id,
            MetricId = entry.MetricId,
            Pattern = entry.Pattern,
            Action = entry.Action,
            CreatedAt = entry.CreatedAt.ToString("o")
        };
    }
}