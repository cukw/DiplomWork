using System.Text.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoAgentPolicy = global::AgentManagementService.AgentPolicy;
using ProtoAgentPolicyVersion = global::AgentManagementService.AgentPolicyVersion;

namespace AgentManagementService.Services;

public partial class AgentManagementServiceImpl
{
    private static readonly JsonSerializerOptions PolicySnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public override async Task<GetAgentPolicyVersionsResponse> GetAgentPolicyVersions(GetAgentPolicyVersionsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get agent policy versions request for agent ID: {AgentId}", request.AgentId);

        try
        {
            if (request.AgentId <= 0)
            {
                return new GetAgentPolicyVersionsResponse { Success = false, Message = "Invalid agent ID" };
            }

            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? Math.Min(request.PageSize, 100) : 20;

            var query = _db.AgentPolicyVersions.Where(v => v.AgentId == request.AgentId);
            var totalCount = await query.CountAsync();
            var versions = await query
                .OrderByDescending(v => v.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new GetAgentPolicyVersionsResponse
            {
                Success = true,
                Message = "Agent policy versions retrieved successfully",
                Versions = { versions.Select(MapPolicyVersionToProto) },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving policy versions for agent ID: {AgentId}", request.AgentId);
            return new GetAgentPolicyVersionsResponse { Success = false, Message = "An error occurred while retrieving policy versions" };
        }
    }

    public override async Task<RestoreAgentPolicyVersionResponse> RestoreAgentPolicyVersion(RestoreAgentPolicyVersionRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Restore agent policy version request: agent ID {AgentId}, version row ID {VersionId}", request.AgentId, request.VersionId);

        try
        {
            if (request.AgentId <= 0 || request.VersionId <= 0)
            {
                return new RestoreAgentPolicyVersionResponse { Success = false, Message = "Invalid agent ID or version ID" };
            }

            var agent = await _db.Agents.FindAsync((int)request.AgentId);
            if (agent is null)
            {
                return new RestoreAgentPolicyVersionResponse { Success = false, Message = "Agent not found" };
            }

            var versionRow = await _db.AgentPolicyVersions.FirstOrDefaultAsync(v => v.Id == request.VersionId && v.AgentId == request.AgentId);
            if (versionRow is null)
            {
                return new RestoreAgentPolicyVersionResponse { Success = false, Message = "Policy version not found" };
            }

            var snapshot = DeserializePolicySnapshot(versionRow.SnapshotJson);
            if (snapshot is null)
            {
                return new RestoreAgentPolicyVersionResponse { Success = false, Message = "Policy snapshot is corrupted" };
            }

            var entity = await _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == request.AgentId);
            var isNew = entity is null;
            entity ??= new Models.AgentPolicy
            {
                AgentId = (int)request.AgentId,
                ComputerId = agent.ComputerId
            };

            var proto = SnapshotToProto(snapshot, (int)request.AgentId, agent.ComputerId);
            // Rollback creates a new policy version identifier to preserve monotonic history.
            proto.PolicyVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            ApplyPolicyFromProto(entity, proto, agent.ComputerId);
            entity.UpdatedAt = DateTime.UtcNow;

            if (isNew)
                _db.AgentPolicies.Add(entity);

            await _db.SaveChangesAsync();

            await SavePolicyVersionSnapshotAsync(
                entity,
                "rollback",
                string.IsNullOrWhiteSpace(request.RequestedBy) ? "panel" : request.RequestedBy.Trim());

            return new RestoreAgentPolicyVersionResponse
            {
                Success = true,
                Message = "Agent policy restored successfully",
                Policy = MapPolicyToProto(entity),
                RestoredFrom = MapPolicyVersionToProto(versionRow)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring policy version {VersionId} for agent {AgentId}", request.VersionId, request.AgentId);
            return new RestoreAgentPolicyVersionResponse { Success = false, Message = "An error occurred while restoring policy version" };
        }
    }

    private async Task SavePolicyVersionSnapshotAsync(Models.AgentPolicy policy, string changeType, string? changedBy)
    {
        var snapshot = CreatePolicySnapshot(policy);
        var row = new Models.AgentPolicyVersion
        {
            AgentId = policy.AgentId,
            PolicyVersion = string.IsNullOrWhiteSpace(policy.PolicyVersion) ? "1" : policy.PolicyVersion,
            ChangeType = NormalizePolicyChangeType(changeType),
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "system" : changedBy.Trim(),
            SnapshotJson = JsonSerializer.Serialize(snapshot, PolicySnapshotJsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentPolicyVersions.Add(row);
        await _db.SaveChangesAsync();
    }

    private static ProtoAgentPolicyVersion MapPolicyVersionToProto(Models.AgentPolicyVersion row)
    {
        return new ProtoAgentPolicyVersion
        {
            Id = row.Id,
            AgentId = row.AgentId,
            PolicyVersion = row.PolicyVersion ?? string.Empty,
            ChangeType = row.ChangeType ?? string.Empty,
            ChangedBy = row.ChangedBy ?? string.Empty,
            CreatedAt = row.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            SnapshotJson = row.SnapshotJson ?? "{}"
        };
    }

    private static string NormalizePolicyChangeType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "update" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "create" or "update" or "delete" or "rollback" => normalized,
            _ => "update"
        };
    }

    private static AgentPolicySnapshot CreatePolicySnapshot(Models.AgentPolicy policy)
    {
        return new AgentPolicySnapshot
        {
            ComputerId = policy.ComputerId,
            PolicyVersion = policy.PolicyVersion,
            CollectionIntervalSec = policy.CollectionIntervalSec,
            HeartbeatIntervalSec = policy.HeartbeatIntervalSec,
            FlushIntervalSec = policy.FlushIntervalSec,
            EnableProcessCollection = policy.EnableProcessCollection,
            EnableBrowserCollection = policy.EnableBrowserCollection,
            EnableActiveWindowCollection = policy.EnableActiveWindowCollection,
            EnableIdleCollection = policy.EnableIdleCollection,
            IdleThresholdSec = policy.IdleThresholdSec,
            BrowserPollIntervalSec = policy.BrowserPollIntervalSec,
            ProcessSnapshotLimit = policy.ProcessSnapshotLimit,
            HighRiskThreshold = policy.HighRiskThreshold,
            AutoLockEnabled = policy.AutoLockEnabled,
            AdminBlocked = policy.AdminBlocked,
            BlockedReason = policy.BlockedReason,
            Browsers = ParseBrowsers(policy.BrowsersJson).ToList()
        };
    }

    private static AgentPolicySnapshot? DeserializePolicySnapshot(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentPolicySnapshot>(json ?? string.Empty, PolicySnapshotJsonOptions);
            if (parsed is null)
                return null;

            parsed.Browsers ??= ["chrome", "edge", "firefox"];
            if (parsed.Browsers.Count == 0)
                parsed.Browsers = ["chrome", "edge", "firefox"];
            return parsed;
        }
        catch
        {
            return null;
        }
    }

    private static ProtoAgentPolicy SnapshotToProto(AgentPolicySnapshot snapshot, int agentId, int fallbackComputerId)
    {
        var proto = new ProtoAgentPolicy
        {
            AgentId = agentId,
            ComputerId = snapshot.ComputerId > 0 ? snapshot.ComputerId : fallbackComputerId,
            PolicyVersion = string.IsNullOrWhiteSpace(snapshot.PolicyVersion)
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                : snapshot.PolicyVersion,
            CollectionIntervalSec = snapshot.CollectionIntervalSec,
            HeartbeatIntervalSec = snapshot.HeartbeatIntervalSec,
            FlushIntervalSec = snapshot.FlushIntervalSec,
            EnableProcessCollection = snapshot.EnableProcessCollection,
            EnableBrowserCollection = snapshot.EnableBrowserCollection,
            EnableActiveWindowCollection = snapshot.EnableActiveWindowCollection,
            EnableIdleCollection = snapshot.EnableIdleCollection,
            IdleThresholdSec = snapshot.IdleThresholdSec,
            BrowserPollIntervalSec = snapshot.BrowserPollIntervalSec,
            ProcessSnapshotLimit = snapshot.ProcessSnapshotLimit,
            HighRiskThreshold = snapshot.HighRiskThreshold,
            AutoLockEnabled = snapshot.AutoLockEnabled,
            AdminBlocked = snapshot.AdminBlocked,
            BlockedReason = snapshot.BlockedReason ?? string.Empty
        };
        proto.Browsers.AddRange((snapshot.Browsers ?? []).Where(x => !string.IsNullOrWhiteSpace(x)));
        return proto;
    }

    private sealed class AgentPolicySnapshot
    {
        public int ComputerId { get; set; }
        public string? PolicyVersion { get; set; }
        public int CollectionIntervalSec { get; set; }
        public int HeartbeatIntervalSec { get; set; }
        public int FlushIntervalSec { get; set; }
        public bool EnableProcessCollection { get; set; }
        public bool EnableBrowserCollection { get; set; }
        public bool EnableActiveWindowCollection { get; set; }
        public bool EnableIdleCollection { get; set; }
        public int IdleThresholdSec { get; set; }
        public int BrowserPollIntervalSec { get; set; }
        public int ProcessSnapshotLimit { get; set; }
        public float HighRiskThreshold { get; set; }
        public bool AutoLockEnabled { get; set; }
        public bool AdminBlocked { get; set; }
        public string? BlockedReason { get; set; }
        public List<string> Browsers { get; set; } = [];
    }
}
