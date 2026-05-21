using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Text.Json;
using AgentClient = Gateway.Protos.Agent.AgentManagementService.AgentManagementServiceClient;
using Gateway.Protos.Agent;
using Gateway.Services;

namespace Gateway.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly AgentClient _agent;
    private readonly IAdminAuditLogger _auditLogger;

    public AgentController(
        AgentClient agent,
        IAdminAuditLogger auditLogger)
    {
        _agent = agent;
        _auditLogger = auditLogger;
    }

    [HttpGet("agents")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status   = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20)
    {
        try
        {
            var resp = await _agent.GetAllAgentsAsync(new GetAllAgentsRequest
            {
                Status   = status   ?? "",
                Page     = page,
                PageSize = pageSize
            });
            return Ok(new
            {
                agents     = resp.Agents.Select(MapAgent),
                totalCount = resp.TotalCount
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("agents/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            var resp = await _agent.GetAgentAsync(new GetAgentRequest { AgentId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(MapAgent(resp.Agent));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("agents")]
    public async Task<IActionResult> Register([FromBody] RegisterAgentDto dto)
    {
        try
        {
            var resp = await _agent.RegisterAgentAsync(new RegisterAgentRequest
            {
                ComputerId     = dto.ComputerId,
                Version        = dto.Version       ?? "1.0.0",
                ConfigVersion  = dto.ConfigVersion ?? "1.0.0"
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapAgent(resp.Agent));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPut("agents/{id:long}")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateAgentDto dto)
    {
        try
        {
            var resp = await _agent.UpdateAgentStatusAsync(new UpdateAgentStatusRequest
            {
                AgentId       = id,
                Status        = dto.Status        ?? "",
                ConfigVersion = dto.ConfigVersion ?? ""
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapAgent(resp.Agent));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("agents/{id:long}/desired-version")]
    public async Task<IActionResult> SetDesiredVersion(long id, [FromBody] SetDesiredVersionDto dto)
    {
        var actor = User.Identity?.Name ?? "panel";
        try
        {
            var desiredVersion = string.IsNullOrWhiteSpace(dto.DesiredVersion) ? string.Empty : dto.DesiredVersion.Trim();
            var enqueueSelfUpdate = dto.EnqueueSelfUpdate ?? !string.IsNullOrWhiteSpace(desiredVersion);

            var resp = await _agent.SetAgentDesiredVersionAsync(new SetAgentDesiredVersionRequest
            {
                AgentId = id,
                DesiredVersion = desiredVersion,
                EnqueueSelfUpdate = enqueueSelfUpdate,
                RequestedBy = string.IsNullOrWhiteSpace(dto.RequestedBy) ? actor : dto.RequestedBy.Trim(),
                CommandKey = dto.CommandKey ?? GetIdempotencyKeyFromHeaders()
            });

            if (!resp.Success)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    "agent.version.set",
                    actor,
                    "agent",
                    id.ToString(),
                    false,
                    400,
                    new { message = resp.Message, desiredVersion, enqueueSelfUpdate }));
                return BadRequest(new { message = resp.Message });
            }

            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.version.set",
                actor,
                "agent",
                id.ToString(),
                true,
                200,
                new { desiredVersion, enqueueSelfUpdate, commandId = resp.Command?.Id }));

            return Ok(new
            {
                message = resp.Message,
                agent = MapAgent(resp.Agent),
                command = MapCommand(resp.Command)
            });
        }
        catch (RpcException ex)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.version.set",
                actor,
                "agent",
                id.ToString(),
                false,
                500,
                new { desiredVersion = dto.DesiredVersion, enqueueSelfUpdate = dto.EnqueueSelfUpdate, rpc = ex.Status.Detail }));
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("rollouts/plan")]
    public async Task<IActionResult> PlanRollout([FromBody] RolloutPlanRequest? dto)
    {
        var desiredVersion = string.IsNullOrWhiteSpace(dto?.DesiredVersion) ? string.Empty : dto.DesiredVersion.Trim();
        if (string.IsNullOrWhiteSpace(desiredVersion))
            return BadRequest(new { message = "desiredVersion is required" });

        try
        {
            var allAgents = await FetchAllAgentsAsync();
            var onlineOnly = dto?.OnlineOnly ?? true;
            var filteredAgents = onlineOnly
                ? allAgents.Where(agent => !string.Equals(agent.Status, "offline", StringComparison.OrdinalIgnoreCase)).ToList()
                : allAgents;

            var requestedAgentIds = (dto?.AgentIds ?? [])
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            if (requestedAgentIds.Count > 0)
            {
                filteredAgents = filteredAgents
                    .Where(agent => requestedAgentIds.Contains(agent.Id))
                    .ToList();
            }

            if (filteredAgents.Count == 0)
            {
                return BadRequest(new { message = "No agents match rollout target filters" });
            }

            var strategy = NormalizeRolloutStrategy(dto?.Strategy);
            var targetIds = filteredAgents
                .OrderBy(agent => agent.Id)
                .Select(agent => (long)agent.Id)
                .ToArray();

            var stages = BuildRolloutStages(
                targetIds,
                strategy,
                dto?.CanaryPercent,
                dto?.StageSize);

            return Ok(new
            {
                desiredVersion,
                strategy,
                totalAgents = targetIds.Length,
                onlineOnly,
                stages = stages.Select(stage => new
                {
                    stage = stage.Stage,
                    label = stage.Label,
                    agentIds = stage.AgentIds,
                    count = stage.AgentIds.Length
                }),
                agents = filteredAgents.Select(MapAgent)
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("rollouts/execute")]
    public async Task<IActionResult> ExecuteRollout([FromBody] RolloutExecuteRequest? dto)
    {
        var actor = User.Identity?.Name ?? "panel";
        var desiredVersion = string.IsNullOrWhiteSpace(dto?.DesiredVersion) ? string.Empty : dto.DesiredVersion.Trim();
        if (string.IsNullOrWhiteSpace(desiredVersion))
            return BadRequest(new { message = "desiredVersion is required" });

        var targetIds = (dto?.AgentIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (targetIds.Length == 0)
            return BadRequest(new { message = "agentIds must contain at least one positive ID" });

        var enqueueSelfUpdate = dto?.EnqueueSelfUpdate ?? true;
        var requestedBy = string.IsNullOrWhiteSpace(dto?.RequestedBy) ? actor : dto!.RequestedBy!.Trim();
        var autoRollbackEnabled = dto?.AutoRollbackEnabled ?? true;
        var observationSeconds = Math.Clamp(dto?.ObservationSeconds ?? 0, 0, 180);
        var failureRateThreshold = Math.Clamp(dto?.FailureRateThreshold ?? 0.3, 0.01, 1.0);
        var maxFailedAgents = Math.Max(0, dto?.MaxFailedAgents ?? 1);

        try
        {
            var allAgents = await FetchAllAgentsAsync();
            var agentById = allAgents.ToDictionary(agent => (long)agent.Id);
            var missingAgentIds = targetIds.Where(id => !agentById.ContainsKey(id)).ToArray();
            var rolloutTargets = targetIds.Where(agentById.ContainsKey).ToArray();

            if (rolloutTargets.Length == 0)
            {
                return BadRequest(new
                {
                    message = "No valid agents found for rollout",
                    missingAgentIds
                });
            }

            var dispatchResults = new List<RolloutDispatchResult>();
            foreach (var agentId in rolloutTargets)
            {
                var agentSnapshot = agentById[agentId];
                try
                {
                    var response = await _agent.SetAgentDesiredVersionAsync(new SetAgentDesiredVersionRequest
                    {
                        AgentId = agentId,
                        DesiredVersion = desiredVersion,
                        EnqueueSelfUpdate = enqueueSelfUpdate,
                        RequestedBy = requestedBy,
                        CommandKey = $"rollout-{desiredVersion}-{agentId}-{Guid.NewGuid():N}"
                    });

                    dispatchResults.Add(new RolloutDispatchResult(
                        AgentId: agentId,
                        AgentVersion: agentSnapshot.Version,
                        Success: response.Success,
                        Message: response.Message,
                        CommandId: response.Command?.Id ?? 0,
                        CommandStatus: response.Command?.Status ?? string.Empty));
                }
                catch (RpcException ex)
                {
                    dispatchResults.Add(new RolloutDispatchResult(
                        AgentId: agentId,
                        AgentVersion: agentSnapshot.Version,
                        Success: false,
                        Message: ex.Status.Detail,
                        CommandId: 0,
                        CommandStatus: string.Empty));
                }
            }

            var successfulDispatches = dispatchResults.Where(result => result.Success).ToArray();

            RolloutEvaluationSummary evaluationSummary;
            RolloutRollbackSummary rollbackSummary;
            if (!autoRollbackEnabled || successfulDispatches.Length == 0 || !enqueueSelfUpdate)
            {
                evaluationSummary = new RolloutEvaluationSummary(
                    Enabled: autoRollbackEnabled,
                    Observed: 0,
                    Failed: 0,
                    FailureRate: 0,
                    RollbackTriggered: false,
                    Reason: "Auto-rollback is disabled or no self-update commands were queued",
                    CommandStatuses: []);
                rollbackSummary = new RolloutRollbackSummary(Triggered: false, Attempted: 0, Succeeded: 0, Failed: 0, Details: []);
            }
            else
            {
                if (observationSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(observationSeconds), HttpContext.RequestAborted);

                var commandStatuses = await EvaluateRolloutCommandsAsync(successfulDispatches, HttpContext.RequestAborted);
                var observed = commandStatuses.Length;
                var failed = commandStatuses.Count(status => IsFailedCommandStatus(status.Status));
                var failureRate = observed == 0 ? 0 : (double)failed / observed;
                var rollbackTriggered = observed > 0 && (failed > maxFailedAgents || failureRate >= failureRateThreshold);
                var reason = rollbackTriggered
                    ? $"Failure threshold exceeded (failed={failed}, observed={observed}, rate={failureRate:0.00})"
                    : "Thresholds are within limits";

                evaluationSummary = new RolloutEvaluationSummary(
                    Enabled: true,
                    Observed: observed,
                    Failed: failed,
                    FailureRate: Math.Round(failureRate, 4),
                    RollbackTriggered: rollbackTriggered,
                    Reason: reason,
                    CommandStatuses: commandStatuses);

                rollbackSummary = rollbackTriggered
                    ? await ExecuteRollbackAsync(successfulDispatches, agentById, requestedBy, HttpContext.RequestAborted)
                    : new RolloutRollbackSummary(Triggered: false, Attempted: 0, Succeeded: 0, Failed: 0, Details: []);
            }

            var succeededCount = dispatchResults.Count(result => result.Success);
            var failedCount = dispatchResults.Count - succeededCount;
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.rollout.execute",
                actor,
                "agent-rollout",
                desiredVersion,
                failedCount == 0,
                200,
                new
                {
                    desiredVersion,
                    totalTargets = rolloutTargets.Length,
                    succeededCount,
                    failedCount,
                    autoRollbackEnabled,
                    rollbackTriggered = evaluationSummary.RollbackTriggered,
                    missingAgentIds
                }));

            return Ok(new
            {
                desiredVersion,
                totalTargets = rolloutTargets.Length,
                succeededCount,
                failedCount,
                missingAgentIds,
                dispatch = dispatchResults.Select(result => new
                {
                    result.AgentId,
                    result.AgentVersion,
                    result.Success,
                    result.Message,
                    result.CommandId,
                    result.CommandStatus
                }),
                autoRollback = evaluationSummary,
                rollback = rollbackSummary
            });
        }
        catch (RpcException ex)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.rollout.execute",
                actor,
                "agent-rollout",
                desiredVersion,
                false,
                500,
                new { rpc = ex.Status.Detail, targetCount = targetIds.Length }));
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpDelete("agents/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try
        {
            var resp = await _agent.DeleteAgentAsync(new DeleteAgentRequest { AgentId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("agents/{id:long}/sync")]
    public async Task<IActionResult> Sync(long id, [FromBody] SyncDto dto)
    {
        try
        {
            var resp = await _agent.CreateSyncBatchAsync(new CreateSyncBatchRequest
            {
                AgentId      = id,
                BatchId      = dto.BatchId      ?? Guid.NewGuid().ToString(),
                RecordsCount = dto.RecordsCount
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapBatch(resp.Batch));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("agents/{id:long}/computer")]
    public async Task<IActionResult> GetByComputer(long id)
    {
        try
        {
            var resp = await _agent.GetAgentsByComputerAsync(
                new GetAgentsByComputerRequest { ComputerId = id });
            return Ok(resp.Agents.Select(MapAgent));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("agents/{id:long}/policy")]
    public async Task<IActionResult> GetPolicy(long id)
    {
        try
        {
            var resp = await _agent.GetAgentPolicyAsync(new GetAgentPolicyRequest { AgentId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(MapPolicy(resp.Policy));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPut("agents/{id:long}/policy")]
    public async Task<IActionResult> UpsertPolicy(long id, [FromBody] UpsertAgentPolicyDto dto)
    {
        try
        {
            var currentResp = await _agent.GetAgentPolicyAsync(new GetAgentPolicyRequest { AgentId = id });
            AgentPolicy current;

            if (currentResp.Success && currentResp.Policy is not null && currentResp.Policy.AgentId > 0)
            {
                current = currentResp.Policy;
            }
            else
            {
                var agentResp = await _agent.GetAgentAsync(new GetAgentRequest { AgentId = id });
                if (!agentResp.Success || agentResp.Agent is null || agentResp.Agent.Id <= 0)
                    return NotFound(new { message = "Agent not found" });

                current = new AgentPolicy
                {
                    AgentId = id,
                    ComputerId = agentResp.Agent.ComputerId,
                    PolicyVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                    CollectionIntervalSec = 5,
                    HeartbeatIntervalSec = 15,
                    FlushIntervalSec = 5,
                    EnableProcessCollection = true,
                    EnableBrowserCollection = true,
                    EnableActiveWindowCollection = true,
                    EnableIdleCollection = true,
                    IdleThresholdSec = 120,
                    BrowserPollIntervalSec = 10,
                    ProcessSnapshotLimit = 50,
                    HighRiskThreshold = 85,
                    AutoLockEnabled = true,
                    EnableWhitelist = true,
                    EnableBlacklist = true,
                    AdminBlocked = false,
                    BlockedReason = ""
                };
                current.Browsers.AddRange(["chrome", "edge", "firefox"]);
            }

            var updated = MergePolicy(current, dto, id);
            var resp = await _agent.UpsertAgentPolicyAsync(new UpsertAgentPolicyRequest { Policy = updated });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapPolicy(resp.Policy));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpDelete("agents/{id:long}/policy")]
    public async Task<IActionResult> DeletePolicy(long id)
    {
        try
        {
            var resp = await _agent.DeleteAgentPolicyAsync(new DeleteAgentPolicyRequest { AgentId = id });
            if (!resp.Success)
            {
                if (resp.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { message = resp.Message });
                return BadRequest(new { message = resp.Message });
            }

            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("agents/{id:long}/policy/versions")]
    public async Task<IActionResult> GetPolicyVersions(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _agent.GetAgentPolicyVersionsAsync(new GetAgentPolicyVersionsRequest
            {
                AgentId = id,
                Page = page,
                PageSize = pageSize
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(new { versions = resp.Versions.Select(MapPolicyVersion), totalCount = resp.TotalCount });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("agents/{id:long}/policy/versions/{versionId:long}/restore")]
    public async Task<IActionResult> RestorePolicyVersion(long id, long versionId, [FromBody] RestorePolicyVersionDto? dto = null)
    {
        try
        {
            var resp = await _agent.RestoreAgentPolicyVersionAsync(new RestoreAgentPolicyVersionRequest
            {
                AgentId = id,
                VersionId = versionId,
                RequestedBy = dto?.RequestedBy ?? User.Identity?.Name ?? "panel"
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });

            return Ok(new
            {
                message = resp.Message,
                policy = MapPolicy(resp.Policy),
                restoredFrom = MapPolicyVersion(resp.RestoredFrom)
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpGet("agents/{id:long}/commands")]
    public async Task<IActionResult> GetCommands(
        long id,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _agent.GetAgentCommandsAsync(new GetAgentCommandsRequest
            {
                AgentId = id,
                Status = status ?? "",
                Type = type ?? "",
                CreatedFrom = from ?? "",
                CreatedTo = to ?? "",
                Page = page,
                PageSize = pageSize
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(new { commands = resp.Commands.Select(MapCommand), totalCount = resp.TotalCount });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    [HttpPost("agents/{id:long}/commands")]
    public async Task<IActionResult> CreateCommand(long id, [FromBody] CreateAgentCommandDto dto)
    {
        var actor = User.Identity?.Name ?? "panel";
        try
        {
            var payloadJson = dto.PayloadJson;
            if (string.IsNullOrWhiteSpace(payloadJson) && dto.Payload is not null)
                payloadJson = JsonSerializer.Serialize(dto.Payload);

            var resp = await _agent.CreateAgentCommandAsync(new CreateAgentCommandRequest
            {
                AgentId = id,
                Type = dto.Type ?? "",
                PayloadJson = payloadJson ?? "{}",
                RequestedBy = dto.RequestedBy ?? actor,
                CommandKey = dto.CommandKey ?? GetIdempotencyKeyFromHeaders()
            });

            if (!resp.Success)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    "agent.command.create",
                    actor,
                    "agent",
                    id.ToString(),
                    false,
                    400,
                    new { message = resp.Message, commandType = dto.Type }));
                return BadRequest(new { message = resp.Message });
            }

            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.command.create",
                actor,
                "agent",
                id.ToString(),
                true,
                200,
                new { commandId = resp.Command?.Id, commandType = resp.Command?.Type ?? dto.Type }));
            return Ok(MapCommand(resp.Command));
        }
        catch (RpcException ex)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.command.create",
                actor,
                "agent",
                id.ToString(),
                false,
                500,
                new { rpc = ex.Status.Detail, commandType = dto.Type }));
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("agents/{id:long}/commands/{commandId:long}/retry")]
    public async Task<IActionResult> RetryCommand(long id, long commandId)
    {
        var actor = User.Identity?.Name ?? "panel";
        try
        {
            var resp = await _agent.RetryAgentCommandAsync(new RetryAgentCommandRequest
            {
                AgentId = id,
                CommandId = commandId,
                RequestedBy = actor
            });

            if (!resp.Success)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    "agent.command.retry",
                    actor,
                    "agent-command",
                    commandId.ToString(),
                    false,
                    400,
                    new { agentId = id, message = resp.Message }));
                return BadRequest(new { message = resp.Message });
            }

            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.command.retry",
                actor,
                "agent-command",
                commandId.ToString(),
                true,
                200,
                new { agentId = id, newCommandId = resp.Command?.Id }));
            return Ok(new
            {
                message = resp.Message,
                command = MapCommand(resp.Command)
            });
        }
        catch (RpcException ex)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                "agent.command.retry",
                actor,
                "agent-command",
                commandId.ToString(),
                false,
                500,
                new { agentId = id, rpc = ex.Status.Detail }));
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("agents/{id:long}/commands/block")]
    public async Task<IActionResult> BlockWorkstation(long id, [FromBody] BlockCommandDto? dto = null)
    {
        return await SetBlockStateAndQueueCommand(id, true, dto?.Reason ?? "Blocked by admin");
    }

    [HttpPost("agents/{id:long}/commands/unblock")]
    public async Task<IActionResult> UnblockWorkstation(long id, [FromBody] BlockCommandDto? dto = null)
    {
        return await SetBlockStateAndQueueCommand(id, false, dto?.Reason ?? "Unblocked by admin");
    }

    [HttpPost("agents/commands/bulk-state")]
    public async Task<IActionResult> BulkSetWorkstationState([FromBody] BulkAgentStateCommandDto dto)
    {
        var actor = User.Identity?.Name ?? "panel";
        var agentIds = dto.AgentIds?
            .Where(id => id > 0)
            .Distinct()
            .ToArray() ?? [];

        if (agentIds.Length == 0)
            return BadRequest(new { message = "agentIds must contain at least one valid ID" });

        var reason = string.IsNullOrWhiteSpace(dto.Reason)
            ? (dto.Blocked ? "Blocked by admin" : "Unblocked by admin")
            : dto.Reason.Trim();

        var succeeded = new List<object>();
        var failed = new List<object>();

        foreach (var agentId in agentIds)
        {
            var result = await SetBlockStateAndQueueCommand(agentId, dto.Blocked, reason);
            switch (result)
            {
                case OkObjectResult ok when ok.Value is not null:
                    succeeded.Add(new { agentId, result = ok.Value });
                    break;
                case ObjectResult obj:
                    failed.Add(new
                    {
                        agentId,
                        statusCode = obj.StatusCode ?? 400,
                        error = obj.Value
                    });
                    break;
                default:
                    failed.Add(new
                    {
                        agentId,
                        statusCode = 400,
                        error = new { message = "Unknown operation result" }
                    });
                    break;
            }
        }

        if (succeeded.Count == 0)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                dto.Blocked ? "agent.bulk.block" : "agent.bulk.unblock",
                actor,
                "agent",
                string.Join(",", agentIds),
                false,
                400,
                new { successCount = 0, failureCount = failed.Count }));
            return BadRequest(new
            {
                message = "Bulk action failed for all agents",
                requestedState = dto.Blocked ? "blocked" : "unblocked",
                successCount = 0,
                failureCount = failed.Count,
                errors = failed
            });
        }

        await _auditLogger.LogAsync(new AdminAuditEvent(
            dto.Blocked ? "agent.bulk.block" : "agent.bulk.unblock",
            actor,
            "agent",
            string.Join(",", agentIds),
            failed.Count == 0,
            200,
            new { successCount = succeeded.Count, failureCount = failed.Count }));

        return Ok(new
        {
            requestedState = dto.Blocked ? "blocked" : "unblocked",
            successCount = succeeded.Count,
            failureCount = failed.Count,
            success = succeeded,
            errors = failed
        });
    }

    private async Task<IActionResult> SetBlockStateAndQueueCommand(long agentId, bool blocked, string reason)
    {
        var actor = User.Identity?.Name ?? "panel";
        var action = blocked ? "agent.block" : "agent.unblock";
        try
        {
            var policyResult = await UpsertPolicy(agentId, new UpsertAgentPolicyDto
            {
                AdminBlocked = blocked,
                BlockedReason = blocked ? reason : ""
            });

            if (policyResult is NotFoundObjectResult notFound)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    action,
                    actor,
                    "agent",
                    agentId.ToString(),
                    false,
                    404,
                    new { reason, message = "Agent policy target not found" }));
                return notFound;
            }
            if (policyResult is BadRequestObjectResult badRequest)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    action,
                    actor,
                    "agent",
                    agentId.ToString(),
                    false,
                    400,
                    new { reason, message = "Policy update rejected" }));
                return badRequest;
            }
            if (policyResult is ObjectResult obj && obj.StatusCode is >= 500)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    action,
                    actor,
                    "agent",
                    agentId.ToString(),
                    false,
                    obj.StatusCode,
                    new { reason, message = "Policy update failed" }));
                return obj;
            }

            var commandResponse = await _agent.CreateAgentCommandAsync(new CreateAgentCommandRequest
            {
                AgentId = agentId,
                Type = blocked ? "BLOCK_WORKSTATION" : "UNBLOCK_WORKSTATION",
                PayloadJson = JsonSerializer.Serialize(new { reason }),
                RequestedBy = actor,
                CommandKey = GetIdempotencyKeyFromHeaders()
            });

            if (!commandResponse.Success)
            {
                await _auditLogger.LogAsync(new AdminAuditEvent(
                    action,
                    actor,
                    "agent",
                    agentId.ToString(),
                    false,
                    400,
                    new { reason, message = commandResponse.Message }));
                return BadRequest(new { message = commandResponse.Message });
            }

            await _auditLogger.LogAsync(new AdminAuditEvent(
                action,
                actor,
                "agent",
                agentId.ToString(),
                true,
                200,
                new { reason, commandId = commandResponse.Command?.Id }));
            return Ok(new
            {
                command = MapCommand(commandResponse.Command),
                requestedState = blocked ? "blocked" : "unblocked"
            });
        }
        catch (RpcException ex)
        {
            await _auditLogger.LogAsync(new AdminAuditEvent(
                action,
                actor,
                "agent",
                agentId.ToString(),
                false,
                500,
                new { reason, rpc = ex.Status.Detail }));
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static object? MapAgent(Agent? a) => a is null ? null : new
    {
        id            = a.Id,
        computerId    = a.ComputerId,
        version       = a.Version,
        status        = a.Status,
        lastHeartbeat = a.LastHeartbeat,
        configVersion = a.ConfigVersion,
        offlineSince  = a.OfflineSince,
        desiredVersion = a.DesiredVersion,
        desiredVersionSetAt = a.DesiredVersionSetAt,
        healthJson = a.HealthJson,
        queueSize = a.QueueSize,
        lastCollectedAt = a.LastCollectedAt,
        lastSentAt = a.LastSentAt,
        lastError = a.LastError,
        policyVersion = a.PolicyVersion,
        capabilitiesJson = a.CapabilitiesJson,
        collectorStatusesJson = a.CollectorStatusesJson,
        sourcePlatform = a.SourcePlatform
    };

    private static object? MapBatch(SyncBatch? b) => b is null ? null : new
    {
        id           = b.Id,
        agentId      = b.AgentId,
        batchId      = b.BatchId,
        status       = b.Status,
        syncedAt     = b.SyncedAt,
        recordsCount = b.RecordsCount
    };

    private static object? MapPolicy(AgentPolicy? p) => p is null ? null : new
    {
        id = p.Id,
        agentId = p.AgentId,
        computerId = p.ComputerId,
        policyVersion = p.PolicyVersion,
        collectionIntervalSec = p.CollectionIntervalSec,
        heartbeatIntervalSec = p.HeartbeatIntervalSec,
        flushIntervalSec = p.FlushIntervalSec,
        enableProcessCollection = p.EnableProcessCollection,
        enableBrowserCollection = p.EnableBrowserCollection,
        enableActiveWindowCollection = p.EnableActiveWindowCollection,
        enableIdleCollection = p.EnableIdleCollection,
        idleThresholdSec = p.IdleThresholdSec,
        browserPollIntervalSec = p.BrowserPollIntervalSec,
        processSnapshotLimit = p.ProcessSnapshotLimit,
        highRiskThreshold = p.HighRiskThreshold,
        autoLockEnabled = p.AutoLockEnabled,
        enableWhitelist = p.EnableWhitelist,
        enableBlacklist = p.EnableBlacklist,
        adminBlocked = p.AdminBlocked,
        blockedReason = p.BlockedReason,
        browsers = p.Browsers.ToArray(),
        whitelistApps = p.WhitelistApps.ToArray(),
        blacklistApps = p.BlacklistApps.ToArray(),
        updatedAt = p.UpdatedAt,
        signature = p.Signature,
        signatureKeyId = p.SignatureKeyId,
        signatureAlg = p.SignatureAlg
    };

    private static object? MapCommand(AgentCommand? c) => c is null ? null : new
    {
        id = c.Id,
        agentId = c.AgentId,
        type = c.Type,
        payloadJson = c.PayloadJson,
        status = c.Status,
        commandKey = c.CommandKey,
        deliveryAttempts = c.DeliveryAttempts,
        maxDeliveryAttempts = c.MaxDeliveryAttempts,
        lastDispatchAt = c.LastDispatchAt,
        nextRetryAt = c.NextRetryAt,
        timeoutAt = c.TimeoutAt,
        deadLetterReason = c.DeadLetterReason,
        requestedBy = c.RequestedBy,
        resultMessage = c.ResultMessage,
        createdAt = c.CreatedAt,
        acknowledgedAt = c.AcknowledgedAt,
        signature = c.Signature,
        signatureKeyId = c.SignatureKeyId,
        signatureAlg = c.SignatureAlg
    };

    private static object? MapPolicyVersion(Gateway.Protos.Agent.AgentPolicyVersion? v) => v is null ? null : new
    {
        id = v.Id,
        agentId = v.AgentId,
        policyVersion = v.PolicyVersion,
        changeType = v.ChangeType,
        changedBy = v.ChangedBy,
        createdAt = v.CreatedAt,
        snapshotJson = v.SnapshotJson
    };

    private static AgentPolicy MergePolicy(AgentPolicy current, UpsertAgentPolicyDto dto, long agentId)
    {
        var merged = new AgentPolicy
        {
            Id = current.Id,
            AgentId = agentId,
            ComputerId = dto.ComputerId ?? current.ComputerId,
            PolicyVersion = dto.PolicyVersion ?? current.PolicyVersion,
            CollectionIntervalSec = dto.CollectionIntervalSec ?? current.CollectionIntervalSec,
            HeartbeatIntervalSec = dto.HeartbeatIntervalSec ?? current.HeartbeatIntervalSec,
            FlushIntervalSec = dto.FlushIntervalSec ?? current.FlushIntervalSec,
            EnableProcessCollection = dto.EnableProcessCollection ?? current.EnableProcessCollection,
            EnableBrowserCollection = dto.EnableBrowserCollection ?? current.EnableBrowserCollection,
            EnableActiveWindowCollection = dto.EnableActiveWindowCollection ?? current.EnableActiveWindowCollection,
            EnableIdleCollection = dto.EnableIdleCollection ?? current.EnableIdleCollection,
            IdleThresholdSec = dto.IdleThresholdSec ?? current.IdleThresholdSec,
            BrowserPollIntervalSec = dto.BrowserPollIntervalSec ?? current.BrowserPollIntervalSec,
            ProcessSnapshotLimit = dto.ProcessSnapshotLimit ?? current.ProcessSnapshotLimit,
            HighRiskThreshold = dto.HighRiskThreshold ?? current.HighRiskThreshold,
            AutoLockEnabled = dto.AutoLockEnabled ?? current.AutoLockEnabled,
            EnableWhitelist = dto.EnableWhitelist ?? current.EnableWhitelist,
            EnableBlacklist = dto.EnableBlacklist ?? current.EnableBlacklist,
            AdminBlocked = dto.AdminBlocked ?? current.AdminBlocked,
            BlockedReason = dto.BlockedReason ?? current.BlockedReason ?? "",
            UpdatedAt = current.UpdatedAt
        };

        IEnumerable<string> browsersSource = dto.Browsers is { Length: > 0 }
            ? dto.Browsers
            : current.Browsers;

        merged.Browsers.AddRange(browsersSource
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant()));

        if (merged.Browsers.Count == 0)
            merged.Browsers.AddRange(["chrome", "edge", "firefox"]);

        IEnumerable<string> whitelistSource = dto.WhitelistApps is { Length: > 0 }
            ? dto.WhitelistApps
            : current.WhitelistApps;

        merged.WhitelistApps.AddRange(whitelistSource
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant()));

        IEnumerable<string> blacklistSource = dto.BlacklistApps is { Length: > 0 }
            ? dto.BlacklistApps
            : current.BlacklistApps;

        merged.BlacklistApps.AddRange(blacklistSource
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant()));

        return merged;
    }

    private async Task<List<Agent>> FetchAllAgentsAsync()
    {
        var page = 1;
        const int pageSize = 200;
        var result = new List<Agent>();

        while (true)
        {
            var response = await _agent.GetAllAgentsAsync(new GetAllAgentsRequest
            {
                Status = string.Empty,
                Page = page,
                PageSize = pageSize
            });

            if (response.Agents.Count == 0)
                break;

            result.AddRange(response.Agents);
            if (result.Count >= response.TotalCount || response.Agents.Count < pageSize)
                break;

            page++;
        }

        return result;
    }

    private static RolloutStage[] BuildRolloutStages(
        long[] targetAgentIds,
        string strategy,
        int? canaryPercent,
        int? stageSize)
    {
        if (targetAgentIds.Length == 0)
            return [];

        if (string.Equals(strategy, "canary", StringComparison.Ordinal))
        {
            var percent = Math.Clamp(canaryPercent ?? 10, 1, 50);
            var canaryCount = Math.Max(1, (int)Math.Round(targetAgentIds.Length * (percent / 100.0), MidpointRounding.AwayFromZero));
            var canaryIds = targetAgentIds.Take(canaryCount).ToArray();
            var productionIds = targetAgentIds.Skip(canaryCount).ToArray();

            var stages = new List<RolloutStage>
            {
                new(1, $"Canary ({percent}%)", canaryIds)
            };
            if (productionIds.Length > 0)
                stages.Add(new RolloutStage(2, "Production rollout", productionIds));
            return stages.ToArray();
        }

        if (string.Equals(strategy, "staged", StringComparison.Ordinal))
        {
            var chunkSize = Math.Clamp(stageSize ?? 25, 1, 500);
            var stages = new List<RolloutStage>();
            var stageNumber = 1;
            for (var index = 0; index < targetAgentIds.Length; index += chunkSize)
            {
                var chunk = targetAgentIds.Skip(index).Take(chunkSize).ToArray();
                stages.Add(new RolloutStage(stageNumber++, $"Stage {stageNumber - 1}", chunk));
            }

            return stages.ToArray();
        }

        return [new RolloutStage(1, "Full rollout", targetAgentIds)];
    }

    private static string NormalizeRolloutStrategy(string? strategy)
    {
        var normalized = string.IsNullOrWhiteSpace(strategy) ? string.Empty : strategy.Trim().ToLowerInvariant();
        return normalized switch
        {
            "canary" => "canary",
            "staged" => "staged",
            _ => "all"
        };
    }

    private async Task<RolloutCommandStatus[]> EvaluateRolloutCommandsAsync(
        IReadOnlyCollection<RolloutDispatchResult> successfulDispatches,
        CancellationToken cancellationToken)
    {
        var statuses = new List<RolloutCommandStatus>();
        foreach (var dispatch in successfulDispatches)
        {
            if (dispatch.CommandId <= 0)
                continue;

            var response = await _agent.GetAgentCommandsAsync(new GetAgentCommandsRequest
            {
                AgentId = dispatch.AgentId,
                Type = "SELF_UPDATE",
                Page = 1,
                PageSize = 50
            }, cancellationToken: cancellationToken);

            var command = response.Commands.FirstOrDefault(cmd => cmd.Id == dispatch.CommandId);
            statuses.Add(new RolloutCommandStatus(
                dispatch.AgentId,
                dispatch.CommandId,
                command?.Status ?? "unknown",
                command?.ResultMessage ?? string.Empty));
        }

        return statuses.ToArray();
    }

    private async Task<RolloutRollbackSummary> ExecuteRollbackAsync(
        IReadOnlyCollection<RolloutDispatchResult> successfulDispatches,
        IReadOnlyDictionary<long, Agent> agentById,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var rollbackDetails = new List<RolloutRollbackResult>();
        foreach (var dispatch in successfulDispatches)
        {
            if (!agentById.TryGetValue(dispatch.AgentId, out var currentAgent))
                continue;

            var rollbackVersion = string.IsNullOrWhiteSpace(dispatch.AgentVersion)
                ? currentAgent.Version
                : dispatch.AgentVersion;
            if (string.IsNullOrWhiteSpace(rollbackVersion))
            {
                rollbackDetails.Add(new RolloutRollbackResult(dispatch.AgentId, false, "Rollback skipped: source version is missing"));
                continue;
            }

            try
            {
                var rollbackResponse = await _agent.SetAgentDesiredVersionAsync(new SetAgentDesiredVersionRequest
                {
                    AgentId = dispatch.AgentId,
                    DesiredVersion = rollbackVersion,
                    EnqueueSelfUpdate = true,
                    RequestedBy = requestedBy,
                    CommandKey = $"rollback-{rollbackVersion}-{dispatch.AgentId}-{Guid.NewGuid():N}"
                }, cancellationToken: cancellationToken);

                rollbackDetails.Add(new RolloutRollbackResult(
                    dispatch.AgentId,
                    rollbackResponse.Success,
                    rollbackResponse.Message));
            }
            catch (RpcException ex)
            {
                rollbackDetails.Add(new RolloutRollbackResult(dispatch.AgentId, false, ex.Status.Detail));
            }
        }

        var succeeded = rollbackDetails.Count(detail => detail.Success);
        return new RolloutRollbackSummary(
            Triggered: true,
            Attempted: rollbackDetails.Count,
            Succeeded: succeeded,
            Failed: rollbackDetails.Count - succeeded,
            Details: rollbackDetails.ToArray());
    }

    private static bool IsFailedCommandStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
        return normalized is "failed" or "deadletter" or "timeout";
    }

    public record RegisterAgentDto(long ComputerId, string? Version, string? ConfigVersion);
    public record UpdateAgentDto(string? Status, string? ConfigVersion);
    public record SetDesiredVersionDto(string? DesiredVersion, bool? EnqueueSelfUpdate = null, string? RequestedBy = null, string? CommandKey = null);
    public sealed record RolloutPlanRequest(
        string? DesiredVersion,
        string? Strategy,
        int? CanaryPercent,
        int? StageSize,
        long[]? AgentIds,
        bool? OnlineOnly);
    public sealed record RolloutExecuteRequest(
        string? DesiredVersion,
        long[] AgentIds,
        bool? AutoRollbackEnabled,
        int? ObservationSeconds,
        double? FailureRateThreshold,
        int? MaxFailedAgents,
        bool? EnqueueSelfUpdate,
        string? RequestedBy);
    public record SyncDto(string? BatchId, int RecordsCount);
    private string GetIdempotencyKeyFromHeaders()
    {
        if (Request.Headers.TryGetValue("X-Idempotency-Key", out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    public record CreateAgentCommandDto(string? Type, string? PayloadJson, object? Payload, string? RequestedBy, string? CommandKey);
    public record BlockCommandDto(string? Reason);
    public record BulkAgentStateCommandDto(long[] AgentIds, bool Blocked, string? Reason);
    public record RestorePolicyVersionDto(string? RequestedBy);
    public record UpsertAgentPolicyDto(
        string? PolicyVersion = null,
        long? ComputerId = null,
        int? CollectionIntervalSec = null,
        int? HeartbeatIntervalSec = null,
        int? FlushIntervalSec = null,
        bool? EnableProcessCollection = null,
        bool? EnableBrowserCollection = null,
        bool? EnableActiveWindowCollection = null,
        bool? EnableIdleCollection = null,
        int? IdleThresholdSec = null,
        int? BrowserPollIntervalSec = null,
        int? ProcessSnapshotLimit = null,
        float? HighRiskThreshold = null,
        bool? AutoLockEnabled = null,
        bool? EnableWhitelist = null,
        bool? EnableBlacklist = null,
        bool? AdminBlocked = null,
        string? BlockedReason = null,
        string[]? Browsers = null,
        string[]? WhitelistApps = null,
        string[]? BlacklistApps = null
    );

    private sealed record RolloutStage(int Stage, string Label, long[] AgentIds);
    private sealed record RolloutDispatchResult(
        long AgentId,
        string? AgentVersion,
        bool Success,
        string Message,
        long CommandId,
        string CommandStatus);
    private sealed record RolloutCommandStatus(long AgentId, long CommandId, string Status, string ResultMessage);
    private sealed record RolloutRollbackResult(long AgentId, bool Success, string Message);
    private sealed record RolloutEvaluationSummary(
        bool Enabled,
        int Observed,
        int Failed,
        double FailureRate,
        bool RollbackTriggered,
        string Reason,
        RolloutCommandStatus[] CommandStatuses);
    private sealed record RolloutRollbackSummary(
        bool Triggered,
        int Attempted,
        int Succeeded,
        int Failed,
        RolloutRollbackResult[] Details);
}
