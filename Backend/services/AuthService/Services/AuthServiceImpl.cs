using Grpc.Core;
using AuthService.Data;
using AuthService.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AuthService.Services;

public class AuthServiceImpl : AuthService.AuthServiceBase
{
    private readonly AuthDbContext _db;
    private readonly ILogger<AuthServiceImpl> _logger;
    private readonly IJwtService _jwtService;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _configuration;

    public AuthServiceImpl(
        AuthDbContext db,
        ILogger<AuthServiceImpl> logger,
        IJwtService jwtService,
        IPasswordService passwordService,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _jwtService = jwtService;
        _passwordService = passwordService;
        _configuration = configuration;
    }

    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Login attempt for user: {Username}", request.Username);

        try
        {
            var user = await _db.AuthUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
            {
                _logger.LogWarning("Login failed: User not found - {Username}", request.Username);
                return new LoginResponse
                {
                    Success = false,
                    Message = "Invalid username or password"
                };
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: User is inactive - {Username}", request.Username);
                return new LoginResponse
                {
                    Success = false,
                    Message = "Account is disabled"
                };
            }

            if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed: Invalid password - {Username}", request.Username);
                return new LoginResponse
                {
                    Success = false,
                    Message = "Invalid username or password"
                };
            }

            // Update last login
            user.LastLogin = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Generate tokens
            var token = _jwtService.GenerateToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Store refresh token
            var refreshTokenExpirationDays = int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");
            var session = new Session
            {
                UserId = user.Id,
                TokenHash = HashToken(refreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenExpirationDays)
            };
            _db.Sessions.Add(session);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Login successful for user: {Username}", request.Username);

            var expirationMinutes = int.Parse(_configuration["Jwt:ExpirationMinutes"] ?? "60");
            return new LoginResponse
            {
                Success = true,
                Message = "Login successful",
                Token = token,
                RefreshToken = refreshToken,
                ExpiresIn = expirationMinutes * 60, // Convert minutes to seconds
                User = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
            return new LoginResponse
            {
                Success = false,
                Message = "An error occurred during login"
            };
        }
    }

    public override async Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

        try
        {
            // Check if user already exists
            var existingUser = await _db.AuthUsers
                .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Email);

            if (existingUser != null)
            {
                _logger.LogWarning("Registration failed: User already exists - {Username}", request.Username);
                return new RegisterResponse
                {
                    Success = false,
                    Message = "Username or email already exists"
                };
            }

            // Get role
            var roleName = string.IsNullOrEmpty(request.Role) ? "user" : request.Role;
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);

            if (role == null)
            {
                _logger.LogWarning("Registration failed: Invalid role - {Role}", roleName);
                return new RegisterResponse
                {
                    Success = false,
                    Message = "Invalid role specified"
                };
            }

            // Create new user
            var user = new AuthUser
            {
                Username = request.Username,
                PasswordHash = _passwordService.HashPassword(request.Password),
                Email = request.Email,
                RoleId = role.Id,
                IsActive = true
            };

            _db.AuthUsers.Add(user);
            await _db.SaveChangesAsync();

            // Reload user with role
            user = await _db.AuthUsers
                .Include(u => u.Role)
                .FirstAsync(u => u.Id == user.Id);

            _logger.LogInformation("Registration successful for user: {Username}", request.Username);

            return new RegisterResponse
            {
                Success = true,
                Message = "Registration successful",
                User = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
            return new RegisterResponse
            {
                Success = false,
                Message = "An error occurred during registration"
            };
        }
    }

    public override async Task<ValidateTokenResponse> ValidateToken(ValidateTokenRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Token validation request");

        try
        {
            if (!_jwtService.ValidateToken(request.Token))
            {
                return new ValidateTokenResponse
                {
                    Valid = false,
                    Message = "Invalid token"
                };
            }

            var principal = _jwtService.GetPrincipalFromExpiredToken(request.Token);
            if (principal == null)
            {
                return new ValidateTokenResponse
                {
                    Valid = false,
                    Message = "Invalid token"
                };
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return new ValidateTokenResponse
                {
                    Valid = false,
                    Message = "Invalid token"
                };
            }

            var user = await _db.AuthUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

            if (user == null)
            {
                return new ValidateTokenResponse
                {
                    Valid = false,
                    Message = "User not found or inactive"
                };
            }

            return new ValidateTokenResponse
            {
                Valid = true,
                Message = "Token is valid",
                User = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return new ValidateTokenResponse
            {
                Valid = false,
                Message = "An error occurred during token validation"
            };
        }
    }

    public override async Task<RefreshTokenResponse> RefreshToken(RefreshTokenRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Token refresh request");

        try
        {
            // Find the session with the refresh token
            var session = await _db.Sessions
                .Include(s => s.User)
                .ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(s => s.TokenHash == HashToken(request.RefreshToken) && s.ExpiresAt > DateTime.UtcNow);

            if (session == null)
            {
                return new RefreshTokenResponse
                {
                    Success = false,
                    Message = "Invalid or expired refresh token"
                };
            }

            if (!session.User.IsActive)
            {
                return new RefreshTokenResponse
                {
                    Success = false,
                    Message = "User account is disabled"
                };
            }

            // Generate new tokens
            var token = _jwtService.GenerateToken(session.User);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // Update session
            session.TokenHash = HashToken(newRefreshToken);
            var refreshTokenExpirationDays = int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");
            session.ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenExpirationDays);
            await _db.SaveChangesAsync();

            return new RefreshTokenResponse
            {
                Success = true,
                Message = "Token refreshed successfully",
                Token = token,
                RefreshToken = newRefreshToken,
                ExpiresIn = 3600 // 1 hour in seconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return new RefreshTokenResponse
            {
                Success = false,
                Message = "An error occurred during token refresh"
            };
        }
    }

    public override async Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Logout request");

        try
        {
            // Get user ID from token
            var principal = _jwtService.GetPrincipalFromExpiredToken(request.Token);
            if (principal == null)
            {
                return new LogoutResponse
                {
                    Success = false,
                    Message = "Invalid token"
                };
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return new LogoutResponse
                {
                    Success = false,
                    Message = "Invalid token"
                };
            }

            // Remove all sessions for the user
            var sessions = await _db.Sessions.Where(s => s.UserId == userId).ToListAsync();
            _db.Sessions.RemoveRange(sessions);
            await _db.SaveChangesAsync();

            return new LogoutResponse
            {
                Success = true,
                Message = "Logout successful"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return new LogoutResponse
            {
                Success = false,
                Message = "An error occurred during logout"
            };
        }
    }

    public override async Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Get user profile request for user ID: {UserId}", request.UserId);

        try
        {
            var user = await _db.AuthUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == request.UserId);

            if (user == null)
            {
                return new GetUserProfileResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            return new GetUserProfileResponse
            {
                Success = true,
                Message = "User profile retrieved successfully",
                User = MapUserToProto(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile for ID: {UserId}", request.UserId);
            return new GetUserProfileResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user profile"
            };
        }
    }

    private static User MapUserToProto(AuthUser user)
    {
        return new User
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email ?? "",
            Role = user.Role?.Name ?? "user",
            IsActive = user.IsActive,
            LastLogin = user.LastLogin?.ToString("o") ?? "",
            CreatedAt = user.CreatedAt.ToString("o")
        };
    }

    private static string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashedBytes);
    }
}