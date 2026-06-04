using System.IO;
using System.Reflection;
using System.Text.Json;

namespace DockPetWin.Core.Outing;

public sealed class OutingCatalogLoader
{
    private const string ResourceRoot = "DockPetWin.Resources.Outing";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutingCatalog LoadCatalog()
    {
        var assembly = typeof(OutingCatalogLoader).Assembly;
        var collectables = LoadJson<List<OutingCollectable>>(assembly, "collectables.json") ?? [];
        var events = LoadJson<List<OutingEvent>>(assembly, "events.json") ?? [];

        collectables = collectables
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.ChineseName)
                && !string.IsNullOrWhiteSpace(item.ImagePath)
                && item.Rarity is >= 1 and <= 5
                && ResourceExists(assembly, item.ImagePath))
            .ToList();

        events = events
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.ChineseDescription))
            .ToList();

        return new OutingCatalog(collectables, events, item => OpenResourceStream(assembly, item.ImagePath));
    }

    private static T? LoadJson<T>(Assembly assembly, string relativePath)
    {
        try
        {
            using var stream = OpenResourceStream(assembly, relativePath);
            return stream is null
                ? default
                : JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static bool ResourceExists(Assembly assembly, string relativePath)
    {
        using var stream = OpenResourceStream(assembly, relativePath);
        return stream is not null;
    }

    private static Stream? OpenResourceStream(Assembly assembly, string relativePath)
    {
        var resourceName = ResourceName(relativePath);
        return assembly.GetManifestResourceStream(resourceName);
    }

    private static string ResourceName(string relativePath)
    {
        return $"{ResourceRoot}.{relativePath.Replace('\\', '.').Replace('/', '.')}";
    }
}
