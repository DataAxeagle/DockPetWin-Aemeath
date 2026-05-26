namespace DockPetWin.Core.Statistics;

public sealed class UsageStatistics
{
    public DateTime FirstStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public double TotalCompanionSeconds { get; set; }
    public int CompletedWaterReminders { get; set; }
    public int CompletedMovementReminders { get; set; }

    public UsageStatistics Clone()
    {
        return new UsageStatistics
        {
            FirstStartedAt = FirstStartedAt,
            LastUpdatedAt = LastUpdatedAt,
            TotalCompanionSeconds = TotalCompanionSeconds,
            CompletedWaterReminders = CompletedWaterReminders,
            CompletedMovementReminders = CompletedMovementReminders
        };
    }
}
