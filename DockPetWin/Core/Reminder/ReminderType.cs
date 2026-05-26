namespace DockPetWin.Core.Reminder;

public enum ReminderType
{
    Water,
    Movement
}

public static class ReminderTypeExtensions
{
    public static string Message(this ReminderType type, string salutation)
    {
        return type switch
        {
            ReminderType.Water => $"{salutation}，该喝水啦。",
            ReminderType.Movement => $"{salutation}，起来走一走吧。",
            _ => $"{salutation}，休息一下吧。"
        };
    }
}
