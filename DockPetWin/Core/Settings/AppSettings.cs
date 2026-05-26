namespace DockPetWin.Core.Settings;

public sealed class AppSettings
{
    public string CatName { get; set; } = "爱弥斯";
    public string CatIdentifier { get; set; } = "Aemeath";
    public string UserSalutation { get; set; } = "漂泊者";
    public string SelectedAssetPackID { get; set; } = "my-pink-character";
    public bool RemindersEnabled { get; set; } = true;
    public double WaterReminderIntervalSeconds { get; set; } = 30 * 60;
    public double MovementReminderIntervalSeconds { get; set; } = 60 * 60;
    public bool ReminderSchemaInitialized { get; set; }
    public List<ReminderSettings> Reminders { get; set; } = [];
    public double RestDurationMinimumSeconds { get; set; } = 2 * 60;
    public double RestDurationMaximumSeconds { get; set; } = 5 * 60;
    public double WalkDurationMinimumSeconds { get; set; } = 2 * 60;
    public double WalkDurationMaximumSeconds { get; set; } = 5 * 60;
    public double WalkBaseSpeed { get; set; } = 36;
    public double CatScalePercent { get; set; } = 20;
    public double StartPositionPercent { get; set; } = 75;
    public string? ActivityDisplayID { get; set; }

    public static AppSettings Defaults => new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            CatName = CatName,
            CatIdentifier = CatIdentifier,
            UserSalutation = UserSalutation,
            SelectedAssetPackID = SelectedAssetPackID,
            RemindersEnabled = RemindersEnabled,
            WaterReminderIntervalSeconds = WaterReminderIntervalSeconds,
            MovementReminderIntervalSeconds = MovementReminderIntervalSeconds,
            ReminderSchemaInitialized = ReminderSchemaInitialized,
            Reminders = Reminders.Select(reminder => reminder.Clone()).ToList(),
            RestDurationMinimumSeconds = RestDurationMinimumSeconds,
            RestDurationMaximumSeconds = RestDurationMaximumSeconds,
            WalkDurationMinimumSeconds = WalkDurationMinimumSeconds,
            WalkDurationMaximumSeconds = WalkDurationMaximumSeconds,
            WalkBaseSpeed = WalkBaseSpeed,
            CatScalePercent = CatScalePercent,
            StartPositionPercent = StartPositionPercent,
            ActivityDisplayID = ActivityDisplayID
        };
    }

    public void Normalize()
    {
        CatName = string.IsNullOrWhiteSpace(CatName) ? "爱弥斯" : CatName.Trim();
        CatIdentifier = string.IsNullOrWhiteSpace(CatIdentifier) ? "Aemeath" : CatIdentifier.Trim();
        UserSalutation = string.IsNullOrWhiteSpace(UserSalutation) ? "漂泊者" : UserSalutation.Trim();
        SelectedAssetPackID = string.IsNullOrWhiteSpace(SelectedAssetPackID) ? "my-pink-character" : SelectedAssetPackID.Trim();
        ActivityDisplayID = string.IsNullOrWhiteSpace(ActivityDisplayID) ? null : ActivityDisplayID.Trim();
        CatScalePercent = Math.Clamp(CatScalePercent, 4, 100);
        StartPositionPercent = Math.Clamp(StartPositionPercent, 0, 100);
        WalkBaseSpeed = Math.Clamp(WalkBaseSpeed, 10, 180);

        RestDurationMinimumSeconds = Math.Clamp(RestDurationMinimumSeconds, 10, 24 * 60 * 60);
        RestDurationMaximumSeconds = Math.Clamp(RestDurationMaximumSeconds, RestDurationMinimumSeconds, 24 * 60 * 60);
        WalkDurationMinimumSeconds = Math.Clamp(WalkDurationMinimumSeconds, 10, 24 * 60 * 60);
        WalkDurationMaximumSeconds = Math.Clamp(WalkDurationMaximumSeconds, WalkDurationMinimumSeconds, 24 * 60 * 60);
        WaterReminderIntervalSeconds = Math.Clamp(WaterReminderIntervalSeconds, 60, 24 * 60 * 60);
        MovementReminderIntervalSeconds = Math.Clamp(MovementReminderIntervalSeconds, 60, 24 * 60 * 60);
        EnsureDefaultReminders();
        foreach (var reminder in Reminders)
        {
            reminder.Normalize(UserSalutation);
        }
    }

    private void EnsureDefaultReminders()
    {
        if (!ReminderSchemaInitialized && Reminders.Count == 0)
        {
            Reminders =
            [
                ReminderSettings.WaterDefault(WaterReminderIntervalSeconds, UserSalutation),
                ReminderSettings.MovementDefault(MovementReminderIntervalSeconds, UserSalutation)
            ];
        }

        ReminderSchemaInitialized = true;
    }
}

public sealed class ReminderSettings
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "custom";
    public bool Enabled { get; set; } = true;
    public string ActionType { get; set; } = "reminder";
    public string ScheduleType { get; set; } = "interval";
    public double IntervalSeconds { get; set; } = 60 * 60;
    public string TimeOfDay { get; set; } = "";
    public string DaysOfWeek { get; set; } = "";
    public int DayOfMonth { get; set; } = 1;
    public string FixedMessage { get; set; } = "";
    public bool UseAiMessage { get; set; }
    public string AiPrompt { get; set; } = "";
    public string TaskPrompt { get; set; } = "";
    public bool SaveOutput { get; set; } = true;
    public string OutputDirectory { get; set; } = "workspace/output/scheduled-tasks";

    public ReminderSettings Clone()
    {
        return new ReminderSettings
        {
            Id = Id,
            Title = Title,
            Category = Category,
            Enabled = Enabled,
            ActionType = ActionType,
            ScheduleType = ScheduleType,
            IntervalSeconds = IntervalSeconds,
            TimeOfDay = TimeOfDay,
            DaysOfWeek = DaysOfWeek,
            DayOfMonth = DayOfMonth,
            FixedMessage = FixedMessage,
            UseAiMessage = UseAiMessage,
            AiPrompt = AiPrompt,
            TaskPrompt = TaskPrompt,
            SaveOutput = SaveOutput,
            OutputDirectory = OutputDirectory
        };
    }

    public void Normalize(string salutation)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? $"reminder-{Guid.NewGuid():N}"[..22] : SanitizeId(Id);
        Title = string.IsNullOrWhiteSpace(Title) ? "提醒" : Title.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? "custom" : Category.Trim().ToLowerInvariant();
        ActionType = NormalizeChoice(ActionType, "reminder", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "reminder", "agent_task" });
        ScheduleType = NormalizeChoice(ScheduleType, "interval", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "interval", "daily", "weekly", "monthly" });
        IntervalSeconds = Math.Clamp(IntervalSeconds, 60, 366 * 24 * 60 * 60);
        DayOfMonth = Math.Clamp(DayOfMonth, 1, 31);
        TimeOfDay = NormalizeTimeOfDay(TimeOfDay);
        DaysOfWeek = string.IsNullOrWhiteSpace(DaysOfWeek) ? "mon,tue,wed,thu,fri,sat,sun" : DaysOfWeek.Trim().ToLowerInvariant();
        FixedMessage = string.IsNullOrWhiteSpace(FixedMessage)
            ? $"{salutation}，休息一下吧。"
            : FixedMessage.Trim();
        AiPrompt = string.IsNullOrWhiteSpace(AiPrompt)
            ? $"请用爱弥斯的语气写一句简短提醒：{Title}。称呼用户为{salutation}。"
            : AiPrompt.Trim();
        TaskPrompt = string.IsNullOrWhiteSpace(TaskPrompt) ? FixedMessage : TaskPrompt.Trim();
        OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? "workspace/output/scheduled-tasks" : OutputDirectory.Trim();
    }

    public static ReminderSettings WaterDefault(double intervalSeconds, string salutation)
    {
        return new ReminderSettings
        {
            Id = "water",
            Title = "喝水提醒",
            Category = "water",
            Enabled = true,
            ActionType = "reminder",
            ScheduleType = "interval",
            IntervalSeconds = intervalSeconds,
            FixedMessage = $"{salutation}，该喝水啦。",
            UseAiMessage = false,
            AiPrompt = $"请用爱弥斯的语气写一句简短喝水提醒，称呼用户为{salutation}。"
        };
    }

    public static ReminderSettings MovementDefault(double intervalSeconds, string salutation)
    {
        return new ReminderSettings
        {
            Id = "movement",
            Title = "走动提醒",
            Category = "movement",
            Enabled = true,
            ActionType = "reminder",
            ScheduleType = "interval",
            IntervalSeconds = intervalSeconds,
            FixedMessage = $"{salutation}，起来走一走吧。",
            UseAiMessage = false,
            AiPrompt = $"请用爱弥斯的语气写一句简短走动提醒，称呼用户为{salutation}。"
        };
    }

    private static string NormalizeChoice(string value, string fallback, IReadOnlySet<string> allowed)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static string NormalizeTimeOfDay(string value)
    {
        if (TimeSpan.TryParse(value, out var parsed))
        {
            return parsed.ToString(@"hh\:mm");
        }

        return "09:00";
    }

    private static string SanitizeId(string value)
    {
        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? $"reminder-{Guid.NewGuid():N}"[..22] : id;
    }
}
