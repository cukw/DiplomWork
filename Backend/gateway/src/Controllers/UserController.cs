using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using UserClient = Gateway.Protos.User.UserService.UserServiceClient;
using Gateway.Protos.User;

namespace Gateway.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserClient _user;

    public UserController(UserClient user) => _user = user;

    [HttpGet("users")]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var resp = await _user.GetAllUsersAsync(new GetAllUsersRequest
            {
                Page     = page,
                PageSize = pageSize
            });
            return Ok(new
            {
                users      = resp.Users.Select(MapUser),
                totalCount = resp.TotalCount,
                page,
                pageSize
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("users/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            var resp = await _user.GetUserProfileAsync(new GetUserProfileRequest { UserId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(MapUser(resp.UserProfile));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("users")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        try
        {
            var resp = await _user.CreateUserAsync(new CreateUserRequest
            {
                AuthUserId = dto.AuthUserId,
                FullName   = dto.FullName   ?? "",
                Department = dto.Department ?? "",
                Hostname   = dto.Hostname   ?? "",
                OsVersion  = dto.OsVersion  ?? "",
                IpAddress  = dto.IpAddress  ?? "",
                MacAddress = dto.MacAddress ?? ""
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapUser(resp.UserProfile));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPut("users/{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var resp = await _user.UpdateUserProfileAsync(new UpdateUserProfileRequest
            {
                UserId     = id,
                FullName   = dto.FullName   ?? "",
                Department = dto.Department ?? ""
            });
            if (!resp.Success) return BadRequest(new { message = resp.Message });
            return Ok(MapUser(resp.UserProfile));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpDelete("users/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try
        {
            var resp = await _user.DeleteUserAsync(new DeleteUserRequest { UserId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("department/{department}")]
    public async Task<IActionResult> GetByDepartment(string department)
    {
        try
        {
            var resp = await _user.GetUsersByDepartmentAsync(
                new GetUsersByDepartmentRequest { Department = department });
            return Ok(resp.Users.Select(MapUser));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static object? MapUser(UserProfile? u) => u is null ? null : new
    {
        id          = u.Id,
        authUserId  = u.AuthUserId,
        fullName    = u.FullName,
        department  = u.Department,
        createdAt   = u.CreatedAt,
        computer    = MapComputer(u.Computer)
    };

    private static object? MapComputer(ComputerInfo? c) => c is null ? null : new
    {
        id         = c.Id,
        hostname   = c.Hostname,
        osVersion  = c.OsVersion,
        ipAddress  = c.IpAddress,
        macAddress = c.MacAddress,
        status     = c.Status,
        lastSeen   = c.LastSeen
    };

    public record CreateUserDto(
        long    AuthUserId,
        string? FullName,
        string? Department,
        string? Hostname,
        string? OsVersion,
        string? IpAddress,
        string? MacAddress);

    public record UpdateUserDto(string? FullName, string? Department);
}
