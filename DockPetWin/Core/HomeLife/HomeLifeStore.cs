using System.IO;
using System.Text;
using System.Text.Json;

namespace DockPetWin.Core.HomeLife;

public sealed class HomeLifeStore
{
    private const int HomeLifeSummaryEntryStep = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string rootDirectory;
    private readonly string logPath;
    private readonly string calendarDirectory;
    private readonly string summaryDirectory;
    private readonly string scheduleStatePath;

    public HomeLifeStore()
    {
        rootDirectory = Path.Combine(AppContext.BaseDirectory, "UserData", "Agents", "home-life");
        logPath = Path.Combine(rootDirectory, "activity-log.jsonl");
        calendarDirectory = Path.Combine(rootDirectory, "calendar");
        summaryDirectory = Path.Combine(rootDirectory, "summaries");
        scheduleStatePath = Path.Combine(rootDirectory, "schedule-state.json");
        EnsureDirectories();
        EnsureDailySummaries();
    }

    public string RootDirectory => rootDirectory;
    public string LogPath => logPath;
    public string CalendarDirectory => calendarDirectory;
    public string SummaryDirectory => summaryDirectory;
    public string ScheduleStatePath => scheduleStatePath;

    public void Append(HomeLifeEntry entry)
    {
        EnsureDirectories();
        entry.DurationSeconds = Math.Max(0, (entry.EndedAt - entry.StartedAt).TotalSeconds);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(logPath, json + Environment.NewLine, Encoding.UTF8);
        AppendMarkdown(entry);
        UpdateDailySummaryForDate(entry.StartedAt.Date, finalizePastDate: false);
    }

    public IReadOnlyList<HomeLifeEntry> LoadRecent(int count)
    {
        EnsureDirectories();
        if (!File.Exists(logPath))
        {
            return [];
        }

        var rows = new List<HomeLifeEntry>();
        foreach (var line in File.ReadLines(logPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<HomeLifeEntry>(line, JsonOptions);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.Activity))
                {
                    rows.Add(entry);
                }
            }
            catch
            {
                // Keep the home window resilient if a local log line is edited by hand.
            }
        }

        return rows
            .OrderBy(entry => entry.StartedAt)
            .TakeLast(Math.Max(0, count))
            .Reverse()
            .ToList();
    }

    public string BuildRecentSummary(int count)
    {
        var recent = LoadRecent(count);
        if (recent.Count == 0)
        {
            return "最近还没有小屋行事记录。";
        }

        return string.Join(
            Environment.NewLine,
            recent.Select(entry =>
                $"- {entry.StartedAt:MM-dd HH:mm} {entry.Activity}，持续 {FormatDuration(entry.DurationSeconds)}，心情：{entry.Mood}"));
    }

    public HomeScheduleState? LoadScheduleState()
    {
        EnsureDirectories();
        if (!File.Exists(scheduleStatePath))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<HomeScheduleState>(File.ReadAllText(scheduleStatePath), JsonOptions);
            return state is { Schedule.Count: > 0 } ? state : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveScheduleState(HomeScheduleState state)
    {
        EnsureDirectories();
        File.WriteAllText(scheduleStatePath, JsonSerializer.Serialize(state, JsonOptions), Encoding.UTF8);
    }

    public void ClearScheduleState()
    {
        try
        {
            if (File.Exists(scheduleStatePath))
            {
                File.Delete(scheduleStatePath);
            }
        }
        catch
        {
            // Schedule state is a convenience cache; failed cleanup should not break the home window.
        }
    }

    private void AppendMarkdown(HomeLifeEntry entry)
    {
        var date = entry.StartedAt.ToString("yyyy-MM-dd");
        var path = Path.Combine(calendarDirectory, $"{date}.md");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"# {date} 小屋行事历{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
        }

        var interrupted = entry.InterruptedByUser ? "是" : "否";
        var block = $"""
        ## {entry.StartedAt:HH:mm:ss} - {entry.EndedAt:HH:mm:ss}

        - 活动：{entry.Activity}
        - 具体内容：{entry.Details}
        - 持续时间：{FormatDuration(entry.DurationSeconds)}
        - 心情：{entry.Mood}
        - 触发：{entry.Trigger}
        - 被用户打断：{interrupted}

        """;
        File.AppendAllText(path, block + Environment.NewLine, Encoding.UTF8);
    }

    private void EnsureDailySummaries()
    {
        var files = Directory.EnumerateFiles(calendarDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            if (!DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var date))
            {
                continue;
            }

            UpdateDailySummaryFile(file, date.Date < DateTime.Now.Date);
        }
    }

    private void UpdateDailySummaryForDate(DateTime date, bool finalizePastDate)
    {
        var path = Path.Combine(calendarDirectory, $"{date:yyyy-MM-dd}.md");
        if (File.Exists(path))
        {
            UpdateDailySummaryFile(path, finalizePastDate);
        }
    }

    private void UpdateDailySummaryFile(string calendarPath, bool finalizePastDate)
    {
        var stats = AnalyzeCalendarMarkdown(calendarPath);
        if (stats.EntryCount == 0)
        {
            return;
        }

        Directory.CreateDirectory(summaryDirectory);
        var dateText = Path.GetFileNameWithoutExtension(calendarPath);
        var summaryPath = Path.Combine(summaryDirectory, $"{dateText}.md");
        var previousEntryCount = ReadGeneratedCount(summaryPath, "生成时活动条数");
        var shouldWrite = !File.Exists(summaryPath)
            || (finalizePastDate && previousEntryCount != stats.EntryCount)
            || stats.EntryCount >= previousEntryCount + HomeLifeSummaryEntryStep;
        if (!shouldWrite)
        {
            return;
        }

        File.WriteAllText(summaryPath, $"""
        # {dateText} 小屋活动摘要

        ## 来源

        - 明细：`home-life/calendar/{dateText}.md`
        - 原始事件流：`home-life/activity-log.jsonl`
        - 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}
        - 生成时活动条数：{stats.EntryCount}

        ## 概览

        - 原始活动记录：{stats.EntryCount} 条。
        - 时间范围：{stats.TimeRange}
        - 这是自动轻量摘要，用于快速接续小屋生活；需要完整细节时仍以明细为准。

        ## 高频活动

        {stats.TopActivities}

        ## 触发来源

        {stats.TopTriggers}
        """, Encoding.UTF8);
    }

    private static HomeLifeDailyStats AnalyzeCalendarMarkdown(string path)
    {
        var entryCount = 0;
        var firstTime = "";
        var lastTime = "";
        var activities = new List<string>();
        var triggers = new List<string>();

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                entryCount++;
                var time = line.TrimStart('#', ' ').Trim();
                if (string.IsNullOrWhiteSpace(firstTime))
                {
                    firstTime = time;
                }

                lastTime = time;
                continue;
            }

            if (line.StartsWith("- 活动：", StringComparison.Ordinal))
            {
                activities.Add(line["- 活动：".Length..].Trim());
                continue;
            }

            if (line.StartsWith("- 触发：", StringComparison.Ordinal))
            {
                triggers.Add(line["- 触发：".Length..].Trim());
            }
        }

        return new HomeLifeDailyStats(
            entryCount,
            string.IsNullOrWhiteSpace(firstTime) ? "暂无" : $"{firstTime} 到 {lastTime}",
            BuildTopList(activities, 8),
            BuildTopList(triggers, 6));
    }

    private static string BuildTopList(IEnumerable<string> items, int maxItems)
    {
        var rows = items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .GroupBy(item => item)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(group => $"- {group.Key}：{group.Count()} 次")
            .ToList();

        return rows.Count == 0 ? "- 暂无记录。" : string.Join(Environment.NewLine, rows);
    }

    private static int ReadGeneratedCount(string path, string marker)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var value = line[(index + marker.Length)..].TrimStart('：', ':', ' ');
            var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var count))
            {
                return count;
            }
        }

        return 0;
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(rootDirectory);
        Directory.CreateDirectory(calendarDirectory);
        Directory.CreateDirectory(summaryDirectory);
        if (!File.Exists(logPath))
        {
            File.WriteAllText(logPath, "", Encoding.UTF8);
        }
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

    private sealed record HomeLifeDailyStats(
        int EntryCount,
        string TimeRange,
        string TopActivities,
        string TopTriggers);
}
