using Grpc.Core;
using AgentManagementService.Data;
using AgentManagementService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UserLookup = AgentManagementService.UserLookup;
using ProtoAgent = global::AgentManagementService.Agent;
using ProtoSyncBatch = global::AgentManagementService.SyncBatch;

namespace AgentManagementService.Services;

public partial class AgentManagementServiceImpl : AgentManagementService.AgentManagementServiceBase
{
    private readonly AgentDbContext _db;
    private readonly ILogger<AgentManagementServiceImpl> _logger;
    private readonly ControlPlaneSigningService _controlPlaneSigning;
    private readonly UserLookup.UserService.UserServiceClient _userServiceClient;
    private readonly CommandDeliveryOptions _commandDeliveryOptions;

    public AgentManagementServiceImpl(
        AgentDbContext db,
        ILogger<AgentManagementServiceImpl> logger,
        ControlPlaneSigningService controlPlaneSigning,
        UserLookup.UserService.UserServiceClient userServiceClient,
        IOptions<CommandDeliveryOptions> commandDeliveryOptions)
    {
        _db = db;
        _logger = logger;
        _controlPlaneSigning = controlPlaneSigning;
        _userServiceClient = userServiceClient;
        _commandDeliveryOptions = commandDeliveryOptions?.Value ?? new CommandDeliveryOptions();
    }

    public override async Task<RegisterAgentResponse> RegisterAgent(RegisterAgentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Register agent request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            if (request.ComputerId <= 0)
            {
                return new RegisterAgentResponse
                {
                    Success = false,
                    Message = "Computer ID must be greater than zero"
                };
            }

            if (request.ComputerId > int.MaxValue)
            {
                return new RegisterAgentResponse
                {
                    Success = false,
                    Message = "Computer ID is out of supported range"
                };
            }

            UserLookup.GetComputerInfoResponse computerInfoResponse;
            try
            {
                computerInfoResponse = await _userServiceClient.GetComputerInfoAsync(
                    new UserLookup.GetComputerInfoRequest { ComputerId = request.ComputerId },
                    cancellationToken: context.CancellationToken);
            }
            catch (RpcException ex)
            {
                _logger.LogWarning(ex, "UserService GetComputerInfo failed for computer ID: {ComputerId}", request.ComputerId);
                return new RegisterAgentResponse
                {
                    Success = false,
                    Message = "Cannot validate computer in UserService"
                };
            }

            if (!computerInfoResponse.Success || computerInfoResponse.Computer == null || computerInfoResponse.Computer.Id <= 0)
            {
                return new RegisterAgentResponse
                {
                    Success = false,
                    Message = $"Computer {request.ComputerId} is not registered in UserService"
                };
            }

            // Check if agent already exists for this computer
            var existingAgent = await _db.Agents
                .FirstOrDefaultAsync(a => a.ComputerId == request.ComputerId);

            if (existingAgent != null)
            {
                TouchRegisteredAgent(existingAgent, request);
                await _db.SaveChangesAsync(context.CancellationToken);

                return new RegisterAgentResponse
                {
                    Success = true,
                    Message = "Agent registration refreshed",
                    Agent = MapAgentToProto(existingAgent)
                };
            }

            var agent = new Models.Agent
            {
                ComputerId = (int)request.ComputerId,
                Version = request.Version,
                Status = "online",
                ConfigVersion = request.ConfigVersion,
                LastHeartbeat = DateTime.UtcNow
            };

            _db.Agents.Add(agent);
            await _db.SaveChangesAsync(context.CancellationToken);

            return new RegisterAgentResponse
            {
                Success = true,
                Message = "Agent registered successfully",
                Agent = MapAgentToProto(agent)
            };
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Concurrent agent registration detected for computer ID: {ComputerId}", request.ComputerId);
            _db.ChangeTracker.Clear();

            var existingAgent = await _db.Agents
                .FirstOrDefaultAsync(a => a.ComputerId == request.ComputerId, context.CancellationToken);

            if (existingAgent is not null)
            {
                TouchRegisteredAgent(existingAgent, request);
                await _db.SaveChangesAsync(context.CancellationToken);

                return new RegisterAgentResponse
                {
                    Success = true,
                    Message = "Agent registration refreshed",
                    Agent = MapAgentToProto(existingAgent)
                };
            }

            return new RegisterAgentResponse
            {
                Success = false,
                Message = "Agent registration conflicted but existing agent was not found"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering agent for computer ID: {ComputerId}", request.ComputerId);
            return new RegisterAgentResponse
            {
                Success = false,
                Message = "An error occurred while registering agent"
            };
        }
    }

    private static void TouchRegisteredAgent(Models.Agent agent, RegisterAgentRequest request)
    {
        agent.Version = string.IsNullOrWhiteSpace(request.Version) ? agent.Version : request.Version.Trim();
        agent.ConfigVersion = string.IsNullOrWhiteSpace(request.ConfigVersion) ? agent.ConfigVersion : request.ConfigVersion.Trim();
        agent.Status = "online";
        agent.LastHeartbeat = DateTime.UtcNow;
        agent.OfflineSince = null;
        agent.LastError = string.Empty;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        HasPostgresSqlState(ex, "23505");

    private static bool HasPostgresSqlState(Exception? ex, string sqlState)
    {
        while (ex is not null)
        {
            if (ex is PostgresException postgresException && postgresException.SqlState == sqlState)
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    public override async Task<UpdateAgentStatusResponse> UpdateAgentStatus(UpdateAgentStatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update agent status request for agent ID: {AgentId}, status: {Status}", request.AgentId, request.Status);

        try
        {
            Models.Agent? agent = await _db.Agents.FindAsync(request.AgentId);

            if (agent == null)
            {
                return new UpdateAgentStatusResponse
                {
                    Success = false,
                    Message = "Agent not found"
                };
            }

            var previousStatus = agent.Status;
            agent.Status = request.Status;
            agent.LastHeartbeat = DateTime.UtcNow;
            
            if (!string.IsNullOrEmpty(request.ConfigVersion))
                agent.ConfigVersion = request.ConfigVersion;

            ApplyHealthSnapshot(agent, request);

            // Update offline timestamp if status changed to offline
            if (previousStatus != "offline" && request.Status == "offline")
            {
                agent.OfflineSince = DateTime.UtcNow;
            }
            else if (previousStatus == "offline" && request.Status != "offline")
            {
                agent.OfflineSince = null;
            }

            await _db.SaveChangesAsync();

            return new UpdateAgentStatusResponse
            {
                Success = true,
                Message = "Agent status updated successfully",
                Agent = MapAgentToProto(agent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent status for ID: {AgentId}", request.AgentId);
            return new UpdateAgentStatusResponse
            {
                Success = false,
                Message = "An error occurred while updating agent status"
            };
        }
    }

    public override async Task<SetAgentDesiredVersionResponse> SetAgentDesiredVersion(SetAgentDesiredVersionRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "Set desired version request for agent ID: {AgentId}, desiredVersion: {DesiredVersion}, enqueue: {Enqueue}",
            request.AgentId,
            request.DesiredVersion,
            request.EnqueueSelfUpdate);

        try
        {
            if (request.AgentId <= 0 || request.AgentId > int.MaxValue)
            {
                return new SetAgentDesiredVersionResponse
                {
                    Success = false,
                    Message = "Invalid agent ID"
                };
            }

            var agent = await _db.Agents.FindAsync((int)request.AgentId);
            if (agent is null)
            {
                return new SetAgentDesiredVersionResponse
                {
                    Success = false,
                    Message = "Agent not found"
                };
            }

            var desiredVersion = string.IsNullOrWhiteSpace(request.DesiredVersion)
                ? string.Empty
                : request.DesiredVersion.Trim();

            if (desiredVersion.Length > 20)
            {
                return new SetAgentDesiredVersionResponse
                {
                    Success = false,
                    Message = "Desired version is too long (max 20 characters)"
                };
            }

            if (string.IsNullOrWhiteSpace(desiredVersion))
            {
                agent.DesiredVersion = null;
                agent.DesiredVersionSetAt = null;
                await _db.SaveChangesAsync(context.CancellationToken);

                return new SetAgentDesiredVersionResponse
                {
                    Success = true,
                    Message = "Desired version cleared",
                    Agent = MapAgentToProto(agent)
                };
            }

            agent.DesiredVersion = desiredVersion;
            agent.DesiredVersionSetAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(context.CancellationToken);

            global::AgentManagementService.AgentCommand? createdCommand = null;
            if (request.EnqueueSelfUpdate)
            {
                var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetVersion = desiredVersion
                });

                var commandResponse = await CreateAgentCommand(new CreateAgentCommandRequest
                {
                    AgentId = request.AgentId,
                    Type = "SELF_UPDATE",
                    PayloadJson = payloadJson,
                    RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "panel" : request.RequestedBy.Trim(),
                    CommandKey = NormalizeCommandKey(request.CommandKey)
                }, context);

                if (!commandResponse.Success)
                {
                    return new SetAgentDesiredVersionResponse
                    {
                        Success = false,
                        Message = $"Desired version saved, but self-update command was not created: {commandResponse.Message}",
                        Agent = MapAgentToProto(agent)
                    };
                }

                createdCommand = commandResponse.Command;
            }

            return new SetAgentDesiredVersionResponse
            {
                Success = true,
                Message = request.EnqueueSelfUpdate
                    ? "Desired version saved and self-update command queued"
                    : "Desired version saved",
                Agent = MapAgentToProto(agent),
                Command = createdCommand
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting desired version for agent ID: {AgentId}", request.AgentId);
            return new SetAgentDesiredVersionResponse
            {
                Success = false,
                Message = "An error occurred while setting desired version"
            };
        }
    }

    public override async Task<GetAgentResponse> GetAgent(GetAgentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get agent request for agent ID: {AgentId}", request.AgentId);

        try
        {
            var agent = await _db.Agents.FindAsync(request.AgentId);

            if (agent == null)
            {
                return new GetAgentResponse
                {
                    Success = false,
                    Message = "Agent not found"
                };
            }

            return new GetAgentResponse
            {
                Success = true,
                Message = "Agent retrieved successfully",
                Agent = MapAgentToProto(agent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent for ID: {AgentId}", request.AgentId);
            return new GetAgentResponse
            {
                Success = false,
                Message = "An error occurred while retrieving agent"
            };
        }
    }

    public override async Task<GetAgentsByComputerResponse> GetAgentsByComputer(GetAgentsByComputerRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get agents by computer request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            var agents = await _db.Agents
                .Where(a => a.ComputerId == request.ComputerId)
                .ToListAsync();

            var agentProtos = agents.Select(a => MapAgentToProto(a)).ToList();

            return new GetAgentsByComputerResponse
            {
                Success = true,
                Message = "Agents retrieved successfully",
                Agents = { agentProtos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agents for computer ID: {ComputerId}", request.ComputerId);
            return new GetAgentsByComputerResponse
            {
                Success = false,
                Message = "An error occurred while retrieving agents"
            };
        }
    }

    public override async Task<GetAllAgentsResponse> GetAllAgents(GetAllAgentsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get all agents request with status: {Status}", request.Status);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.Agents.AsQueryable();
            
            if (!string.IsNullOrEmpty(request.Status))
                query = query.Where(a => a.Status == request.Status);
            
            var totalCount = await query.CountAsync();
            var agents = await query
                .OrderByDescending(a => a.LastHeartbeat)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var agentProtos = agents.Select(a => MapAgentToProto(a)).ToList();

            return new GetAllAgentsResponse
            {
                Success = true,
                Message = "Agents retrieved successfully",
                Agents = { agentProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all agents");
            return new GetAllAgentsResponse
            {
                Success = false,
                Message = "An error occurred while retrieving agents"
            };
        }
    }

    public override async Task<DeleteAgentResponse> DeleteAgent(DeleteAgentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Delete agent request for agent ID: {AgentId}", request.AgentId);

        try
        {
            var agent = await _db.Agents.FindAsync(request.AgentId);

            if (agent == null)
            {
                return new DeleteAgentResponse
                {
                    Success = false,
                    Message = "Agent not found"
                };
            }

            _db.Agents.Remove(agent);
            await _db.SaveChangesAsync();

            return new DeleteAgentResponse
            {
                Success = true,
                Message = "Agent deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent for ID: {AgentId}", request.AgentId);
            return new DeleteAgentResponse
            {
                Success = false,
                Message = "An error occurred while deleting agent"
            };
        }
    }

    public override async Task<CreateSyncBatchResponse> CreateSyncBatch(CreateSyncBatchRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Create sync batch request for agent ID: {AgentId}", request.AgentId);

        try
        {
            var syncBatch = new Models.SyncBatch
            {
                AgentId = (int)request.AgentId,
                BatchId = request.BatchId,
                Status = "pending",
                RecordsCount = request.RecordsCount
            };

            _db.SyncBatches.Add(syncBatch);
            await _db.SaveChangesAsync();

            return new CreateSyncBatchResponse
            {
                Success = true,
                Message = "Sync batch created successfully",
                Batch = MapSyncBatchToProto(syncBatch)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating sync batch for agent ID: {AgentId}", request.AgentId);
            return new CreateSyncBatchResponse
            {
                Success = false,
                Message = "An error occurred while creating sync batch"
            };
        }
    }

    public override async Task<UpdateSyncBatchResponse> UpdateSyncBatch(UpdateSyncBatchRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Update sync batch request for batch ID: {BatchId}", request.BatchId);

        try
        {
            Models.SyncBatch? syncBatch = await _db.SyncBatches.FindAsync(request.BatchId);

            if (syncBatch == null)
            {
                return new UpdateSyncBatchResponse
                {
                    Success = false,
                    Message = "Sync batch not found"
                };
            }

            if (!string.IsNullOrEmpty(request.Status))
                syncBatch.Status = request.Status;
            
            if (request.Status == "success")
                syncBatch.SyncedAt = DateTime.UtcNow;
            
            syncBatch.RecordsCount = request.RecordsCount;

            await _db.SaveChangesAsync();

            return new UpdateSyncBatchResponse
            {
                Success = true,
                Message = "Sync batch updated successfully",
                Batch = MapSyncBatchToProto(syncBatch)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating sync batch for ID: {BatchId}", request.BatchId);
            return new UpdateSyncBatchResponse
            {
                Success = false,
                Message = "An error occurred while updating sync batch"
            };
        }
    }

    public override async Task<GetSyncBatchResponse> GetSyncBatch(GetSyncBatchRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get sync batch request for batch ID: {BatchId}", request.BatchId);

        try
        {
            var syncBatch = await _db.SyncBatches.FindAsync(request.BatchId);

            if (syncBatch == null)
            {
                return new GetSyncBatchResponse
                {
                    Success = false,
                    Message = "Sync batch not found"
                };
            }

            return new GetSyncBatchResponse
            {
                Success = true,
                Message = "Sync batch retrieved successfully",
                Batch = MapSyncBatchToProto(syncBatch)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync batch for ID: {BatchId}", request.BatchId);
            return new GetSyncBatchResponse
            {
                Success = false,
                Message = "An error occurred while retrieving sync batch"
            };
        }
    }

    public override async Task<GetSyncBatchesByAgentResponse> GetSyncBatchesByAgent(GetSyncBatchesByAgentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get sync batches by agent request for agent ID: {AgentId}", request.AgentId);

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.SyncBatches.Where(s => s.AgentId == request.AgentId);
            
            if (!string.IsNullOrEmpty(request.Status))
                query = query.Where(s => s.Status == request.Status);
            
            var totalCount = await query.CountAsync();
            var syncBatches = await query
                .OrderByDescending(s => s.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var batchProtos = syncBatches.Select(sb => MapSyncBatchToProto(sb)).ToList();

            return new GetSyncBatchesByAgentResponse
            {
                Success = true,
                Message = "Sync batches retrieved successfully",
                Batches = { batchProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync batches for agent ID: {AgentId}", request.AgentId);
            return new GetSyncBatchesByAgentResponse
            {
                Success = false,
                Message = "An error occurred while retrieving sync batches"
            };
        }
    }

    public override async Task<GetPendingSyncBatchesResponse> GetPendingSyncBatches(GetPendingSyncBatchesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get pending sync batches request");

        try
        {
            var page = request.Page > 0 ? request.Page : 1;
            var pageSize = request.PageSize > 0 ? request.PageSize : 10;
            
            var query = _db.SyncBatches.Where(s => s.Status == "pending");
            
            var totalCount = await query.CountAsync();
            var syncBatches = await query
                .OrderBy(s => s.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var batchProtos = syncBatches.Select(sb => MapSyncBatchToProto(sb)).ToList();

            return new GetPendingSyncBatchesResponse
            {
                Success = true,
                Message = "Pending sync batches retrieved successfully",
                Batches = { batchProtos },
                TotalCount = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending sync batches");
            return new GetPendingSyncBatchesResponse
            {
                Success = false,
                Message = "An error occurred while retrieving pending sync batches"
            };
        }
    }

    private ProtoAgent MapAgentToProto(Models.Agent agent)
    {
        return new ProtoAgent
        {
            Id = agent.Id,
            ComputerId = agent.ComputerId,
            Version = agent.Version,
            Status = agent.Status,
            LastHeartbeat = agent.LastHeartbeat?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            ConfigVersion = agent.ConfigVersion ?? "",
            OfflineSince = agent.OfflineSince?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            DesiredVersion = agent.DesiredVersion ?? "",
            DesiredVersionSetAt = agent.DesiredVersionSetAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            HealthJson = agent.HealthJson ?? "{}",
            QueueSize = agent.QueueSize,
            LastCollectedAt = agent.LastCollectedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            LastSentAt = agent.LastSentAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            LastError = agent.LastError ?? string.Empty,
            PolicyVersion = agent.PolicyVersion ?? string.Empty,
            CapabilitiesJson = agent.CapabilitiesJson ?? "{}",
            CollectorStatusesJson = agent.CollectorStatusesJson ?? "{}",
            SourcePlatform = agent.SourcePlatform ?? string.Empty
        };
    }

    private static void ApplyHealthSnapshot(Models.Agent agent, UpdateAgentStatusRequest request)
    {
        var hasSnapshot =
            request.QueueSize > 0 ||
            !string.IsNullOrWhiteSpace(request.HealthJson) ||
            !string.IsNullOrWhiteSpace(request.CapabilitiesJson) ||
            !string.IsNullOrWhiteSpace(request.CollectorStatusesJson) ||
            !string.IsNullOrWhiteSpace(request.LastCollectedAt) ||
            !string.IsNullOrWhiteSpace(request.LastSentAt) ||
            !string.IsNullOrWhiteSpace(request.LastError) ||
            !string.IsNullOrWhiteSpace(request.PolicyVersion) ||
            !string.IsNullOrWhiteSpace(request.SourcePlatform);

        if (!hasSnapshot)
            return;

        agent.QueueSize = Math.Max(0, request.QueueSize);
        agent.LastCollectedAt = ParseOptionalUtc(request.LastCollectedAt);
        agent.LastSentAt = ParseOptionalUtc(request.LastSentAt);
        agent.LastError = TrimTo(request.LastError, 500);
        agent.PolicyVersion = TrimToNullable(request.PolicyVersion, 50);
        agent.SourcePlatform = TrimToNullable(request.SourcePlatform, 50);
        agent.HealthJson = NormalizeJsonText(request.HealthJson, "{}");
        agent.CapabilitiesJson = NormalizeJsonText(request.CapabilitiesJson, "{}");
        agent.CollectorStatusesJson = NormalizeJsonText(request.CollectorStatusesJson, "{}");
    }

    private static DateTime? ParseOptionalUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.UtcDateTime : null;
    }

    private static string TrimTo(string? value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TrimToNullable(string? value, int maxLength)
    {
        var trimmed = TrimTo(value, maxLength);
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeJsonText(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static ProtoSyncBatch MapSyncBatchToProto(Models.SyncBatch syncBatch)
    {
        return new ProtoSyncBatch
        {
            Id = syncBatch.Id,
            AgentId = syncBatch.AgentId,
            BatchId = syncBatch.BatchId,
            Status = syncBatch.Status,
            SyncedAt = syncBatch.SyncedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "",
            RecordsCount = syncBatch.RecordsCount
        };
    }
}
