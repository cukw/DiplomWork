using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
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

    public record RegisterAgentDto(long ComputerId, string? Version, string? ConfigVersion);
    public record UpdateAgentDto(string? Status, string? ConfigVersion);
    public record SyncDto(string? BatchId, int RecordsCount);
}
