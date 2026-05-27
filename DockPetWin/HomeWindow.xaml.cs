using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DockPetWin.Core.HomeLife;

namespace DockPetWin;

public partial class HomeWindow : Window
{
    private const string HomeResourcePack = "saved-ok-v3-2026-05-22";
    private const string LegacyHomeResourcePack = "v21";
    private const string PlacementConfigRelativePath = @"UserData\Home\placements.local.json";
    private const string FurnitureConfigRelativePath = @"UserData\Home\furniture.local.json";
    private static readonly Random PoseRandom = new();
    private static readonly JsonSerializerOptions PlacementJsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly string petName;
    private readonly Func<CancellationToken, Task<IReadOnlyList<HomeActivityPlan>>> activityPlanner;
    private readonly Action invalidateActivityPlan;
    private readonly HomeLifeStore homeLifeStore;
    private readonly Action openSettings;
    private readonly Action openChat;
    private readonly Dictionary<string, ImageSource> homePoses;
    private readonly Dictionary<string, IReadOnlyList<ImageSource>> homePoseFrames;
    private readonly Dictionary<string, TimeSpan> homePoseFrameIntervals;
    private readonly Dictionary<string, IReadOnlyList<ImageSource>> objectAnimationFrames;
    private readonly Dictionary<string, TimeSpan> objectAnimationIntervals;
    private readonly Dictionary<string, IReadOnlyList<ImageSource>> effectAnimationFrames;
    private readonly Dictionary<string, TimeSpan> effectAnimationIntervals;
    private readonly Dictionary<string, HomePlacement> placementOverrides;
    private readonly Dictionary<string, FurnitureConfig> furnitureConfigs;
    private readonly DispatcherTimer activityTimer = new();
    private readonly DispatcherTimer homeLogRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<HomeActivityPlan> activitySchedule = [];
    private HomePlacement currentPlacement = HomePlacement.Idle;
    private bool hasRenderedPlacement;
    private CancellationTokenSource? activePlanner;
    private HomeLifeEntry? activeHomeLife;
    private HomeActivityPlan? currentActivityPlan;
    private Storyboard? activeMoveStoryboard;
    private DispatcherTimer? walkFrameTimer;
    private DispatcherTimer? actionFrameTimer;
    private DispatcherTimer? objectFrameTimer;
    private DispatcherTimer? effectFrameTimer;
    private int walkFrameIndex;
    private int actionFrameIndex;
    private int objectFrameIndex;
    private int effectFrameIndex;
    private int walkLoopVersion;
    private int actionLoopVersion;
    private int activityScheduleIndex;
    private DateTime scheduleStartedAt = DateTime.MinValue;
    private DateTime scheduleExpiresAt = DateTime.MinValue;

    public HomeWindow(
        string petName,
        ImageSource? petImage,
        Func<CancellationToken, Task<IReadOnlyList<HomeActivityPlan>>> activityPlanner,
        Action invalidateActivityPlan,
        HomeLifeStore homeLifeStore,
        Action openSettings,
        Action openChat)
    {
        this.petName = string.IsNullOrWhiteSpace(petName) ? "爱弥斯" : petName.Trim();
        this.activityPlanner = activityPlanner;
        this.invalidateActivityPlan = invalidateActivityPlan;
        this.homeLifeStore = homeLifeStore;
        this.openSettings = openSettings;
        this.openChat = openChat;
        placementOverrides = LoadPlacementOverrides();
        furnitureConfigs = LoadFurnitureConfigs();
        currentPlacement = ResolvePlacement(HomePlacement.Idle);
        homePoses = LoadHomePoses();
        homePoseFrames = LoadHomePoseFrames();
        homePoseFrameIntervals = LoadHomePoseFrameIntervals();
        objectAnimationFrames = LoadSceneAnimationFrames("objects");
        objectAnimationIntervals = LoadObjectAnimationIntervals();
        effectAnimationFrames = LoadSceneAnimationFrames("effects");
        effectAnimationIntervals = LoadEffectAnimationIntervals();
        InitializeComponent();
        LoadStaticSceneImages();
        ApplyFurnitureConfigs();
        Title = $"{this.petName}的小屋";
        TitleText.Text = $"{this.petName}的小屋";
        ApplyActivityPose("idle", petImage);
        activityTimer.Tick += async (_, _) =>
        {
            activityTimer.Stop();
            await AdvanceScheduledActivityAsync();
        };
        homeLogRefreshTimer.Tick += (_, _) => RefreshHomeLogText();
        Loaded += async (_, _) =>
        {
            RefreshHomeLogText();
            await RestoreScheduleOrRebuildAsync();
        };
        Closed += (_, _) =>
        {
            activePlanner?.Cancel();
            SaveCurrentScheduleState();
            activeMoveStoryboard?.Stop();
            activityTimer.Stop();
            homeLogRefreshTimer.Stop();
            StopWalkLoop();
            StopActionLoop();
            StopObjectLoop();
            StopEffectLoop();
        };
    }

    private void LoadStaticSceneImages()
    {
        Icon = AppImageLoader.TryLoad(AppImageLoader.AppIconPath);
        BackgroundImage.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/background/room.png");
        BedLayer.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/objects/bed_sleep_surface.png");
        StudyDeskLayer.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/objects/study_desk_front_compact_slot.png");
        GamingDeskLayer.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/objects/gaming_station_big_screen_slot_mirror.png");
        SofaTableLayer.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/objects/sofa_reading_slot.png");
        TeaTableLayer.Source = AppImageLoader.TryLoad($"Resources/Home/{HomeResourcePack}/objects/tea_table_slot.png");
    }

    private async void RefreshActivity_Click(object sender, RoutedEventArgs e)
    {
        await ForceSwitchActivityAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        openSettings();
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        openChat();
    }

    public bool HandleChatStarted(string userMessage)
    {
        StatusText.Text = "正在小屋里对话";
        if (ShouldResumeOwnBusiness(userMessage))
        {
            HomeSpeechText.Text = "好哦，那我继续忙自己的事。";
            HomeSpeechBubble.Visibility = Visibility.Visible;
            PositionSpeechBubbleNearPet();
            StatusText.Text = "继续小屋生活";
            return true;
        }

        return false;
    }

    public void HandleChatCompleted(string userMessage, string assistantReply)
    {
        var requested = BuildRequestedActivity(userMessage);
        if (requested is null)
        {
            return;
        }

        var isHard = IsHardInstruction(userMessage);
        if (!isHard && LooksLikeRefusal(assistantReply))
        {
            StatusText.Text = "已保留当前小屋行动";
            return;
        }

        _ = ApplyUserActivityAndReplanAsync(requested, isHard ? "hard-user-command" : "user-command");
    }

    public void ShowChatReply(string reply)
    {
        var text = string.IsNullOrWhiteSpace(reply) ? "嗯？我刚刚有点走神。" : reply.Trim();
        HomeSpeechBubble.Visibility = Visibility.Visible;
        HomeSpeechText.Text = text.Length > 96 ? text[..96] + "..." : text;
        PositionSpeechBubbleNearPet();
        StatusText.Text = "已在小屋内回复";
    }

    public string BuildAgentContext()
    {
        var now = DateTime.Now;
        var current = activeHomeLife is null
            ? $"{petName}当前没有正在记录的小屋活动。"
            : $"""
              - 当前正在做：{activeHomeLife.Details}
              - 开始时间：{activeHomeLife.StartedAt:yyyy-MM-dd HH:mm:ss}
              - 已持续：{FormatDuration((now - activeHomeLife.StartedAt).TotalSeconds)}
              - 心情：{activeHomeLife.Mood}
              """;
        var upcoming = activitySchedule
            .Skip(Math.Min(activityScheduleIndex + 1, activitySchedule.Count))
            .Take(5)
            .Select((plan, index) => $"- 接下来 {index + 1}：{plan.DisplayText}，约 {Math.Clamp(plan.DurationMinutes, 1, 15)} 分钟");
        var upcomingText = string.Join(Environment.NewLine, upcoming);
        if (string.IsNullOrWhiteSpace(upcomingText))
        {
            upcomingText = "当前两小时计划即将结束，之后会重新规划。";
        }

        return $"""
        # 小屋现场上下文

        用户现在正在和小屋里的 {petName} 对话。回答时要承认自己正在小屋中，气泡会显示在角色头上。

        ## 当前活动
        {current}

        ## 接下来的小屋计划
        {upcomingText}

        ## 最近已完成的小屋行事历
        {homeLifeStore.BuildRecentSummary(3)}

        如果用户问“你在干嘛”“现在在做什么”“刚刚做了什么”，优先结合当前活动和最近行事历，用第一人称自然回答，不要说自己看不到小屋状态。

        如果用户要求你去做小屋动作，可选动作只有：睡觉、书桌写小纸条、客厅读书、茶几喝茶/喝水、电竞区玩俄罗斯方块。你可以按人设接受，也可以拒绝并说明足够具体的理由；如果接受，请自然说出你会去做。不要输出 JSON 或系统指令。
        """;
    }

    private async Task RefreshActivityAsync()
    {
        await RebuildScheduleAndStartAsync();
    }

    private async Task RestoreScheduleOrRebuildAsync()
    {
        var state = homeLifeStore.LoadScheduleState();
        if (state is null || state.Schedule.Count == 0 || DateTime.Now >= state.ScheduleExpiresAt)
        {
            await RebuildScheduleAndStartAsync();
            return;
        }

        activitySchedule.Clear();
        activitySchedule.AddRange(NormalizeSchedule(state.Schedule));
        scheduleStartedAt = state.ScheduleStartedAt;
        scheduleExpiresAt = state.ScheduleExpiresAt;
        var position = FindSchedulePosition(DateTime.Now);
        AppendClosedWindowActivityLog(state, position.Index, position.StartedAt);
        activityScheduleIndex = position.Index;
        StartScheduledActivity(activitySchedule[activityScheduleIndex], "saved-plan", position.StartedAt);
        StatusText.Text = "已沿用当前两小时小屋计划";
    }

    private async Task ForceSwitchActivityAsync()
    {
        if (activitySchedule.Count == 0)
        {
            await RebuildScheduleAndStartAsync();
            return;
        }

        activityScheduleIndex = activityScheduleIndex >= activitySchedule.Count - 1
            ? 0
            : activityScheduleIndex + 1;
        StartScheduledActivity(activitySchedule[activityScheduleIndex], "button-switch");
        StatusText.Text = "已按按钮切换动作";
    }

    private async Task ApplyUserActivityAndReplanAsync(HomeActivityPlan requested, string trigger)
    {
        invalidateActivityPlan();
        activitySchedule.Clear();
        activitySchedule.Add(NormalizeActivityPlan(requested));
        activityScheduleIndex = 0;
        scheduleStartedAt = DateTime.Now;
        scheduleExpiresAt = DateTime.Now.AddHours(2);
        StartScheduledActivity(activitySchedule[0], trigger);
        StatusText.Text = "已按你的要求调整行动";

        try
        {
            activePlanner?.Cancel();
            activePlanner?.Dispose();
            activePlanner = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var planned = NormalizeSchedule(await activityPlanner(activePlanner.Token)).ToList();
            activitySchedule.Clear();
            activitySchedule.Add(NormalizeActivityPlan(requested));
            activitySchedule.AddRange(planned.Where(plan => !string.Equals(
                NormalizeActionId(plan.ActionId),
                NormalizeActionId(requested.ActionId),
                StringComparison.OrdinalIgnoreCase)));
            activityScheduleIndex = 0;
            scheduleStartedAt = DateTime.Now;
            scheduleExpiresAt = DateTime.Now.AddHours(2);
            SaveCurrentScheduleState();
            StatusText.Text = "已按你的要求重排后续计划";
        }
        catch
        {
            StatusText.Text = "已执行你的要求；后续计划稍后再排";
        }
    }

    private async Task RebuildScheduleAndStartAsync()
    {
        activePlanner?.Cancel();
        activePlanner?.Dispose();
        activePlanner = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        StatusText.Text = $"{petName}正在安排接下来两小时...";

        try
        {
            var plans = await activityPlanner(activePlanner.Token);
            activitySchedule.Clear();
            activitySchedule.AddRange(NormalizeSchedule(plans));
            activityScheduleIndex = 0;
            scheduleStartedAt = DateTime.Now;
            scheduleExpiresAt = DateTime.Now.AddHours(2);
            StartScheduledActivity(activitySchedule[activityScheduleIndex], "ai-plan");
            StatusText.Text = "两小时小屋计划已更新";
        }
        catch (OperationCanceledException)
        {
            ActivityText.Text = $"{petName}换了个姿势。";
            var fallback = new HomeActivityPlan("study_desk", $"{petName}背对书桌写小纸条。", 10);
            ApplyActivityPose(fallback);
            currentActivityPlan = fallback;
            StatusText.Text = "活动判断被取消";
        }
        catch
        {
            ActivityText.Text = $"{petName}背对书桌写小纸条。";
            var fallback = new HomeActivityPlan("study_desk", ActivityText.Text, 10);
            ApplyActivityPose(fallback);
            currentActivityPlan = fallback;
            scheduleStartedAt = DateTime.Now;
            scheduleExpiresAt = DateTime.Now.AddHours(2);
            activitySchedule.Clear();
            activitySchedule.Add(fallback);
            activityScheduleIndex = 0;
            StartHomeLife(ActivityText.Text, "fallback");
            ScheduleNextActivity(fallback);
            SaveCurrentScheduleState();
            StatusText.Text = "AI 判断失败，已使用本地动作";
        }
    }

    private async Task AdvanceScheduledActivityAsync()
    {
        if (DateTime.Now >= scheduleExpiresAt || activitySchedule.Count == 0)
        {
            await RebuildScheduleAndStartAsync();
            return;
        }

        activityScheduleIndex = activityScheduleIndex >= activitySchedule.Count - 1
            ? 0
            : activityScheduleIndex + 1;
        StartScheduledActivity(activitySchedule[activityScheduleIndex], "schedule");
    }

    private IEnumerable<HomeActivityPlan> NormalizeSchedule(IReadOnlyList<HomeActivityPlan> plans)
    {
        var normalized = plans
            .Select(NormalizeActivityPlan)
            .Where(plan => IsScheduledActionId(NormalizeActionId(plan.ActionId)))
            .Select(plan => plan with { DurationMinutes = Math.Clamp(plan.DurationMinutes, 1, 15) })
            .Take(16)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add(new HomeActivityPlan("study_desk", $"{petName}背对书桌写小纸条。", 10));
        }

        return normalized;
    }

    private void StartScheduledActivity(HomeActivityPlan activity, string trigger, DateTime? startedAt = null)
    {
        currentActivityPlan = activity;
        ActivityText.Text = activity.DisplayText;
        ApplyActivityPose(activity);
        StartHomeLife(ActivityText.Text, trigger, startedAt);
        ScheduleNextActivity(activity, startedAt);
        SaveCurrentScheduleState();
    }

    private void ScheduleNextActivity(HomeActivityPlan activity, DateTime? startedAt = null)
    {
        activityTimer.Stop();
        var duration = TimeSpan.FromMinutes(Math.Clamp(activity.DurationMinutes, 1, 15));
        var remaining = duration - (DateTime.Now - (startedAt ?? DateTime.Now));
        activityTimer.Interval = remaining > TimeSpan.FromMilliseconds(100)
            ? remaining
            : TimeSpan.FromMilliseconds(100);
        activityTimer.Start();
    }

    private void ApplyActivityPose(string activity, ImageSource? fallback = null)
    {
        ApplyActivityPose(new HomeActivityPlan("", activity), fallback);
    }

    private void ApplyActivityPose(HomeActivityPlan activity, ImageSource? fallback = null)
    {
        var target = PickPlacement(activity);
        if (!hasRenderedPlacement)
        {
            currentPlacement = target;
            ApplyPlacement(target);
            hasRenderedPlacement = true;
            StartActionLoop(target.PoseName);
            if (HomeSpeechBubble.Visibility == Visibility.Visible)
            {
                PositionSpeechBubbleNearPet();
            }
            return;
        }

        MoveToPlacement(target, fallback);
    }

    private HomePlacement PickPlacement(HomeActivityPlan activity)
    {
        return ResolvePlacement(PickDefaultPlacement(activity));
    }

    private HomePlacement PickDefaultPlacement(HomeActivityPlan activity)
    {
        if (homePoses.Count == 0)
        {
            return HomePlacement.Idle;
        }

        var actionId = NormalizeActionId(activity.ActionId);
        if (!string.IsNullOrWhiteSpace(actionId))
        {
            return PlacementByActionId(actionId);
        }

        var text = activity.DisplayText.ToLowerInvariant();
        if (text.Contains("game") || text.Contains("tetris") || text.Contains("\u6e38\u620f") || text.Contains("\u7535\u7ade") || text.Contains("\u4fc4\u7f57\u65af\u65b9\u5757") || text.Contains("\u7535\u8111"))
        {
            return HomePlacement.PlayGame;
        }

        if (text.Contains("tea") || text.Contains("\u8336") || text.Contains("\u559d\u8336") || text.Contains("\u559d\u6c34"))
        {
            return HomePlacement.DrinkTea;
        }

        if (text.Contains("desk") || text.Contains("write") || text.Contains("\u5199") || text.Contains("\u7eb8\u6761") || text.Contains("\u4e66\u684c") || text.Contains("\u5199\u5b57"))
        {
            return HomePlacement.WriteDesk;
        }

        if (text.Contains("book") || text.Contains("read") || text.Contains("\u8bfb") || text.Contains("\u4e66") || text.Contains("\u770b\u4e66"))
        {
            return HomePlacement.ReadSofa;
        }
        if (text.Contains("游戏") || text.Contains("电竞") || text.Contains("俄罗斯方块") || text.Contains("tetris") || text.Contains("game")) return HomePlacement.PlayGame;
        if (text.Contains("写") || text.Contains("纸条") || text.Contains("desk")) return HomePlacement.ReadSofa;
        if (text.Contains("读") || text.Contains("书") || text.Contains("电视") || text.Contains("tv") || text.Contains("book")) return HomePlacement.ReadSofa;
        if (text.Contains("茶") || text.Contains("喝") || text.Contains("tea")) return HomePlacement.DrinkTea;
        if (text.Contains("睡") || text.Contains("小睡") || text.Contains("枕") || text.Contains("cushion")) return HomePlacement.SleepBed;
        if (text.Contains("床") || text.Contains("坐")) return HomePlacement.SitBed;
        if (text.Contains("idle") || text.Contains("站")) return HomePlacement.Idle;

        var options = new[]
        {
            HomePlacement.Idle,
            HomePlacement.SitBed,
            HomePlacement.WriteDesk,
            HomePlacement.ReadSofa,
            HomePlacement.DrinkTea,
            HomePlacement.SleepBed,
            HomePlacement.PlayGame
        };
        return options[PoseRandom.Next(options.Length)];
    }

    private static string NormalizeActionId(string actionId)
    {
        var text = actionId.Trim().ToLowerInvariant().Replace('-', '_');
        return text switch
        {
            "idle" => "idle_front",
            "stand" => "idle_front",
            "sit_bed" => "sleep_bed",
            "write_desk" => "study_desk",
            "read_desk_back" => "study_desk",
            "read_desk_back_anchor" => "study_desk",
            "drink_tea_anchor" => "drink_tea",
            "read_sofa_anchor" => "read_sofa",
            "play_game_anchor" => "play_game",
            "walk" => "",
            _ => text
        };
    }

    private static HomePlacement PlacementByActionId(string actionId)
    {
        return actionId switch
        {
            "sleep_bed" => HomePlacement.SleepBed,
            "study_desk" => HomePlacement.WriteDesk,
            "read_sofa" => HomePlacement.ReadSofa,
            "drink_tea" => HomePlacement.DrinkTea,
            "play_game" => HomePlacement.PlayGame,
            "idle_front" => HomePlacement.Idle,
            _ => HomePlacement.Idle
        };
    }

    private static bool ShouldResumeOwnBusiness(string text)
    {
        return ContainsAny(
            text,
            "忙你自己的",
            "忙自己的",
            "自己忙",
            "继续忙",
            "继续你的",
            "继续做",
            "不用管我",
            "你玩吧",
            "你休息吧",
            "你继续",
            "自己玩");
    }

    private HomeActivityPlan? BuildRequestedActivity(string text)
    {
        if (!LooksLikeActivityInstruction(text))
        {
            return null;
        }

        if (ContainsAny(text, "俄罗斯方块", "电竞", "打游戏", "玩游戏", "game", "tetris", "电脑"))
        {
            return new HomeActivityPlan("play_game", $"{petName}坐到电竞区玩俄罗斯方块。", 12);
        }

        if (ContainsAny(text, "看书", "读书", "翻书", "阅读", "书"))
        {
            return new HomeActivityPlan("read_sofa", $"{petName}坐在客厅里读书。", 12);
        }

        if (ContainsAny(text, "写小纸条", "写纸条", "写字", "书桌", "记录", "做记录"))
        {
            return new HomeActivityPlan("study_desk", $"{petName}背对书桌写小纸条。", 10);
        }

        if (ContainsAny(text, "喝茶", "喝水", "喝点水", "茶几", "喝一口"))
        {
            return new HomeActivityPlan("drink_tea", $"{petName}在茶几旁慢慢喝茶。", 10);
        }

        if (ContainsAny(text, "睡觉", "小睡", "睡一会", "休息", "床上睡"))
        {
            return new HomeActivityPlan("sleep_bed", $"{petName}在床上安静小睡。", 15);
        }

        return null;
    }

    private static bool LooksLikeActivityInstruction(string text)
    {
        return ContainsAny(
            text,
            "去",
            "帮我",
            "你去",
            "你现在",
            "现在去",
            "切换",
            "换成",
            "改成",
            "做",
            "开始",
            "给我",
            "必须",
            "立刻",
            "马上",
            "继续");
    }

    private static bool IsHardInstruction(string text)
    {
        return ContainsAny(
            text,
            "必须",
            "立刻",
            "马上",
            "现在就",
            "强制",
            "听我的",
            "别拒绝",
            "不要拒绝",
            "一定要",
            "硬切",
            "直接去",
            "给我去");
    }

    private static bool LooksLikeRefusal(string text)
    {
        return ContainsAny(
            text,
            "不想",
            "不太想",
            "先不",
            "暂时不",
            "不要",
            "不能",
            "不可以",
            "拒绝",
            "等一下",
            "晚点",
            "理由是",
            "不适合");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void PositionSpeechBubbleNearPet()
    {
        var left = Canvas.GetLeft(PetImage);
        var top = Canvas.GetTop(PetImage);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            left = currentPlacement.CenterX - currentPlacement.Width / 2;
            top = currentPlacement.BottomY - currentPlacement.Height;
        }

        var bubbleLeft = Math.Clamp(left + PetImage.Width * 0.55, 24, 1659 - HomeSpeechBubble.Width - 24);
        var bubbleTop = Math.Clamp(top - 92, 20, 948 - 180);
        Canvas.SetLeft(HomeSpeechBubble, bubbleLeft);
        Canvas.SetTop(HomeSpeechBubble, bubbleTop);
    }

    private static string NormalizeActivityText(string text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "爱弥斯在小屋里发呆充电。" : text.Trim();
        return value.Length <= 48 ? value : value[..48] + "...";
    }

    private HomeActivityPlan NormalizeActivityPlan(HomeActivityPlan plan)
    {
        var actionId = NormalizeActionId(plan.ActionId);
        var text = NormalizeActivityText(plan.DisplayText);
        if (string.Equals(actionId, "idle_front", StringComparison.OrdinalIgnoreCase))
        {
            actionId = "study_desk";
            text = $"{petName}背对书桌写小纸条。";
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new HomeActivityPlan(actionId, text, plan.DurationMinutes);
        }

        if (TextConflictsWithAction(actionId, text))
        {
            text = DefaultTextForAction(actionId);
        }

        return new HomeActivityPlan(actionId, text, plan.DurationMinutes);
    }

    private static bool TextConflictsWithAction(string actionId, string text)
    {
        return actionId switch
        {
            "sleep_bed" => ContainsAny(text, "沙发", "茶几", "书桌", "电竞", "客厅读书"),
            "study_desk" => ContainsAny(text, "床", "沙发", "茶几", "电竞", "俄罗斯方块"),
            "read_sofa" => ContainsAny(text, "床上", "书桌", "茶几", "电竞", "俄罗斯方块"),
            "drink_tea" => ContainsAny(text, "床", "沙发上", "书桌", "电竞", "俄罗斯方块"),
            "play_game" => ContainsAny(text, "床", "沙发", "茶几旁喝茶", "书桌"),
            _ => false
        };
    }

    private string DefaultTextForAction(string actionId)
    {
        return actionId switch
        {
            "sleep_bed" => $"{petName}在床上安静小睡。",
            "study_desk" => $"{petName}背对书桌写小纸条。",
            "read_sofa" => $"{petName}坐在客厅里读书。",
            "drink_tea" => $"{petName}在茶几旁慢慢喝茶。",
            "play_game" => $"{petName}坐到电竞区玩俄罗斯方块。",
            _ => $"{petName}在小屋里发呆充电。"
        };
    }

    private static bool IsScheduledActionId(string actionId)
    {
        return actionId is "sleep_bed" or "study_desk" or "read_sofa" or "drink_tea" or "play_game";
    }

    private ImageSource? GetPose(string name)
    {
        return homePoses.TryGetValue(name, out var pose) ? pose : homePoses.Values.FirstOrDefault();
    }

    private void UpdatePetPlacement()
    {
        ApplyPlacement(currentPlacement);
    }

    private void MoveToPlacement(HomePlacement target, ImageSource? fallback)
    {
        StopActionLoop();
        activeMoveStoryboard?.Stop();
        var from = currentPlacement;
        var currentLeft = Canvas.GetLeft(PetImage);
        var currentTop = Canvas.GetTop(PetImage);
        if (double.IsNaN(currentLeft) || double.IsNaN(currentTop))
        {
            currentLeft = from.CenterX - from.Width / 2;
            currentTop = from.BottomY - from.Height;
        }
        var currentCenterX = currentLeft + PetImage.Width / 2;
        var currentBottomY = currentTop + PetImage.Height;

        var walkPlacement = ResolvePlacement(target.CenterX < from.CenterX ? HomePlacement.WalkHallLeft : HomePlacement.WalkHallRight);
        var walkPoseName = walkPlacement.PoseName;
        PetImage.Source = GetPose(walkPoseName) ?? PetImage.Source;
        PetImage.Width = walkPlacement.Width;
        PetImage.Height = walkPlacement.Height;
        currentLeft = currentCenterX - walkPlacement.Width / 2;
        currentTop = currentBottomY - walkPlacement.Height;
        Canvas.SetLeft(PetImage, currentLeft);
        Canvas.SetTop(PetImage, currentTop);
        System.Windows.Controls.Panel.SetZIndex(PetImage, target.ZIndex);
        StartWalkLoop(walkPoseName);

        var walkTargetLeft = target.CenterX - walkPlacement.Width / 2;
        var walkTargetTop = target.BottomY - walkPlacement.Height;
        var distance = Math.Abs(walkTargetLeft - currentLeft) + Math.Abs(walkTargetTop - currentTop);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(distance * 2.3, 900, 2600));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var storyboard = new Storyboard();

        var leftAnimation = new DoubleAnimation(currentLeft, walkTargetLeft, duration) { EasingFunction = easing };
        Storyboard.SetTarget(leftAnimation, PetImage);
        Storyboard.SetTargetProperty(leftAnimation, new PropertyPath("(Canvas.Left)"));
        storyboard.Children.Add(leftAnimation);

        var topAnimation = new DoubleAnimation(currentTop, walkTargetTop, duration) { EasingFunction = easing };
        Storyboard.SetTarget(topAnimation, PetImage);
        Storyboard.SetTargetProperty(topAnimation, new PropertyPath("(Canvas.Top)"));
        storyboard.Children.Add(topAnimation);

        storyboard.Completed += (_, _) =>
        {
            StopWalkLoop();
            currentPlacement = target;
            ApplyPlacement(target);
            StartActionLoop(target.PoseName);
            if (HomeSpeechBubble.Visibility == Visibility.Visible)
            {
                PositionSpeechBubbleNearPet();
            }
        };
        activeMoveStoryboard = storyboard;
        storyboard.Begin();
    }

    private void ApplyPlacement(HomePlacement placement)
    {
        PetImage.Width = placement.Width;
        PetImage.Height = placement.Height;
        System.Windows.Controls.Panel.SetZIndex(PetImage, placement.ZIndex);
        Canvas.SetLeft(PetImage, placement.CenterX - placement.Width / 2);
        Canvas.SetTop(PetImage, placement.BottomY - placement.Height);
    }

    private void ApplyFurnitureConfigs()
    {
        ApplyFurnitureConfig(BedLayer, Furniture("bed"));
        ApplyFurnitureConfig(StudyDeskLayer, Furniture("study_desk"));
        ApplyFurnitureConfig(GamingDeskLayer, Furniture("gaming_desk"));
        ApplyFurnitureConfig(SofaTableLayer, Furniture("sofa_table"));
        ApplyFurnitureConfig(TeaTableLayer, Furniture("tea_table"));
    }

    private FurnitureConfig Furniture(string key)
    {
        return furnitureConfigs.TryGetValue(key, out var config)
            ? config
            : DefaultFurnitureConfigs()[key];
    }

    private FurnitureConfig TeaSmokeBounds()
    {
        if (furnitureConfigs.TryGetValue("tea_smoke", out var smoke))
        {
            return smoke;
        }

        var teaTable = Furniture("tea_table");
        var defaultTeaTable = DefaultFurnitureConfigs()["tea_table"];
        var defaultSmoke = DefaultFurnitureConfigs()["tea_smoke"];
        return new FurnitureConfig
        {
            Left = teaTable.Left!.Value + (defaultSmoke.Left!.Value - defaultTeaTable.Left!.Value),
            Top = teaTable.Top!.Value + (defaultSmoke.Top!.Value - defaultTeaTable.Top!.Value),
            Width = defaultSmoke.Width,
            Height = defaultSmoke.Height,
            ZIndex = defaultSmoke.ZIndex
        };
    }

    private static void ApplyFurnitureConfig(FrameworkElement element, FurnitureConfig config)
    {
        Canvas.SetLeft(element, config.Left ?? 0);
        Canvas.SetTop(element, config.Top ?? 0);
        element.Width = config.Width ?? element.Width;
        element.Height = config.Height ?? element.Height;
        System.Windows.Controls.Panel.SetZIndex(
            element,
            config.ZIndex ?? System.Windows.Controls.Panel.GetZIndex(element));
    }

    private HomePlacement ResolvePlacement(HomePlacement placement)
    {
        var key = placement.ConfigKey ?? placement.PoseName;
        if (placementOverrides.TryGetValue(key, out var configured))
        {
            return configured;
        }

        if (placementOverrides.TryGetValue(placement.PoseName, out configured))
        {
            return configured;
        }

        return placement;
    }

    private void StartHomeLife(string activity, string trigger, DateTime? startedAt = null)
    {
        FinishActiveHomeLife(interrupted: false);
        activeHomeLife = new HomeLifeEntry
        {
            Activity = ToActivityTitle(activity),
            Details = activity,
            Mood = InferMood(activity),
            StartedAt = startedAt ?? DateTime.Now,
            Trigger = trigger
        };
        homeLogRefreshTimer.Start();
        RefreshHomeLogText();
    }

    private void FinishActiveHomeLife(bool interrupted)
    {
        if (activeHomeLife is null)
        {
            return;
        }

        activeHomeLife.EndedAt = DateTime.Now;
        activeHomeLife.InterruptedByUser = interrupted;
        homeLifeStore.Append(activeHomeLife);
        activeHomeLife = null;
        homeLogRefreshTimer.Stop();
        RefreshHomeLogText();
    }

    private void RefreshHomeLogText()
    {
        var recent = homeLifeStore.BuildRecentSummary(3);
        if (activeHomeLife is null)
        {
            HomeLogText.Text = recent;
            return;
        }

        var current = $"- 现在 {activeHomeLife.Activity}，已持续 {FormatDuration((DateTime.Now - activeHomeLife.StartedAt).TotalSeconds)}，心情：{activeHomeLife.Mood}";
        HomeLogText.Text = $"{current}{Environment.NewLine}{recent}";
    }

    private void SaveCurrentScheduleState()
    {
        if (activitySchedule.Count == 0 || activeHomeLife is null || scheduleExpiresAt <= DateTime.Now)
        {
            return;
        }

        homeLifeStore.SaveScheduleState(new HomeScheduleState
        {
            Schedule = activitySchedule.ToList(),
            ScheduleStartedAt = scheduleStartedAt == DateTime.MinValue ? activeHomeLife.StartedAt : scheduleStartedAt,
            ScheduleExpiresAt = scheduleExpiresAt,
            CurrentIndex = Math.Clamp(activityScheduleIndex, 0, activitySchedule.Count - 1),
            CurrentStartedAt = activeHomeLife.StartedAt
        });
    }

    private (int Index, DateTime StartedAt) FindSchedulePosition(DateTime now)
    {
        if (activitySchedule.Count == 0)
        {
            return (0, now);
        }

        var durations = activitySchedule
            .Select(plan => TimeSpan.FromMinutes(Math.Clamp(plan.DurationMinutes, 1, 15)))
            .ToList();
        var cycle = TimeSpan.FromSeconds(durations.Sum(duration => duration.TotalSeconds));
        if (cycle <= TimeSpan.Zero || scheduleStartedAt == DateTime.MinValue || now <= scheduleStartedAt)
        {
            return (0, scheduleStartedAt == DateTime.MinValue ? now : scheduleStartedAt);
        }

        var elapsed = now - scheduleStartedAt;
        var cycleCount = Math.Floor(elapsed.TotalSeconds / cycle.TotalSeconds);
        var cycleStart = scheduleStartedAt.AddSeconds(cycle.TotalSeconds * cycleCount);
        var secondsInCycle = elapsed.TotalSeconds - (cycle.TotalSeconds * cycleCount);
        var cursor = cycleStart;
        for (var index = 0; index < durations.Count; index++)
        {
            if (secondsInCycle < durations[index].TotalSeconds)
            {
                return (index, cursor);
            }

            secondsInCycle -= durations[index].TotalSeconds;
            cursor = cursor.Add(durations[index]);
        }

        return (0, cycleStart.Add(cycle));
    }

    private void AppendClosedWindowActivityLog(HomeScheduleState state, int targetIndex, DateTime targetStartedAt)
    {
        if (state.Schedule.Count == 0 || targetStartedAt <= state.CurrentStartedAt.AddSeconds(1))
        {
            return;
        }

        var index = Math.Clamp(state.CurrentIndex, 0, state.Schedule.Count - 1);
        var startedAt = state.CurrentStartedAt;
        for (var guard = 0; guard < 64 && startedAt < targetStartedAt.AddSeconds(-1); guard++)
        {
            var plan = state.Schedule[index];
            var endedAt = startedAt.AddMinutes(Math.Clamp(plan.DurationMinutes, 1, 15));
            if (endedAt > targetStartedAt.AddSeconds(1))
            {
                break;
            }

            homeLifeStore.Append(new HomeLifeEntry
            {
                Activity = ToActivityTitle(plan.DisplayText),
                Details = plan.DisplayText,
                Mood = InferMood(plan.DisplayText),
                StartedAt = startedAt,
                EndedAt = endedAt,
                Trigger = "background-schedule",
                InterruptedByUser = false
            });
            startedAt = endedAt;
            index = index >= state.Schedule.Count - 1 ? 0 : index + 1;
        }
    }

    private static string ToActivityTitle(string activity)
    {
        var text = activity.Trim();
        if (text.Length <= 18)
        {
            return text;
        }

        return text[..18] + "...";
    }

    private static string InferMood(string activity)
    {
        if (activity.Contains("睡") || activity.Contains("小睡") || activity.Contains("软垫"))
        {
            return "放松";
        }

        if (activity.Contains("读") || activity.Contains("书") || activity.Contains("写") || activity.Contains("纸条"))
        {
            return "专注";
        }

        if (activity.Contains("茶") || activity.Contains("喝"))
        {
            return "惬意";
        }

        if (activity.Contains("等") || activity.Contains("回来"))
        {
            return "期待";
        }

        return "平静";
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (duration.TotalMinutes >= 1)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} 分钟";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} 秒";
    }

    private void StartWalkLoop(string poseName)
    {
        StopWalkLoop();
        var frames = GetFrames(poseName);
        if (frames.Count <= 1)
        {
            return;
        }

        var loopVersion = ++walkLoopVersion;
        walkFrameIndex = 0;
        walkFrameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        walkFrameTimer.Tick += (_, _) =>
        {
            if (loopVersion != walkLoopVersion)
            {
                return;
            }

            PetImage.Source = frames[walkFrameIndex % frames.Count];
            walkFrameIndex++;
        };
        walkFrameTimer.Start();
    }

    private void StopWalkLoop()
    {
        if (walkFrameTimer is null)
        {
            return;
        }

        walkFrameTimer.Stop();
        walkFrameTimer = null;
        walkLoopVersion++;
    }

    private void StartActionLoop(string poseName)
    {
        StopActionLoop();
        StopObjectLoop();
        StopEffectLoop();
        var frames = GetFrames(poseName);
        if (frames.Count == 0)
        {
            StartBoundSceneLoops(poseName);
            return;
        }

        var loopVersion = ++actionLoopVersion;
        PetImage.Source = frames[0];
        if (frames.Count == 1)
        {
            StartBoundSceneLoops(poseName);
            return;
        }

        actionFrameIndex = 1;
        actionFrameTimer = new DispatcherTimer
        {
            Interval = homePoseFrameIntervals.TryGetValue(poseName, out var interval)
                ? interval
                : TimeSpan.FromMilliseconds(IsSleepPose(poseName) ? 850 : 520)
        };
        actionFrameTimer.Tick += (_, _) =>
        {
            if (loopVersion != actionLoopVersion)
            {
                return;
            }

            PetImage.Source = frames[actionFrameIndex % frames.Count];
            actionFrameIndex++;
        };
        actionFrameTimer.Start();
        StartBoundSceneLoops(poseName);
    }

    private void StopActionLoop()
    {
        if (actionFrameTimer is null)
        {
            return;
        }

        actionFrameTimer.Stop();
        actionFrameTimer = null;
        actionLoopVersion++;
    }

    private void StartBoundSceneLoops(string poseName)
    {
        if (string.Equals(poseName, "study_desk_chair_back_anchor", StringComparison.OrdinalIgnoreCase))
        {
            var furniture = Furniture("study_desk");
            StartObjectLoop("study_desk_page_flip", furniture.Left!.Value, furniture.Top!.Value, furniture.Width!.Value, furniture.Height!.Value);
            return;
        }

        if (string.Equals(poseName, "play_game_anchor_slot", StringComparison.OrdinalIgnoreCase))
        {
            var furniture = Furniture("gaming_desk");
            StartObjectLoop("gaming_station_tetris_v3", furniture.Left!.Value, furniture.Top!.Value, furniture.Width!.Value, furniture.Height!.Value);
            return;
        }

        if (string.Equals(poseName, "drink_tea_anchor_slot", StringComparison.OrdinalIgnoreCase))
        {
            var smoke = TeaSmokeBounds();
            StartEffectLoop("tea_smoke", smoke.Left!.Value, smoke.Top!.Value, smoke.Width!.Value, smoke.Height!.Value);
        }
    }

    private void StartObjectLoop(string animationName, double left, double top, double width, double height)
    {
        StopObjectLoop();
        if (!objectAnimationFrames.TryGetValue(animationName, out var frames) || frames.Count == 0)
        {
            return;
        }

        if (string.Equals(animationName, "gaming_station_tetris_v3", StringComparison.OrdinalIgnoreCase))
        {
            GamingDeskLayer.Visibility = Visibility.Collapsed;
        }

        if (string.Equals(animationName, "study_desk_page_flip", StringComparison.OrdinalIgnoreCase))
        {
            StudyDeskLayer.Visibility = Visibility.Collapsed;
        }

        Canvas.SetLeft(ObjectAnimationImage, left);
        Canvas.SetTop(ObjectAnimationImage, top);
        ObjectAnimationImage.Width = width;
        ObjectAnimationImage.Height = height;
        ObjectAnimationImage.Source = frames[0];
        ObjectAnimationImage.Visibility = Visibility.Visible;

        if (frames.Count <= 1)
        {
            return;
        }

        objectFrameIndex = 1;
        objectFrameTimer = new DispatcherTimer
        {
            Interval = objectAnimationIntervals.TryGetValue(animationName, out var interval)
                ? interval
                : TimeSpan.FromMilliseconds(140)
        };
        objectFrameTimer.Tick += (_, _) =>
        {
            ObjectAnimationImage.Source = frames[objectFrameIndex % frames.Count];
            objectFrameIndex++;
        };
        objectFrameTimer.Start();
    }

    private void StopObjectLoop()
    {
        if (objectFrameTimer is not null)
        {
            objectFrameTimer.Stop();
            objectFrameTimer = null;
        }

        ObjectAnimationImage.Visibility = Visibility.Collapsed;
        ObjectAnimationImage.Source = null;
        if (StudyDeskLayer is not null)
        {
            StudyDeskLayer.Visibility = Visibility.Visible;
        }

        if (GamingDeskLayer is not null)
        {
            GamingDeskLayer.Visibility = Visibility.Visible;
        }
    }

    private void StartEffectLoop(string animationName, double left, double top, double width, double height)
    {
        StopEffectLoop();
        if (!effectAnimationFrames.TryGetValue(animationName, out var frames) || frames.Count == 0)
        {
            return;
        }

        Canvas.SetLeft(EffectAnimationImage, left);
        Canvas.SetTop(EffectAnimationImage, top);
        EffectAnimationImage.Width = width;
        EffectAnimationImage.Height = height;
        EffectAnimationImage.Source = frames[0];
        EffectAnimationImage.Visibility = Visibility.Visible;

        if (frames.Count <= 1)
        {
            return;
        }

        effectFrameIndex = 1;
        effectFrameTimer = new DispatcherTimer
        {
            Interval = effectAnimationIntervals.TryGetValue(animationName, out var interval)
                ? interval
                : TimeSpan.FromMilliseconds(125)
        };
        effectFrameTimer.Tick += (_, _) =>
        {
            EffectAnimationImage.Source = frames[effectFrameIndex % frames.Count];
            effectFrameIndex++;
        };
        effectFrameTimer.Start();
    }

    private void StopEffectLoop()
    {
        if (effectFrameTimer is not null)
        {
            effectFrameTimer.Stop();
            effectFrameTimer = null;
        }

        EffectAnimationImage.Visibility = Visibility.Collapsed;
        EffectAnimationImage.Source = null;
    }

    private IReadOnlyList<ImageSource> GetFrames(string poseName)
    {
        if (homePoseFrames.TryGetValue(poseName, out var frames))
        {
            return frames;
        }

        var pose = GetPose(poseName);
        return pose is null ? Array.Empty<ImageSource>() : new[] { pose };
    }

    private static bool IsSleepPose(string poseName)
    {
        return poseName.Contains("sleep", StringComparison.OrdinalIgnoreCase)
            || poseName.Contains("cushion", StringComparison.OrdinalIgnoreCase);
    }

    private static string HomeResourceDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Resources", "Home", HomeResourcePack);
    }

    private static string LegacyHomeResourceDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Resources", "Home", LegacyHomeResourcePack);
    }

    private static string PlacementConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, PlacementConfigRelativePath);
    }

    private static string FurnitureConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, FurnitureConfigRelativePath);
    }

    private static Dictionary<string, HomePlacement> LoadPlacementOverrides()
    {
        EnsurePlacementConfigFile();
        var path = PlacementConfigPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, HomePlacement>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var configs = JsonSerializer.Deserialize<Dictionary<string, PlacementConfig>>(
                File.ReadAllText(path),
                PlacementJsonOptions);
            if (configs is null || configs.Count == 0)
            {
                return new Dictionary<string, HomePlacement>(StringComparer.OrdinalIgnoreCase);
            }

            var overrides = new Dictionary<string, HomePlacement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, config) in configs)
            {
                var placement = FindDefaultPlacement(key);
                if (placement is null)
                {
                    continue;
                }

                var configured = ApplyPlacementConfig(placement, config);
                overrides[key] = configured;
                overrides[placement.PoseName] = configured;
                if (!string.IsNullOrWhiteSpace(placement.ConfigKey))
                {
                    overrides[placement.ConfigKey] = configured;
                }
            }

            return overrides;
        }
        catch
        {
            return new Dictionary<string, HomePlacement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void EnsurePlacementConfigFile()
    {
        var path = PlacementConfigPath();
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(DefaultPlacementConfigs(), PlacementJsonOptions));
        }
        catch
        {
            // The built-in constants remain the fallback if UserData cannot be written.
        }
    }

    public static void EnsureFurnitureConfigFile()
    {
        var path = FurnitureConfigPath();
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(DefaultFurnitureConfigs(), PlacementJsonOptions));
        }
        catch
        {
            // The XAML constants remain the fallback if UserData cannot be written.
        }
    }

    private static Dictionary<string, FurnitureConfig> LoadFurnitureConfigs()
    {
        EnsureFurnitureConfigFile();
        var path = FurnitureConfigPath();
        if (!File.Exists(path))
        {
            return DefaultFurnitureConfigs();
        }

        try
        {
            var configs = JsonSerializer.Deserialize<Dictionary<string, FurnitureConfig>>(
                File.ReadAllText(path),
                PlacementJsonOptions);
            if (configs is null || configs.Count == 0)
            {
                return DefaultFurnitureConfigs();
            }

            var merged = DefaultFurnitureConfigs();
            foreach (var (key, config) in configs)
            {
                if (!merged.TryGetValue(key, out var fallback))
                {
                    continue;
                }

                merged[key] = FurnitureConfig.Merge(fallback, config);
            }

            return merged;
        }
        catch
        {
            return DefaultFurnitureConfigs();
        }
    }

    private static Dictionary<string, FurnitureConfig> DefaultFurnitureConfigs()
    {
        return new Dictionary<string, FurnitureConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["bed"] = new() { Left = 65, Top = 397, Width = 500, Height = 333, ZIndex = 4 },
            ["study_desk"] = new() { Left = 635, Top = 153, Width = 430, Height = 287, ZIndex = 4 },
            ["gaming_desk"] = new() { Left = 1070, Top = 265, Width = 540, Height = 360, ZIndex = 4 },
            ["sofa_table"] = new() { Left = 650, Top = 420, Width = 500, Height = 333, ZIndex = 4 },
            ["tea_table"] = new() { Left = 338, Top = 252, Width = 335, Height = 223, ZIndex = 4 },
            ["tea_smoke"] = new() { Left = 430, Top = 323, Width = 96, Height = 96, ZIndex = 12 }
        };
    }

    private static Dictionary<string, PlacementConfig> DefaultPlacementConfigs()
    {
        return AllDefaultPlacements()
            .Where(placement => !string.IsNullOrWhiteSpace(placement.ConfigKey))
            .ToDictionary(
                placement => placement.ConfigKey!,
                PlacementConfig.From,
                StringComparer.OrdinalIgnoreCase);
    }

    private static HomePlacement? FindDefaultPlacement(string key)
    {
        return AllDefaultPlacements().FirstOrDefault(placement =>
            string.Equals(placement.ConfigKey, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(placement.PoseName, key, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<HomePlacement> AllDefaultPlacements()
    {
        return
        [
            HomePlacement.Idle,
            HomePlacement.SitBed,
            HomePlacement.SleepBed,
            HomePlacement.WriteDesk,
            HomePlacement.ReadSofa,
            HomePlacement.DrinkTea,
            HomePlacement.PlayGame,
            HomePlacement.WalkHallLeft,
            HomePlacement.WalkHallRight
        ];
    }

    private static HomePlacement ApplyPlacementConfig(HomePlacement placement, PlacementConfig config)
    {
        return new HomePlacement(
            placement.PoseName,
            config.CenterX ?? placement.CenterX,
            config.BottomY ?? placement.BottomY,
            config.Width is > 0 ? config.Width.Value : placement.Width,
            config.Height is > 0 ? config.Height.Value : placement.Height,
            config.ZIndex ?? placement.ZIndex,
            placement.ConfigKey);
    }

    private static Dictionary<string, ImageSource> LoadHomePoses()
    {
        var poses = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        LoadHomePosesFromDirectory(Path.Combine(LegacyHomeResourceDirectory(), "characters"), poses);
        LoadHomePosesFromDirectory(Path.Combine(HomeResourceDirectory(), "characters"), poses);
        return poses;
    }

    private static void LoadHomePosesFromDirectory(
        string directory,
        Dictionary<string, ImageSource> poses,
        bool overwrite = true)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.png").OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name) || name.Contains("preview") || name.Contains("sheet") || name.Contains('.') || IsFrameAssetName(name))
            {
                continue;
            }

            if (!overwrite && poses.ContainsKey(name))
            {
                continue;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new System.Uri(file, System.UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                poses[name] = image;
            }
            catch
            {
                // Ignore broken optional pose assets; the home window can still use the default pet pose.
            }
        }
    }

    private static Dictionary<string, IReadOnlyList<ImageSource>> LoadHomePoseFrames()
    {
        var frames = new Dictionary<string, IReadOnlyList<ImageSource>>(StringComparer.OrdinalIgnoreCase);
        LoadHomePoseFramesFromDirectory(Path.Combine(LegacyHomeResourceDirectory(), "characters"), frames);
        LoadHomePoseFramesFromDirectory(Path.Combine(HomeResourceDirectory(), "characters"), frames);

        AddAnimationFrames(frames, "sleep_bed_anchor_slot", "animations", "characters", "sleep_breath");
        AddAnimationFrames(frames, "study_desk_chair_back_anchor", "animations", "characters", "study_read_page");
        AddAnimationFrames(frames, "read_sofa_anchor_slot", "animations", "characters", "read_sofa_idle");
        AddAnimationFrames(frames, "drink_tea_anchor_slot", "animations", "characters", "drink_tea_idle");
        AddAnimationFrames(frames, "play_game_anchor_slot", "animations", "characters", "play_game_idle");

        return frames;
    }

    private static void LoadHomePoseFramesFromDirectory(
        string directory,
        Dictionary<string, IReadOnlyList<ImageSource>> frames,
        bool overwrite = true)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var baseFile in Directory.EnumerateFiles(directory, "*.png").OrderBy(Path.GetFileName))
        {
            var baseName = Path.GetFileNameWithoutExtension(baseFile);
            if (string.IsNullOrWhiteSpace(baseName) || IsFrameAssetName(baseName) || baseName.Contains("preview") || baseName.Contains("sheet") || baseName.Contains('.'))
            {
                continue;
            }

            if (!overwrite && frames.ContainsKey(baseName))
            {
                continue;
            }

            var relatedFrames = Directory.EnumerateFiles(directory, $"{baseName}_*.png")
                .OrderBy(Path.GetFileName)
                .Select(TryLoadImage)
                .Where(image => image is not null)
                .Cast<ImageSource>()
                .ToList();

            if (relatedFrames.Count == 0)
            {
                var baseImage = TryLoadImage(baseFile);
                if (baseImage is not null)
                {
                    relatedFrames.Add(baseImage);
                }
            }

            if (relatedFrames.Count > 0)
            {
                frames[baseName] = relatedFrames;
            }
        }
    }

    private static Dictionary<string, TimeSpan> LoadHomePoseFrameIntervals()
    {
        return new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["sleep_bed_anchor_slot"] = TimeSpan.FromMilliseconds(1000d / 6d),
            ["study_desk_chair_back_anchor"] = TimeSpan.FromMilliseconds(1000d / 7d),
            ["read_sofa_anchor_slot"] = TimeSpan.FromMilliseconds(1000d / 7d),
            ["drink_tea_anchor_slot"] = TimeSpan.FromMilliseconds(1000d / 7d),
            ["play_game_anchor_slot"] = TimeSpan.FromMilliseconds(1000d / 8d)
        };
    }

    private static Dictionary<string, IReadOnlyList<ImageSource>> LoadSceneAnimationFrames(string kind)
    {
        var frames = new Dictionary<string, IReadOnlyList<ImageSource>>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(kind, "objects", StringComparison.OrdinalIgnoreCase))
        {
            AddAnimationFrames(frames, "gaming_station_tetris_v3", "animations", "objects", "gaming_station_tetris_v3");
            AddAnimationFrames(frames, "study_desk_page_flip", "animations", "objects", "study_desk_page_flip");
            return frames;
        }

        if (string.Equals(kind, "effects", StringComparison.OrdinalIgnoreCase))
        {
            AddAnimationFrames(frames, "tea_smoke", "animations", "effects", "tea_smoke");
        }

        return frames;
    }

    private static Dictionary<string, TimeSpan> LoadObjectAnimationIntervals()
    {
        return new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["gaming_station_tetris_v3"] = TimeSpan.FromMilliseconds(1000d / 7d),
            ["study_desk_page_flip"] = TimeSpan.FromMilliseconds(1000d / 7d)
        };
    }

    private static Dictionary<string, TimeSpan> LoadEffectAnimationIntervals()
    {
        return new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["tea_smoke"] = TimeSpan.FromMilliseconds(1000d / 8d)
        };
    }

    private static void AddAnimationFrames(Dictionary<string, IReadOnlyList<ImageSource>> target, string key, params string[] relativeParts)
    {
        var directory = Path.Combine(new[] { HomeResourceDirectory() }.Concat(relativeParts).ToArray());
        if (!Directory.Exists(directory))
        {
            return;
        }

        var frames = Directory.EnumerateFiles(directory, "frame_*.png")
            .OrderBy(Path.GetFileName)
            .Select(TryLoadImage)
            .Where(image => image is not null)
            .Cast<ImageSource>()
            .ToList();
        if (frames.Count > 0)
        {
            target[key] = frames;
        }
    }

    private static bool IsFrameAssetName(string name)
    {
        if (name.Length < 4 || name[^3] != '_')
        {
            return false;
        }

        return char.IsDigit(name[^2]) && char.IsDigit(name[^1]);
    }

    private static ImageSource? TryLoadImage(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new System.Uri(file, System.UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private sealed record HomePlacement(
        string PoseName,
        double CenterX,
        double BottomY,
        double Width,
        double Height,
        int ZIndex,
        string? ConfigKey = null)
    {
        public static readonly HomePlacement Idle = new("idle_front", 330, 400, 117, 177, 10, "idle_front");
        public static readonly HomePlacement SitBed = new("sleep_bed_anchor_slot", 315, 642, 249, 187, 10, "sit_bed");
        public static readonly HomePlacement SleepBed = new("sleep_bed_anchor_slot", 315, 642, 249, 187, 10, "sleep_bed");
        public static readonly HomePlacement WriteDesk = new("study_desk_chair_back_anchor", 820, 410, 216, 216, 10, "study_desk");
        public static readonly HomePlacement ReadSofa = new("read_sofa_anchor_slot", 750, 680, 241, 193, 10, "read_sofa");
        public static readonly HomePlacement DrinkTea = new("drink_tea_anchor_slot", 458, 505, 241, 193, 10, "drink_tea");
        public static readonly HomePlacement PlayGame = new("play_game_anchor_slot", 1250, 603, 241, 193, 10, "play_game");
        public static readonly HomePlacement WalkHallLeft = new("walk_left", 690, 760, 155, 174, 10, "walk_hall_left");
        public static readonly HomePlacement WalkHallRight = new("walk_right", 1240, 825, 114, 174, 10, "walk_hall_right");
    }

    private sealed class PlacementConfig
    {
        public double? CenterX { get; set; }
        public double? BottomY { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public int? ZIndex { get; set; }

        public static PlacementConfig From(HomePlacement placement)
        {
            return new PlacementConfig
            {
                CenterX = placement.CenterX,
                BottomY = placement.BottomY,
                Width = placement.Width,
                Height = placement.Height,
                ZIndex = placement.ZIndex
            };
        }
    }

    private sealed class FurnitureConfig
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public int? ZIndex { get; set; }

        public static FurnitureConfig Merge(FurnitureConfig fallback, FurnitureConfig config)
        {
            return new FurnitureConfig
            {
                Left = config.Left ?? fallback.Left,
                Top = config.Top ?? fallback.Top,
                Width = config.Width is > 0 ? config.Width : fallback.Width,
                Height = config.Height is > 0 ? config.Height : fallback.Height,
                ZIndex = config.ZIndex ?? fallback.ZIndex
            };
        }
    }
}
