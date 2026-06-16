using System.Text.Json;
using System.Globalization;
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

            var previousPolicyVersion = entity.PolicyVersion;
            ApplyPolicyFromProto(entity, proto, agent.ComputerId);
            if (string.IsNullOrWhiteSpace(proto.PolicyVersion) ||
                string.Equals(entity.PolicyVersion, previousPolicyVersion, StringComparison.Ordinal))
            {
                entity.PolicyVersion = NewPolicyVersion(previousPolicyVersion);
            }
            entity.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(entity.PolicyVersion))
            {
                entity.PolicyVersion = NewPolicyVersion();
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
            if (request.AgentId <= 0 || request.AgentId > int.MaxValue)
                return new GetPendingAgentCommandsResponse { Success = false, Message = "Invalid agent ID" };

            var limit = request.Limit > 0 ? Math.Min(request.Limit, 100) : 20;
            var now = DateTime.UtcNow;
            var dispatchTimeoutSeconds = Math.Clamp(_commandDeliveryOptions.DispatchTimeoutSeconds, 5, 3600);
            var agentId = (int)request.AgentId;

            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);
            var commands = await _db.AgentCommands
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM agent_commands
                    WHERE agent_id = {agentId}
                      AND status = 'pending'
                      AND (next_retry_at IS NULL OR next_retry_at <= NOW())
                    ORDER BY id
                    LIMIT {limit}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(context.CancellationToken);

            foreach (var command in commands)
            {
                command.DeliveryAttempts = Math.Max(0, command.DeliveryAttempts) + 1;
                command.Status = "running";
                command.LastDispatchAt = now;
                command.TimeoutAt = now.AddSeconds(dispatchTimeoutSeconds);
                command.NextRetryAt = null;
                command.ResultMessage = string.Empty;
            }

            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);

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
        _logger.LogInformation(
            "Get agent commands request for agent ID: {AgentId}, status: {Status}, type: {Type}, from: {From}, to: {To}",
            request.AgentId,
            request.Status,
            request.Type,
            request.CreatedFrom,
            request.CreatedTo);

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
            if (!string.IsNullOrWhiteSpace(request.Type))
            {
                var commandType = NormalizeCommandType(request.Type);
                query = query.Where(c => c.Type == commandType);
            }

            if (!TryParseUtcDateTime(request.CreatedFrom, out var fromUtc))
            {
                return new GetAgentCommandsResponse { Success = false, Message = "Invalid created_from value. Expected ISO-8601 date/time." };
            }
            if (!TryParseUtcDateTime(request.CreatedTo, out var toUtc))
            {
                return new GetAgentCommandsResponse { Success = false, Message = "Invalid created_to value. Expected ISO-8601 date/time." };
            }
            if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
            {
                return new GetAgentCommandsResponse { Success = false, Message = "created_from must be less than or equal to created_to" };
            }
            if (fromUtc is not null)
            {
                query = query.Where(c => c.CreatedAt >= fromUtc.Value);
            }
            if (toUtc is not null)
            {
                query = query.Where(c => c.CreatedAt <= toUtc.Value);
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
            if (request.AgentId <= 0 || request.AgentId > int.MaxValue)
                return new CreateAgentCommandResponse { Success = false, Message = "Invalid agent ID" };

            var agentId = (int)request.AgentId;
            var agentExists = await _db.Agents.AnyAsync(a => a.Id == agentId);
            if (!agentExists)
                return new CreateAgentCommandResponse { Success = false, Message = "Agent not found" };

            var commandType = NormalizeCommandType(request.Type);
            if (string.IsNullOrWhiteSpace(commandType))
                return new CreateAgentCommandResponse { Success = false, Message = "Command type is required" };

            var payloadJson = NormalizeJsonObjectString(request.PayloadJson);
            var normalizedCommandKey = NormalizeCommandKey(request.CommandKey);
            if (string.IsNullOrWhiteSpace(normalizedCommandKey))
                normalizedCommandKey = $"cmd-{Guid.NewGuid():N}";

            var existing = await _db.AgentCommands
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(c => c.AgentId == agentId && c.CommandKey == normalizedCommandKey);

            if (existing is not null)
            {
                return new CreateAgentCommandResponse
                {
                    Success = true,
                    Message = "Agent command already exists (idempotent replay)",
                    Command = MapCommandToProto(existing)
                };
            }

            var command = new Models.AgentCommand
            {
                AgentId = agentId,
                CommandKey = normalizedCommandKey,
                Type = commandType,
                PayloadJson = payloadJson,
                Status = "pending",
                RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "panel" : request.RequestedBy.Trim(),
                CreatedAt = DateTime.UtcNow,
                DeliveryAttempts = 0,
                MaxDeliveryAttempts = Math.Max(1, _commandDeliveryOptions.MaxDeliveryAttempts),
                NextRetryAt = null
            };

            _db.AgentCommands.Add(command);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                var replay = await _db.AgentCommands
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefaultAsync(c => c.AgentId == agentId && c.CommandKey == normalizedCommandKey);

                if (replay is not null)
                {
                    return new CreateAgentCommandResponse
                    {
                        Success = true,
                        Message = "Agent command already exists (idempotent replay)",
                        Command = MapCommandToProto(replay)
                    };
                }

                throw;
            }

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

            var normalizedStatus = NormalizeCommandStatus(request.Status);
            var now = DateTime.UtcNow;

            command.Status = normalizedStatus;
            command.ResultMessage = (request.ResultMessage ?? string.Empty).Trim();
            command.TimeoutAt = null;
            command.NextRetryAt = null;

            if (normalizedStatus is "success" or "failed" or "ignored" or "deadletter" or "timeout")
            {
                command.AcknowledgedAt = now;
            }

            if (normalizedStatus is "deadletter" or "timeout")
            {
                command.DeadLetterReason = string.IsNullOrWhiteSpace(command.ResultMessage)
                    ? $"Command marked as {normalizedStatus}"
                    : command.ResultMessage;
                await PersistDeadLetterAsync(command, now, context.CancellationToken);
            }

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

    public override async Task<RetryAgentCommandResponse> RetryAgentCommand(RetryAgentCommandRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Retry agent command request for agent ID: {AgentId}, command ID: {CommandId}", request.AgentId, request.CommandId);

        try
        {
            if (request.AgentId <= 0 || request.AgentId > int.MaxValue)
                return new RetryAgentCommandResponse { Success = false, Message = "Invalid agent ID" };

            if (request.CommandId <= 0 || request.CommandId > int.MaxValue)
                return new RetryAgentCommandResponse { Success = false, Message = "Invalid command ID" };

            var agentId = (int)request.AgentId;
            var commandId = (int)request.CommandId;

            var sourceCommand = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == commandId);
            if (sourceCommand is null)
                return new RetryAgentCommandResponse { Success = false, Message = "Command not found" };

            if (sourceCommand.AgentId != agentId)
                return new RetryAgentCommandResponse { Success = false, Message = "Command does not belong to specified agent" };

            var sourceStatus = NormalizeCommandStatus(sourceCommand.Status);
            if (sourceStatus is not ("deadletter" or "timeout" or "failed"))
            {
                return new RetryAgentCommandResponse
                {
                    Success = false,
                    Message = $"Command with status '{sourceStatus}' cannot be retried"
                };
            }

            var retryCommandType = NormalizeCommandType(sourceCommand.Type);
            if (string.IsNullOrWhiteSpace(retryCommandType))
                return new RetryAgentCommandResponse { Success = false, Message = "Source command type is invalid" };

            var retryCommand = new Models.AgentCommand
            {
                AgentId = sourceCommand.AgentId,
                CommandKey = NormalizeCommandKey($"retry-{sourceCommand.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"),
                Type = retryCommandType,
                PayloadJson = NormalizeJsonObjectString(sourceCommand.PayloadJson),
                Status = "pending",
                RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "panel" : request.RequestedBy.Trim(),
                ResultMessage = $"Retry of command {sourceCommand.Id}",
                CreatedAt = DateTime.UtcNow,
                DeliveryAttempts = 0,
                MaxDeliveryAttempts = sourceCommand.MaxDeliveryAttempts > 0
                    ? sourceCommand.MaxDeliveryAttempts
                    : Math.Max(1, _commandDeliveryOptions.MaxDeliveryAttempts),
                LastDispatchAt = null,
                NextRetryAt = null,
                TimeoutAt = null,
                AcknowledgedAt = null,
                DeadLetterReason = string.Empty
            };

            _db.AgentCommands.Add(retryCommand);
            await _db.SaveChangesAsync();

            return new RetryAgentCommandResponse
            {
                Success = true,
                Message = "Retry command created successfully",
                Command = MapCommandToProto(retryCommand)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying command ID: {CommandId} for agent ID: {AgentId}", request.CommandId, request.AgentId);
            return new RetryAgentCommandResponse { Success = false, Message = "An error occurred while retrying command" };
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
            PolicyVersion = NewPolicyVersion(),
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
        entity.EnableWhitelist = proto.EnableWhitelist;
        entity.EnableBlacklist = proto.EnableBlacklist;

        var browsers = proto.Browsers.Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (browsers.Length == 0)
            browsers = ["chrome", "edge", "firefox"];

        entity.BrowsersJson = JsonSerializer.Serialize(browsers);
        entity.WhitelistJson = JsonSerializer.Serialize(NormalizeAppList(proto.WhitelistApps));
        entity.BlacklistJson = JsonSerializer.Serialize(NormalizeAppList(proto.BlacklistApps));
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
            EnableWhitelist = policy.EnableWhitelist,
            EnableBlacklist = policy.EnableBlacklist,
            BlockedReason = policy.BlockedReason ?? string.Empty,
            UpdatedAt = policy.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        proto.Browsers.AddRange(browsers);
        proto.WhitelistApps.AddRange(ParseAppList(policy.WhitelistJson));
        proto.BlacklistApps.AddRange(ParseAppList(policy.BlacklistJson));
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
            CreatedAt = FormatUtc(command.CreatedAt),
            AcknowledgedAt = FormatUtc(command.AcknowledgedAt),
            CommandKey = command.CommandKey ?? string.Empty,
            DeliveryAttempts = command.DeliveryAttempts,
            MaxDeliveryAttempts = command.MaxDeliveryAttempts,
            LastDispatchAt = FormatUtc(command.LastDispatchAt),
            NextRetryAt = FormatUtc(command.NextRetryAt),
            TimeoutAt = FormatUtc(command.TimeoutAt),
            DeadLetterReason = command.DeadLetterReason ?? string.Empty
        };
        _controlPlaneSigning.ApplyCommandSignature(proto);
        return proto;
    }

    private async Task PersistDeadLetterAsync(Models.AgentCommand command, DateTime failedAtUtc, CancellationToken cancellationToken)
    {
        var alreadyExists = await _db.AgentCommandDeadLetters
            .AnyAsync(x => x.AgentCommandId == command.Id, cancellationToken);

        if (alreadyExists)
            return;

        _db.AgentCommandDeadLetters.Add(new Models.AgentCommandDeadLetter
        {
            AgentCommandId = command.Id,
            AgentId = command.AgentId,
            CommandKey = command.CommandKey ?? string.Empty,
            Type = command.Type ?? string.Empty,
            PayloadJson = command.PayloadJson ?? "{}",
            Reason = command.DeadLetterReason ?? string.Empty,
            DeliveryAttempts = command.DeliveryAttempts,
            FailedAt = failedAtUtc
        });
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

    private static string[] ParseAppList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value);
            return NormalizeAppList(parsed);
        }
        catch
        {
            return [];
        }
    }

    private static string[] NormalizeAppList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeCommandType(string? type)
    {
        return string.IsNullOrWhiteSpace(type)
            ? string.Empty
            : type.Trim().ToUpperInvariant().Replace(' ', '_');
    }

    private static bool TryParseUtcDateTime(string? raw, out DateTime? parsedUtc)
    {
        parsedUtc = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (!DateTimeOffset.TryParse(raw, out var dto))
            return false;

        parsedUtc = dto.UtcDateTime;
        return true;
    }

    private static string NormalizeCommandStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "success" : status.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pending" or "running" or "success" or "failed" or "ignored" or "deadletter" or "timeout" => normalized,
            _ => "success"
        };
    }

    private static string NormalizeCommandKey(string? commandKey)
    {
        if (string.IsNullOrWhiteSpace(commandKey))
            return string.Empty;

        var normalized = commandKey.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static string FormatUtc(DateTime? value)
    {
        return value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? string.Empty;
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

    private static string NewPolicyVersion(string? previousVersion = null)
    {
        var candidate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(candidate, previousVersion, StringComparison.Ordinal))
            return candidate;

        var fallback = $"{candidate}-{Guid.NewGuid():N}";
        return fallback.Length <= 50 ? fallback : fallback[..50];
    }
}
