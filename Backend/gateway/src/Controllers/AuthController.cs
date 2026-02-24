using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using AuthClient = Gateway.Protos.Auth.AuthService.AuthServiceClient;
using Gateway.Protos.Auth;

namespace Gateway.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthClient _auth;

    public AuthController(AuthClient auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var resp = await _auth.LoginAsync(new LoginRequest
            {
                Username = dto.Username,
                Password = dto.Password
            });
            if (!resp.Success)
                return Unauthorized(new { message = resp.Message });

            // Debug logging for token
            Console.WriteLine($"Gateway received token - Length: {resp.Token.Length}, Contains dots: {resp.Token.Contains(".")}, Preview: {resp.Token.Substring(0, Math.Min(50, resp.Token.Length))}");

            return Ok(new
            {
                token        = resp.Token,
                refreshToken = resp.RefreshToken,
                expiresIn    = resp.ExpiresIn,
                user         = MapUser(resp.User)
            });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var resp = await _auth.RegisterAsync(new RegisterRequest
            {
                Username = dto.Username,
                Email    = dto.Email,
                Password = dto.Password,
                Role     = dto.Role ?? "user"
            });
            if (!resp.Success)
                return BadRequest(new { message = resp.Message });

            return Ok(new { message = resp.Message, user = MapUser(resp.User) });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var token = TryExtractBearerToken(Request.Headers["Authorization"].ToString());
            if (string.IsNullOrWhiteSpace(token))
                return Ok(new { message = "No token provided, local logout only" });

            var resp  = await _auth.LogoutAsync(new LogoutRequest { Token = token });
            return Ok(new { message = resp.Message });
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        try
        {
            var userIdStr = User.FindFirst("sub")?.Value
                            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid token claims" });

            var resp = await _auth.GetUserProfileAsync(new GetUserProfileRequest { UserId = userId });
            if (!resp.Success)
                return NotFound(new { message = resp.Message });

            return Ok(MapUser(resp.User));
        }
        catch (RpcException ex)
        {
            return StatusCode(500, new { message = ex.Status.Detail });
        }
    }

    private static string? TryExtractBearerToken(string? rawAuthorization)
    {
        if (string.IsNullOrWhiteSpace(rawAuthorization))
            return null;

        foreach (var part in rawAuthorization.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                continue;

            var token = part["Bearer ".Length..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "undefined", StringComparison.OrdinalIgnoreCase))
                continue;

            return token;
        }

        return null;
    }

    private static object MapUser(User? u) => u is null ? new { } : new
    {
        id        = u.Id,
        username  = u.Username,
        email     = u.Email,
        role      = u.Role,
        isActive  = u.IsActive,
        lastLogin = u.LastLogin,
        createdAt = u.CreatedAt
    };

    public record LoginDto(string Username, string Password);
    public record RegisterDto(string Username, string Email, string Password, string? Role);
}
