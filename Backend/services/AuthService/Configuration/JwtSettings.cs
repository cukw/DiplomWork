namespace AuthService.Configuration;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string ActiveKeyId { get; set; } = "legacy";
    public Dictionary<string, string> Keys { get; set; } = new();
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ExpirationMinutes { get; set; } = "60";
}
