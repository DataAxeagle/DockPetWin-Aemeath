using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentToolTrace
{
    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("arguments_json")]
    public string ArgumentsJson { get; set; } = "{}";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("preview")]
    public string Preview { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }
}
