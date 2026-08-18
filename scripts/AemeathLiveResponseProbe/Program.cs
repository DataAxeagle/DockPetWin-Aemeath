using System.Text.Json;
using DockPetWin.Core.Agents;

var prompts = args.Length > 0
    ? args
    : [
        "你最近喜欢吃什么？",
        "我之前说过喜欢什么水果吗？如果没有记录，请直接告诉我没有。",
        "我今天中午吃了面。"
    ];

var store = new AgentStore(AgentConversationMode.Immersive);
var settings = store.LoadSettings();
if (string.IsNullOrWhiteSpace(settings.ResolveApiKey()))
{
    throw new InvalidOperationException("当前桌宠没有可用的模型 API Key，无法执行真实回答测试。");
}

var client = new AgentChatClient();
var allowedTools = new[]
{
    "tool_specs", "search_knowledge", "knowledge_search", "read_knowledge", "knowledge_read",
    "list_files", "find_files", "find_file", "search_files", "file_search", "read_file", "write_file", "handle_read"
};

foreach (var prompt in prompts)
{
    var systemPrompt = store.BuildSystemPrompt(settings);
    var relevantMemories = store.BuildRelevantMemoryContext(prompt);
    if (!string.IsNullOrWhiteSpace(relevantMemories))
    {
        systemPrompt += $"{Environment.NewLine}{Environment.NewLine}# 与本轮问题相关的过往记忆（仅在确实相关时自然使用）{Environment.NewLine}{Environment.NewLine}{relevantMemories.Trim()}";
    }

    var reply = await client.SendWithToolsAsync(
        settings,
        systemPrompt,
        Array.Empty<AgentChatMessage>(),
        prompt,
        store,
        CancellationToken.None,
        null,
        allowedTools);
    reply = AgentStore.ApplyImmersiveGroundingGuard(prompt, reply);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        prompt,
        retrieved_memory = !string.IsNullOrWhiteSpace(relevantMemories),
        reply
    }));
}
