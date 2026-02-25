using System.Text.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoAgentCommand = global::AgentManagementService.AgentCommand;
using ProtoAgentPolicy = global::AgentManagementService.AgentPolicy;

namespace AgentManagementService.Services;

public partial class AgentManagementServiceImpl
{
    public override async Task<GetAgentPolicyResponse> GetAgentPolicy(GetAgentPolicyRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get agent policy request for agent ID: {AgentId}", request.AgentId);

        try
        {
            if (request.AgentId <= 0)
            {
                return new GetAgentPolicyResponse { Success = false, Message = "Invalid agent ID" };
            }

            var policy = await GetOrCreatePolicyEntityAsync((int)request.AgentId);
            if (policy is null)
            {
                return new GetAgentPolicyResponse { Success = false, Message = "Agent not found" };
            }

            return new GetAgentPolicyResponse
            {
                Success = true,
                Message = "Agent policy retrieved successfully",
                Policy = MapPolicyToProto(policy)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent policy for agent ID: {AgentId}", request.AgentId);
            return new GetAgentPolicyResponse { Success = false, Message = "An error occurred while retrieving agent policy" };
        }
    }

    public override async Task<UpsertAgentPolicyResponse> UpsertAgentPolicy(UpsertAgentPolicyRequest request, ServerCallContext context)
    {
        var proto = request.Policy;
        _logger.LogInformation("Upsert agent policy request for agent ID: {AgentId}", proto?.AgentId);

        try
        {
            if (proto is null || proto.AgentId <= 0)
            {
                return new UpsertAgentPolicyResponse { Success = false, Message = "AgentId is required in policy" };
            }

            var agent = await _db.Agents.FindAsync((int)proto.AgentId);
            if (agent is null)
            {
                return new UpsertAgentPolicyResponse { Success = false, Message = "Agent not found" };
            }

            var entity = await _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == agent.Id);
            var isNew = entity is null;
            entity ??= new Models.AgentPolicy
            {
                AgentId = agent.Id,
                ComputerId = agent.ComputerId
            };

            ApplyPolicyFromProto(entity, proto, agent.ComputerId);
            entity.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(entity.PolicyVersion))
            {
                entity.PolicyVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            }

            if (isNew)
                _db.AgentPolicies.Add(entity);

            await _db.SaveChangesAsync();
            await SavePolicyVersionSnapshotAsync(entity, isNew ? "create" : "update", "system");

            return new UpsertAgentPolicyResponse
            {
                Success = true,
                Message = isNew ? "Agent policy created successfully" : "Agent policy updated successfully",
                Policy = MapPolicyToProto(entity)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting agent policy for agent ID: {AgentId}", proto?.AgentId);
            return new UpsertAgentPolicyResponse { Success = false, Message = "An error occurred while saving agent policy" };
        }
    }

    public override async Task<DeleteAgentPolicyResponse> DeleteAgentPolicy(DeleteAgentPolicyRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete agent policy request for agent ID: {AgentId}", request.AgentId);

        try
        {
            if (request.AgentId <= 0)
                return new DeleteAgentPolicyResponse { Success = false, Message = "Invalid agent ID" };

            var policy = await _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == request.AgentId);
            if (policy is null)
            {
                var agentExists = await _db.Agents.AnyAsync(a => a.Id == request.AgentId);
                if (!agentExists)
                    return new DeleteAgentPolicyResponse { Success = false, Message = "Agent not found" };

                // Idempotent delete.
                return new DeleteAgentPolicyResponse { Success = true, Message = "Agent policy already deleted" };
            }

            await SavePolicyVersionSnapshotAsync(policy, "delete", "system");
            _db.AgentPolicies.Remove(policy);
            await _db.SaveChangesAsync();

            return new DeleteAgentPolicyResponse
            {
                Success = true,
                Message = "Agent policy deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent policy for agent ID: {AgentId}", request.AgentId);
            return new DeleteAgentPolicyResponse { Success = false, Message = "An error occurred while deleting agent policy" };
        }
    }

    public override async Task<GetPendingAgentCommandsResponse> GetPendingAgentCommands(GetPendingAgentCommandsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get pending agent commands request for agent ID: {AgentId}", request.AgentId);

        try
        {
            var limit = request.Limit > 0 ? Math.Min(request.Limit, 100) : 20;
            var commands = await _db.AgentCommands
                .Where(c => c.AgentId == request.AgentId && c.Status == "pending")
                .OrderBy(c => c.Id)
                .Take(limit)
                .ToListAsync();

            return new GetPendingAgentCommandsResponse
            {
                Success = true,
                Message = "Pending agent commands retrieved successfully",
                Commands = { commands.Select(MapCommandToProto) }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending commands for agent ID: {AgentId}", request.AgentId);
            return new GetPendingAgentCommandsResponse { Success = false, Message = "An error occurred while retrieving pending commands" };
        }
    }

    public override async Task<GetAgentCommandsResponse> GetAgentCommands(GetAgentCommandsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get agent commands request for agent ID: {AgentId}, status: {Status}", request.AgentId, request.Status);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? Math.Min(request.PageSize, 100) : 20;
            var query = _db.AgentCommands.Where(c => c.AgentId == request.AgentId);
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                var status = request.Status.Trim().ToLowerInvariant();
                query = query.Where(c => c.Status == status);
            }

            var totalCount = await query.CountAsync();
            var commands = await query
                .OrderByDescending(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new GetAgentCommandsResponse
            {
                Success = true,
                Message = "Agent commands retrieved successfully",
                Commands = { commands.Select(MapCommandToProto) },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving commands for agent ID: {AgentId}", request.AgentId);
            return new GetAgentCommandsResponse { Success = false, Message = "An error occurred while retrieving commands" };
        }
    }

    public override async Task<CreateAgentCommandResponse> CreateAgentCommand(CreateAgentCommandRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Create agent command request for agent ID: {AgentId}, type: {Type}", request.AgentId, request.Type);

        try
        {
            if (request.AgentId <= 0)
                return new CreateAgentCommandResponse { Success = false, Message = "Invalid agent ID" };

            var agentExists = await _db.Agents.AnyAsync(a => a.Id == request.AgentId);
            if (!agentExists)
                return new CreateAgentCommandResponse { Success = false, Message = "Agent not found" };

            var commandType = NormalizeCommandType(request.Type);
            if (string.IsNullOrWhiteSpace(commandType))
                return new CreateAgentCommandResponse { Success = false, Message = "Command type is required" };

            var payloadJson = NormalizeJsonObjectString(request.PayloadJson);

            var command = new Models.AgentCommand
            {
                AgentId = (int)request.AgentId,
                Type = commandType,
                PayloadJson = payloadJson,
                Status = "pending",
                RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "panel" : request.RequestedBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.AgentCommands.Add(command);
            await _db.SaveChangesAsync();

            return new CreateAgentCommandResponse
            {
                Success = true,
                Message = "Agent command created successfully",
                Command = MapCommandToProto(command)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating command for agent ID: {AgentId}", request.AgentId);
            return new CreateAgentCommandResponse { Success = false, Message = "An error occurred while creating command" };
        }
    }

    public override async Task<AckAgentCommandResponse> AckAgentCommand(AckAgentCommandRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Ack agent command request for command ID: {CommandId}, status: {Status}", request.CommandId, request.Status);

        try
        {
            if (request.CommandId <= 0)
                return new AckAgentCommandResponse { Success = false, Message = "Invalid command ID" };

            var command = await _db.AgentCommands.FindAsync(request.CommandId);
            if (command is null)
                return new AckAgentCommandResponse { Success = false, Message = "Command not found" };

            command.Status = NormalizeCommandStatus(request.Status);
            command.ResultMessage = (request.ResultMessage ?? string.Empty).Trim();
            command.AcknowledgedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return new AckAgentCommandResponse
            {
                Success = true,
                Message = "Agent command acknowledged successfully",
                Command = MapCommandToProto(command)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging command ID: {CommandId}", request.CommandId);
            return new AckAgentCommandResponse { Success = false, Message = "An error occurred while acknowledging command" };
        }
    }

    private async Task<Models.AgentPolicy?> GetOrCreatePolicyEntityAsync(int agentId)
    {
        var existing = await _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == agentId);
        if (existing is not null)
            return existing;

        var agent = await _db.Agents.FindAsync(agentId);
        if (agent is null)
            return null;

        var policy = new Models.AgentPolicy
        {
            AgentId = agent.Id,
            ComputerId = agent.ComputerId,
            PolicyVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            UpdatedAt = DateTime.UtcNow
        };
        _db.AgentPolicies.Add(policy);
        await _db.SaveChangesAsync();
        await SavePolicyVersionSnapshotAsync(policy, "create", "system");
        return policy;
    }

    private static void ApplyPolicyFromProto(Models.AgentPolicy entity, ProtoAgentPolicy proto, int fallbackComputerId)
    {
        entity.AgentId = (int)proto.AgentId;
        entity.ComputerId = proto.ComputerId > 0 ? (int)proto.ComputerId : fallbackComputerId;
        entity.PolicyVersion = string.IsNullOrWhiteSpace(proto.PolicyVersion)
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            : proto.PolicyVersion.Trim();
        entity.CollectionIntervalSec = Clamp(proto.CollectionIntervalSec, 1, 3600, 5);
        entity.HeartbeatIntervalSec = Clamp(proto.HeartbeatIntervalSec, 5, 3600, 15);
        entity.FlushIntervalSec = Clamp(proto.FlushIntervalSec, 1, 3600, 5);
        entity.EnableProcessCollection = proto.EnableProcessCollection;
        entity.EnableBrowserCollection = proto.EnableBrowserCollection;
        entity.EnableActiveWindowCollection = proto.EnableActiveWindowCollection;
        entity.EnableIdleCollection = proto.EnableIdleCollection;
        entity.IdleThresholdSec = Clamp(proto.IdleThresholdSec, 1, 86400, 120);
        entity.BrowserPollIntervalSec = Clamp(proto.BrowserPollIntervalSec, 1, 3600, 10);
        entity.ProcessSnapshotLimit = Clamp(proto.ProcessSnapshotLimit, 1, 500, 50);
        entity.HighRiskThreshold = proto.HighRiskThreshold <= 0 ? 85f : proto.HighRiskThreshold;
        entity.AutoLockEnabled = proto.AutoLockEnabled;
        entity.AdminBlocked = proto.AdminBlocked;
        entity.BlockedReason = string.IsNullOrWhiteSpace(proto.BlockedReason) ? null : proto.BlockedReason.Trim();

        var browsers = proto.Browsers.Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (browsers.Length == 0)
            browsers = ["chrome", "edge", "firefox"];

        entity.BrowsersJson = JsonSerializer.Serialize(browsers);
    }

    private ProtoAgentPolicy MapPolicyToProto(Models.AgentPolicy policy)
    {
        var browsers = ParseBrowsers(policy.BrowsersJson);
        var proto = new ProtoAgentPolicy
        {
            Id = policy.Id,
            AgentId = policy.AgentId,
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
            BlockedReason = policy.BlockedReason ?? string.Empty,
            UpdatedAt = policy.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        proto.Browsers.AddRange(browsers);
        _controlPlaneSigning.ApplyPolicySignature(proto);
        return proto;
    }

    private ProtoAgentCommand MapCommandToProto(Models.AgentCommand command)
    {
        var proto = new ProtoAgentCommand
        {
            Id = command.Id,
            AgentId = command.AgentId,
            Type = command.Type,
            PayloadJson = command.PayloadJson ?? "{}",
            Status = command.Status,
            RequestedBy = command.RequestedBy ?? string.Empty,
            ResultMessage = command.ResultMessage ?? string.Empty,
            CreatedAt = command.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            AcknowledgedAt = command.AcknowledgedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? string.Empty
        };
        _controlPlaneSigning.ApplyCommandSignature(proto);
        return proto;
    }

    private static string[] ParseBrowsers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ["chrome", "edge", "firefox"];
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value);
            return parsed?.Where(b => !string.IsNullOrWhiteSpace(b)).ToArray() is { Length: > 0 } arr
                ? arr
                : ["chrome", "edge", "firefox"];
        }
        catch
        {
            return ["chrome", "edge", "firefox"];
        }
    }

    private static string NormalizeCommandType(string? type)
    {
        return string.IsNullOrWhiteSpace(type)
            ? string.Empty
            : type.Trim().ToUpperInvariant().Replace(' ', '_');
    }

    private static string NormalizeCommandStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "success" : status.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pending" or "running" or "success" or "failed" or "ignored" => normalized,
            _ => "success"
        };
    }

    private static string NormalizeJsonObjectString(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return "{}";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch
        {
            return JsonSerializer.Serialize(new Dictionary<string, string> { ["raw"] = payloadJson });
        }
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value <= 0)
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
