using System.IO;
using System.Text.Json;

namespace DockPetWin.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string settingsPath;

    public SettingsStore()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "UserData");
        settingsPath = Path.Combine(root, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return AppSettings.Defaults;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.Defaults;
            settings.Normalize();
            return settings;
        }
        catch
        {
            return AppSettings.Defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }
}
