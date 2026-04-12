namespace ActivityService.Services;

public sealed class ActivityRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 90;
    public int BatchSize { get; set; } = 1000;
    public int SweepIntervalMinutes { get; set; } = 30;
}
