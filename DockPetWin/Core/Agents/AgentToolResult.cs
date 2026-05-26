using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentToolResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    public static AgentToolResult Success(string tool, string summary, string content, string? handle = null)
    {
        return new AgentToolResult
        {
            Ok = true,
            Tool = tool,
            Summary = summary,
            Content = content,
            Handle = handle
        };
    }

    public static AgentToolResult Error(string tool, string code, string message, string summary = "工具执行失败。")
    {
        return new AgentToolResult
        {
            Ok = false,
            Tool = tool,
            Summary = summary,
            ErrorCode = code,
            ErrorMessage = message,
            Content = message
        };
    }
}
