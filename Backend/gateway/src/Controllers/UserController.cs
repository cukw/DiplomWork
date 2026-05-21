using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using System.Security.Claims;
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
                var rollback = await TryDeleteAuthAccountAsync(
                    authUserId,
                    "Created auth account was rolled back");
                _logger.LogError(
                    ex,
                    "UserService CreateUser failed after creating auth account {AuthUserId}",
                    authUserId);

                return StatusCode(500, new
                {
                    message = $"User profile creation failed after auth account creation. {rollback.Message}"
                });
            }

            if (!resp.Success)
            {
                (bool Success, string Message)? rollback = createdAuthAccount
                    ? await TryDeleteAuthAccountAsync(authUserId, "Created auth account was rolled back")
                    : null;

                return BadRequest(new
                {
                    message = rollback is null
                        ? resp.Message
                        : $"{resp.Message}. {rollback.Value.Message}"
                });
            }

            return Ok(MapCreatedUser(resp.UserProfile, authUser, resp.Message));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("computers/enroll")]
    public async Task<IActionResult> EnrollComputer([FromBody] EnrollComputerDto dto)
    {
        var authUserId = GetAuthenticatedAuthUserId();
        if (authUserId <= 0)
            return Unauthorized(new { message = "Invalid token claims" });

        try
        {
            var resp = await _user.EnrollComputerForAuthUserAsync(new EnrollComputerForAuthUserRequest
            {
                AuthUserId = authUserId,
                FullName   = string.IsNullOrWhiteSpace(dto.FullName) ? (User.Identity?.Name ?? "") : dto.FullName.Trim(),
                Department = dto.Department ?? "",
                Hostname   = dto.Hostname ?? "",
                OsVersion  = dto.OsVersion ?? "",
                IpAddress  = dto.IpAddress ?? "",
                MacAddress = dto.MacAddress ?? ""
            });

            if (!resp.Success)
                return Conflict(new { message = resp.Message });

            return Ok(MapEnrollment(resp));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("computers/session/end")]
    public async Task<IActionResult> EndComputerSession([FromBody] EndComputerSessionDto dto)
    {
        var authUserId = GetAuthenticatedAuthUserId();
        if (authUserId <= 0)
            return Unauthorized(new { message = "Invalid token claims" });

        try
        {
            var resp = await _user.EndComputerSessionForAuthUserAsync(new EndComputerSessionForAuthUserRequest
            {
                AuthUserId = authUserId,
                SessionId  = dto.SessionId,
                ComputerId = dto.ComputerId
            });

            if (!resp.Success)
                return NotFound(new { message = resp.Message });

            return Ok(new
            {
                message = resp.Message,
                sessionId = resp.SessionId,
                computer = MapComputer(resp.Computer)
            });
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
    public async Task<IActionResult> Delete(long id, [FromQuery] bool deleteAuthAccount = false)
    {
        try
        {
            long authUserId = 0;
            if (deleteAuthAccount)
            {
                var profile = await _user.GetUserProfileAsync(new GetUserProfileRequest { UserId = id });
                if (!profile.Success)
                    return NotFound(new { message = profile.Message });

                authUserId = profile.UserProfile.AuthUserId;
            }

            var resp = await _user.DeleteUserAsync(new DeleteUserRequest { UserId = id });
            if (!resp.Success) return NotFound(new { message = resp.Message });

            if (deleteAuthAccount && authUserId > 0)
            {
                var authDelete = await TryDeleteAuthAccountAsync(authUserId);
                if (!authDelete.Success)
                {
                    return Ok(new
                    {
                        message = $"{resp.Message}. {authDelete.Message}",
                        authDeleted = false
                    });
                }

                return Ok(new
                {
                    message = $"{resp.Message}. Auth account deleted successfully",
                    authDeleted = true
                });
            }

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
        computer    = MapComputer(u.Computer),
        computers   = u.Computers.Select(MapComputer)
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

    private static object MapEnrollment(EnrollComputerForAuthUserResponse resp) => new
    {
        message = resp.Message,
        sessionId = resp.SessionId,
        sessionExpiresAt = resp.SessionExpiresAt,
        createdUser = resp.CreatedUser,
        createdComputer = resp.CreatedComputer,
        createdSession = resp.CreatedSession,
        user = MapUser(resp.UserProfile),
        computer = MapComputer(resp.Computer)
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

    private async Task<(bool Success, string Message)> TryDeleteAuthAccountAsync(
        long authUserId,
        string successMessage = "Auth account deleted successfully")
    {
        try
        {
            var rollback = await _auth.DeleteAuthUserAsync(new AuthProto.DeleteAuthUserRequest
            {
                UserId = authUserId
            });

            if (rollback.Success)
                return (true, successMessage);

            _logger.LogWarning(
                "Failed to delete auth account {AuthUserId}: {Message}",
                authUserId,
                rollback.Message);
            return (false, $"Auth account {authUserId} could not be deleted: {rollback.Message}");
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete auth account {AuthUserId}. Status={StatusCode}",
                authUserId,
                ex.StatusCode);
            return (false, $"Auth account {authUserId} could not be deleted");
        }
    }

    private long GetAuthenticatedAuthUserId()
    {
        var userIdStr = User.FindFirst("sub")?.Value
                        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdStr, out var userId) ? userId : 0;
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

    public record EnrollComputerDto(
        string? FullName,
        string? Department,
        string? Hostname,
        string? OsVersion,
        string? IpAddress,
        string? MacAddress);

    public record EndComputerSessionDto(long SessionId, long ComputerId);
}
