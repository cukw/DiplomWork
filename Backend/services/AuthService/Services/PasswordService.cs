using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace AuthService.Services;

public interface IPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public class PasswordService : IPasswordService
{
    private readonly ILogger<PasswordService> _logger;

    public PasswordService(ILogger<PasswordService> logger)
    {
        _logger = logger;
    }

    public string HashPassword(string password)
    {
        // Generate a 128-bit salt using a secure PRNG
        byte[] salt = new byte[128 / 8];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // Derive a 256-bit subkey (use HMACSHA1 with 10,000 iterations)
        string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password!,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA1,
            iterationCount: 10000,
            numBytesRequested: 256 / 8));

        // Store both the salt and the hash
        return $"{Convert.ToBase64String(salt)}:{hashed}";
    }

    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            // Extract the salt from the stored hash
            var parts = hash.Split(':');
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid password hash format");
                return false;
            }

            var salt = Convert.FromBase64String(parts[0]);
            var storedHash = parts[1];

            // Compute the hash of the provided password
            string computedHash = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password!,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA1,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            // Compare the computed hash with the stored hash
            return computedHash == storedHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password");
            return false;
        }
    }
}