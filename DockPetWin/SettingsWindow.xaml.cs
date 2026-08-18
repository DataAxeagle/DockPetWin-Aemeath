using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DockPetWin.Core.Agents;
using DockPetWin.Core.Settings;
using DockPetWin.Core.Statistics;
using DockPetWin.Platform;

namespace DockPetWin;

public partial class SettingsWindow : Window
{
    private readonly Func<IEnumerable<string>> assetPackIDsProvider;
    private readonly Func<string, string> assetPackStatusProvider;
    private readonly string assetPacksRoot;
    private readonly AgentStore agentStore = new();
    private readonly ObservableCollection<ReminderEditorRow> reminderRows = [];
    private AgentChatSettings agentSettings = new();

    public SettingsWindow(
        AppSettings settings,
        IEnumerable<string> assetPackIDs,
        Func<IEnumerable<string>> assetPackIDsProvider,
        Func<string, string> assetPackStatusProvider,
        string assetPacksRoot,
        UsageStatistics statistics,
        bool focusAiSettings = false)
    {
        InitializeComponent();
        Icon = AppImageLoader.TryLoad(AppImageLoader.AppIconPath);
        this.assetPackIDsProvider = assetPackIDsProvider;
        this.assetPackStatusProvider = assetPackStatusProvider;
        this.assetPacksRoot = assetPacksRoot;
        Statistics = statistics.Clone();
        Settings = settings.Clone();
        Settings.Normalize();
        agentSettings = agentStore.LoadSettings();
        ReminderGrid.ItemsSource = reminderRows;
        PopulateAssetPacks(assetPackIDs);
        Populate(Settings);
        if (focusAiSettings)
        {
            Loaded += (_, _) =>
            {
                SettingsTabs.SelectedIndex = 1;
                AiApiKeyBox.Focus();
            };
        }
    }

    public AppSettings Settings { get; private set; }
    public UsageStatistics Statistics { get; }
    public IReadOnlyList<OptionChoice> ActionTypeOptions { get; } =
    [
        new("reminder", "只提醒"),
        new("agent_task", "执行任务")
    ];
    public IReadOnlyList<OptionChoice> ScheduleTypeOptions { get; } =
    [
        new("interval", "间隔"),
        new("daily", "每日"),
        new("weekly", "每周"),
        new("monthly", "每月")
    ];
    public IReadOnlyList<OptionChoice> SearchProviderOptions { get; } =
    [
        new("tavily", "Tavily 搜索"),
        new("bing", "Bing 搜索")
    ];
    public IReadOnlyList<string> WeekdayOptions { get; } = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
    public IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, 24).ToList();
    public IReadOnlyList<int> MinuteOptions { get; } = Enumerable.Range(0, 60).ToList();
    public IReadOnlyList<int> MonthDayOptions { get; } = Enumerable.Range(1, 31).ToList();

    private void Populate(AppSettings settings)
    {
        CatNameBox.Text = PreferAgentValue(agentSettings.PetName, settings.CatName);
        CatIdentifierBox.Text = PreferAgentValue(agentSettings.PetIdentifier, settings.CatIdentifier);
        AssetPackBox.Text = settings.SelectedAssetPackID;
        UserSalutationBox.Text = PreferAgentValue(agentSettings.UserSalutation, settings.UserSalutation);
        PopulateDisplays(settings.ActivityDisplayID);
        CatScaleBox.Text = Format(settings.CatScalePercent);
        StartPositionBox.Text = Format(settings.StartPositionPercent);
        RestMinBox.Text = Format(settings.RestDurationMinimumSeconds / 60);
        RestMaxBox.Text = Format(settings.RestDurationMaximumSeconds / 60);
        WalkMinBox.Text = Format(settings.WalkDurationMinimumSeconds / 60);
        WalkMaxBox.Text = Format(settings.WalkDurationMaximumSeconds / 60);
        QQMusicSingingBox.IsChecked = settings.EnableQQMusicSinging;
        RemindersEnabledBox.IsChecked = settings.RemindersEnabled;
        reminderRows.Clear();
        foreach (var reminder in settings.Reminders)
        {
            reminderRows.Add(ReminderEditorRow.FromSettings(reminder));
        }

        AiBaseUrlBox.Text = agentSettings.BaseUrl;
        AiModelBox.Text = agentSettings.Model;
        AiApiKeyBox.Password = agentSettings.ApiKey;
        AiApiKeyEnvBox.Text = agentSettings.ApiKeyEnv;
        SearchProviderBox.SelectedValue = string.IsNullOrWhiteSpace(agentSettings.SearchProvider)
            ? "tavily"
            : agentSettings.SearchProvider;
        TavilyApiKeyBox.Password = agentSettings.TavilyApiKey;
        TavilyApiKeyEnvBox.Text = string.IsNullOrWhiteSpace(agentSettings.TavilyApiKeyEnv)
            ? "TAVILY_API_KEY"
            : agentSettings.TavilyApiKeyEnv;
        StatisticsText.Text = StatisticsTextValue();
        UpdateAssetPackStatus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var updated))
        {
            System.Windows.MessageBox.Show(this, "请检查数值设置。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings = updated;
        SaveAgentSettings();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RefreshAssetPacks_Click(object sender, RoutedEventArgs e)
    {
        PopulateAssetPacks(assetPackIDsProvider());
        UpdateAssetPackStatus();
    }

    private void OpenAssetFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(assetPacksRoot);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = assetPacksRoot,
            UseShellExecute = true
        });
    }

    private void OpenAgentsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(agentStore.RootDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = agentStore.RootDirectory,
            UseShellExecute = true
        });
    }

    private void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        var salutation = string.IsNullOrWhiteSpace(UserSalutationBox.Text) ? "漂泊者" : UserSalutationBox.Text.Trim();
        reminderRows.Add(new ReminderEditorRow
        {
            Id = $"custom-{DateTime.Now:yyyyMMddHHmmss}",
            Title = "新提醒",
            Category = "custom",
            Enabled = true,
            ActionType = "reminder",
            ScheduleType = "interval",
            IntervalMinutes = 60,
            TimeHour = 9,
            TimeMinute = 0,
            Monday = true,
            DayOfMonth = 1,
            FixedMessage = $"{salutation}，休息一下吧。",
            UseAiMessage = false,
            AiPrompt = $"用爱弥斯的语气写一句简短提醒，称呼用户为{salutation}。",
            TaskPrompt = "到点提醒用户。",
            SaveOutput = true,
            OutputDirectory = "workspace/output/scheduled-tasks"
        });
        ReminderGrid.SelectedIndex = reminderRows.Count - 1;
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderGrid.SelectedItem is ReminderEditorRow selected)
        {
            reminderRows.Remove(selected);
        }
    }

    private void AssetPackBox_Changed(object sender, EventArgs e)
    {
        UpdateAssetPackStatus();
    }

    private bool TryReadSettings(out AppSettings settings)
    {
        settings = Settings.Clone();

        if (!TryReadDouble(CatScaleBox.Text, out var scale)
            || !TryReadDouble(StartPositionBox.Text, out var startPosition)
            || !TryReadDouble(RestMinBox.Text, out var restMin)
            || !TryReadDouble(RestMaxBox.Text, out var restMax)
            || !TryReadDouble(WalkMinBox.Text, out var walkMin)
            || !TryReadDouble(WalkMaxBox.Text, out var walkMax)
            || reminderRows.Any(row => row.IntervalMinutes <= 0 || row.DayOfMonth < 1 || row.DayOfMonth > 31))
        {
            return false;
        }

        settings.CatName = CatNameBox.Text;
        settings.CatIdentifier = CatIdentifierBox.Text;
        settings.SelectedAssetPackID = AssetPackBox.Text;
        settings.UserSalutation = UserSalutationBox.Text;
        settings.ActivityDisplayID = DisplayBox.SelectedValue as string;
        settings.CatScalePercent = scale;
        settings.StartPositionPercent = startPosition;
        settings.RestDurationMinimumSeconds = restMin * 60;
        settings.RestDurationMaximumSeconds = restMax * 60;
        settings.WalkDurationMinimumSeconds = walkMin * 60;
        settings.WalkDurationMaximumSeconds = walkMax * 60;
        settings.EnableQQMusicSinging = QQMusicSingingBox.IsChecked == true;
        settings.RemindersEnabled = RemindersEnabledBox.IsChecked == true;
        settings.ReminderSchemaInitialized = true;
        settings.Reminders = reminderRows
            .Select(row => row.ToSettings())
            .ToList();
        SyncLegacyReminderIntervals(settings);
        settings.Normalize();
        return true;
    }

    private void SaveAgentSettings()
    {
        agentSettings.Provider = "deepseek";
        agentSettings.PetName = Settings.CatName;
        agentSettings.PetIdentifier = Settings.CatIdentifier;
        agentSettings.UserSalutation = Settings.UserSalutation;
        agentSettings.BaseUrl = string.IsNullOrWhiteSpace(AiBaseUrlBox.Text)
            ? "https://api.deepseek.com"
            : AiBaseUrlBox.Text.Trim();
        agentSettings.Model = string.IsNullOrWhiteSpace(AiModelBox.Text)
            ? "deepseek-v4-flash"
            : AiModelBox.Text.Trim();
        agentSettings.ApiKey = AiApiKeyBox.Password.Trim();
        agentSettings.ApiKeyEnv = string.IsNullOrWhiteSpace(AiApiKeyEnvBox.Text)
            ? "DEEPSEEK_API_KEY"
            : AiApiKeyEnvBox.Text.Trim();
        agentSettings.SearchProvider = SearchProviderBox.SelectedValue as string ?? "tavily";
        agentSettings.TavilyApiKey = TavilyApiKeyBox.Password.Trim();
        agentSettings.TavilyApiKeyEnv = string.IsNullOrWhiteSpace(TavilyApiKeyEnvBox.Text)
            ? "TAVILY_API_KEY"
            : TavilyApiKeyEnvBox.Text.Trim();
        agentStore.SaveSettings(agentSettings);
    }

    private static void SyncLegacyReminderIntervals(AppSettings settings)
    {
        var water = settings.Reminders.FirstOrDefault(item =>
            string.Equals(item.Id, "water", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Category, "water", StringComparison.OrdinalIgnoreCase));
        if (water is not null)
        {
            settings.WaterReminderIntervalSeconds = water.IntervalSeconds;
        }

        var movement = settings.Reminders.FirstOrDefault(item =>
            string.Equals(item.Id, "movement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Category, "movement", StringComparison.OrdinalIgnoreCase));
        if (movement is not null)
        {
            settings.MovementReminderIntervalSeconds = movement.IntervalSeconds;
        }
    }

    private void PopulateAssetPacks(IEnumerable<string> assetPackIDs)
    {
        var current = AssetPackBox.Text;
        AssetPackBox.Items.Clear();
        AssetPackBox.Items.Add("default-lizz");
        foreach (var id in assetPackIDs.Where(id => id != "default-lizz").Distinct().Order(StringComparer.OrdinalIgnoreCase))
        {
            AssetPackBox.Items.Add(id);
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            AssetPackBox.Text = current;
        }
    }

    private void PopulateDisplays(string? selectedDisplayID)
    {
        DisplayBox.Items.Clear();
        DisplayBox.Items.Add(new DisplayOption("", "主显示器"));
        foreach (var option in TaskbarGeometry.DisplayOptions())
        {
            DisplayBox.Items.Add(option);
        }

        DisplayBox.SelectedValue = selectedDisplayID ?? "";
        if (DisplayBox.SelectedIndex < 0)
        {
            DisplayBox.SelectedIndex = 0;
        }
    }

    private string StatisticsTextValue()
    {
        var total = TimeSpan.FromSeconds(Statistics.TotalCompanionSeconds);
        return $"陪伴 {Math.Floor(total.TotalHours):0}小时{total.Minutes:00}分钟，喝水完成 {Statistics.CompletedWaterReminders} 次，走动完成 {Statistics.CompletedMovementReminders} 次";
    }

    private void UpdateAssetPackStatus()
    {
        if (AssetPackStatusText is null)
        {
            return;
        }

        AssetPackStatusText.Text = assetPackStatusProvider(AssetPackBox.Text);
    }

    private static bool TryReadDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static string PreferAgentValue(string agentValue, string appValue)
    {
        return string.IsNullOrWhiteSpace(agentValue) ? appValue : agentValue.Trim();
    }
}

public sealed record OptionChoice(string Value, string Label);

public sealed class ReminderEditorRow : INotifyPropertyChanged
{
    private string scheduleType = "interval";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "custom";
    public bool Enabled { get; set; } = true;
    public string ActionType { get; set; } = "reminder";

    public string ScheduleType
    {
        get => scheduleType;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "interval" : value.Trim();
            if (string.Equals(scheduleType, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            scheduleType = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWeekly));
            OnPropertyChanged(nameof(IsMonthly));
        }
    }

    public double IntervalMinutes { get; set; } = 60;
    public int TimeHour { get; set; } = 9;
    public int TimeMinute { get; set; }
    public bool Monday { get; set; } = true;
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public string FixedMessage { get; set; } = "";
    public bool UseAiMessage { get; set; }
    public string AiPrompt { get; set; } = "";
    public string TaskPrompt { get; set; } = "";
    public bool SaveOutput { get; set; } = true;
    public string OutputDirectory { get; set; } = "workspace/output/scheduled-tasks";

    public bool IsWeekly => string.Equals(ScheduleType, "weekly", StringComparison.OrdinalIgnoreCase);
    public bool IsMonthly => string.Equals(ScheduleType, "monthly", StringComparison.OrdinalIgnoreCase);

    public static ReminderEditorRow FromSettings(ReminderSettings settings)
    {
        var row = new ReminderEditorRow
        {
            Id = settings.Id,
            Title = settings.Title,
            Category = settings.Category,
            Enabled = settings.Enabled,
            ActionType = settings.ActionType,
            ScheduleType = settings.ScheduleType,
            IntervalMinutes = Math.Round(settings.IntervalSeconds / 60, 2),
            TimeHour = ParseTimeHour(settings.TimeOfDay),
            TimeMinute = ParseTimeMinute(settings.TimeOfDay),
            DayOfMonth = settings.DayOfMonth,
            FixedMessage = settings.FixedMessage,
            UseAiMessage = settings.UseAiMessage,
            AiPrompt = settings.AiPrompt,
            TaskPrompt = settings.TaskPrompt,
            SaveOutput = settings.SaveOutput,
            OutputDirectory = settings.OutputDirectory
        };
        row.ApplyDays(settings.DaysOfWeek);
        return row;
    }

    public ReminderSettings ToSettings()
    {
        return new ReminderSettings
        {
            Id = Id,
            Title = Title,
            Category = Category,
            Enabled = Enabled,
            ActionType = ActionType,
            ScheduleType = ScheduleType,
            IntervalSeconds = IntervalMinutes * 60,
            TimeOfDay = $"{Math.Clamp(TimeHour, 0, 23):00}:{Math.Clamp(TimeMinute, 0, 59):00}",
            DaysOfWeek = ComposeDaysOfWeek(),
            DayOfMonth = DayOfMonth,
            FixedMessage = FixedMessage,
            UseAiMessage = UseAiMessage,
            AiPrompt = AiPrompt,
            TaskPrompt = TaskPrompt,
            SaveOutput = SaveOutput,
            OutputDirectory = OutputDirectory
        };
    }

    private static int ParseTimeHour(string value)
    {
        return TimeSpan.TryParse(value, out var parsed) ? Math.Clamp(parsed.Hours, 0, 23) : 9;
    }

    private static int ParseTimeMinute(string value)
    {
        return TimeSpan.TryParse(value, out var parsed) ? Math.Clamp(parsed.Minutes, 0, 59) : 0;
    }

    private void ApplyDays(string value)
    {
        Monday = Tuesday = Wednesday = Thursday = Friday = Saturday = Sunday = false;

        foreach (var token in (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "mon":
                case "monday":
                case "1":
                case "周一":
                case "星期一":
                    Monday = true;
                    break;
                case "tue":
                case "tuesday":
                case "2":
                case "周二":
                case "星期二":
                    Tuesday = true;
                    break;
                case "wed":
                case "wednesday":
                case "3":
                case "周三":
                case "星期三":
                    Wednesday = true;
                    break;
                case "thu":
                case "thursday":
                case "4":
                case "周四":
                case "星期四":
                    Thursday = true;
                    break;
                case "fri":
                case "friday":
                case "5":
                case "周五":
                case "星期五":
                    Friday = true;
                    break;
                case "sat":
                case "saturday":
                case "6":
                case "周六":
                case "星期六":
                    Saturday = true;
                    break;
                case "sun":
                case "sunday":
                case "0":
                case "7":
                case "周日":
                case "周天":
                case "星期日":
                case "星期天":
                    Sunday = true;
                    break;
            }
        }

        if (!Monday && !Tuesday && !Wednesday && !Thursday && !Friday && !Saturday && !Sunday)
        {
            Monday = true;
        }
    }

    private string ComposeDaysOfWeek()
    {
        var days = new List<string>();
        if (Monday) days.Add("mon");
        if (Tuesday) days.Add("tue");
        if (Wednesday) days.Add("wed");
        if (Thursday) days.Add("thu");
        if (Friday) days.Add("fri");
        if (Saturday) days.Add("sat");
        if (Sunday) days.Add("sun");
        return days.Count == 0 ? "mon" : string.Join(",", days);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
