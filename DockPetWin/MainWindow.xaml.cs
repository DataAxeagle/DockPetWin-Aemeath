using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using DockPetWin.Core.Assets;
using DockPetWin.Core.Backup;
using DockPetWin.Core.CodexBridge;
using DockPetWin.Core.Agents;
using DockPetWin.Core.HomeLife;
using DockPetWin.Core.Reminder;
using DockPetWin.Core.Settings;
using DockPetWin.Core.StateMachine;
using DockPetWin.Core.Statistics;
using DockPetWin.Platform;
using DockPetWin.UI.PetWindow;
using DockPetWin.UI.Tray;
using WpfPoint = System.Windows.Point;

namespace DockPetWin;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer animationTimer = new();
    private readonly DispatcherTimer movementTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer stateTimer = new();
    private readonly DispatcherTimer reminderTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer statisticsTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private static readonly System.Text.Json.JsonSerializerOptions TaskRecordJsonOptions = new() { WriteIndented = false };
    private readonly DispatcherTimer restingBlinkTimer = new();
    private readonly DispatcherTimer restingBlinkFrameTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer codexBridgeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer bubbleAutoHideTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool apiSetupHintShown;
    private readonly SettingsStore settingsStore = new();
    private readonly UsageStatisticsStore usageStatisticsStore = new();
    private readonly UserDataBackupStore userDataBackupStore = new();
    private readonly AssetPackLoader assetPackLoader = new();
    private readonly CodexBridgeStore codexBridgeStore = new();
    private readonly AgentStore agentStore = new();
    private readonly AgentChatClient agentChatClient = new();
    private readonly HomeLifeStore homeLifeStore = new();
    private readonly CatStateMachine stateMachine = new();
    private readonly Random random = new();

    private AppSettings settings = AppSettings.Defaults;
    private CatAssetPack assetPack = null!;
    private PetWindowController petWindow = null!;
    private ReminderScheduler reminderScheduler = null!;
    private TrayIconController trayIcon = null!;
    private UsageStatistics usageStatistics = new();
    private TaskbarActivityArea activityArea;
    private IReadOnlyList<BitmapImage> activeFrames = [];
    private IReadOnlyList<BitmapImage> restingBlinkFrames = [];
    private int frameIndex;
    private int restingBlinkFrameIndex;
    private int direction = 1;
    private bool isExitRequested;
    private ReminderSettings? activeReminder;
    private bool isPollingReminder;
    private CodexBridgeMessage? activeCodexNotification;
    private AgentChatWindow? agentChatWindow;
    private HomeWindow? homeWindow;
    private string? lastScheduledTaskOutputPath;
    private IReadOnlyList<HomeActivityPlan>? cachedHomeSchedule;
    private DateTime cachedHomeScheduleExpiresAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppImageLoader.TryLoad(AppImageLoader.AppIconPath);

        Loaded += OnLoaded;
        animationTimer.Tick += (_, _) => AdvanceAnimation();
        movementTimer.Tick += (_, _) => AdvancePosition();
        stateTimer.Tick += (_, _) =>
        {
            stateTimer.Stop();
            if (stateTimer.Tag is CatState scheduledState)
            {
                stateMachine.FinishScheduledState(scheduledState);
            }
        };
        stateMachine.Transitioned += (_, newState) => ApplyState(newState);
        stateMachine.DurationScheduled += ScheduleStateTimer;
        reminderTimer.Tick += async (_, _) => await PollRemindersAsync();
        reminderTimer.Tick += (_, _) => UpdateTray();
        statisticsTimer.Tick += (_, _) => RecordCompanionMinute();
        restingBlinkTimer.Tick += (_, _) => BeginRestingBlink();
        restingBlinkFrameTimer.Tick += (_, _) => AdvanceRestingBlink();
        codexBridgeTimer.Tick += (_, _) => PollCodexBridge();
        bubbleAutoHideTimer.Tick += (_, _) =>
        {
            bubbleAutoHideTimer.Stop();
            if (activeReminder is null && activeCodexNotification is null)
            {
                HideBubble();
            }
        };
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            SaveUserData();
            trayIcon?.Dispose();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        settings = settingsStore.Load();
        _ = agentStore.LoadProfile();
        _ = agentStore.LoadSettings();
        usageStatistics = usageStatisticsStore.Load();
        assetPack = assetPackLoader.LoadSelectedPack(settings.SelectedAssetPackID);
        reminderScheduler = new ReminderScheduler(settings);
        trayIcon = new TrayIconController(assetPack.DialoguePoses.FirstOrDefault()?.UriSource.LocalPath);
        trayIcon.PetRequested += () => InvokeOnUiThread(() => stateMachine.Pet());
        trayIcon.ToggleStateRequested += () => InvokeOnUiThread(() => stateMachine.ToggleLongDurationState());
        trayIcon.AgentChatRequested += () => InvokeOnUiThread(() => ShowAgentChatWindow());
        trayIcon.HomeRequested += () => InvokeOnUiThread(() => ShowHomeWindow());
        trayIcon.ClearCodexNotificationsRequested += () => InvokeOnUiThread(() => ClearCodexNotification());
        trayIcon.SettingsRequested += () => InvokeOnUiThread(() => ShowSettingsWindow());
        trayIcon.ToggleVisibilityRequested += () => InvokeOnUiThread(() => ToggleVisibilityFromTray());
        trayIcon.RestartRequested += () => InvokeOnUiThread(() => RestartApplication());
        trayIcon.ExitRequested += () => InvokeOnUiThread(() => ExitApplication());
        petWindow = new PetWindowController(
            this,
            CatImage,
            MirrorTransform,
            assetPack.DefaultSourceSize);

        ApplySettings(reposition: true);
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        stateMachine.Start();
        movementTimer.Start();
        reminderTimer.Start();
        statisticsTimer.Start();
        codexBridgeTimer.Start();
        UpdateTray();
        Dispatcher.BeginInvoke(MaybeShowApiSetupHint, DispatcherPriority.Background);
    }

    private void InvokeOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
    }

    private void MaybeShowApiSetupHint()
    {
        if (apiSetupHintShown)
        {
            return;
        }

        var agentSettings = agentStore.LoadSettings();
        if (IsAgentApiConfigured())
        {
            return;
        }

        apiSetupHintShown = true;
        SetRandomImage(assetPack.DialoguePoses);
        ShowBubble(
            "漂泊者，拉海洛的频率已经接上了。\n不过我还听不清你的回声：先把 DeepSeek API Key 交给这个终端，我才能真正回应你。\n如果你还想让我替你去星海里查找消息，再补上 Tavily Key 就好。",
            ("去设置", () => ShowSettingsWindow(focusAiSettings: true)),
            ("稍后", HideBubble));
    }

    private void ApplySettings(bool reposition)
    {
        settings.Normalize();
        petWindow.SetImageScale(settings.CatScalePercent);

        var dpi = VisualTreeHelper.GetDpi(this);
        activityArea = TaskbarGeometry.Current(dpi.DpiScaleX, dpi.DpiScaleY, settings.ActivityDisplayID);
        stateMachine.UpdateDurations(
            TimeSpan.FromSeconds(settings.WalkDurationMinimumSeconds),
            TimeSpan.FromSeconds(settings.WalkDurationMaximumSeconds),
            TimeSpan.FromSeconds(settings.RestDurationMinimumSeconds),
            TimeSpan.FromSeconds(settings.RestDurationMaximumSeconds));

        if (reposition)
        {
            var anchor = activityArea.AnchorForPercent(settings.StartPositionPercent, petWindow.PetSize);
            petWindow.SetAnchor(anchor, activityArea.Edge);
        }
        UpdateTray();
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var anchor = petWindow.CurrentAnchor(activityArea.Edge);
            ApplySettings(reposition: false);
            petWindow.SetAnchor(activityArea.ClampAnchor(anchor, petWindow.PetSize), activityArea.Edge);
        });
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                SaveUserData();
                return;
            }

            if (e.Mode != PowerModes.Resume)
            {
                return;
            }

            var anchor = petWindow.CurrentAnchor(activityArea.Edge);
            ApplySettings(reposition: false);
            petWindow.SetAnchor(activityArea.ClampAnchor(anchor, petWindow.PetSize), activityArea.Edge);
        });
    }

    private void ApplyState(CatState state)
    {
        animationTimer.Stop();
        StopRestingBlink();
        activeFrames = [];
        frameIndex = 0;
        ApplyStateSourceSize(state.Kind);

        switch (state.Kind)
        {
            case CatStateKind.Walking:
                activeFrames = assetPack.WalkFrames;
                animationTimer.Interval = TimeSpan.FromSeconds(1 / assetPack.WalkFps);
                SetFrame(0);
                animationTimer.Start();
                break;
            case CatStateKind.Resting:
                StartRestingPose();
                break;
            case CatStateKind.Transitioning:
                SetRandomImage(assetPack.TransitionPoses);
                break;
            case CatStateKind.Dragged:
                SetRandomImage(assetPack.HeldPoses);
                break;
        }
        UpdateTray();
    }

    private void ScheduleStateTimer(CatState scheduledState, TimeSpan duration)
    {
        stateTimer.Stop();
        stateTimer.Interval = duration;
        stateTimer.Tag = scheduledState;
        stateTimer.Start();
    }

    private void AdvanceAnimation()
    {
        if (activeFrames.Count == 0)
        {
            return;
        }

        frameIndex = (frameIndex + 1) % activeFrames.Count;
        SetFrame(frameIndex);
    }

    private void StartRestingPose()
    {
        var blinkFrames = assetPack.RestingBlinkFrames;
        if (blinkFrames.Count >= 2)
        {
            restingBlinkFrames = BuildRestingBlinkSequence(blinkFrames);
            petWindow.SetImage(restingBlinkFrames[0]);
            ScheduleNextRestingBlink();
            return;
        }

        var basePoses = assetPack.RestingBasePoses;
        SetRandomImage(basePoses.Count > 0 ? basePoses : assetPack.RestingPoses);
    }

    private static IReadOnlyList<BitmapImage> BuildRestingBlinkSequence(IReadOnlyList<BitmapImage> frames)
    {
        if (frames.Count == 2)
        {
            return [frames[0], frames[1], frames[0]];
        }

        return [frames[0], frames[1], frames[2], frames[1], frames[0]];
    }

    private void ScheduleNextRestingBlink()
    {
        restingBlinkTimer.Stop();
        restingBlinkTimer.Interval = TimeSpan.FromSeconds(random.Next(4, 9));
        restingBlinkTimer.Start();
    }

    private void BeginRestingBlink()
    {
        if (stateMachine.State.Kind != CatStateKind.Resting || restingBlinkFrames.Count < 2)
        {
            StopRestingBlink();
            return;
        }

        restingBlinkTimer.Stop();
        restingBlinkFrameIndex = 0;
        petWindow.SetImage(restingBlinkFrames[restingBlinkFrameIndex]);
        restingBlinkFrameTimer.Start();
    }

    private void AdvanceRestingBlink()
    {
        if (stateMachine.State.Kind != CatStateKind.Resting || restingBlinkFrames.Count < 2)
        {
            StopRestingBlink();
            return;
        }

        restingBlinkFrameIndex++;
        if (restingBlinkFrameIndex >= restingBlinkFrames.Count)
        {
            restingBlinkFrameTimer.Stop();
            petWindow.SetImage(restingBlinkFrames[0]);
            ScheduleNextRestingBlink();
            return;
        }

        petWindow.SetImage(restingBlinkFrames[restingBlinkFrameIndex]);
    }

    private void StopRestingBlink()
    {
        restingBlinkTimer.Stop();
        restingBlinkFrameTimer.Stop();
        restingBlinkFrames = [];
        restingBlinkFrameIndex = 0;
    }

    private void AdvancePosition()
    {
        if (stateMachine.State.Kind != CatStateKind.Walking)
        {
            return;
        }

        var current = petWindow.CurrentAnchor(activityArea.Edge);
        var delta = direction * settings.WalkBaseSpeed * movementTimer.Interval.TotalSeconds;
        var moved = activityArea.MoveAnchor(current, delta, petWindow.PetSize);

        if (activityArea.IsAtStart(moved, petWindow.PetSize))
        {
            direction = 1;
        }
        else if (activityArea.IsAtEnd(moved, petWindow.PetSize))
        {
            direction = -1;
        }

        petWindow.SetAnchor(moved, activityArea.Edge);
        petWindow.SetMirrored(activityArea.UsesHorizontalMovement && direction < 0);
    }

    private void SetFrame(int index)
    {
        if (index >= 0 && index < activeFrames.Count)
        {
            petWindow.SetImage(activeFrames[index]);
        }
    }

    private void SetRandomImage(IReadOnlyList<BitmapImage> images)
    {
        if (images.Count > 0)
        {
            petWindow.SetImage(images[random.Next(images.Count)]);
            return;
        }

        petWindow.SetImage(assetPack.WalkFrames.FirstOrDefault());
    }

    private void ApplyStateSourceSize(CatStateKind stateKind)
    {
        if (petWindow is null)
        {
            return;
        }

        var anchor = petWindow.CurrentAnchor(activityArea.Edge);
        petWindow.SetSourceSize(stateKind == CatStateKind.Dragged
            ? assetPack.HeldSourceSize
            : assetPack.DefaultSourceSize);
        petWindow.SetAnchor(anchor, activityArea.Edge);
    }

    private void CatImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            stateMachine.Pet();
            e.Handled = true;
            return;
        }

        stateTimer.Stop();
        stateMachine.BeginDrag();
        DragMove();
        var anchor = petWindow.CurrentAnchor(activityArea.Edge);
        petWindow.SetAnchor(activityArea.ClampAnchor(anchor, petWindow.PetSize), activityArea.Edge);
        stateMachine.EndDrag();
        e.Handled = true;
    }

    private void CatImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = CreatePetMenu();
        AddPetMenuItem(menu, $"当前状态：{StatusText()}", enabled: false);
        menu.Items.Add(new Separator());

        AddPetMenuItem(menu, $"摸摸{settings.CatName}", stateMachine.Pet);
        AddPetMenuItem(menu, stateMachine.State.Kind == CatStateKind.Walking ? "让她休息一下" : "让她散步", stateMachine.ToggleLongDurationState);
        AddPetMenuItem(menu, "回到小屋", ShowHomeWindow);
        AddPetMenuItem(menu, $"和{settings.CatName}聊天", ShowAgentChatWindow);

        menu.Items.Add(new Separator());
        AddPetMenuItem(menu, "清除 Codex 提醒", ClearCodexNotification);
        menu.Items.Add(new Separator());
        AddPetMenuItem(menu, "偏好设置", () => ShowSettingsWindow());
        AddPetMenuItem(menu, IsVisible ? $"暂时隐藏{settings.CatName}" : $"显示{settings.CatName}", ToggleVisibilityFromTray);
        menu.Items.Add(new Separator());
        AddPetMenuItem(menu, "重启应用", RestartApplication);
        AddPetMenuItem(menu, "退出应用", ExitApplication);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static ContextMenu CreatePetMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 252)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 185, 207)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            MinWidth = 190,
            HasDropShadow = true
        };
        return menu;
    }

    private static MenuItem AddPetMenuItem(ContextMenu menu, string header, Action? action = null, bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            Padding = new Thickness(12, 7, 18, 7),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 42, 54)),
            FontSize = 13,
            MinHeight = 30
        };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        menu.Items.Add(item);
        return item;
    }

    private void ShowSettingsWindow(bool focusAiSettings = false)
    {
        var previousAssetPackID = settings.SelectedAssetPackID;
        var window = new SettingsWindow(
            settings,
            assetPackLoader.CustomPackIDs(),
            assetPackLoader.CustomPackIDs,
            assetPackLoader.ValidationSummary,
            assetPackLoader.CustomPacksRoot(),
            usageStatistics,
            focusAiSettings)
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        settings = window.Settings;
        settingsStore.Save(settings);
        SaveUserData();
        reminderScheduler.Reset(settings);
        if (settings.SelectedAssetPackID != previousAssetPackID)
        {
            assetPack = assetPackLoader.LoadSelectedPack(settings.SelectedAssetPackID);
            petWindow = new PetWindowController(
                this,
                CatImage,
                MirrorTransform,
                assetPack.DefaultSourceSize);
        }
        ApplySettings(reposition: true);
        ApplyState(stateMachine.State);
        if (apiSetupHintShown && IsAgentApiConfigured() && activeReminder is null && activeCodexNotification is null)
        {
            HideBubble();
        }
    }

    private bool IsAgentApiConfigured()
    {
        return !string.IsNullOrWhiteSpace(agentStore.LoadSettings().ResolveApiKey());
    }

    private async Task PollRemindersAsync()
    {
        if (isPollingReminder || activeCodexNotification is not null)
        {
            return;
        }

        if (activeReminder is not null || stateMachine.State.Kind == CatStateKind.Dragged)
        {
            return;
        }

        isPollingReminder = true;
        try
        {
            settings = settingsStore.Load();
            reminderScheduler.Reconcile(settings);
            var due = reminderScheduler.DueReminder(settings, stateMachine.State.IsLongDuration);
            if (due is null)
            {
                return;
            }

            activeReminder = due;
            if (string.Equals(due.ActionType, "agent_task", StringComparison.OrdinalIgnoreCase))
            {
                var taskMessage = await ExecuteScheduledTaskAsync(due);
                CompleteReminder(due);
                ShowBubble(
                    taskMessage,
                    ("查看结果", OpenLastScheduledTaskOutput),
                    ("知道了", HideBubble));
                return;
            }

            var message = await BuildReminderMessageAsync(due);
            ShowBubble(
                message,
                ("完成啦", () => CompleteReminder(due)),
                ("稍等5分钟", () => SnoozeReminder(due)));
        }
        finally
        {
            isPollingReminder = false;
        }
    }

    private async Task<string> ExecuteScheduledTaskAsync(ReminderSettings reminder)
    {
        var startedAt = DateTime.Now;
        var toolTraces = new List<AgentToolTrace>();
        try
        {
            var agentSettings = agentStore.LoadSettings();
            if (string.IsNullOrWhiteSpace(agentSettings.ResolveApiKey()))
            {
                var message = $"定时任务「{reminder.Title}」需要先配置 API key。";
                lastScheduledTaskOutputPath = SaveScheduledTaskRun(reminder, startedAt, DateTime.Now, "blocked", message, toolTraces, "missing_api_key");
                return message;
            }

            var prompt = $"""
            你正在执行一个爱弥斯的定时任务，不是普通聊天。

            任务名称：{reminder.Title}
            任务要求：
            {reminder.TaskPrompt}

            执行要求：
            - 必须给出具体结果，不能只说“完成了”。
            - 如果任务涉及新闻、资讯、搜索、最近动态，优先调用 `web_search` 工具获取真实搜索结果。
            - 如果使用了工具，基于工具结果继续整理最终答案。
            - 输出中文 Markdown，简洁但有信息量。
            """;
            var reply = await agentChatClient.SendWithToolsAsync(
                agentSettings,
                agentStore.BuildSystemPrompt(agentSettings),
                [],
                prompt,
                agentStore,
                CancellationToken.None,
                trace => toolTraces.Add(trace));
            lastScheduledTaskOutputPath = SaveScheduledTaskRun(reminder, startedAt, DateTime.Now, "completed", reply, toolTraces, null);

            var suffix = string.IsNullOrWhiteSpace(lastScheduledTaskOutputPath)
                ? ""
                : $"\n记录：{lastScheduledTaskOutputPath}";
            return TrimBubbleText($"定时任务「{reminder.Title}」完成：\n{reply}{suffix}", 150);
        }
        catch (Exception ex)
        {
            var error = $"定时任务「{reminder.Title}」失败：{ex.Message}";
            lastScheduledTaskOutputPath = SaveScheduledTaskRun(reminder, startedAt, DateTime.Now, "failed", error, toolTraces, ex.Message);

            return TrimBubbleText(error, 150);
        }
    }

    private string SaveScheduledTaskRun(
        ReminderSettings reminder,
        DateTime startedAt,
        DateTime finishedAt,
        string status,
        string content,
        IReadOnlyList<AgentToolTrace> toolTraces,
        string? error)
    {
        var root = Path.GetFullPath(agentStore.RootDirectory);
        var directory = (reminder.OutputDirectory ?? "").Trim().Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine("workspace", "output", "scheduled-tasks");
        }

        var targetDirectory = Path.GetFullPath(Path.IsPathRooted(directory)
            ? directory
            : Path.Combine(root, directory));
        if (!targetDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            targetDirectory = Path.Combine(agentStore.WorkspaceDirectory, "output", "scheduled-tasks");
        }

        targetDirectory = Path.Combine(targetDirectory, startedAt.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(targetDirectory);
        var safeId = new string(reminder.Id.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(safeId))
        {
            safeId = "scheduled-task";
        }

        var path = Path.Combine(targetDirectory, $"{startedAt:HHmmss}-{safeId}.md");
        var duration = finishedAt - startedAt;
        var toolMarkdown = toolTraces.Count == 0
            ? "本次没有工具调用。"
            : string.Join(Environment.NewLine + Environment.NewLine, toolTraces.Select(FormatToolTraceMarkdown));
        var markdown = $"""
        # {reminder.Title}

        - 状态：{status}
        - 开始：{startedAt:yyyy-MM-dd HH:mm:ss}
        - 结束：{finishedAt:yyyy-MM-dd HH:mm:ss}
        - 耗时：{duration.TotalSeconds:0.0} 秒
        - ID：{reminder.Id}
        - 调度：{reminder.ScheduleType}
        - 保存开关：{reminder.SaveOutput}
        - 工具调用：{toolTraces.Count}

        ## 任务要求

        {reminder.TaskPrompt}

        ## 工具调用

        {toolMarkdown}

        ## 执行结果

        {content}
        """;
        if (!string.IsNullOrWhiteSpace(error))
        {
            markdown += $"{Environment.NewLine}{Environment.NewLine}## 错误{Environment.NewLine}{Environment.NewLine}{error}";
        }

        File.WriteAllText(path, markdown, System.Text.Encoding.UTF8);
        AppendScheduledTaskIndex(reminder, startedAt, finishedAt, status, path, toolTraces.Count, error);
        return path;
    }

    private void AppendScheduledTaskIndex(
        ReminderSettings reminder,
        DateTime startedAt,
        DateTime finishedAt,
        string status,
        string outputPath,
        int toolCount,
        string? error)
    {
        var runRoot = Path.Combine(agentStore.TasksDirectory, "scheduled-runs");
        Directory.CreateDirectory(runRoot);
        var record = new
        {
            id = $"{startedAt:yyyyMMddHHmmss}-{reminder.Id}",
            reminder_id = reminder.Id,
            title = reminder.Title,
            status,
            started_at = startedAt.ToString("O"),
            finished_at = finishedAt.ToString("O"),
            duration_seconds = Math.Round((finishedAt - startedAt).TotalSeconds, 2),
            tool_count = toolCount,
            output_path = outputPath,
            error
        };
        var json = System.Text.Json.JsonSerializer.Serialize(record, TaskRecordJsonOptions);
        File.AppendAllText(Path.Combine(runRoot, "index.jsonl"), json + Environment.NewLine, System.Text.Encoding.UTF8);
    }

    private static string FormatToolTraceMarkdown(AgentToolTrace trace)
    {
        var preview = string.IsNullOrWhiteSpace(trace.Preview) ? "(无内容)" : trace.Preview;
        var handle = string.IsNullOrWhiteSpace(trace.Handle) ? "" : $"{Environment.NewLine}- handle：`{trace.Handle}`";
        var error = string.IsNullOrWhiteSpace(trace.ErrorCode) ? "" : $"{Environment.NewLine}- 错误码：`{trace.ErrorCode}`";
        return $"""
        ### 工具 {trace.Round}: {trace.Tool}

        - 状态：{(trace.Ok ? "OK" : "失败")}
        - 摘要：{trace.Summary}
        - 参数：`{trace.ArgumentsJson}`
        {handle}{error}

        ```text
        {preview}
        ```
        """;
    }

    private void OpenLastScheduledTaskOutput()
    {
        if (string.IsNullOrWhiteSpace(lastScheduledTaskOutputPath) || !File.Exists(lastScheduledTaskOutputPath))
        {
            HideBubble();
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = lastScheduledTaskOutputPath,
            UseShellExecute = true
        });
    }

    private static string TrimBubbleText(string text, int maxChars)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private async Task<string> BuildReminderMessageAsync(ReminderSettings reminder)
    {
        var fixedMessage = string.IsNullOrWhiteSpace(reminder.FixedMessage)
            ? $"{settings.UserSalutation}，休息一下吧。"
            : reminder.FixedMessage.Trim();
        if (!reminder.UseAiMessage)
        {
            return fixedMessage;
        }

        try
        {
            var agentSettings = agentStore.LoadSettings();
            if (string.IsNullOrWhiteSpace(agentSettings.ResolveApiKey()))
            {
                return fixedMessage;
            }

            var prompt = $"""
            请以爱弥斯的身份生成一句简短提醒文案。
            当前名称：{settings.CatName}
            英文标识：{settings.CatIdentifier}
            对用户称呼：{settings.UserSalutation}
            提醒标题：{reminder.Title}
            固定文案参考：{fixedMessage}
            用户自定义提示：{reminder.AiPrompt}
            要求：只输出一句中文，语气自然，40字以内，不要 Markdown。
            """;
            var reply = await agentChatClient.SendAsync(
                agentSettings,
                "你是爱弥斯的提醒文案生成器，只输出一句提醒。",
                [],
                prompt,
                CancellationToken.None);
            return string.IsNullOrWhiteSpace(reply) ? fixedMessage : reply.Trim();
        }
        catch
        {
            return fixedMessage;
        }
    }

    private void CompleteReminder(ReminderSettings reminder)
    {
        reminderScheduler.Complete(reminder);
        if (string.Equals(reminder.Category, "water", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reminder.Id, "water", StringComparison.OrdinalIgnoreCase))
        {
            usageStatistics.CompletedWaterReminders++;
        }
        else if (string.Equals(reminder.Category, "movement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reminder.Id, "movement", StringComparison.OrdinalIgnoreCase))
        {
            usageStatistics.CompletedMovementReminders++;
        }

        SaveUserData();
        activeReminder = null;
        HideBubble();
    }
    private void PollCodexBridge()
    {
        var messages = codexBridgeStore.ReadNewInboxMessages();
        if (messages.Count == 0)
        {
            return;
        }

        ShowCodexNotification(messages[^1]);
    }

    private void ShowCodexNotification(CodexBridgeMessage message)
    {
        activeCodexNotification = message;
        if (!IsVisible)
        {
            Show();
        }

        SetRandomImage(assetPack.DialoguePoses);
        var title = string.IsNullOrWhiteSpace(message.Title) ? "Codex" : message.Title.Trim();
        ShowBubble(
            $"{title}\n{message.Message}",
            ("知道了", ClearCodexNotification));
        trayIcon?.ShowNotification(title, message.Message);
    }

    private void ClearCodexNotification()
    {
        activeCodexNotification = null;
        codexBridgeStore.MarkInboxConsumed();
        HideBubble();
    }

    private void ShowAskCodexWindow()
    {
        var window = new CodexPromptWindow
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        codexBridgeStore.AppendOutboxQuestion(window.Message);
        try
        {
            System.Windows.Clipboard.SetText(window.Message);
        }
        catch
        {
            // Clipboard access can fail when another app owns it; the outbox write is the durable path.
        }

        SetRandomImage(assetPack.DialoguePoses);
        ShowBubble(
            $"已写入 Codex outbox，并复制到剪贴板。\n{codexBridgeStore.OutboxPath}",
            ("知道了", HideBubble));
        trayIcon?.ShowNotification("已发送给 Codex", "内容已写入 outbox，并复制到剪贴板。");
    }

    private void ShowAgentChatWindow()
    {
        stateMachine.Rest();

        if (agentChatWindow is { IsVisible: true })
        {
            agentChatWindow.Activate();
            return;
        }

        agentChatWindow = new AgentChatWindow(ShowAgentReply, OnAgentChatUserMessage, OnAgentChatAssistantReply, BuildAgentChatExtraContext)
        {
            Owner = homeWindow is { IsVisible: true } ? homeWindow : this
        };
        agentChatWindow.Closed += (_, _) => agentChatWindow = null;
        agentChatWindow.Show();
    }

    private void ShowHomeWindow()
    {
        stateMachine.Rest();

        if (homeWindow is { IsVisible: true })
        {
            homeWindow.Activate();
            return;
        }

        var homePose = assetPack.RestingBasePoses.FirstOrDefault()
            ?? assetPack.RestingPoses.FirstOrDefault()
            ?? assetPack.DialoguePoses.FirstOrDefault()
            ?? assetPack.WalkFrames.FirstOrDefault();
        homeWindow = new HomeWindow(
            settings.CatName,
            homePose,
            BuildHomeActivityScheduleAsync,
            InvalidateHomeSchedule,
            homeLifeStore,
            () => ShowSettingsWindow(),
            ShowAgentChatWindow)
        {
            Owner = this
        };
        homeWindow.Closed += (_, _) =>
        {
            homeWindow = null;
            if (!isExitRequested)
            {
                Show();
                UpdateTray();
            }
        };
        homeWindow.Show();
        Hide();
        UpdateTray();
    }

    private bool OnAgentChatUserMessage(string text)
    {
        if (homeWindow is { IsVisible: true })
        {
            return homeWindow.HandleChatStarted(text);
        }

        return false;
    }

    private void OnAgentChatAssistantReply(string userMessage, string assistantReply)
    {
        if (homeWindow is { IsVisible: true })
        {
            homeWindow.HandleChatCompleted(userMessage, assistantReply);
        }
    }

    private void InvalidateHomeSchedule()
    {
        cachedHomeSchedule = null;
        cachedHomeScheduleExpiresAt = DateTime.MinValue;
    }

    private string? BuildAgentChatExtraContext()
    {
        return homeWindow is { IsVisible: true }
            ? homeWindow.BuildAgentContext()
            : null;
    }

    private void ShowAgentReply(string text)
    {
        if (homeWindow is { IsVisible: true })
        {
            homeWindow.ShowChatReply(text);
            return;
        }

        ShowAgentReplyBubble(text);
    }

    private async Task<IReadOnlyList<HomeActivityPlan>> BuildHomeActivityScheduleAsync(CancellationToken cancellationToken)
    {
        if (cachedHomeSchedule is { Count: > 0 } && DateTime.Now < cachedHomeScheduleExpiresAt)
        {
            return cachedHomeSchedule;
        }

        var fallback = RandomHomeSchedule();
        try
        {
            var agentSettings = agentStore.LoadSettings();
            if (string.IsNullOrWhiteSpace(agentSettings.ResolveApiKey()))
            {
                cachedHomeSchedule = fallback;
                cachedHomeScheduleExpiresAt = DateTime.Now.AddHours(2);
                return fallback;
            }

            var now = DateTime.Now;
            var prompt = $"""
            你正在为爱弥斯的小屋生活系统安排接下来 2 小时的行动计划。

            当前名称：{settings.CatName}
            对用户称呼：{settings.UserSalutation}
            当前真实时间：{now:yyyy-MM-dd HH:mm:ss dddd}
            当前状态：{StatusText()}
            最近三次小屋行事：
            {homeLifeStore.BuildRecentSummary(3)}

            可选动作只能是以下 action_id 之一：
            - sleep_bed：在床上睡觉或小睡。
            - study_desk：背对书桌写小纸条、做记录、看设定。
            - read_sofa：坐在沙发旁/客厅里读书。
            - drink_tea：在茶几旁喝茶或喝水。
            - play_game：坐到电竞区玩俄罗斯方块。

            要求：
            - 结合真实时间、最近行事和爱弥斯人设安排 8 到 12 个动作。
            - 每个动作 duration_minutes 必须是 5 到 15 之间的整数。
            - 单个动作不要超过 15 分钟；如果确实适合连续做同一件事，可以连续安排两段同 action_id。
            - 尽量不要让整张计划表都是睡觉；深夜可以多睡，下午可以喝茶/读书/玩游戏，工作时段可以书桌记录。
            - display_text 必须和 action_id 对得上，不能出现 action_id 是 sleep_bed 但文案写沙发/茶几/书桌。
            - display_text 用一句自然中文，28字以内，适合小窗气泡显示。
            - 像生活状态，不要像系统提示。
            - 不要把最近三次刚做过的同类活动放在计划开头，除非当前时间非常适合。
            - 不要 Markdown，不要解释，只输出 JSON。

            JSON 格式：顶层字段 schedule 是数组；数组每一项包含 action_id、display_text、duration_minutes。
            示例含义：action_id 为 drink_tea，display_text 为“{settings.CatName}在茶几旁慢慢喝茶。”，duration_minutes 为 10。
            """;

            var reply = await agentChatClient.SendAsync(
                agentSettings,
                "你是爱弥斯的小屋生活计划器，只输出合法 JSON。",
                [],
                prompt,
                cancellationToken);
            var schedule = ParseHomeActivitySchedule(reply, fallback);
            cachedHomeSchedule = schedule;
            cachedHomeScheduleExpiresAt = DateTime.Now.AddHours(2);
            return schedule;
        }
        catch
        {
            cachedHomeSchedule = fallback;
            cachedHomeScheduleExpiresAt = DateTime.Now.AddHours(2);
            return fallback;
        }
    }

    private IReadOnlyList<HomeActivityPlan> RandomHomeSchedule()
    {
        var hour = DateTime.Now.Hour;
        if (hour is >= 0 and <= 6)
        {
            return
            [
                new HomeActivityPlan("sleep_bed", $"{settings.CatName}在床上安静小睡。", 15),
                new HomeActivityPlan("sleep_bed", $"{settings.CatName}抱着软枕继续睡。", 15),
                new HomeActivityPlan("drink_tea", $"{settings.CatName}在茶几旁喝点水。", 10),
                new HomeActivityPlan("read_sofa", $"{settings.CatName}坐在客厅里翻书。", 10),
                new HomeActivityPlan("study_desk", $"{settings.CatName}背对书桌写小纸条。", 10)
            ];
        }

        if (hour is >= 14 and <= 17)
        {
            return
            [
                new HomeActivityPlan("drink_tea", $"{settings.CatName}在茶几旁慢慢喝茶。", 10),
                new HomeActivityPlan("read_sofa", $"{settings.CatName}坐在客厅里读书。", 12),
                new HomeActivityPlan("study_desk", $"{settings.CatName}背对书桌写小纸条。", 10),
                new HomeActivityPlan("play_game", $"{settings.CatName}坐到电竞区玩俄罗斯方块。", 12),
                new HomeActivityPlan("sleep_bed", $"{settings.CatName}在床上安静小睡。", 15)
            ];
        }

        return
        [
            new HomeActivityPlan("study_desk", $"{settings.CatName}背对书桌写小纸条。", 10),
            new HomeActivityPlan("read_sofa", $"{settings.CatName}坐在客厅里读书。", 12),
            new HomeActivityPlan("drink_tea", $"{settings.CatName}在茶几旁慢慢喝茶。", 10),
            new HomeActivityPlan("play_game", $"{settings.CatName}坐到电竞区玩俄罗斯方块。", 12),
            new HomeActivityPlan("sleep_bed", $"{settings.CatName}在床上安静小睡。", 15)
        ];
    }

    private static IReadOnlyList<HomeActivityPlan> ParseHomeActivitySchedule(string reply, IReadOnlyList<HomeActivityPlan> fallback)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return fallback;
        }

        try
        {
            var json = ExtractJsonArrayOrObject(reply);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            var plans = new List<HomeActivityPlan>();
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                AddPlansFromArray(root, plans);
            }
            else if (root.TryGetProperty("schedule", out var schedule) && schedule.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                AddPlansFromArray(schedule, plans);
            }

            return plans.Count == 0 ? fallback : plans;
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddPlansFromArray(System.Text.Json.JsonElement array, List<HomeActivityPlan> plans)
    {
        foreach (var item in array.EnumerateArray())
        {
            var actionId = item.TryGetProperty("action_id", out var action)
                ? action.GetString()
                : null;
            var displayText = item.TryGetProperty("display_text", out var text)
                ? text.GetString()
                : null;
            var duration = item.TryGetProperty("duration_minutes", out var minutes) && minutes.TryGetInt32(out var parsedMinutes)
                ? parsedMinutes
                : 10;

            actionId = NormalizeHomeActionId(actionId);
            if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(displayText))
                {
                    continue;
                }

            plans.Add(new HomeActivityPlan(actionId, displayText.Trim(), Math.Clamp(duration, 1, 15)));
        }
    }

    private static string ExtractJsonArrayOrObject(string text)
    {
        var trimmed = text.Trim();
        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (arrayStart >= 0 && arrayEnd > arrayStart && (objectStart < 0 || arrayStart < objectStart))
        {
            return trimmed[arrayStart..(arrayEnd + 1)];
        }

        if (objectStart >= 0 && objectEnd > objectStart)
        {
            return trimmed[objectStart..(objectEnd + 1)];
        }

        return trimmed;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return text.Trim();
        }

        return text[start..(end + 1)];
    }

    private static string NormalizeHomeActionId(string? actionId)
    {
        var text = actionId?.Trim().ToLowerInvariant().Replace('-', '_') ?? "";
        return text switch
        {
            "sleep_bed" or "study_desk" or "read_sofa" or "drink_tea" or "play_game" => text,
            "sleep" or "bed" or "nap" => "sleep_bed",
            "desk" or "write_desk" or "read_desk" => "study_desk",
            "sofa" or "read" or "read_book" => "read_sofa",
            "tea" or "water" => "drink_tea",
            "game" or "gaming" or "tetris" => "play_game",
            "walk" => "",
            "idle" or "stand" => "",
            _ => ""
        };
    }

    private void ShowAgentReplyBubble(string reply)
    {
        if (activeReminder is not null || activeCodexNotification is not null)
        {
            return;
        }

        var text = reply.Trim();
        if (text.Length > 48)
        {
            text = text[..48] + "...";
        }

        SetRandomImage(assetPack.DialoguePoses);
        if (agentChatWindow is not null && agentChatWindow.IsVisible)
        {
            ShowTransientBubble(text);
            return;
        }

        ShowBubble(text, ("看完整对话", () => agentChatWindow?.Activate()), ("知道了", HideBubble));
    }

    private void SnoozeReminder(ReminderSettings reminder)
    {
        reminderScheduler.Snooze(reminder, TimeSpan.FromMinutes(5));
        activeReminder = null;
        HideBubble();
    }

    private void ShowBubble(string message, params (string Title, Action Action)[] actions)
    {
        ShowBubble(message, image: null, showInput: false, actions);
    }

    private void ShowBubble(
        string message,
        ImageSource? image,
        bool showInput,
        params (string Title, Action Action)[] actions)
    {
        bubbleAutoHideTimer.Stop();
        var anchor = petWindow.CurrentAnchor(activityArea.Edge);
        BubbleText.Text = message;
        BubbleImage.Source = image;
        BubbleImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        BubbleInputPanel.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
        BubbleButtons.Children.Clear();
        foreach (var action in actions)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = action.Title,
                MinWidth = 76,
                Margin = new Thickness(4, 0, 4, 0)
            };
            button.Click += (_, _) => action.Action();
            BubbleButtons.Children.Add(button);
        }

        BubbleBorder.Visibility = Visibility.Visible;
        BubbleTextScrollViewer.MaxHeight = MaxBubbleTextHeightFor(anchor, image is not null, showInput, actions.Length);
        RootLayout.UpdateLayout();
        var maxExtraTopHeight = MaxExtraTopHeightFor(anchor);
        var extraTopHeight = Math.Min(BubbleBorder.ActualHeight + 8, maxExtraTopHeight);
        petWindow.SetExtraTopContent(Math.Max(280, BubbleBorder.ActualWidth), extraTopHeight);
        petWindow.SetAnchor(activityArea.ClampAnchor(anchor, petWindow.PetSize), activityArea.Edge);
    }

    private void ShowTransientBubble(string message)
    {
        ShowBubble(message);
        bubbleAutoHideTimer.Start();
    }

    private void HideBubble()
    {
        bubbleAutoHideTimer.Stop();
        var anchor = petWindow.CurrentAnchor(activityArea.Edge);
        BubbleBorder.Visibility = Visibility.Collapsed;
        BubbleImage.Source = null;
        BubbleImage.Visibility = Visibility.Collapsed;
        BubbleInputPanel.Visibility = Visibility.Collapsed;
        BubbleButtons.Children.Clear();
        BubbleTextScrollViewer.MaxHeight = 220;
        petWindow.SetExtraTopContent(0, 0);
        petWindow.SetAnchor(activityArea.ClampAnchor(anchor, petWindow.PetSize), activityArea.Edge);
    }

    private double MaxBubbleTextHeightFor(WpfPoint anchor, bool hasImage, bool hasInput, int actionCount)
    {
        var maxExtraTopHeight = MaxExtraTopHeightFor(anchor);
        var reservedHeight = 48d;
        if (hasImage)
        {
            reservedHeight += 104d;
        }

        if (hasInput)
        {
            reservedHeight += 32d;
        }

        if (actionCount > 0)
        {
            reservedHeight += 40d;
        }

        return Math.Clamp(maxExtraTopHeight - reservedHeight, 72d, 220d);
    }

    private double MaxExtraTopHeightFor(WpfPoint anchor)
    {
        var availableAbovePet = anchor.Y - activityArea.Screen.Top - petWindow.PetSize.Height - 8;
        return Math.Max(0, availableAbovePet);
    }

    private void ToggleVisibilityFromTray()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
        UpdateTray();
    }

    private void UpdateTray()
    {
        trayIcon?.Update(
            IsVisible,
            stateMachine.State.Kind == CatStateKind.Walking,
            StatusText(),
            null);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        UpdateTray();
    }

    private void ExitApplication()
    {
        isExitRequested = true;
        SaveUserData();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void RestartApplication()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            ExitApplication();
            return;
        }

        isExitRequested = true;
        SaveUserData();
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        });
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void RecordCompanionMinute()
    {
        usageStatistics.TotalCompanionSeconds += statisticsTimer.Interval.TotalSeconds;
        SaveUserData();
    }

    private void SaveUserData()
    {
        usageStatisticsStore.Save(usageStatistics);
        userDataBackupStore.Save(settings, usageStatistics);
    }

    private string StatusText()
    {
        return stateMachine.State.Kind switch
        {
            CatStateKind.Walking => $"{settings.CatName}正在散步",
            CatStateKind.Resting => $"{settings.CatName}正在休息",
            CatStateKind.Transitioning => $"{settings.CatName}伸了个懒腰",
            CatStateKind.Dragged => $"{settings.CatName}被抱起来了",
            _ => "DockPetWin"
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}小时{duration.Minutes:00}分钟";
        }

        return $"{Math.Max(0, duration.Minutes)}分{duration.Seconds:00}秒";
    }

}

