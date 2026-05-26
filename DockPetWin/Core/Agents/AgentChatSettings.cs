using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentChatSettings
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "deepseek";

    [JsonPropertyName("pet_name")]
    public string PetName { get; set; } = "";

    [JsonPropertyName("pet_identifier")]
    public string PetIdentifier { get; set; } = "";

    [JsonPropertyName("user_salutation")]
    public string UserSalutation { get; set; } = "";

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-v4-flash";

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("api_key_env")]
    public string ApiKeyEnv { get; set; } = "DEEPSEEK_API_KEY";

    [JsonPropertyName("search_provider")]
    public string SearchProvider { get; set; } = "tavily";

    [JsonPropertyName("tavily_api_key")]
    public string TavilyApiKey { get; set; } = "";

    [JsonPropertyName("tavily_api_key_env")]
    public string TavilyApiKeyEnv { get; set; } = "TAVILY_API_KEY";

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("max_history_messages")]
    public int MaxHistoryMessages { get; set; } = 30;

    [JsonPropertyName("enable_tools")]
    public bool EnableTools { get; set; } = true;

    [JsonPropertyName("max_tool_rounds")]
    public int MaxToolRounds { get; set; } = 8;

    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        return string.IsNullOrWhiteSpace(ApiKeyEnv)
            ? ""
            : Environment.GetEnvironmentVariable(ApiKeyEnv.Trim())?.Trim() ?? "";
    }

    public string ResolveTavilyApiKey()
    {
        if (!string.IsNullOrWhiteSpace(TavilyApiKey))
        {
            return TavilyApiKey.Trim();
        }

        return string.IsNullOrWhiteSpace(TavilyApiKeyEnv)
            ? ""
            : Environment.GetEnvironmentVariable(TavilyApiKeyEnv.Trim())?.Trim() ?? "";
    }
}
