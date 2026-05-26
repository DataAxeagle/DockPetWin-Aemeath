using System.IO;
using System.Text.Json;

namespace DockPetWin.Core.Statistics;

public sealed class UsageStatisticsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string statisticsPath;

    public UsageStatisticsStore()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "UserData");
        statisticsPath = Path.Combine(root, "usage-statistics.json");
    }

    public UsageStatistics Load()
    {
        try
        {
            if (!File.Exists(statisticsPath))
            {
                return new UsageStatistics();
            }

            return JsonSerializer.Deserialize<UsageStatistics>(File.ReadAllText(statisticsPath), JsonOptions)
                ?? new UsageStatistics();
        }
        catch
        {
            return new UsageStatistics();
        }
    }

    public void Save(UsageStatistics statistics)
    {
        statistics.LastUpdatedAt = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(statisticsPath)!);
        File.WriteAllText(statisticsPath, JsonSerializer.Serialize(statistics, JsonOptions));
    }
}
