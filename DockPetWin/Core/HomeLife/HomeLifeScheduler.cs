using System.Windows.Threading;

namespace DockPetWin.Core.HomeLife;

public sealed class HomeLifeScheduler
{
    private readonly HomeLifeStore store;
    private readonly Func<CancellationToken, Task<IReadOnlyList<HomeActivityPlan>>> planBuilder;
    private readonly Func<string> petNameProvider;
    private readonly DispatcherTimer timer = new();
    private CancellationTokenSource? activePlanRequest;
    private bool isRunning;
    private bool isProcessing;

    public HomeLifeScheduler(
        HomeLifeStore store,
        Func<CancellationToken, Task<IReadOnlyList<HomeActivityPlan>>> planBuilder,
        Func<string> petNameProvider)
    {
        this.store = store;
        this.planBuilder = planBuilder;
        this.petNameProvider = petNameProvider;
        timer.Tick += async (_, _) => await ProcessAsync();
    }

    public void Start()
    {
        if (isRunning)
        {
            return;
        }

        isRunning = true;
        _ = ProcessAsync();
    }

    public void Stop()
    {
        isRunning = false;
        timer.Stop();
        activePlanRequest?.Cancel();
    }

    public async Task RefreshAsync()
    {
        if (!isRunning)
        {
            return;
        }

        await ProcessAsync();
    }

    private async Task ProcessAsync()
    {
        if (!isRunning || isProcessing)
        {
            return;
        }

        isProcessing = true;
        timer.Stop();
        try
        {
            var now = DateTime.Now;
            var state = store.LoadScheduleState();
            if (state is null || state.Schedule.Count == 0 || now >= state.ScheduleExpiresAt)
            {
                await BuildNewScheduleAsync(now);
                return;
            }

            AdvanceSavedSchedule(state, now);
        }
        finally
        {
            isProcessing = false;
            ScheduleNextWake();
        }
    }

    private async Task BuildNewScheduleAsync(DateTime now)
    {
        activePlanRequest?.Cancel();
        activePlanRequest?.Dispose();
        activePlanRequest = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IReadOnlyList<HomeActivityPlan> plans;
        try
        {
            plans = await planBuilder(activePlanRequest.Token);
        }
        catch
        {
            plans = [HomeActivityPlan.Idle(petNameProvider(), "")];
        }

        var schedule = NormalizeSchedule(plans).ToList();
        if (schedule.Count == 0)
        {
            schedule.Add(HomeActivityPlan.Idle(petNameProvider(), ""));
        }

        store.SaveScheduleState(new HomeScheduleState
        {
            Schedule = schedule,
            ScheduleStartedAt = now,
            ScheduleExpiresAt = now.AddHours(2),
            CurrentIndex = 0,
            CurrentStartedAt = now
        });
    }

    private void AdvanceSavedSchedule(HomeScheduleState state, DateTime now)
    {
        var schedule = NormalizeSchedule(state.Schedule).ToList();
        if (schedule.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(state.CurrentIndex, 0, schedule.Count - 1);
        var startedAt = state.CurrentStartedAt;
        if (startedAt == default || startedAt < state.ScheduleStartedAt)
        {
            startedAt = state.ScheduleStartedAt;
        }

        var guardLimit = schedule.Count * 24;
        var changed = false;
        for (var guard = 0; guard < guardLimit; guard++)
        {
            var plan = schedule[index];
            var endedAt = startedAt.AddMinutes(Math.Clamp(plan.DurationMinutes, 1, 15));
            if (endedAt > now || endedAt > state.ScheduleExpiresAt)
            {
                break;
            }

            store.Append(new HomeLifeEntry
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
            index = index >= schedule.Count - 1 ? 0 : index + 1;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        state.Schedule = schedule;
        state.CurrentIndex = index;
        state.CurrentStartedAt = startedAt;
        store.SaveScheduleState(state);
    }

    private void ScheduleNextWake()
    {
        if (!isRunning)
        {
            return;
        }

        var next = TimeSpan.FromMinutes(5);
        var state = store.LoadScheduleState();
        if (state is { Schedule.Count: > 0 } && DateTime.Now < state.ScheduleExpiresAt)
        {
            var index = Math.Clamp(state.CurrentIndex, 0, state.Schedule.Count - 1);
            var duration = TimeSpan.FromMinutes(Math.Clamp(state.Schedule[index].DurationMinutes, 1, 15));
            var dueIn = state.CurrentStartedAt.Add(duration) - DateTime.Now;
            next = dueIn > TimeSpan.FromSeconds(10) ? dueIn : TimeSpan.FromSeconds(10);
        }

        timer.Interval = next > TimeSpan.FromMinutes(15) ? TimeSpan.FromMinutes(15) : next;
        timer.Start();
    }

    private static IEnumerable<HomeActivityPlan> NormalizeSchedule(IReadOnlyList<HomeActivityPlan> plans)
    {
        return plans
            .Select(NormalizeActivityPlan)
            .Where(plan => IsScheduledActionId(NormalizeActionId(plan.ActionId)))
            .Select(plan => plan with { DurationMinutes = Math.Clamp(plan.DurationMinutes, 1, 15) })
            .Take(16);
    }

    private static HomeActivityPlan NormalizeActivityPlan(HomeActivityPlan plan)
    {
        var actionId = NormalizeActionId(plan.ActionId);
        var text = string.IsNullOrWhiteSpace(plan.DisplayText)
            ? DefaultTextForAction(actionId)
            : plan.DisplayText.Trim();
        return new HomeActivityPlan(actionId, text, Math.Clamp(plan.DurationMinutes, 1, 15));
    }

    private static string NormalizeActionId(string actionId)
    {
        var text = actionId.Trim().ToLowerInvariant().Replace('-', '_');
        return text switch
        {
            "idle" => "study_desk",
            "write_desk" => "study_desk",
            "sleep_bed_anchor_slot" => "sleep_bed",
            "study_desk_chair_back_anchor" => "study_desk",
            "drink_tea_anchor_slot" => "drink_tea",
            "read_sofa_anchor_slot" => "read_sofa",
            "play_game_anchor_slot" => "play_game",
            "cook_kitchen_anchor_slot" => "cook_kitchen",
            "cook" => "cook_kitchen",
            "cooking" => "cook_kitchen",
            "kitchen" => "cook_kitchen",
            _ => text
        };
    }

    private static bool IsScheduledActionId(string actionId)
    {
        return actionId is "sleep_bed" or "study_desk" or "read_sofa" or "drink_tea" or "play_game" or "cook_kitchen";
    }

    private static string DefaultTextForAction(string actionId)
    {
        return actionId switch
        {
            "sleep_bed" => "\u7231\u5f25\u65af\u5728\u5e8a\u4e0a\u5b89\u9759\u5c0f\u7761\u3002",
            "study_desk" => "\u7231\u5f25\u65af\u80cc\u5bf9\u4e66\u684c\u5199\u5c0f\u7eb8\u6761\u3002",
            "read_sofa" => "\u7231\u5f25\u65af\u5750\u5728\u5ba2\u5385\u91cc\u8bfb\u4e66\u3002",
            "drink_tea" => "\u7231\u5f25\u65af\u5728\u8336\u51e0\u65c1\u6162\u6162\u559d\u8336\u3002",
            "play_game" => "\u7231\u5f25\u65af\u5750\u5230\u7535\u7ade\u533a\u73a9\u4fc4\u7f57\u65af\u65b9\u5757\u3002",
            "cook_kitchen" => "\u7231\u5f25\u65af\u7ad9\u5728\u53a8\u623f\u7076\u53f0\u65c1\u714e\u86cb\u3002",
            _ => "\u7231\u5f25\u65af\u80cc\u5bf9\u4e66\u684c\u5199\u5c0f\u7eb8\u6761\u3002"
        };
    }

    private static string ToActivityTitle(string activity)
    {
        var text = activity.Trim();
        if (text.Length <= 18)
        {
            return text;
        }

        foreach (var marker in new[] { "\u3002", "\uff0c", "\u3001" })
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index is > 0 and <= 18)
            {
                return text[..index];
            }
        }

        return text[..18];
    }

    private static string InferMood(string activity)
    {
        if (ContainsAny(activity, "\u7761", "\u5c0f\u7761", "\u5e8a"))
        {
            return "\u653e\u677e";
        }

        if (ContainsAny(activity, "\u6e38\u620f", "\u4fc4\u7f57\u65af\u65b9\u5757", "\u7535\u7ade"))
        {
            return "\u4e13\u6ce8";
        }

        if (ContainsAny(activity, "\u8336", "\u6c34", "\u8bfb\u4e66", "\u7ffb\u4e66"))
        {
            return "\u5b89\u9759";
        }

        if (ContainsAny(activity, "\u714e\u86cb", "\u505a\u996d", "\u53a8\u623f"))
        {
            return "\u8ba4\u771f";
        }

        return "\u5e73\u7a33";
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
