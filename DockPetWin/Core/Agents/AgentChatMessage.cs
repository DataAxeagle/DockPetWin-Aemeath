using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;
}
