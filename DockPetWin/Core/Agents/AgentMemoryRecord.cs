using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

/// <summary>
/// A compact, traceable fact distilled from local conversation history.
/// The original chat remains the source of truth; this record only controls recall.
/// </summary>
public sealed class AgentMemoryRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "shared_episode";

    [JsonPropertyName("importance")]
    public int Importance { get; set; } = 3;

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; } = 3;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("last_mentioned_at")]
    public DateTime LastMentionedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("decay_days")]
    public int? DecayDays { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "conversation.jsonl";
}
