namespace DockPetWin.Core.HomeLife;

public sealed class HomeScheduleState
{
    public List<HomeActivityPlan> Schedule { get; set; } = [];
    public DateTime ScheduleStartedAt { get; set; } = DateTime.Now;
    public DateTime ScheduleExpiresAt { get; set; } = DateTime.Now;
    public int CurrentIndex { get; set; }
    public DateTime CurrentStartedAt { get; set; } = DateTime.Now;
}
