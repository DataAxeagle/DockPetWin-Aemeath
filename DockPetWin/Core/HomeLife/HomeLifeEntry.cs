namespace DockPetWin.Core.HomeLife;

public sealed class HomeLifeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Activity { get; set; } = "";
    public string Details { get; set; } = "";
    public string Mood { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime EndedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; }
    public string Trigger { get; set; } = "self";
    public bool InterruptedByUser { get; set; }
}
