namespace DockPetWin.Core.HomeLife;

public sealed record HomeActivityPlan(string ActionId, string DisplayText, int DurationMinutes = 15)
{
    public static HomeActivityPlan Idle(string petName, string userSalutation)
    {
        return new HomeActivityPlan("study_desk", $"{petName}背对书桌写小纸条。", 10);
    }
}
