namespace Gateway.Models;

public sealed class AlertRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Severity { get; set; } = "medium";
    public string Metric { get; set; } = "anomaly_count";
    public string Operator { get; set; } = "gte";
    public decimal Threshold { get; set; } = 1;
    public int WindowMinutes { get; set; } = 15;
    public string? ActivityType { get; set; }
    public int? UserId { get; set; }
    public int? ComputerId { get; set; }
    public bool NotifyInApp { get; set; } = true;
    public bool NotifyEmail { get; set; }
    public int CooldownMinutes { get; set; } = 10;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
