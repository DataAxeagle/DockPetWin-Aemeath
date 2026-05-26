using System.Text.Json.Serialization;

namespace DockPetWin.Core.CodexBridge;

public sealed class CodexBridgeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Codex";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "DockPetWin";
}
