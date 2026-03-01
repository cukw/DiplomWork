namespace Gateway.Models;

public sealed class AppSettingsDocumentEntity
{
    public int Id { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
