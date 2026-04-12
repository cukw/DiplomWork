using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Models;

namespace AuthService.Services;

public interface IJwtService
{
    string GenerateToken(AuthUser user);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    bool ValidateToken(string token);
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string GenerateToken(AuthUser user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var (activeKeyId, activeKey) = GetActiveSigningKey();
        
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role?.Name ?? "user")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:ExpirationMinutes"] ?? "60")),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(activeKey, SecurityAlgorithms.HmacSha256Signature),
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["kid"] = activeKeyId
            }
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);
        
        _logger.LogInformation("Generated JWT token for user {UserId}. Token length: {Length}, Token preview: {Preview}",
            user.Id, tokenString.Length, tokenString.Substring(0, Math.Min(50, tokenString.Length)));
        
        return tokenString;
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, BuildValidationParameters(validateLifetime: false), out SecurityToken securityToken);
            
            if (securityToken is not JwtSecurityToken jwtSecurityToken || 
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogWarning("Invalid token algorithm");
                return null;
            }

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating expired token");
            return null;
        }
    }

    public bool ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        
        try
        {
            tokenHandler.ValidateToken(token, BuildValidationParameters(validateLifetime: true), out SecurityToken validatedToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            return false;
        }
    }

    private TokenValidationParameters BuildValidationParameters(bool validateLifetime)
    {
        var keyRing = GetJwtKeyRing();
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = validateLifetime,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidateAudience = validateLifetime,
            ValidAudience = _configuration["Jwt:Audience"],
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKeyResolver = (_, _, kid, _) => ResolveSigningKeys(keyRing, kid)
        };
    }

    private (string keyId, SymmetricSecurityKey key) GetActiveSigningKey()
    {
        var keyRing = GetJwtKeyRing();
        var configuredActiveKeyId = _configuration["Jwt:ActiveKeyId"]?.Trim();

        if (!string.IsNullOrWhiteSpace(configuredActiveKeyId) && keyRing.TryGetValue(configuredActiveKeyId, out var configuredSecret))
        {
            return (configuredActiveKeyId, BuildSecurityKey(configuredSecret));
        }

        var first = keyRing.First();
        return (first.Key, BuildSecurityKey(first.Value));
    }

    private IDictionary<string, string> GetJwtKeyRing()
    {
        var keyRing = _configuration
            .GetSection("Jwt:Keys")
            .GetChildren()
            .Where(section => !string.IsNullOrWhiteSpace(section.Key) && !string.IsNullOrWhiteSpace(section.Value))
            .ToDictionary(section => section.Key.Trim(), section => section.Value!.Trim(), StringComparer.Ordinal);

        if (keyRing.Count > 0)
            return keyRing;

        var legacyKey = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(legacyKey))
            throw new InvalidOperationException("JWT keys are not configured. Set Jwt:Keys or Jwt:Key.");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["legacy"] = legacyKey.Trim()
        };
    }

    private static IEnumerable<SecurityKey> ResolveSigningKeys(IDictionary<string, string> keyRing, string? keyId)
    {
        if (!string.IsNullOrWhiteSpace(keyId) && keyRing.TryGetValue(keyId, out var specificSecret))
            return [BuildSecurityKey(specificSecret)];

        return keyRing.Values.Select(BuildSecurityKey).ToArray();
    }

    private static SymmetricSecurityKey BuildSecurityKey(string secret)
    {
        return new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
    }
}
