using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuthService.Services;
using AuthService.Models;
using AuthService.Data;

namespace AuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IJwtService _jwtService;
    private readonly IPasswordService _passwordService;
    private readonly AuthDbContext _context;

    public AuthController(IJwtService jwtService, IPasswordService passwordService, AuthDbContext context)
    {
        _jwtService = jwtService;
        _passwordService = passwordService;
        _context = context;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Находим пользователя в базе данных
            var user = await _context.AuthUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Проверяем пароль
            if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Обновляем время последнего входа
            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Генерируем JWT токен
            var token = _jwtService.GenerateToken(user);

            // Возвращаем ответ
            return Ok(new
            {
                token = token,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    role = user.Role?.Name
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred during login", error = ex.Message });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            // Проверяем, существует ли пользователь
            var existingUser = await _context.AuthUsers
                .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Email);

            if (existingUser != null)
            {
                return BadRequest(new { message = "Username or email already exists" });
            }

            // Получаем роль пользователя (по умолчанию "user")
            var userRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.Name == "user");

            if (userRole == null)
            {
                // Создаем роль "user", если она не существует
                userRole = new Role { Name = "user", Description = "Regular user" };
                _context.Roles.Add(userRole);
                await _context.SaveChangesAsync();
            }

            // Создаем нового пользователя
            var newUser = new AuthUser
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = _passwordService.HashPassword(request.Password),
                RoleId = userRole.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.AuthUsers.Add(newUser);
            await _context.SaveChangesAsync();

            // Генерируем JWT токен
            var token = _jwtService.GenerateToken(newUser);

            return Ok(new
            {
                token = token,
                user = new
                {
                    id = newUser.Id,
                    username = newUser.Username,
                    email = newUser.Email,
                    role = userRole.Name
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred during registration", error = ex.Message });
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // В реальном приложении здесь можно добавить токен в черный список
        return Ok(new { message = "Logged out successfully", success = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            // Получаем ID пользователя из токена
            var userIdClaim = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid token" });
            }

            // Находим пользователя в базе данных
            var user = await _context.AuthUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                role = user.Role?.Name,
                isActive = user.IsActive,
                lastLogin = user.LastLogin,
                createdAt = user.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred", error = ex.Message });
        }
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}