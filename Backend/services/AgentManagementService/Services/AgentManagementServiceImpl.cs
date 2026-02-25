using Grpc.Core;
using AgentManagementService.Data;
using AgentManagementService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using ProtoAgent = global::AgentManagementService.Agent;
using ProtoSyncBatch = global::AgentManagementService.SyncBatch;

namespace AgentManagementService.Services;

public partial class AgentManagementServiceImpl : AgentManagementService.AgentManagementServiceBase
{
    private readonly AgentDbContext _db;
    private readonly ILogger<AgentManagementServiceImpl> _logger;
    private readonly ControlPlaneSigningService _controlPlaneSigning;

    public AgentManagementServiceImpl(
        AgentDbContext db,
        ILogger<AgentManagementServiceImpl> logger,
        ControlPlaneSigningService controlPlaneSigning)
    {
        _db = db;
        _logger = logger;
        _controlPlaneSigning = controlPlaneSigning;
    }

    public override async Task<RegisterAgentResponse> RegisterAgent(RegisterAgentRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Register agent request for computer ID: {ComputerId}", request.ComputerId);

        try
        {
            // Check if agent already exists for this computer
            var existingAgent = await _db.Agents
                .FirstOrDefaultAsync(a => a.ComputerId == request.ComputerId);

            if (existingAgent != null)
            {
                return new RegisterAgentResponse
                {
                    Success = false,
                    Message = "Agent already exists for this computer"
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
            await _db.SaveChangesAsync();

            return new RegisterAgentResponse
            {
                Success = true,
                Message = "Agent registered successfully",
                Agent = MapAgentToProto(agent)
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
            OfflineSince = agent.OfflineSince?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? ""
        };
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
