using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentToolCall
{
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; set; } = [];
}
