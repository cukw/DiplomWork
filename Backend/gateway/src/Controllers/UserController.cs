using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using AuthClient = Gateway.Protos.Auth.AuthService.AuthServiceClient;
using AuthProto = Gateway.Protos.Auth;
using UserClient = Gateway.Protos.User.UserService.UserServiceClient;
using Gateway.Protos.User;

namespace Gateway.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserClient _user;
    private readonly AuthClient _auth;
    private readonly ILogger<UserController> _logger;

    public UserController(UserClient user, AuthClient auth, ILogger<UserController> logger)
    {
        _user = user;
        _auth = auth;
        _logger = logger;
    }

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
            var authUserId = dto.AuthUserId;
            AuthProto.User? authUser = null;
            var createdAuthAccount = false;

            if (authUserId <= 0)
            {
                var validationError = ValidateAuthAccountFields(dto);
                if (validationError is not null)
                    return BadRequest(new { message = validationError });

                var authResp = await _auth.RegisterAsync(new AuthProto.RegisterRequest
                {
                    Username = dto.Username?.Trim() ?? "",
                    Email    = dto.Email?.Trim() ?? "",
                    Password = dto.Password ?? "",
                    Role     = string.IsNullOrWhiteSpace(dto.Role) ? "user" : dto.Role.Trim()
                });

                if (!authResp.Success)
                    return BadRequest(new { message = authResp.Message });

                authUser = authResp.User;
                authUserId = authResp.User.Id;
                createdAuthAccount = true;
            }

            CreateUserResponse resp;
            try
            {
                resp = await _user.CreateUserAsync(new CreateUserRequest
                {
                    AuthUserId = authUserId,
                    FullName   = dto.FullName   ?? "",
                    Department = dto.Department ?? "",
                    Hostname   = dto.Hostname   ?? "",
                    OsVersion  = dto.OsVersion  ?? "",
                    IpAddress  = dto.IpAddress  ?? "",
                    MacAddress = dto.MacAddress ?? ""
                });
            }
            catch (RpcException ex) when (createdAuthAccount)
            {
                var rollbackMessage = await TryRollbackAuthAccountAsync(authUserId);
                _logger.LogError(
                    ex,
                    "UserService CreateUser failed after creating auth account {AuthUserId}",
                    authUserId);

                return StatusCode(500, new
                {
                    message = $"User profile creation failed after auth account creation. {rollbackMessage}"
                });
            }

            if (!resp.Success)
            {
                var rollbackMessage = createdAuthAccount
                    ? await TryRollbackAuthAccountAsync(authUserId)
                    : null;

                return BadRequest(new
                {
                    message = rollbackMessage is null
                        ? resp.Message
                        : $"{resp.Message}. {rollbackMessage}"
                });
            }

            return Ok(MapCreatedUser(resp.UserProfile, authUser, resp.Message));
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

    private static object? MapCreatedUser(UserProfile? u, AuthProto.User? authUser, string message) => u is null ? null : new
    {
        id          = u.Id,
        authUserId  = u.AuthUserId,
        fullName    = u.FullName,
        department  = u.Department,
        createdAt   = u.CreatedAt,
        computer    = MapComputer(u.Computer),
        authUser    = MapAuthUser(authUser),
        message
    };

    private static object? MapAuthUser(AuthProto.User? u) => u is null ? null : new
    {
        id        = u.Id,
        username  = u.Username,
        email     = u.Email,
        role      = u.Role,
        isActive  = u.IsActive,
        lastLogin = u.LastLogin,
        createdAt = u.CreatedAt
    };

    private static string? ValidateAuthAccountFields(CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            return "Username is required when AuthUserId is not provided";

        if (string.IsNullOrWhiteSpace(dto.Password))
            return "Password is required when AuthUserId is not provided";

        return null;
    }

    private async Task<string> TryRollbackAuthAccountAsync(long authUserId)
    {
        try
        {
            var rollback = await _auth.DeleteAuthUserAsync(new AuthProto.DeleteAuthUserRequest
            {
                UserId = authUserId
            });

            if (rollback.Success)
                return "Created auth account was rolled back";

            _logger.LogWarning(
                "Failed to rollback auth account {AuthUserId}: {Message}",
                authUserId,
                rollback.Message);
            return $"Created auth account {authUserId} could not be rolled back: {rollback.Message}";
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to rollback auth account {AuthUserId}. Status={StatusCode}",
                authUserId,
                ex.StatusCode);
            return $"Created auth account {authUserId} could not be rolled back";
        }
    }

    public record CreateUserDto(
        long    AuthUserId,
        string? Username,
        string? Email,
        string? Password,
        string? Role,
        string? FullName,
        string? Department,
        string? Hostname,
        string? OsVersion,
        string? IpAddress,
        string? MacAddress);

    public record UpdateUserDto(string? FullName, string? Department);
}
