using System.IO;
using System.Text.Json.Serialization;

namespace DockPetWin.Core.Outing;

public sealed class OutingEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("chinese_description")]
    public string ChineseDescription { get; set; } = "";

    [JsonPropertyName("english_description")]
    public string EnglishDescription { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
}

public sealed class OutingCollectable
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("chinese_name")]
    public string ChineseName { get; set; } = "";

    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = "";

    [JsonPropertyName("rarity")]
    public int Rarity { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("image_path")]
    public string ImagePath { get; set; } = "";
}

public sealed class OutingCatalog
{
    public OutingCatalog(
        IReadOnlyList<OutingCollectable> collectables,
        IReadOnlyList<OutingEvent> events,
        Func<OutingCollectable, Stream?> openImageStream)
    {
        Collectables = collectables;
        Events = events;
        this.openImageStream = openImageStream;
    }

    private readonly Func<OutingCollectable, Stream?> openImageStream;

    public IReadOnlyList<OutingCollectable> Collectables { get; }
    public IReadOnlyList<OutingEvent> Events { get; }

    public Stream? OpenImageStreamFor(OutingCollectable collectable)
    {
        return openImageStream(collectable);
    }
}

public abstract record OutingReward
{
    public sealed record Event(OutingEvent Value) : OutingReward;
    public sealed record Collectable(OutingCollectable Value) : OutingReward;
}

public sealed class CollectableInventoryEntry
{
    public string CollectableID { get; set; } = "";
    public int Count { get; set; }
    public DateTime FirstAcquiredAt { get; set; }
    public DateTime LastAcquiredAt { get; set; }
}

public sealed class CollectableInventory
{
    public Dictionary<string, CollectableInventoryEntry> Entries { get; set; } = [];
    public string? RecentNewCollectableID { get; set; }

    public bool RecordCollectable(string collectableID, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        if (Entries.TryGetValue(collectableID, out var entry))
        {
            entry.Count++;
            entry.LastAcquiredAt = now;
            RecentNewCollectableID = null;
            return false;
        }

        Entries[collectableID] = new CollectableInventoryEntry
        {
            CollectableID = collectableID,
            Count = 1,
            FirstAcquiredAt = now,
            LastAcquiredAt = now
        };
        RecentNewCollectableID = collectableID;
        return true;
    }

    public void ClearRecentNewMarker()
    {
        RecentNewCollectableID = null;
    }
}
