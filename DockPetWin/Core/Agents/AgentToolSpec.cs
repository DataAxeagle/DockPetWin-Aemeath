using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentToolSpec
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; set; } = [];

    [JsonPropertyName("returns_handle")]
    public bool ReturnsHandle { get; set; }

    [JsonPropertyName("write_access")]
    public bool WriteAccess { get; set; }
}
