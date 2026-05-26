using DockPetWin.Core.Settings;

namespace DockPetWin.Core.Reminder;

public sealed class ReminderScheduler
{
    private readonly Dictionary<string, DateTime> dueTimes = new(StringComparer.OrdinalIgnoreCase);
    private ReminderSettings? pendingReminder;

    public ReminderScheduler(AppSettings settings)
    {
        Reset(settings);
    }

    public void Reset(AppSettings settings)
    {
        settings.Normalize();
        dueTimes.Clear();
        foreach (var reminder in settings.Reminders.Where(item => item.Enabled))
        {
            dueTimes[reminder.Id] = NextDue(reminder, DateTime.Now);
        }

        pendingReminder = null;
    }

    public void Reconcile(AppSettings settings)
    {
        settings.Normalize();
        var activeIds = settings.Reminders
            .Where(item => item.Enabled)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in dueTimes.Keys.ToList())
        {
            if (!activeIds.Contains(id))
            {
                dueTimes.Remove(id);
            }
        }

        foreach (var reminder in settings.Reminders.Where(item => item.Enabled))
        {
            if (!dueTimes.ContainsKey(reminder.Id))
            {
                dueTimes[reminder.Id] = NextDue(reminder, DateTime.Now);
            }
        }

        if (pendingReminder is not null && !activeIds.Contains(pendingReminder.Id))
        {
            pendingReminder = null;
        }
    }

    public ReminderSettings? DueReminder(AppSettings settings, bool whenCatInLongDurationState)
    {
        if (!settings.RemindersEnabled)
        {
            return null;
        }

        Reconcile(settings);
        if (pendingReminder is not null)
        {
            return whenCatInLongDurationState || IsTask(pendingReminder) ? pendingReminder : null;
        }

        var now = DateTime.Now;
        var due = settings.Reminders
            .Where(item => item.Enabled)
            .Where(item => dueTimes.TryGetValue(item.Id, out var dueAt) && now >= dueAt)
            .OrderBy(item => dueTimes[item.Id])
            .FirstOrDefault();
        pendingReminder = due?.Clone();
        return pendingReminder is null || whenCatInLongDurationState || IsTask(pendingReminder)
            ? pendingReminder
            : null;
    }

    public void Complete(ReminderSettings reminder)
    {
        pendingReminder = null;
        dueTimes[reminder.Id] = NextDue(reminder, DateTime.Now);
    }

    public void Snooze(ReminderSettings reminder, TimeSpan delay)
    {
        pendingReminder = null;
        dueTimes[reminder.Id] = DateTime.Now.Add(delay);
    }

    public void Clear()
    {
        pendingReminder = null;
    }

    private static bool IsTask(ReminderSettings reminder)
    {
        return string.Equals(reminder.ActionType, "agent_task", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime NextDue(ReminderSettings reminder, DateTime now)
    {
        return reminder.ScheduleType switch
        {
            "daily" => NextDaily(reminder, now),
            "weekly" => NextWeekly(reminder, now),
            "monthly" => NextMonthly(reminder, now),
            _ => now.AddSeconds(reminder.IntervalSeconds)
        };
    }

    private static DateTime NextDaily(ReminderSettings reminder, DateTime now)
    {
        var time = ParseTime(reminder.TimeOfDay);
        var candidate = now.Date.Add(time);
        return candidate > now ? candidate : candidate.AddDays(1);
    }

    private static DateTime NextWeekly(ReminderSettings reminder, DateTime now)
    {
        var days = ParseDaysOfWeek(reminder.DaysOfWeek);
        var time = ParseTime(reminder.TimeOfDay);
        for (var offset = 0; offset <= 7; offset++)
        {
            var day = now.Date.AddDays(offset);
            if (!days.Contains(day.DayOfWeek))
            {
                continue;
            }

            var candidate = day.Add(time);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return now.AddDays(7);
    }

    private static DateTime NextMonthly(ReminderSettings reminder, DateTime now)
    {
        var time = ParseTime(reminder.TimeOfDay);
        for (var monthOffset = 0; monthOffset <= 13; monthOffset++)
        {
            var month = new DateTime(now.Year, now.Month, 1).AddMonths(monthOffset);
            var day = Math.Min(reminder.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
            var candidate = new DateTime(month.Year, month.Month, day).Add(time);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return now.AddMonths(1);
    }

    private static TimeSpan ParseTime(string value)
    {
        return TimeSpan.TryParse(value, out var parsed) ? parsed : TimeSpan.FromHours(9);
    }

    private static HashSet<DayOfWeek> ParseDaysOfWeek(string value)
    {
        var result = new HashSet<DayOfWeek>();
        foreach (var token in (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "mon" or "monday" or "1" or "周一" or "星期一":
                    result.Add(DayOfWeek.Monday);
                    break;
                case "tue" or "tuesday" or "2" or "周二" or "星期二":
                    result.Add(DayOfWeek.Tuesday);
                    break;
                case "wed" or "wednesday" or "3" or "周三" or "星期三":
                    result.Add(DayOfWeek.Wednesday);
                    break;
                case "thu" or "thursday" or "4" or "周四" or "星期四":
                    result.Add(DayOfWeek.Thursday);
                    break;
                case "fri" or "friday" or "5" or "周五" or "星期五":
                    result.Add(DayOfWeek.Friday);
                    break;
                case "sat" or "saturday" or "6" or "周六" or "星期六":
                    result.Add(DayOfWeek.Saturday);
                    break;
                case "sun" or "sunday" or "0" or "7" or "周日" or "周天" or "星期日" or "星期天":
                    result.Add(DayOfWeek.Sunday);
                    break;
            }
        }

        if (result.Count == 0)
        {
            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            {
                result.Add(day);
            }
        }

        return result;
    }
}
