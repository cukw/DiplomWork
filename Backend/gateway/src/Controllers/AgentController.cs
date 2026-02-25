using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Text.Json;
using AgentClient = Gateway.Protos.Agent.AgentManagementService.AgentManagementServiceClient;
using Gateway.Protos.Agent;

namespace Gateway.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly AgentClient _agent;

    public AgentController(AgentClient agent) => _agent = agent;

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
    public async Task<IActionResult> GetCommands(long id, [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _agent.GetAgentCommandsAsync(new GetAgentCommandsRequest
            {
                AgentId = id,
                Status = status ?? "",
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
                RequestedBy = dto.RequestedBy ?? User.Identity?.Name ?? "panel"
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapCommand(resp.Command));
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
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

    private async Task<IActionResult> SetBlockStateAndQueueCommand(long agentId, bool blocked, string reason)
    {
        try
        {
            var policyResult = await UpsertPolicy(agentId, new UpsertAgentPolicyDto
            {
                AdminBlocked = blocked,
                BlockedReason = blocked ? reason : ""
            });

            if (policyResult is NotFoundObjectResult notFound)
                return notFound;
            if (policyResult is BadRequestObjectResult badRequest)
                return badRequest;
            if (policyResult is ObjectResult obj && obj.StatusCode is >= 500)
                return obj;

            var commandResponse = await _agent.CreateAgentCommandAsync(new CreateAgentCommandRequest
            {
                AgentId = agentId,
                Type = blocked ? "BLOCK_WORKSTATION" : "UNBLOCK_WORKSTATION",
                PayloadJson = JsonSerializer.Serialize(new { reason }),
                RequestedBy = User.Identity?.Name ?? "panel"
            });

            if (!commandResponse.Success) return BadRequest(new { message = commandResponse.Message });
            return Ok(new
            {
                command = MapCommand(commandResponse.Command),
                requestedState = blocked ? "blocked" : "unblocked"
            });
        }
        catch (RpcException ex) { return StatusCode(500, new { message = ex.Status.Detail }); }
    }

    private static object? MapAgent(Agent? a) => a is null ? null : new
    {
        id            = a.Id,
        computerId    = a.ComputerId,
        version       = a.Version,
        status        = a.Status,
        lastHeartbeat = a.LastHeartbeat,
        configVersion = a.ConfigVersion,
        offlineSince  = a.OfflineSince
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
        adminBlocked = p.AdminBlocked,
        blockedReason = p.BlockedReason,
        browsers = p.Browsers.ToArray(),
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

        return merged;
    }

    public record RegisterAgentDto(long ComputerId, string? Version, string? ConfigVersion);
    public record UpdateAgentDto(string? Status, string? ConfigVersion);
    public record SyncDto(string? BatchId, int RecordsCount);
    public record CreateAgentCommandDto(string? Type, string? PayloadJson, object? Payload, string? RequestedBy);
    public record BlockCommandDto(string? Reason);
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
        bool? AdminBlocked = null,
        string? BlockedReason = null,
        string[]? Browsers = null
    );
}
