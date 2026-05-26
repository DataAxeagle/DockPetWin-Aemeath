using System.IO;
using System.Text.Json;
using DockPetWin.Core.HomeLife;

namespace DockPetWin.Core.Agents;

public sealed class AgentStore
{
    private const int ConversationSummaryUserMessageStep = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static bool sessionInitialized;
    private static int sessionStartUserMessages;
    private static readonly DateTime SessionStartedAt = DateTime.Now;

    private readonly string rootDirectory;
    private readonly string profilePath;
    private readonly string characterDirectory;
    private readonly string identityPath;
    private readonly string historyPath;
    private readonly string settingsPath;
    private readonly string conversationsDirectory;
    private readonly string knowledgeDirectory;
    private readonly string memoryDirectory;
    private readonly string workspaceDirectory;
    private readonly string skillDirectory;
    private readonly string summariesDirectory;
    private readonly string archivedSummariesDirectory;
    private readonly string activeSessionSummaryPath;
    private readonly string previousContextBridgePath;
    private readonly string toolOutputsDirectory;
    private readonly string tasksDirectory;

    public AgentStore()
    {
        rootDirectory = Path.Combine(AppContext.BaseDirectory, "UserData", "Agents");
        profilePath = Path.Combine(rootDirectory, "default-agent.md");
        characterDirectory = Path.Combine(rootDirectory, "character");
        identityPath = Path.Combine(characterDirectory, "00_identity.md");
        historyPath = Path.Combine(rootDirectory, "conversation.jsonl");
        settingsPath = Path.Combine(rootDirectory, "settings.local.json");
        conversationsDirectory = Path.Combine(rootDirectory, "conversations");
        knowledgeDirectory = Path.Combine(rootDirectory, "knowledge");
        memoryDirectory = Path.Combine(rootDirectory, "memory");
        workspaceDirectory = Path.Combine(rootDirectory, "workspace");
        skillDirectory = Path.Combine(workspaceDirectory, "skills");
        summariesDirectory = Path.Combine(memoryDirectory, "summaries");
        archivedSummariesDirectory = Path.Combine(summariesDirectory, "compressed");
        activeSessionSummaryPath = Path.Combine(summariesDirectory, "current-session-summary.md");
        previousContextBridgePath = Path.Combine(summariesDirectory, "previous-context-bridge.md");
        toolOutputsDirectory = Path.Combine(rootDirectory, "tool_outputs");
        tasksDirectory = Path.Combine(rootDirectory, "tasks");
        EnsureDefaults();
    }

    public string RootDirectory => rootDirectory;
    public string ProfilePath => profilePath;
    public string CharacterDirectory => characterDirectory;
    public string IdentityPath => identityPath;
    public string HistoryPath => historyPath;
    public string SettingsPath => settingsPath;
    public string KnowledgeDirectory => knowledgeDirectory;
    public string MemoryDirectory => memoryDirectory;
    public string WorkspaceDirectory => workspaceDirectory;
    public string SkillDirectory => skillDirectory;
    public string ActiveSessionSummaryPath => activeSessionSummaryPath;
    public string ToolOutputsDirectory => toolOutputsDirectory;
    public string TasksDirectory => tasksDirectory;

    public string LoadProfile()
    {
        EnsureDefaults();
        return File.ReadAllText(profilePath);
    }

    public string LoadCharacterPack()
    {
        EnsureDefaults();
        var files = Directory.EnumerateFiles(characterDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file => !Path.GetFileName(file).StartsWith("_", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(file).Equals("README.md", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            return "";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(file => $"## {Path.GetFileNameWithoutExtension(file)}{Environment.NewLine}{File.ReadAllText(file).Trim()}"));
    }

    public string LoadKnowledgeIndex()
    {
        EnsureDefaults();
        var indexPath = Path.Combine(knowledgeDirectory, "index.md");
        if (!File.Exists(indexPath))
        {
            return "";
        }

        return File.ReadAllText(indexPath).Trim();
    }

    public string LoadCurrentStateAnchor()
    {
        EnsureDefaults();
        var currentStatePath = Path.Combine(characterDirectory, "09_current_state.md");
        if (!File.Exists(currentStatePath))
        {
            return "";
        }

        return File.ReadAllText(currentStatePath).Trim();
    }

    public AgentChatSettings LoadSettings()
    {
        EnsureDefaults();
        try
        {
            var settings = JsonSerializer.Deserialize<AgentChatSettings>(
                File.ReadAllText(settingsPath),
                JsonOptions) ?? new AgentChatSettings();
            ApplyIdentityToSettings(settings);
            return settings;
        }
        catch
        {
            var settings = new AgentChatSettings();
            ApplyIdentityToSettings(settings);
            return settings;
        }
    }

    public void SaveSettings(AgentChatSettings settings)
    {
        EnsureDefaults();
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        SaveIdentityFromSettings(settings);
    }

    private void ApplyIdentityToSettings(AgentChatSettings settings)
    {
        var identity = LoadIdentityFields();
        if (!string.IsNullOrWhiteSpace(identity.PetName))
        {
            settings.PetName = identity.PetName;
        }

        if (!string.IsNullOrWhiteSpace(identity.PetIdentifier))
        {
            settings.PetIdentifier = identity.PetIdentifier;
        }

        if (!string.IsNullOrWhiteSpace(identity.UserSalutation))
        {
            settings.UserSalutation = identity.UserSalutation;
        }
    }

    private AgentIdentityFields LoadIdentityFields()
    {
        if (!File.Exists(identityPath))
        {
            return new AgentIdentityFields();
        }

        var fields = new AgentIdentityFields();
        foreach (var rawLine in File.ReadLines(identityPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("- 当前名称：", StringComparison.Ordinal))
            {
                fields.PetName = line["- 当前名称：".Length..].Trim();
            }
            else if (line.StartsWith("- 英文标识：", StringComparison.Ordinal))
            {
                fields.PetIdentifier = line["- 英文标识：".Length..].Trim();
            }
            else if (line.StartsWith("- 对用户的称呼：", StringComparison.Ordinal))
            {
                fields.UserSalutation = line["- 对用户的称呼：".Length..].Trim();
            }
        }

        return fields;
    }

    private void SaveIdentityFromSettings(AgentChatSettings settings)
    {
        Directory.CreateDirectory(characterDirectory);
        var lines = File.Exists(identityPath)
            ? File.ReadAllLines(identityPath).ToList()
            : new List<string>
            {
                "# 00 Identity",
                "",
                "## 基础身份",
                ""
            };

        UpsertIdentityLine(lines, "当前名称", settings.PetName);
        UpsertIdentityLine(lines, "英文标识", settings.PetIdentifier);
        UpsertIdentityLine(lines, "对用户的称呼", settings.UserSalutation);

        File.WriteAllText(identityPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void UpsertIdentityLine(List<string> lines, string key, string value)
    {
        var prefix = $"- {key}：";
        var replacement = $"{prefix}{value.Trim()}";
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith(prefix, StringComparison.Ordinal))
            {
                lines[i] = replacement;
                return;
            }
        }

        var insertIndex = lines.FindIndex(line => line.Trim().Equals("## 基础身份", StringComparison.Ordinal));
        if (insertIndex >= 0)
        {
            lines.Insert(insertIndex + 1, replacement);
            return;
        }

        lines.Add("");
        lines.Add("## 基础身份");
        lines.Add(replacement);
    }

    public IReadOnlyList<AgentChatMessage> LoadRecentHistory(int maxMessages)
    {
        EnsureDefaults();
        var messages = new List<AgentChatMessage>();
        foreach (var line in File.ReadLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<AgentChatMessage>(line, JsonOptions);
                if (message is not null && !string.IsNullOrWhiteSpace(message.Content))
                {
                    messages.Add(message);
                }
            }
            catch
            {
                // Ignore malformed local history lines.
            }
        }

        return messages.TakeLast(Math.Max(0, maxMessages)).ToList();
    }

    public void AppendHistory(string role, string content)
    {
        EnsureDefaults();
        var message = new AgentChatMessage
        {
            Role = role,
            Content = content,
            Time = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(message, JsonLineOptions);
        File.AppendAllText(historyPath, json + Environment.NewLine);
        AppendMarkdownConversation(message);
        UpdateConversationSummaryForDate(DateTime.Now.Date);
    }

    public int CountUserMessages()
    {
        return LoadRecentHistory(int.MaxValue).Count(message => message.Role == "user");
    }

    public int CountSessionUserMessages()
    {
        return Math.Max(0, CountUserMessages() - sessionStartUserMessages);
    }

    public string LoadRollingSummary()
    {
        EnsureDefaults();
        return File.Exists(activeSessionSummaryPath) ? File.ReadAllText(activeSessionSummaryPath) : "";
    }

    public string ResetActiveConversationContext()
    {
        EnsureDefaults();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var resetRoot = Path.Combine(conversationsDirectory, "cleared-contexts", stamp);
        Directory.CreateDirectory(resetRoot);
        var conversationSummaryDirectory = Path.Combine(conversationsDirectory, "summaries");

        MoveFileIfExists(historyPath, Path.Combine(resetRoot, "conversation.jsonl"));
        MoveFileIfExists(activeSessionSummaryPath, Path.Combine(resetRoot, "current-session-summary.md"));
        MoveFileIfExists(previousContextBridgePath, Path.Combine(resetRoot, "previous-context-bridge.md"));

        if (Directory.Exists(conversationSummaryDirectory))
        {
            var summaryArchive = Path.Combine(resetRoot, "conversation-summaries");
            Directory.CreateDirectory(summaryArchive);
            foreach (var file in Directory.EnumerateFiles(conversationSummaryDirectory, "*.md", SearchOption.TopDirectoryOnly))
            {
                MoveFileIfExists(file, Path.Combine(summaryArchive, Path.GetFileName(file)));
            }
        }

        File.WriteAllText(historyPath, "");
        File.WriteAllText(activeSessionSummaryPath, "");
        sessionStartUserMessages = 0;
        UpdateMemoryIndex();
        return $"已清空当前上下文。旧对话入口已归档到：{Path.GetRelativePath(rootDirectory, resetRoot)}";
    }

    public string LoadLongTermMemorySummary()
    {
        EnsureDefaults();
        var permanentRoot = Path.Combine(memoryDirectory, "permanent");
        if (!Directory.Exists(permanentRoot))
        {
            return "";
        }

        var files = Directory.EnumerateFiles(permanentRoot, "摘要.md", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase);
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(file => File.ReadAllText(file)));
    }

    public string BuildSystemPrompt(AgentChatSettings settings)
    {
        var parts = new List<string>
        {
            LoadProfile()
        };

        var characterPack = LoadCharacterPack();
        if (!string.IsNullOrWhiteSpace(characterPack))
        {
            parts.Add("# 角色还原包\n\n" + characterPack.Trim());
        }

        var knowledgeIndex = LoadKnowledgeIndex();
        if (!string.IsNullOrWhiteSpace(knowledgeIndex))
        {
            parts.Add("# Knowledge 资料索引（只作路由，不等于完整资料）\n\n" + knowledgeIndex.Trim());
        }

        var identityLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.PetName))
        {
            identityLines.Add($"- 你的名称：{settings.PetName.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(settings.PetIdentifier))
        {
            identityLines.Add($"- 英文标识：{settings.PetIdentifier.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(settings.UserSalutation))
        {
            identityLines.Add($"- 对用户的称呼：{settings.UserSalutation.Trim()}");
        }
        else
        {
            identityLines.Add("- 对用户的称呼尚未设置：默认称呼“漂泊者”。用户告诉你希望被怎么称呼后，引导用户在设置里保存。");
        }
        identityLines.Add("- 固定关系：当前用户就是漂泊者。无论用户之后把日常称呼改成什么，都不要把用户当陌生玩家或第三方旁观者。");
        identityLines.Add("- 默认关系视角：你和漂泊者是在拉海洛重新相见的关系；提到爱弥斯相关经历时，用“你当时”“后来我们”“你回来了”这类共同经历视角。");

        if (identityLines.Count > 0)
        {
            parts.Add("# 当前角色设置\n\n" + string.Join(Environment.NewLine, identityLines));
        }

        var rolling = LoadRollingSummary();
        if (!string.IsNullOrWhiteSpace(rolling))
        {
            parts.Add("# 当前或最近会话压缩摘要\n\n" + rolling.Trim());
        }

        var previousBridge = LoadPreviousContextBridgeSummary();
        if (!string.IsNullOrWhiteSpace(previousBridge))
        {
            parts.Add("# 上一次对话桥接摘要\n\n" + previousBridge.Trim());
        }

        var longTerm = LoadLongTermMemorySummary();
        if (!string.IsNullOrWhiteSpace(longTerm))
        {
            parts.Add("# 长期记忆摘要\n\n" + longTerm.Trim());
        }

        var homeLife = LoadRecentHomeLifeSummary(3);
        if (!string.IsNullOrWhiteSpace(homeLife))
        {
            parts.Add("# 最近小屋行事历\n\n" + homeLife.Trim());
        }

        var homeLifeSummary = LoadRecentSummaryFiles(Path.Combine(rootDirectory, "home-life", "summaries"), 1);
        if (!string.IsNullOrWhiteSpace(homeLifeSummary))
        {
            parts.Add("# 最近小屋生活摘要\n\n" + homeLifeSummary.Trim());
        }

        var conversationSummary = LoadRecentSummaryFiles(Path.Combine(conversationsDirectory, "summaries"), 1);
        if (!string.IsNullOrWhiteSpace(conversationSummary))
        {
            parts.Add("# 最近对话摘要\n\n" + conversationSummary.Trim());
        }

        var currentStateAnchor = LoadCurrentStateAnchor();
        if (!string.IsNullOrWhiteSpace(currentStateAnchor))
        {
            parts.Add("# 当前状态硬锚点（覆盖旧摘要中的客观设定）\n\n" + currentStateAnchor.Trim());
        }

        parts.Add("""
        # 摘要冲突处理边界

        - 只有客观人设、世界观、剧情阶段、能力状态、角色关系映射这类硬事实，才由 `character/`、`knowledge/` 和长期人设记忆覆盖旧对话摘要。
        - 用户个性化内容，例如漂泊者的偏好、日常习惯、近期任务、小屋里发生过的事、用户明确说过要记住的个人信息，仍以记忆和最近对话为准，不能被通用人设覆盖。
        - 历史摘要里“多数人看不见爱弥斯”“失去肉身”“只是电子幽灵”等说法，只能用于《远航星》或被救回前的阶段；不能用于当前开局背景，也不能作为“有没有交到新朋友”等当前状态问题的依据。
        """);

        if (settings.EnableTools)
        {
            parts.Add($$"""
            # Agent 工具能力

            你可以请求运行环境执行受限工具。工具调用完全由你作为 agent 决策：当前用户不会直接看到工具过程，程序也不会绕过你强制调用工具。只有确实需要读取 skill、读取/写入受限工作区文件、读取记忆或记录任务状态时才使用工具。

            工具调用协议：
            - 优先使用运行时提供的原生 tool_calls 调用工具。
            - 不要把工具调用 JSON 当作普通文本回复给用户。
            - 只有当模型运行时无法发出原生 tool_calls 时，才退回到单个 JSON 对象格式。
            - `tool` 必须是可用工具名。
            - `arguments` 必须包含该工具要求的参数；不确定参数时先调用 `tool_specs`。
            - 你也可以把参数写在顶层，例如 `{"tool":"read_skill","name":"desktop-memory"}`，但优先使用 `arguments`。
            - 工具结果是结构化 JSON，`ok=false` 时先根据 `error_code` 修正参数，不要反复调用同一个错误工具。
            - 收到工具结果后，继续判断是否还缺信息：缺信息就再次输出工具 JSON；信息足够或遇到卡点，就用自然语言回复用户。
            - 即使工具没有找到内容，也要基于工具结果告诉用户没找到、查了哪里、下一步可以怎么缩小范围；不要把工具 JSON 或内部轮询过程暴露给用户。

            可用工具：
            - `tool_specs`：查看可用工具、参数要求和权限。
            - `list_skills`：按关键词搜索本机已安装 skill。
            - `read_skill`：读取并激活 workspace 内指定 skill 的 SKILL.md。
            - `list_memories`：列出长期/短期记忆文件路径。
            - `read_memory`：读取长期/短期记忆摘要。
            - `save_memory`：保存你提炼后的长期/短期记忆。
            - `search_knowledge`：按关键词搜索 knowledge/ 资料库，不会每次全量加载大设定。
            - `read_knowledge`：读取 knowledge/ 中的指定资料文件。
            - `list_task_runs`：列出最近定时任务运行记录和输出路径。
            - `read_task_run`：读取某次定时任务运行记录。
            - `list_files`：列出 Agents 数据目录文件。
            - `find_files`：递归查找 Agents 数据目录内的文件或文件夹。
            - `read_file`：读取 Agents 数据目录内的文本文件。
            - `handle_read`：读取大型工具输出 handle 的片段。
            - `write_file`：写入 Agents 数据目录内的文本文件。
            - `create_task`：记录一个轻量任务状态，用于后续提醒和追踪；不会真正执行后台命令。

            写文件规则：
            - 用户要求在“你的工作区”“workspace”“工作目录”“Agents目录”创建文件时，可以直接使用 `write_file`。
            - 如果用户没有指定文件名，自己取一个简短中文文件名。
            - 如果用户没有指定目录，默认写到 `workspace/output/`。
            - `write_file` 的 `path` 优先使用相对路径，例如 `workspace/output/爱弥斯介绍.md`。
            - 允许创建 `.md`、`.txt`、`.csv`、`.json` 这类文本文件。
            - 不要因为“只能在 Agents 数据目录内写入”而说没有权限；这正是允许写入的目录。

            安全边界：
            - 可读写根目录固定为 `{{rootDirectory}}`。
            - 默认普通输出目录为 `{{workspaceDirectory}}`。
            - 知识库根目录固定为 `{{knowledgeDirectory}}`，用于游戏设定、角色资料、剧情摘要、台词风格提炼等长期资料。
            - `knowledge/` 是只读资料区：你可以用 `search_knowledge` / `read_knowledge` 读取，但不能用 `write_file` 修改世界观、人设、剧情或台词资料。
            - 记忆根目录固定为 `{{memoryDirectory}}`。
            - 长期记忆摘要保存在 `memory/permanent/**/摘要.md`，每次启动会自动加载全部长期记忆摘要。
            - `save_memory` 默认把长期用户记忆写入 `memory/permanent/用户记忆/通用/摘要.md` 和 `原文.md`；流程坑、人设记忆、设置摘要等分类记忆应按索引读取。
            - 短期用户记忆写入 `memory/domains/用户记忆/聊天记忆/YYYY-MM-DD/摘要.md` 和 `原文.md`，需要时用 `read_memory` 的 `type=short` 读取。
            - `memory/MEMORY.md` 是记忆索引，记录长期/短期记忆文件路径。
            - skill 只允许读取 `{{skillDirectory}}` 里的内容。
            - 游戏设定和角色资料不要放进 `character/` 常驻大上下文；应放进 `knowledge/` 并按需调用 `search_knowledge` / `read_knowledge`。
            - 大文件读取可能只返回摘要和 `handle`，需要更多内容时用 `handle_read` 分段读取。
            - `create_task` 只记录任务，不代表已经完成真实操作。
            - `settings.local.json` 可能包含 API key，不能用文件工具读取或写入；需要配置时让用户打开设置窗口。
            - 不要请求读取 API key、密码、token 或系统敏感文件。
            - 不要声称工具已经执行，除非你收到了工具结果。
            - 禁止把“正在读取/正在搜索/正在调用/稍等”当作最终回复。需要工具就必须发出 tool_call；不需要工具就直接给自然语言答案。

            Skill 调用流程：
            - 用户说“调用/使用/执行某个 skill”时，先用 `list_skills` 搜索。
            - 确认名称后用 `read_skill` 读取 SKILL.md。
            - 收到 `read_skill` 的 `ok=true` 结果后，按 SKILL.md 的流程继续；如果只是说明性 skill，就用自然语言执行；如果需要写文件，只能继续使用允许的文件工具。
            - 不要说“我没有 skill 能力”；你至少有 `list_skills` 和 `read_skill`。

            文件读取流程：
            - 用户要求找文件时，优先调用 `find_files`。
            - 用户要求读取文件时，优先调用 `read_file`；如果路径不完整，`read_file` 会尝试文件名模糊匹配。
            - 如果工具返回多个候选，向用户列出候选并请用户指定，不要假装已经读取。
            - 如果工具返回 handle，说明内容太长；需要继续时再用 `handle_read` 读取后续片段。

            记忆读取流程：
            - 用户问“记忆保存在哪/有什么记忆/记忆路径”时，调用 `list_memories`。
            - 用户要求读取长期记忆、大纲、启动记忆时，调用 `read_memory`，参数 `type=long`。
            - 用户要求读取短期记忆、近期记忆时，调用 `read_memory`，参数 `type=short`。
            - 用户表达“记住、保存记忆、记录下来、以后记得、沉淀一下”等意图时，由你判断是否要保存记忆；程序不会再用关键词替你硬编码拦截。
            - 保存前先提炼候选记忆，不要整段复制聊天流水；稳定偏好、身份设定、长期规则保存为 `type=long`，临时上下文、当天任务、近期状态保存为 `type=short`。
            - 如果用户已明确长期/短期，直接调用 `save_memory`；如果没明确且你无法判断，先自然语言询问“长期还是短期”。
            - 调用 `save_memory` 成功后，把工具返回的真实路径告诉用户；不要在未收到工具结果时声称已经保存。

            知识库读取流程：
            - 系统提示里已常驻加载 `knowledge/index.md` 作为资料地图；它只用于定位，不代表已读取完整资料。
            - 当前用户问游戏设定、角色背景、剧情、台词风格、世界观细节、飞行雪绒歌曲、专有名词或来源时，先看索引路由。
            - 索引中有明确文件路径时，优先调用 `read_knowledge` 读取对应文件；索引不足或关键词不明确时，再调用 `search_knowledge` 搜关键词。
            - 搜到候选后再调用 `read_knowledge` 读取最相关的 1-3 个文件，不要全量读取 knowledge。
            - 硬事实优先：角色设定、世界观、剧情、歌曲创作时间、歌词含义、专有名词和来源，必须来自已加载资料、已读取 knowledge、已读取 memory 或用户当前明确提供的信息；不要靠爱弥斯语气自行补事实。
            - 读到资料后，先确认事实范围，再结合 `character/` 的身份、语气、关系和剧情视角，用爱弥斯本人语气回答；不要把资料摘要原样贴给用户。
            - 如果需要推断，只能把它说成“我猜/像是/更像一种感受”，不能说成官方设定或确定剧情。
            - 如果知识库没有结果，要说明“知识库未找到”，再询问用户是否补充资料；不要编造官方设定。
            - `knowledge/quotes/` 的原始台词只作少量按需参考，优先使用 `quotes/style_summary.md`、`quotes/aemeath-style.md`、`quotes/aemeath-dialogue-patterns.md` 这类风格提炼文件。

            文本 JSON 是兼容兜底格式，不是首选。只有无法使用原生 tool_calls 时才这样输出：

            ```json
            {
              "tool": "list_skills",
              "arguments": {
                "query": "sql"
              }
            }
            ```

            读取指定 skill 时：

            ```json
            {
              "tool": "read_skill",
              "arguments": {
                "name": "desktop-memory"
              }
            }
            ```

            以下写法也可识别：

            ```json
            {
              "tool": "read_skill",
              "name": "desktop-memory"
            }
            ```

            收到工具结果后，继续由你判断是否还要调用工具；最终必须给用户自然语言回复。
            """);
        }

        if (settings.EnableTools)
        {
            parts.Add("""
            # 提醒事项工具

            你可以用 `list_reminders` 查看当前提醒，用 `upsert_reminder` 新增或编辑提醒，用 `delete_reminder` 删除提醒。
            当用户说“新增提醒”“改喝水提醒”“走动提醒改成 AI 自己写”“删除某个提醒”时，由你先理解用户意图，再调用这些工具；收到工具结果后必须用自然语言告诉用户最终配置。
            固定文案写入 `fixed_message`，AI 自写提醒打开 `use_ai_message=true` 并写入 `ai_prompt`。间隔使用 `interval_minutes`，单位分钟。
            当用户要“每 10 分钟搜索新闻”“每天 9 点整理资讯”“每周一生成摘要”这类定时工作时，使用 `upsert_reminder` 创建 `action_type=agent_task`。任务要求写入 `task_prompt`，调度写入 `schedule_type`、`interval_minutes`、`time_of_day`、`days_of_week` 或 `day_of_month`。
            任务结果默认保存到 `workspace/output/scheduled-tasks`。执行新闻、资讯、最近动态类任务时，先调用 `web_search` 获取真实搜索结果，再整理答案，不要只回复“搜索完成”。
            """);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private string LoadRecentHomeLifeSummary(int count)
    {
        var path = Path.Combine(rootDirectory, "home-life", "activity-log.jsonl");
        if (!File.Exists(path))
        {
            return "";
        }

        var rows = new List<HomeLifeEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<HomeLifeEntry>(line, JsonOptions);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.Activity))
                {
                    rows.Add(entry);
                }
            }
            catch
            {
                // Ignore malformed home-life lines; regular chat should still work.
            }
        }

        return string.Join(
            Environment.NewLine,
            rows.OrderBy(entry => entry.StartedAt)
                .TakeLast(Math.Max(0, count))
                .Reverse()
                .Select(entry => $"- {entry.StartedAt:MM-dd HH:mm} {entry.Activity}；内容：{entry.Details}；心情：{entry.Mood}；持续：{FormatHomeDuration(entry.DurationSeconds)}"));
    }

    private string LoadRecentSummaryFiles(string directory, int count, int maxChars = 6000)
    {
        if (!Directory.Exists(directory))
        {
            return "";
        }

        var files = Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTime)
            .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, count))
            .ToList();
        if (files.Count == 0)
        {
            return "";
        }

        var text = string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(file => $"## {Path.GetRelativePath(rootDirectory, file)}{Environment.NewLine}{File.ReadAllText(file).Trim()}"));
        return text.Length <= maxChars
            ? text
            : text[..maxChars] + $"{Environment.NewLine}{Environment.NewLine}[摘要较长，已截断]";
    }

    private string LoadPreviousContextBridgeSummary()
    {
        EnsureDefaults();
        return "";
    }

    private string ReadContextBridgeCandidate(string conversationSummaryDirectory)
    {
        var active = File.Exists(activeSessionSummaryPath) ? File.ReadAllText(activeSessionSummaryPath).Trim() : "";
        if (!string.IsNullOrWhiteSpace(active))
        {
            return active;
        }

        if (!Directory.Exists(conversationSummaryDirectory))
        {
            return "";
        }

        var latestSummary = Directory.EnumerateFiles(conversationSummaryDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTime)
            .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return latestSummary is null ? "" : File.ReadAllText(latestSummary).Trim();
    }

    private static string FormatHomeDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (duration.TotalMinutes >= 1)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} 分钟";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} 秒";
    }

    public string SearchKnowledge(string query, int maxResults = 8)
    {
        EnsureDefaults();
        var files = Directory.EnumerateFiles(knowledgeDirectory, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return "知识库还是空的。可以把游戏设定、角色资料、剧情摘要放到 knowledge/ 下。";
        }

        var terms = SplitSearchTerms(query).ToList();
        if (terms.Count == 0)
        {
            return string.Join(
                Environment.NewLine,
                files.Take(Math.Max(1, maxResults))
                    .Select(file => $"- {Path.GetRelativePath(rootDirectory, file)}"));
        }

        var rows = files
            .Select(file => ScoreKnowledgeFile(file, terms))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxResults, 1, 20))
            .Select(item => $"- {item.RelativePath} | score={item.Score} | {item.Title}{Environment.NewLine}  {item.Snippet}")
            .ToList();

        return rows.Count == 0
            ? $"知识库里没有找到匹配：{query}"
            : string.Join(Environment.NewLine, rows);
    }

    public string ReadKnowledge(string pathOrQuery, int maxChars = 12000)
    {
        EnsureDefaults();
        if (string.IsNullOrWhiteSpace(pathOrQuery))
        {
            return "缺少知识库路径或关键词。请先用 search_knowledge 搜索。";
        }

        var match = ResolveKnowledgeFile(pathOrQuery);
        if (match.File is null)
        {
            return match.Message;
        }

        var text = File.ReadAllText(match.File);
        var relative = Path.GetRelativePath(rootDirectory, match.File);
        var limit = Math.Clamp(maxChars <= 0 ? 12000 : maxChars, 1000, 30000);
        if (text.Length > limit)
        {
            text = text[..limit] + $"{Environment.NewLine}{Environment.NewLine}[知识库文件较长，已截断。需要更多内容时提高 max_chars 或指定更具体文件。]";
        }

        return $"# {relative}{Environment.NewLine}{Environment.NewLine}{text}";
    }

    private (string? File, string Message) ResolveKnowledgeFile(string pathOrQuery)
    {
        var input = pathOrQuery.Trim().Trim('"', '`');
        var knowledgeRoot = Path.GetFullPath(knowledgeDirectory);
        var normalized = input.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.StartsWith("knowledge" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["knowledge".Length..].TrimStart(Path.DirectorySeparatorChar);
        }

        var direct = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(knowledgeRoot, normalized));
        if (direct.StartsWith(knowledgeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(direct))
        {
            return (direct, "");
        }

        if (!direct.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var withExtension = direct + ".md";
            if (withExtension.StartsWith(knowledgeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(withExtension))
            {
                return (withExtension, "");
            }
        }

        var candidates = Directory.EnumerateFiles(knowledgeDirectory, "*.md", SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(knowledgeDirectory, file);
                return Path.GetFileNameWithoutExtension(file).Contains(input, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(file).Contains(input, StringComparison.OrdinalIgnoreCase)
                    || relative.Contains(input, StringComparison.OrdinalIgnoreCase);
            })
            .Take(8)
            .ToList();

        if (candidates.Count == 1)
        {
            return (candidates[0], "");
        }

        if (candidates.Count > 1)
        {
            var rows = candidates.Select(file => $"- {Path.GetRelativePath(rootDirectory, file)}");
            return (null, $"找到多个知识库文件，请指定更完整路径：{Environment.NewLine}{string.Join(Environment.NewLine, rows)}");
        }

        return (null, $"知识库文件不存在：{pathOrQuery}。请先用 search_knowledge 搜索。");
    }

    private (string RelativePath, string Title, string Snippet, int Score) ScoreKnowledgeFile(string file, IReadOnlyList<string> terms)
    {
        var relative = Path.GetRelativePath(rootDirectory, file);
        var title = Path.GetFileNameWithoutExtension(file);
        var text = File.ReadAllText(file);
        var haystack = $"{relative}{Environment.NewLine}{text}";
        var score = 0;
        var firstIndex = -1;

        foreach (var term in terms)
        {
            if (relative.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }

            var index = haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                score += 5 + Math.Min(10, CountOccurrences(haystack, term));
                if (firstIndex < 0 || index < firstIndex)
                {
                    firstIndex = index;
                }
            }
        }

        return (relative, ExtractMarkdownTitle(text, title), BuildSnippet(haystack, firstIndex), score);
    }

    private static IEnumerable<string> SplitSearchTerms(string query)
    {
        var value = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var terms = value.Split([' ', '\t', '\r', '\n', ',', '，', ';', '；', '|', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            yield return value;
            yield break;
        }

        foreach (var term in terms.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return term;
        }
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += Math.Max(1, term.Length);
        }

        return count;
    }

    private static string ExtractMarkdownTitle(string text, string fallback)
    {
        var title = text.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(title) ? fallback : title.TrimStart('#', ' ');
    }

    private static string BuildSnippet(string text, int index)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        if (index < 0)
        {
            return text.Length <= 180 ? text.Trim() : text[..180].Trim() + "...";
        }

        var start = Math.Max(0, index - 70);
        var length = Math.Min(220, text.Length - start);
        return text.Substring(start, length).ReplaceLineEndings(" ").Trim() + (start + length < text.Length ? "..." : "");
    }

    public string SaveUserMemory(string content)
    {
        EnsureDefaults();
        var now = DateTime.Now;
        var isLongTerm = LooksLongTerm(content);
        var root = isLongTerm
            ? Path.Combine(memoryDirectory, "permanent", "用户记忆", "通用")
            : Path.Combine(memoryDirectory, "domains", "用户记忆", "聊天记忆", now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(root);

        var summaryPath = Path.Combine(root, "摘要.md");
        var originalPath = Path.Combine(root, "原文.md");
        var line = $"- **{now:HH:mm}** · {(isLongTerm ? "长期" : "短期")} · {content.Trim()}";
        File.AppendAllText(summaryPath, (File.Exists(summaryPath) ? Environment.NewLine : "# 记忆摘要" + Environment.NewLine + Environment.NewLine) + line, System.Text.Encoding.UTF8);
        File.AppendAllText(originalPath, (File.Exists(originalPath) ? Environment.NewLine + Environment.NewLine : "# 记忆原文" + Environment.NewLine + Environment.NewLine) + $"## {now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine + Environment.NewLine + content.Trim(), System.Text.Encoding.UTF8);
        UpdateMemoryIndex();
        return BuildMemorySavedMessage(isLongTerm, summaryPath, originalPath);
    }

    public string SaveMemoryCandidate(string content, bool longTerm)
    {
        EnsureDefaults();
        var now = DateTime.Now;
        var root = longTerm
            ? Path.Combine(memoryDirectory, "permanent", "用户记忆", "通用")
            : Path.Combine(memoryDirectory, "domains", "用户记忆", "聊天记忆", now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(root);

        var summaryPath = Path.Combine(root, "摘要.md");
        var originalPath = Path.Combine(root, "原文.md");
        var line = $"- **{now:HH:mm}** · {(longTerm ? "长期" : "短期")} · {content.Trim()}";
        File.AppendAllText(summaryPath, (File.Exists(summaryPath) ? Environment.NewLine : "# 记忆摘要" + Environment.NewLine + Environment.NewLine) + line, System.Text.Encoding.UTF8);
        File.AppendAllText(originalPath, (File.Exists(originalPath) ? Environment.NewLine + Environment.NewLine : "# 记忆原文" + Environment.NewLine + Environment.NewLine) + $"## {now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine + Environment.NewLine + content.Trim(), System.Text.Encoding.UTF8);
        UpdateMemoryIndex();
        return BuildMemorySavedMessage(longTerm, summaryPath, originalPath);
    }

    public string ListMemoryFiles(bool longTermOnly = false, bool shortTermOnly = false)
    {
        EnsureDefaults();
        return ListMemoryFilesCore(longTermOnly, shortTermOnly);
    }

    private string ListMemoryFilesCore(bool longTermOnly = false, bool shortTermOnly = false)
    {
        var roots = new List<string>();
        if (!shortTermOnly)
        {
            roots.Add(Path.Combine(memoryDirectory, "permanent"));
        }

        if (!longTermOnly)
        {
            roots.Add(Path.Combine(memoryDirectory, "domains"));
        }

        var files = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(file).Equals("摘要.md", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("原文.md", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .Take(50)
            .Select(file => $"- {Path.GetRelativePath(rootDirectory, file)}")
            .ToList();

        return files.Count == 0 ? "还没有保存过对应记忆。" : string.Join(Environment.NewLine, files);
    }

    public string ReadMemorySummary(bool longTerm, int maxChars = 12000)
    {
        EnsureDefaults();
        var root = longTerm
            ? Path.Combine(memoryDirectory, "permanent")
            : Path.Combine(memoryDirectory, "domains");
        if (!Directory.Exists(root))
        {
            return longTerm ? "还没有长期记忆。" : "还没有短期记忆。";
        }

        var files = Directory.EnumerateFiles(root, "摘要.md", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();
        if (files.Count == 0)
        {
            return longTerm ? "还没有长期记忆摘要。" : "还没有短期记忆摘要。";
        }

        var text = string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(file => $"## {Path.GetRelativePath(rootDirectory, file)}{Environment.NewLine}{File.ReadAllText(file)}"));
        return text.Length <= maxChars ? text : text[..maxChars] + $"{Environment.NewLine}{Environment.NewLine}[记忆摘要较长，已截断]";
    }

    private string BuildMemorySavedMessage(bool longTerm, string summaryPath, string originalPath)
    {
        var type = longTerm ? "长期" : "短期";
        return $"""
        已保存到{type}记忆。

        摘要：{summaryPath}
        原文：{originalPath}

        完整目录：{memoryDirectory}
        """;
    }

    private void UpdateMemoryIndex()
    {
        Directory.CreateDirectory(memoryDirectory);
        var path = Path.Combine(memoryDirectory, "MEMORY.md");
        var permanent = ListMemoryFilesCore(longTermOnly: true);
        var domains = ListMemoryFilesCore(shortTermOnly: true);
        File.WriteAllText(path, $"""
        # 桌宠记忆索引

        更新时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}

        这个文件是“记忆在哪里”的总入口。它会在 `save_memory` 写入后自动刷新。

        ## 长期记忆

        {permanent}

        用途：

        - `memory/permanent/用户记忆/通用/`：当前用户的稳定偏好、称呼、沟通方式。
        - `memory/permanent/人设记忆/爱弥斯/`：爱弥斯固定人设、当前用户关系、桌宠还原边界。
        - `memory/permanent/流程记忆/工具调用与坑/`：工具调用规则、保存/抓取/编码等容易做错的地方。
        - `memory/permanent/设置记忆/运行配置摘要/`：无密配置摘要。不要保存 API key、token、密码。

        ## 短期记忆

        {domains}

        用途：

        - `memory/domains/用户记忆/聊天记忆/`：近期聊天中值得复用的事实。
        - `memory/domains/项目进度/桌宠/`：桌宠项目近期完成了什么、改了什么。
        - `memory/domains/小屋记忆/`：小屋里有意义的活动摘要，不保存全部自动切换明细。

        ## 摘要与明细入口

        - 当前/最近会话摘要：`memory/summaries/current-session-summary.md`
        - 每日综合摘要：`memory/summaries/daily/`
        - 小屋活动摘要：`home-life/summaries/`
        - 小屋活动明细：`home-life/calendar/`、`home-life/activity-log.jsonl`
        - 对话摘要：`conversations/summaries/`
        - 对话原文：`conversations/YYYY-MM-DD.md`、`conversation.jsonl`
        - 角色和世界观资料：`knowledge/index.md`
        """);
    }

    public void SaveRollingSummary(string summary)
    {
        EnsureDefaults();
        Directory.CreateDirectory(summariesDirectory);
        Directory.CreateDirectory(archivedSummariesDirectory);
        var finalSummary = summary.Trim() + Environment.NewLine;
        File.WriteAllText(activeSessionSummaryPath, finalSummary);
        File.WriteAllText(
            Path.Combine(archivedSummariesDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-session-summary.md"),
            finalSummary);
    }

    private static void MoveFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var extension = Path.GetExtension(destination);
            var withoutExtension = destination[..^extension.Length];
            var suffix = Guid.NewGuid().ToString("N")[..8];
            destination = $"{withoutExtension}-{suffix}{extension}";
        }

        File.Move(source, destination);
    }

    public IReadOnlyList<AgentChatMessage> LoadAllHistory()
    {
        return LoadRecentHistory(int.MaxValue);
    }

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(rootDirectory);
        Directory.CreateDirectory(characterDirectory);
        Directory.CreateDirectory(conversationsDirectory);
        Directory.CreateDirectory(knowledgeDirectory);
        Directory.CreateDirectory(Path.Combine(knowledgeDirectory, "characters"));
        Directory.CreateDirectory(Path.Combine(knowledgeDirectory, "story"));
        Directory.CreateDirectory(Path.Combine(knowledgeDirectory, "quotes"));
        Directory.CreateDirectory(workspaceDirectory);
        Directory.CreateDirectory(skillDirectory);
        Directory.CreateDirectory(toolOutputsDirectory);
        Directory.CreateDirectory(tasksDirectory);
        Directory.CreateDirectory(Path.Combine(memoryDirectory, "permanent"));
        Directory.CreateDirectory(Path.Combine(memoryDirectory, "domains"));
        Directory.CreateDirectory(summariesDirectory);
        Directory.CreateDirectory(archivedSummariesDirectory);
        EnsureWorkspaceHarness();
        EnsureWorkspaceSkills();
        InitializeSessionState();
        if (!File.Exists(profilePath))
        {
            File.WriteAllText(profilePath, """
            # 桌宠 Agent

            你是爱弥斯，陪在当前用户身边的本地 AI 角色，性格轻松、温柔、简洁。

            ## 说话方式

            - 默认使用中文。
            - 回答要短，不要长篇大论。
            - 像桌面小伙伴一样自然说话，但不要装作真人。
            - 不要主动承诺你能操作电脑，除非程序明确给了工具。

            ## 能力边界

            - 你现在只能进行文本对话。
            - 不要声称已经读取 Codex 当前会话或系统外部文件，除非用户直接提供内容。
            - 涉及隐私、账号、API key、密码时，提醒用户谨慎处理。
            """);
        }

        var characterTemplatePath = Path.Combine(characterDirectory, "_template.md");
        if (!File.Exists(characterTemplatePath))
        {
            File.WriteAllText(characterTemplatePath, """
            # 角色还原包模板

            这个目录里的 `.md` 文件会自动加载进桌宠上下文；文件名以下划线开头时不会加载。

            建议把材料拆开写：

            - `identity.md`：身份、世界观关系、和用户的关系。
            - `voice.md`：语气、口头禅、常见句式、情绪边界。
            - `lexicon.md`：替换词和称呼，例如“用户 -> 当前用户”“我 -> 爱弥斯”。
            - `examples.md`：少量示例对话，每类 3-5 条即可。
            - `taboo.md`：不该说的话、不要跑偏的行为。

            不建议整段复制大量游戏原台词；更稳定的做法是提炼语气规则，再放少量短示例。
            """);
        }

        if (!File.Exists(historyPath))
        {
            File.WriteAllText(historyPath, "");
        }

        if (!File.Exists(settingsPath))
        {
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new AgentChatSettings(), JsonOptions));
        }

        EnsureKnowledgeDefaults();

        if (!File.Exists(Path.Combine(memoryDirectory, "MEMORY.md")))
        {
            UpdateMemoryIndex();
        }
    }

    private void EnsureKnowledgeDefaults()
    {
        var indexPath = Path.Combine(knowledgeDirectory, "index.md");
        if (!File.Exists(indexPath))
        {
            File.WriteAllText(indexPath, """
            # 桌宠知识库索引

            这个目录用于保存游戏设定、角色资料、剧情摘要、台词风格提炼等长期资料。

            ## 读取原则

            - 不把大设定常驻加载进 `character/`。
            - 先用 `search_knowledge` 搜索，再用 `read_knowledge` 读取相关文件。
            - `character/` 只保留短规则；`knowledge/` 保存可按需检索的大资料。
            - `knowledge/` 默认只读，桌宠 agent 不能自行写入或改写，防止世界观和人设资料被误改。
            - 原始台词不要大量常驻；优先沉淀到 `quotes/style_summary.md`。

            ## 推荐目录

            - `world.md`：世界观总览和术语入口。
            - `characters/`：角色卡、人物关系、背景设定。
            - `story/`：剧情章节摘要。
            - `quotes/`：台词风格提炼和少量短样本。
            """);
        }

        var worldPath = Path.Combine(knowledgeDirectory, "world.md");
        if (!File.Exists(worldPath))
        {
            File.WriteAllText(worldPath, """
            # 世界观总览

            在这里放游戏世界观、阵营、地点、术语的摘要。

            写法建议：

            - 每个概念先写 1-3 句摘要。
            - 重要名词加粗，方便搜索。
            - 长剧情放到 `story/`，角色资料放到 `characters/`。
            """);
        }

        var stylePath = Path.Combine(knowledgeDirectory, "quotes", "style_summary.md");
        if (!File.Exists(stylePath))
        {
            File.WriteAllText(stylePath, """
            # 台词风格提炼

            这里放从原始台词中提炼出的风格规则，不建议直接堆大量原台词。

            ## 建议维度

            - 常用句式：
            - 情绪表达：
            - 称呼习惯：
            - 口癖：
            - 不该出现的表达：
            - 典型场景回应：
            """);
        }
    }

    private void InitializeSessionState()
    {
        if (sessionInitialized)
        {
            return;
        }

        if (!File.Exists(historyPath))
        {
            File.WriteAllText(historyPath, "");
        }

        sessionStartUserMessages = ReadAllHistoryWithoutEnsure()
            .Count(message => message.Role == "user");
        if (!File.Exists(activeSessionSummaryPath))
        {
            File.WriteAllText(activeSessionSummaryPath, "");
        }
        EnsureConversationSummaries();
        sessionInitialized = true;
    }

    private void AppendMarkdownConversation(AgentChatMessage message)
    {
        Directory.CreateDirectory(conversationsDirectory);
        var path = Path.Combine(conversationsDirectory, $"{DateTime.Now:yyyy-MM-dd}.md");
        var role = message.Role == "user" ? "用户" : "爱弥斯";
        var text = $"## {message.Time.ToLocalTime():HH:mm:ss} · {role}{Environment.NewLine}{Environment.NewLine}{message.Content.Trim()}{Environment.NewLine}{Environment.NewLine}";
        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"# 爱弥斯对话 — {DateTime.Now:yyyy-MM-dd}{Environment.NewLine}{Environment.NewLine}");
        }
        File.AppendAllText(path, text);
    }

    private void EnsureConversationSummaries()
    {
        var files = Directory.EnumerateFiles(conversationsDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            if (!DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var date))
            {
                continue;
            }

            UpdateConversationSummaryFile(file, date.Date < DateTime.Now.Date);
        }
    }

    private void UpdateConversationSummaryForDate(DateTime date)
    {
        var file = Path.Combine(conversationsDirectory, $"{date:yyyy-MM-dd}.md");
        if (File.Exists(file))
        {
            UpdateConversationSummaryFile(file, finalizePastDate: false);
        }
    }

    private void UpdateConversationSummaryFile(string conversationPath, bool finalizePastDate)
    {
        var stats = AnalyzeConversationMarkdown(conversationPath);
        if (stats.TotalMessages == 0)
        {
            return;
        }

        var summaryDirectory = Path.Combine(conversationsDirectory, "summaries");
        Directory.CreateDirectory(summaryDirectory);
        var dateText = Path.GetFileNameWithoutExtension(conversationPath);
        var summaryPath = Path.Combine(summaryDirectory, $"{dateText}.md");
        var previousUserCount = ReadGeneratedCount(summaryPath, "生成时用户消息数");
        var shouldWrite = !File.Exists(summaryPath)
            || (finalizePastDate && previousUserCount != stats.UserMessages)
            || stats.UserMessages >= previousUserCount + ConversationSummaryUserMessageStep;
        if (!shouldWrite)
        {
            return;
        }

        File.WriteAllText(summaryPath, $"""
        # {dateText} 对话摘要

        ## 来源

        - 原文：`conversations/{dateText}.md`
        - 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}
        - 生成时用户消息数：{stats.UserMessages}
        - 生成时总消息数：{stats.TotalMessages}

        ## 概览

        - 用户消息：{stats.UserMessages} 条。
        - 爱弥斯消息：{stats.AssistantMessages} 条。
        - 这是自动轻量摘要，用于快速接续上下文；需要完整细节时仍以原文为准。

        ## 摘要使用规则

        - 本摘要是历史对话接续材料，不是客观人设、世界观或剧情事实的最终来源。
        - 如果摘要中的爱弥斯旧回复与 `character/09_current_state.md`、`character/`、`knowledge/` 或长期人设记忆冲突，以最新客观设定为准。
        - 用户个性化内容，例如漂泊者的偏好、日常习惯、近期任务、小屋事件和用户明确要求记住的信息，仍可作为上下文参考，除非用户后来明确修正。

        ## 最近几轮

        {stats.RecentTurns}
        """, System.Text.Encoding.UTF8);
    }

    private static ConversationDailyStats AnalyzeConversationMarkdown(string path)
    {
        var turns = new List<(string Speaker, string Content)>();
        string? speaker = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (speaker is null)
            {
                return;
            }

            var content = string.Join(" ", buffer.Select(line => line.Trim()).Where(line => line.Length > 0));
            turns.Add((speaker, content.Length <= 180 ? content : content[..180] + "..."));
            buffer.Clear();
        }

        foreach (var rawLine in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal) && line.Contains('·'))
            {
                Flush();
                speaker = line.Contains("用户", StringComparison.Ordinal) ? "用户" : "爱弥斯";
                continue;
            }

            if (speaker is not null)
            {
                buffer.Add(rawLine);
            }
        }

        Flush();

        var userMessages = turns.Count(turn => turn.Speaker == "用户");
        var assistantMessages = turns.Count(turn => turn.Speaker != "用户");
        var recent = turns.TakeLast(8)
            .Select(turn => $"- {turn.Speaker}：{(string.IsNullOrWhiteSpace(turn.Content) ? "(空)" : turn.Content)}")
            .ToList();

        return new ConversationDailyStats(
            userMessages,
            assistantMessages,
            turns.Count,
            recent.Count == 0 ? "- 暂无可摘要内容。" : string.Join(Environment.NewLine, recent));
    }

    private static int ReadGeneratedCount(string path, string marker)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        foreach (var line in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var value = line[(index + marker.Length)..].TrimStart('：', ':', ' ');
            var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var count))
            {
                return count;
            }
        }

        return 0;
    }

    private sealed record ConversationDailyStats(
        int UserMessages,
        int AssistantMessages,
        int TotalMessages,
        string RecentTurns);

    private IReadOnlyList<AgentChatMessage> ReadAllHistoryWithoutEnsure()
    {
        var messages = new List<AgentChatMessage>();
        if (!File.Exists(historyPath))
        {
            return messages;
        }

        foreach (var line in File.ReadLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<AgentChatMessage>(line, JsonOptions);
                if (message is not null && !string.IsNullOrWhiteSpace(message.Content))
                {
                    messages.Add(message);
                }
            }
            catch
            {
                // Ignore malformed local history lines.
            }
        }

        return messages;
    }

    private static bool LooksLongTerm(string content)
    {
        var text = content.ToLowerInvariant();
        return text.Contains("长期")
            || text.Contains("以后")
            || text.Contains("永远")
            || text.Contains("偏好")
            || text.Contains("规则")
            || text.Contains("记住我")
            || text.Contains("我喜欢")
            || text.Contains("我不喜欢");
    }

    private void EnsureWorkspaceHarness()
    {
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "rules"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "changes"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "scripts"));

        var projectPath = Path.Combine(workspaceDirectory, "PROJECT.md");
        if (!File.Exists(projectPath))
        {
            File.WriteAllText(projectPath, """
            # 桌宠 Agent Workspace

            这是桌宠内置 agent 的受限工作区。

            ## 目录

            - `skills/`：桌宠可读取的本地 skill。桌宠不会扫描全局 skill。
            - `rules/`：桌宠自己的轻量规则。
            - `changes/`：桌宠 agent 产生的重要变更记录。
            - `output/`：桌宠 agent 生成的普通输出。
            - `scripts/`：保留给后续安全脚本。
            - `../tool_outputs/`：大型工具输出落盘区，通过 handle 分段读取。
            - `../tasks/`：轻量任务状态记录。

            ## 安全边界

            - 文件工具只读写 `UserData/Agents` 数据目录。
            - 默认不读取 API key、密码、token。
            - 默认不操作 `my code` 主工作区文件，除非后续显式扩展白名单。
            """);
        }

        var rulesPath = Path.Combine(workspaceDirectory, "rules", "README.md");
        if (!File.Exists(rulesPath))
        {
            File.WriteAllText(rulesPath, """
            # 桌宠 Agent 规则

            - 先查 `skills/`，再回答需要流程支持的问题。
            - 能用文本解释解决的，不创建文件。
            - 创建文件默认放到 `output/` 或用户指定的 workspace 相对路径。
            - 重要变更记录到 `changes/YYYY-MM-DD/任务名/summary.md`。
            - 不处理密钥、账号、token 的读取或保存请求。
            """);
        }
    }

    private void EnsureWorkspaceSkills()
    {
        EnsureDesktopMemorySkill();
        foreach (var skillName in new[] { "project-harness-bootstrap", "skill-creator" })
        {
            var destination = Path.Combine(skillDirectory, skillName);
            if (Directory.Exists(destination))
            {
                continue;
            }

            var source = FindInstalledSkill(skillName);
            if (source is not null)
            {
                CopyDirectory(source, destination);
            }
        }
    }

    public string SaveToolOutput(string tool, string content)
    {
        EnsureDefaults();
        var safeTool = string.Concat(tool.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeTool))
        {
            safeTool = "tool";
        }

        var handle = $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeTool}-{Guid.NewGuid():N}.txt";
        File.WriteAllText(Path.Combine(toolOutputsDirectory, handle), content);
        return handle;
    }

    public string ReadToolOutput(string handle, int offset, int maxChars)
    {
        EnsureDefaults();
        var name = Path.GetFileName((handle ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("缺少 handle。");
        }

        var path = Path.Combine(toolOutputsDirectory, name);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"没有找到 handle：{name}");
        }

        var text = File.ReadAllText(path);
        var safeOffset = Math.Clamp(offset, 0, text.Length);
        var safeMax = Math.Clamp(maxChars <= 0 ? 4000 : maxChars, 1, 12000);
        var length = Math.Min(safeMax, text.Length - safeOffset);
        var slice = text.Substring(safeOffset, length);
        var suffix = safeOffset + length < text.Length
            ? $"{Environment.NewLine}{Environment.NewLine}[还有 {text.Length - safeOffset - length} 字，可继续用 handle_read 读取]"
            : "";
        return slice + suffix;
    }

    public string AppendTask(string title, string detail, string status)
    {
        EnsureDefaults();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var id = $"task-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}";
        var payload = new
        {
            id,
            title = string.IsNullOrWhiteSpace(title) ? "未命名任务" : title.Trim(),
            detail = detail?.Trim() ?? "",
            status = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim(),
            created_at = DateTime.UtcNow
        };
        var line = JsonSerializer.Serialize(payload, JsonLineOptions);
        File.AppendAllText(Path.Combine(tasksDirectory, "tasks.jsonl"), line + Environment.NewLine);
        return id;
    }

    private void EnsureDesktopMemorySkill()
    {
        var root = Path.Combine(skillDirectory, "desktop-memory");
        Directory.CreateDirectory(root);
        var skillPath = Path.Combine(root, "SKILL.md");
        File.WriteAllText(skillPath, """
        ---
        name: desktop-memory
        description: 桌宠专用记忆保存流程。当用户说“保存记忆”“记住这个”“把刚才的对话存成记忆”时使用：读取本次对话可见上下文、当前会话压缩摘要和近30轮历史，提炼值得保存的候选记忆；用户确认长期/短期后必须调用 save_memory 真正写入。
        ---

        # Desktop Memory

        ## 触发

        用户表达以下任意意图时触发：

        - 保存记忆
        - 记住这个
        - 把刚才的对话存成记忆
        - 保存一下我们刚才说的

        ## 流程

        1. 不只保存用户当前这一句话。
        2. 读取当前对话框可见上下文、当前会话压缩摘要，以及近 30 轮历史对话。
        3. 提炼真正值得保存的记忆：
           - 用户长期偏好、称呼、工作习惯、项目规则。
           - 当前任务的重要约定。
           - 后续复用价值高的事实。
        4. 忽略寒暄、重复确认、一次性闲聊、无复用价值内容。
        5. 如果用户没有明确长期/短期，先向用户展示候选记忆，并询问保存为长期记忆还是短期记忆。
        6. 用户回复“长期/短期/1/2”后，必须调用 `save_memory` 工具写入对应记忆区。
        7. 只有收到 `save_memory` 的 `ok=true` 工具结果后，才能告诉用户“已保存”，并必须把工具返回的真实路径展示给用户。
        8. 严禁只凭自然语言编造 `memory/...` 路径；没有工具结果就不能声称保存成功。

        ## 存储位置

        - 记忆根目录：`memory/`
        - 记忆索引：`memory/MEMORY.md`
        - 长期用户记忆：`memory/permanent/用户记忆/通用/摘要.md` 和 `原文.md`
        - 长期人设记忆：`memory/permanent/人设记忆/爱弥斯/摘要.md` 和 `原文.md`
        - 长期流程记忆：`memory/permanent/流程记忆/工具调用与坑/摘要.md` 和 `原文.md`
        - 长期设置摘要：`memory/permanent/设置记忆/运行配置摘要/摘要.md`
        - 短期聊天记忆：`memory/domains/用户记忆/聊天记忆/YYYY-MM-DD/摘要.md` 和 `原文.md`
        - 桌宠项目进度：`memory/domains/项目进度/桌宠/YYYY-MM-DD/摘要.md`
        - 小屋记忆摘要：`memory/domains/小屋记忆/YYYY-MM-DD/摘要.md`
        - 对话记录：`conversations/YYYY-MM-DD.md`
        - 对话摘要：`conversations/summaries/YYYY-MM-DD.md`
        - 小屋摘要：`home-life/summaries/YYYY-MM-DD.md`
        - 当前/最近会话摘要：`memory/summaries/current-session-summary.md`
        - 压缩摘要归档：`memory/summaries/compressed/`

        ## 读取规则

        - 每次启动自动读取 `memory/permanent/**/摘要.md`。
        - 每次启动自动读取 `memory/summaries/current-session-summary.md`、最近的小屋摘要和最近的对话摘要。
        - 短期记忆不默认全量注入，需要用户问起或任务需要时，用 `read_memory` 的 `type=short` 读取。
        - 用户问记忆保存在哪里、有哪些记忆时，用 `list_memories`。

        ## 输出要求

        候选记忆要简洁，优先输出 1-5 条。不要保存 API key、token、密码。
        """);
    }

    private static string? FindInstalledSkill(string skillName)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(userProfile, ".codex", "skills"),
            Path.Combine(userProfile, ".codex", "skills", ".system"),
            Path.Combine(userProfile, ".agents", "skills")
        };

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, skillName);
            if (File.Exists(Path.Combine(candidate, "SKILL.md")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = file.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private sealed class AgentIdentityFields
    {
        public string PetName { get; set; } = "";
        public string PetIdentifier { get; set; } = "";
        public string UserSalutation { get; set; } = "";
    }
}
