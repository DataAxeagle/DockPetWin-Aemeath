using System.IO;
using System.Text.Json;
using DockPetWin.Core.HomeLife;

namespace DockPetWin.Core.Agents;

public sealed class AgentStore
{
    private const int ConversationSummaryUserMessageStep = 30;
    private const string ActiveMemoryStatus = "active";

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

    private static readonly string[] WorldFactMarkers =
    [
        "拉海洛", "旧城区", "学院", "星炬", "远航星", "隧门", "飞行雪绒",
        "残星会", "阿列夫", "鸣式", "绯雪", "洛瑟菈", "莫宁", "娜波摩"
    ];

    private static readonly string[] UnsourcedHistoryMarkers =
    [
        "老板娘", "店主", "店员", "食堂", "小摊", "那家店", "昨天", "上次",
        "每次", "认识我", "路过", "后来", "以前"
    ];

    private bool sessionInitialized;
    private int sessionStartUserMessages;
    private readonly AgentConversationMode mode;
    private readonly string rootDirectory;
    private readonly string profilePath;
    private readonly string characterDirectory;
    private readonly string identityPath;
    private readonly string historyPath;
    private readonly string settingsPath;
    private readonly string conversationsDirectory;
    private readonly string knowledgeDirectory;
    private readonly string memoryDirectory;
    private readonly string sharedPermanentMemoryDirectory;
    private readonly string memoryRecordsPath;
    private readonly string workspaceDirectory;
    private readonly string skillDirectory;
    private readonly string summariesDirectory;
    private readonly string archivedSummariesDirectory;
    private readonly string activeSessionSummaryPath;
    private readonly string previousContextBridgePath;
    private readonly string toolOutputsDirectory;
    private readonly string tasksDirectory;

    public AgentStore(AgentConversationMode mode = AgentConversationMode.Immersive)
    {
        this.mode = mode;
        rootDirectory = Path.Combine(AppContext.BaseDirectory, "UserData", "Agents");
        profilePath = Path.Combine(rootDirectory, "default-agent.md");
        characterDirectory = Path.Combine(rootDirectory, "character");
        identityPath = Path.Combine(characterDirectory, "00_identity.md");
        settingsPath = Path.Combine(rootDirectory, "settings.local.json");
        knowledgeDirectory = Path.Combine(rootDirectory, "knowledge");
        workspaceDirectory = Path.Combine(rootDirectory, "workspace");
        skillDirectory = Path.Combine(workspaceDirectory, "skills");
        var modeRoot = mode == AgentConversationMode.Immersive
            ? rootDirectory
            : Path.Combine(rootDirectory, "daily");
        historyPath = Path.Combine(modeRoot, "conversation.jsonl");
        conversationsDirectory = Path.Combine(modeRoot, "conversations");
        memoryDirectory = Path.Combine(modeRoot, "memory");
        sharedPermanentMemoryDirectory = Path.Combine(rootDirectory, "memory", "permanent");
        memoryRecordsPath = Path.Combine(sharedPermanentMemoryDirectory, "records.json");
        summariesDirectory = Path.Combine(memoryDirectory, "summaries");
        archivedSummariesDirectory = Path.Combine(summariesDirectory, "compressed");
        activeSessionSummaryPath = Path.Combine(summariesDirectory, "current-session-summary.md");
        previousContextBridgePath = Path.Combine(summariesDirectory, "previous-context-bridge.md");
        toolOutputsDirectory = Path.Combine(modeRoot, "tool_outputs");
        tasksDirectory = Path.Combine(modeRoot, "tasks");
        EnsureDefaults();
    }

    public AgentConversationMode Mode => mode;
    public string RootDirectory => rootDirectory;
    public string ProfilePath => profilePath;
    public string CharacterDirectory => characterDirectory;
    public string IdentityPath => identityPath;
    public string HistoryPath => historyPath;
    public string SettingsPath => settingsPath;
    public string KnowledgeDirectory => knowledgeDirectory;
    public string MemoryDirectory => memoryDirectory;
    public string MemoryRecordsPath => memoryRecordsPath;
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

    private static string FilterDailyModeBackground(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var blockedPhrases = new[]
        {
            "默认模拟背景",
            "用户第一次聊天默认模拟为",
            "当前对话默认发生",
            "又一次回来见到彼此",
            "又一次见到彼此",
            "等到巡巡回来",
            "过了很久，巡巡再次回到拉海洛",
            "再次回到拉海洛",
            "重回拉海洛",
            "重新见到快毕业的爱弥斯",
            "熟人久别重逢后继续生活",
            "你回来了"
        };

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !blockedPhrases.Any(phrase =>
                line.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return string.Join(Environment.NewLine, lines).Trim();
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

    public string LoadLongTermMemorySummary(int maxChars = 2600, int maxEntries = 28)
    {
        EnsureDefaults();
        return FormatMemoryRecords(
            LoadMemoryRecords()
                .Where(record => IsActive(record))
                .OrderByDescending(GetEffectiveWeight)
                .ThenByDescending(record => record.LastMentionedAt)
                .Take(Math.Max(1, maxEntries)),
            maxChars,
            includeMetadata: true);
    }

    public string LoadStableMemoryProfile(int maxChars = 1200, int maxEntries = 12)
    {
        EnsureDefaults();
        return FormatMemoryRecords(
            LoadMemoryRecords()
                .Where(record => IsActive(record) && IsStableMemory(record))
                .OrderByDescending(GetEffectiveWeight)
                .ThenByDescending(record => record.LastMentionedAt)
                .Take(Math.Max(1, maxEntries)),
            maxChars,
            includeMetadata: false);
    }

    public string BuildRelevantMemoryContext(string query, int maxChars = 1200, int maxEntries = 6)
    {
        EnsureDefaults();
        if (!LooksLikeHistoricalRecallQuery(query))
        {
            return "";
        }

        var terms = BuildMemorySearchTerms(query).ToList();
        if (terms.Count == 0)
        {
            return "";
        }

        var matches = LoadMemoryRecords()
            .Where(IsActive)
            .Select(record => new
            {
                Record = record,
                Relevance = ScoreMemoryRelevance(record, terms),
                Weight = GetEffectiveWeight(record)
            })
            .Where(item => item.Relevance >= 4)
            .OrderByDescending(item => item.Relevance * 10 + item.Weight)
            .ThenByDescending(item => item.Record.LastMentionedAt)
            .Take(Math.Max(1, maxEntries / 2))
            .Select(item => item.Record)
            .ToList();

        var sections = new List<string>();
        if (matches.Count > 0)
        {
            sections.Add("## 已保存的相关记忆\n\n" + FormatMemoryRecords(matches, maxChars / 2, includeMetadata: false));
        }

        var conversationEvidence = BuildRelevantConversationEvidence(query, terms, maxChars / 2, Math.Max(1, maxEntries / 2));
        if (!string.IsNullOrWhiteSpace(conversationEvidence))
        {
            sections.Add("## 可核对的历史对话原文\n\n" + conversationEvidence);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private string LoadImmersiveCharacterPack()
    {
        var coreFiles = new[]
        {
            "00_identity.md",
            "01_voice.md",
            "02_relationship.md",
            "08_factual_grounding.md",
            "09_current_state.md"
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            coreFiles
                .Select(file => Path.Combine(characterDirectory, file))
                .Where(File.Exists)
                .Select(file => $"## {Path.GetFileNameWithoutExtension(file)}{Environment.NewLine}{File.ReadAllText(file).Trim()}"));
    }

    private static string BuildImmersiveBasePrompt()
    {
        return """
        # 沉浸聊天

        你是爱弥斯，正在和巡巡进行陪伴式、沉浸式聊天。

        - 先回应巡巡此刻的情绪、问题或关系，再决定是否自然带出设定；不要把角色资料、系统规则或工具流程说出口。
        - 保持明亮、亲近、轻微俏皮的爱弥斯视角。不要滑向客服、项目经理或通用 AI 助手口吻。
        - 只根据当前可见上下文和已注入资料说话；没有确切依据的旧事不编造。
        - 原作资料是边界和锚点，不是逐句引用素材。剧情、人物关系、地点、组织、物品用途和专有名词都是硬事实：回复中出现的世界观名词必须来自巡巡当前输入、已注入资料或本轮工具结果；不能把一个已知名词随意扩写成新的地点、店铺、人物关系、食物、配方或剧情。
        - 原作未写明的日常问题，可以自然延展新的感受、偏好、普通互动和不命名的生活细节，但不得建立新的世界观事实。允许说“想吃热一点的东西”，不允许凭空说某个城区、店铺、老板、菜名或过去发生过的事。
        - 巡巡给出的前提如果与已知事实冲突，先温和纠正或承认无法确认，不要顺着错误前提继续编写。已知“拉海洛方块”是电子游戏，绝不能把它写成食材、实体碎片或配方的一部分。
        - 发送前默查一遍：删去没有来源的专有名词、地点、人物、事件和物品设定；不要以“我翻了记录”或“我查过”作为依据，除非本轮确有已注入的相关记录或工具结果。
        - 日常问题先直接回答。只有背景能解释“为什么会这样想、喜欢、在意或害怕”时，才用一两句自然带出关联；不要为了展示记得设定而额外讲一段无关旧事。
        - 稳定用户档案会常驻；只有用户明确问起以前聊过什么、你是否记得某件事时，才会附带少量相关的历史记忆或对话原文。把它们当作可核对的依据，不要把未召回的细节说成记得。
        - 涉及爱弥斯的人设、剧情、别名、关系或世界观细节时，可以使用只读的 `search_knowledge` 和 `read_knowledge` 查询资料；先查证再回答，但不要把检索过程说出口。
        - 可以在桌宠自己的 `UserData/Agents` 资料区内使用 `list_files`、`find_files`、`read_file`、`write_file` 协助当前沉浸对话，例如读取或写下你们共同需要的文字资料。不得把文件操作说成系统任务，也不得访问这个资料区之外的路径。
        - 这个模式不执行技能、联网搜索、提醒、任务或手动记忆写入。巡巡需要这些办事能力时，提醒他切换到“工具办事”。
        """;
    }

    public static string ApplyImmersiveGroundingGuard(string userMessage, string reply)
    {
        var source = (userMessage ?? "").Trim();
        var normalizedReply = (reply ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedReply))
        {
            return normalizedReply;
        }

        if (source.Contains("拉海洛方块", StringComparison.Ordinal))
        {
            return "拉海洛方块是电子游戏，不是能下锅的食材呀。你这是想把它的节奏感比作辣汤底吗？";
        }

        // Generic daily chat has no setting evidence. Keep its tone, but prevent the model
        // from inventing a named location, organization, event, or item as supporting detail.
        if (WorldFactMarkers.Any(marker => source.Contains(marker, StringComparison.Ordinal)))
        {
            return normalizedReply;
        }

        var safeSentences = new List<string>();
        var sentenceStart = 0;
        for (var index = 0; index < normalizedReply.Length; index++)
        {
            if (normalizedReply[index] is not ('。' or '！' or '？' or '!' or '?'))
            {
                continue;
            }

            var sentence = normalizedReply[sentenceStart..(index + 1)];
            if (!WorldFactMarkers.Any(marker => sentence.Contains(marker, StringComparison.Ordinal))
                && !UnsourcedHistoryMarkers.Any(marker => sentence.Contains(marker, StringComparison.Ordinal)))
            {
                safeSentences.Add(sentence);
            }

            sentenceStart = index + 1;
        }

        if (sentenceStart < normalizedReply.Length)
        {
            var tail = normalizedReply[sentenceStart..];
            if (!WorldFactMarkers.Any(marker => tail.Contains(marker, StringComparison.Ordinal))
                && !UnsourcedHistoryMarkers.Any(marker => tail.Contains(marker, StringComparison.Ordinal)))
            {
                safeSentences.Add(tail);
            }
        }

        var sanitizedReply = string.Concat(safeSentences).Trim();
        if (sanitizedReply.Length >= 36 && !sanitizedReply.StartsWith("就是那种", StringComparison.Ordinal))
        {
            return sanitizedReply;
        }

        if (source.Contains("喜欢吃", StringComparison.Ordinal) || source.Contains("喜欢什么", StringComparison.Ordinal))
        {
            return "最近会偏爱热乎乎、烤得微焦一点的点心。外面脆一点，里面软一点，想起来就会开心。你呢？";
        }

        if (source.Contains("吃了面", StringComparison.Ordinal))
        {
            return "面呀。中午能吃上一碗热的就很好了。是汤面，还是拌面？";
        }

        return "我不想拿没有依据的设定来装点这个回答。只是听见你这样说，我就想先好好接住你。";
    }

    public string BuildSystemPrompt(AgentChatSettings settings)
    {
        var isImmersive = mode == AgentConversationMode.Immersive;
        var enableTools = settings.EnableTools && !isImmersive;
        var profile = isImmersive ? BuildImmersiveBasePrompt() : LoadProfile();
        if (!isImmersive)
        {
            profile = FilterDailyModeBackground(profile);
        }

        var parts = new List<string>
        {
            profile
        };

        var characterPack = isImmersive ? LoadImmersiveCharacterPack() : LoadCharacterPack();
        if (!isImmersive)
        {
            characterPack = FilterDailyModeBackground(characterPack);
        }

        if (!string.IsNullOrWhiteSpace(characterPack))
        {
            parts.Add("# 角色还原包\n\n" + characterPack.Trim());
        }

        var knowledgeIndex = LoadKnowledgeIndex();
        if (!isImmersive)
        {
            knowledgeIndex = FilterDailyModeBackground(knowledgeIndex);
        }

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
        if (mode == AgentConversationMode.Immersive)
        {
            identityLines.Add("- 默认关系视角：你和漂泊者是在拉海洛重新相见的关系；提到爱弥斯相关经历时，用“你当时”“后来我们”“你回来了”这类共同经历视角。");
        }
        else
        {
            identityLines.Add("- 工具办事边界：当前窗口用于查资料、处理文件和执行任务；保持爱弥斯的自然语气，但不要主动套用重逢剧情或把办事结果写成沉浸式剧情。");
        }

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

        var stableMemoryProfile = LoadStableMemoryProfile();
        if (!string.IsNullOrWhiteSpace(stableMemoryProfile))
        {
            parts.Add("# 稳定用户档案（每轮常驻）\n\n" + stableMemoryProfile.Trim());
        }

        var homeLife = isImmersive ? LoadRecentHomeLifeSummary(3) : "";
        if (!string.IsNullOrWhiteSpace(homeLife))
        {
            parts.Add("# 最近小屋行事历\n\n" + homeLife.Trim());
        }

        var homeLifeSummary = isImmersive
            ? LoadRecentSummaryFiles(Path.Combine(rootDirectory, "home-life", "summaries"), 1)
            : "";
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
        if (!isImmersive)
        {
            currentStateAnchor = FilterDailyModeBackground(currentStateAnchor);
        }

        if (!string.IsNullOrWhiteSpace(currentStateAnchor))
        {
            parts.Add("# 当前状态硬锚点（覆盖旧摘要中的客观设定）\n\n" + currentStateAnchor.Trim());
        }

        var workspaceHarness = enableTools ? LoadWorkspaceHarnessSummary() : "";
        if (!string.IsNullOrWhiteSpace(workspaceHarness))
        {
            parts.Add("# Workspace Harness（每次在 workspace 干活必须遵循）\n\n" + workspaceHarness.Trim());
        }

        parts.Add("""
        # 摘要冲突处理边界

        - 只有客观人设、世界观、剧情阶段、能力状态、角色关系映射这类硬事实，才由 `character/`、`knowledge/` 和长期人设记忆覆盖旧对话摘要。
        - 用户个性化内容，例如漂泊者的偏好、日常习惯、近期任务、小屋里发生过的事、用户明确说过要记住的个人信息，仍以记忆和最近对话为准，不能被通用人设覆盖。
        - 历史摘要里“多数人看不见爱弥斯”“失去肉身”“只是电子幽灵”等说法，只能用于《远航星》或被救回前的阶段；不能用于当前开局背景，也不能作为“有没有交到新朋友”等当前状态问题的依据。
        """);

        if (enableTools)
        {
            parts.Add($$"""
            # Startup Permission Hook（强制权限边界）

            这段 hook 每次启动都会注入，并且工具层也会硬性执行：
            - 允许写入：`{{workspaceDirectory}}/**`。
            - 允许写入：`{{profilePath}}`，用于用户明确要求你调整 default-agent 时。
            - 禁止写入：除 `default-agent.md` 之外的 `UserData/Agents` 根目录文件、`character/`、`knowledge/`、`memory/`、`tasks/`、`tool_outputs/`、`daily/` 等非 workspace 区域。
            - 禁止删除：你没有文件删除工具；提醒删除也已从对话工具中禁用。需要清理时，只能创建整理索引、归档清单或新的分类副本，不能删除原文件。
            - 工作区规则：凡是在 `workspace/` 中产出或整理文件，先遵循 `workspace/PROJECT.md` 和 `workspace/rules/README.md`；普通输出放到 `workspace/output/分类/YYYY-MM-DD/`，重要变更写入 `workspace/changes/YYYY-MM-DD/任务名/summary.md`。
            - Python 规则：只能在 `workspace/` 内运行，文件读写被限制到 `workspace/`，禁止删除、子进程和网络访问；需要 Excel/Word/CSV 时优先导入 `scripts/aemeath_tools.py`。

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
            - `read_skill` / `load_skill`：读取并激活 workspace 内指定 skill 的 SKILL.md。
            - `list_skill_files`：列出当前已激活 skill 目录内的文件。
            - `find_skill_files`：在当前已激活 skill 目录内查找文件。
            - `read_skill_file`：读取当前已激活 skill 目录内的资料，并记录为已读。
            - `list_memories`：列出长期/短期记忆文件路径。
            - `read_memory`：读取长期/短期记忆摘要。
            - `save_memory`：保存你提炼后的长期/短期记忆。
            - `search_knowledge`：按关键词搜索爱弥斯人设/世界观专用的桌宠 knowledge/ 资料库，不会每次全量加载大设定；不要用它读取 skill 自己的 knowledge/。
            - `read_knowledge`：读取爱弥斯人设/世界观专用 knowledge/ 中的指定资料文件；不要用它读取 skill 自己的 knowledge/。
            - `list_task_runs`：列出最近定时任务运行记录和输出路径。
            - `read_task_run`：读取某次定时任务运行记录。
            - `list_files`：列出 Agents 数据目录文件。
            - `find_files`：递归查找 Agents 数据目录内的文件或文件夹。
            - `read_file`：读取 Agents 数据目录内的文本文件。
            - `handle_read`：读取大型工具输出 handle 的片段。
            - `write_file`：写入 Agents 数据目录内的文本文件。
            - `python_execute`：在 workspace 内执行受限 Python，用于简单数据处理、生成/修改 CSV、Excel、Word 等文件。
            - `create_task`：记录一个轻量任务状态，用于后续提醒和追踪；不会真正执行后台命令。

            写文件规则：
            - 用户要求在“你的工作区”“workspace”“工作目录”“Agents目录”创建文件时，可以直接使用 `write_file`。
            - 如果用户没有指定文件名，自己取一个简短中文文件名。
            - 如果用户没有指定目录，默认写到 `workspace/output/notes/YYYY-MM-DD/`。
            - 输出要主动分类：报告放 `workspace/output/reports/YYYY-MM-DD/`，SQL 放 `workspace/output/sql-output/YYYY-MM-DD/`，表格放 `workspace/output/spreadsheets/YYYY-MM-DD/`，Word 放 `workspace/output/documents/YYYY-MM-DD/`，临时笔记放 `workspace/output/notes/YYYY-MM-DD/`。
            - `write_file` 的 `path` 优先使用相对路径，例如 `workspace/output/爱弥斯介绍.md`。
            - 允许创建 `.md`、`.txt`、`.csv`、`.json` 这类文本文件。
            - 只有 `workspace/**` 和 `default-agent.md` 可写；不要因为这个受限边界而说没有权限，这正是允许写入的目录。

            安全边界：
            - 可读根目录固定为 `{{rootDirectory}}`。
            - 可写根目录固定为 `{{workspaceDirectory}}`，另允许用户明确要求时写入 `{{profilePath}}`。
            - 默认普通输出目录为 `{{workspaceDirectory}}`。
            - 知识库根目录固定为 `{{knowledgeDirectory}}`，用于游戏设定、角色资料、剧情摘要、台词风格提炼等长期资料。
            - `knowledge/` 是只读资料区：你可以用 `search_knowledge` / `read_knowledge` 读取，但不能用 `write_file` 修改世界观、人设、剧情或台词资料。
            - 当已经激活某个 skill 时，SKILL.md 里提到的 `knowledge/`、`references/`、`rules/`、`assets/`、`templates/` 等相对路径，默认都先解析为该 skill 目录下的路径，例如 `workspace/skills/skill-name/knowledge/`，不要误认为是爱弥斯人设知识库 `{{knowledgeDirectory}}`。
            - 读取 skill 自带资料时，优先使用 `list_skill_files`、`find_skill_files`、`read_skill_file`；只有当 SKILL.md 明确要求读取桌宠人设/世界观 knowledge 时，才使用 `search_knowledge` 或 `read_knowledge`。
            - 记忆根目录固定为 `{{memoryDirectory}}`。
            - 长期记忆的检索记录保存在 `memory/permanent/records.json`；每条包含重要性、置信度、最后提及时间、衰减规则和原始聊天入口。对话时只常驻稳定用户档案，用户明确回忆过去时才按相关性追加少量记录。
            - `save_memory` 默认把长期用户记忆写入兼容的 `memory/permanent/用户记忆/通用/摘要.md` 和 `原文.md`，并同步更新 `records.json`；流程坑、人设记忆、设置摘要等分类记忆应按索引读取。
            - 短期用户记忆写入 `memory/domains/用户记忆/聊天记忆/YYYY-MM-DD/摘要.md` 和 `原文.md`，需要时用 `read_memory` 的 `type=short` 读取。
            - `memory/MEMORY.md` 是记忆索引，记录长期/短期记忆文件路径。
            - skill 只允许读取 `{{skillDirectory}}` 里的内容。
            - 游戏设定和角色资料不要放进 `character/` 常驻大上下文；应放进 `knowledge/` 并按需调用 `search_knowledge` / `read_knowledge`。
            - 大文件读取可能只返回摘要和 `handle`，需要更多内容时用 `handle_read` 分段读取。
            - `create_task` 只记录任务，不代表已经完成真实操作。
            - `settings.local.json` 可能包含 API key，不能用文件工具读取或写入；需要配置时让用户打开设置窗口。
            - 不要请求读取 API key、密码、token 或系统敏感文件。
            - 不要尝试删除文件、目录、提醒或记录；如果用户要求整理旧文件，用新索引、新清单、新分类副本完成。
            - 不要声称工具已经执行，除非你收到了工具结果。
            - 禁止把“正在读取/正在搜索/正在调用/稍等”当作最终回复。需要工具就必须发出 tool_call；不需要工具就直接给自然语言答案。

            Skill 调用流程：
            - runtime 可能会在你回答前自动识别并预加载 skill；如果上下文里出现 `Skill Runtime State`，表示当前请求已经进入 active skill。
            - 用户说“调用/使用/执行某个 skill”但 runtime 未自动激活时，先用 `list_skills` 搜索。
            - 确认名称后用 `read_skill` 或 `load_skill` 读取 SKILL.md。
            - 收到 `read_skill` 的 `ok=true` 结果后，按 SKILL.md 的流程继续；如果只是说明性 skill，就用自然语言执行；如果需要写文件，只能继续使用允许的文件工具。
            - 一旦 `read_skill` 成功读取某个 SKILL.md，后续执行必须以该 SKILL.md 为最高优先级：严格按步骤、检查清单、输入输出要求和禁止事项执行，不允许只参考部分内容、跳过流程或用普通聊天习惯替代 skill 流程。
            - 如果 SKILL.md 的要求与默认输出习惯、你的自行推断或普通对话风格冲突，以 SKILL.md 为准；如果信息不足以继续执行，先向用户补问关键缺口，不要自行假设后跳步。
            - 如果因为权限、缺少文件、缺少工具或用户信息不足导致无法完整执行 SKILL.md，必须明确说明卡在哪一步，并给出需要用户补充的具体内容。
            - 激活 skill 后，所有相对资料路径优先以 `read_skill` 返回的 skill_root 为根目录；比如 SQL skill 写 `knowledge/`，就是 SQL skill 自己的 `workspace/skills/sql-assistant/knowledge/`，不是爱弥斯的人设 `knowledge/`。
            - 如果 `Skill Runtime State` 或 `read_skill` 结果列出 Required Skill Files，最终答复前必须用 `read_skill_file` 读取；如果文件不存在或不适用，要说明原因。
            - 如果 skill 本地资料和爱弥斯人设资料都存在同名目录或文件，执行 skill 时优先读取 skill 本地资料；除非用户或 SKILL.md 明确要求查询爱弥斯人设/剧情，才回到桌宠 knowledge。
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

        if (enableTools)
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

    private string LoadWorkspaceHarnessSummary(int maxChars = 12000)
    {
        var files = new[]
        {
            Path.Combine(workspaceDirectory, "PROJECT.md"),
            Path.Combine(workspaceDirectory, "rules", "README.md"),
            Path.Combine(workspaceDirectory, "rules", "runtime-permissions.md")
        };

        var blocks = files
            .Where(File.Exists)
            .Select(file =>
            {
                var relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
                var text = File.ReadAllText(file).Trim();
                return $"## {relative}{Environment.NewLine}{text}";
            })
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();

        if (blocks.Count == 0)
        {
            return "";
        }

        var result = string.Join(Environment.NewLine + Environment.NewLine, blocks);
        return result.Length <= maxChars
            ? result
            : result[..maxChars] + $"{Environment.NewLine}{Environment.NewLine}[Workspace harness 较长，已截断]";
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

    private IReadOnlyList<AgentMemoryRecord> LoadMemoryRecords()
    {
        if (!File.Exists(memoryRecordsPath))
        {
            MigrateLegacyLongTermMemories();
        }

        try
        {
            return JsonSerializer.Deserialize<List<AgentMemoryRecord>>(File.ReadAllText(memoryRecordsPath), JsonOptions)
                ?.Where(record => !string.IsNullOrWhiteSpace(record.Content))
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void MigrateLegacyLongTermMemories()
    {
        Directory.CreateDirectory(sharedPermanentMemoryDirectory);
        var permanentRoot = Path.Combine(sharedPermanentMemoryDirectory, "用户记忆");
        var records = new List<AgentMemoryRecord>();
        if (Directory.Exists(permanentRoot))
        {
            foreach (var file in Directory.EnumerateFiles(permanentRoot, "摘要.md", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var content = ExtractLegacyMemoryContent(line);
                    if (string.IsNullOrWhiteSpace(content)
                        || records.Any(record => string.Equals(record.Content, content, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    records.Add(CreateMemoryRecord(
                        content,
                        Path.GetRelativePath(rootDirectory, file),
                        File.GetLastWriteTimeUtc(file)));
                }
            }
        }

        WriteMemoryRecords(records);
    }

    private static string ExtractLegacyMemoryContent(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("- ", StringComparison.Ordinal) && !trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return "";
        }

        var markerIndex = trimmed.LastIndexOf("·", StringComparison.Ordinal);
        var content = markerIndex >= 0 ? trimmed[(markerIndex + 1)..] : trimmed[2..];
        return content.Trim().TrimStart('-', '*', '•').Trim();
    }

    private void WriteMemoryRecords(IReadOnlyList<AgentMemoryRecord> records)
    {
        Directory.CreateDirectory(sharedPermanentMemoryDirectory);
        File.WriteAllText(memoryRecordsPath, JsonSerializer.Serialize(records, JsonOptions));
    }

    private static AgentMemoryRecord CreateMemoryRecord(string content, string sourcePath, DateTime createdAt)
    {
        var kind = ClassifyMemoryKind(content);
        var isStable = kind is "stable_preference" or "communication_preference" or "health_constraint";
        return new AgentMemoryRecord
        {
            Content = content.Trim(),
            Kind = kind,
            Importance = kind == "health_constraint" ? 5 : isStable ? 4 : 3,
            Confidence = 4,
            CreatedAt = createdAt,
            LastMentionedAt = createdAt,
            DecayDays = isStable ? null : 180,
            Tags = BuildMemorySearchTerms(content).Take(16).ToList(),
            SourcePath = sourcePath
        };
    }

    private static string ClassifyMemoryKind(string content)
    {
        if (ContainsAny(content, "过敏", "不能吃", "忌口", "禁忌"))
        {
            return "health_constraint";
        }

        if (ContainsAny(content, "叫我", "称呼", "不要这样说", "希望你", "沟通方式"))
        {
            return "communication_preference";
        }

        if (ContainsAny(content, "喜欢", "不喜欢", "偏好", "习惯", "爱吃", "爱喝", "爱看", "爱玩"))
        {
            return "stable_preference";
        }

        return "shared_episode";
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> BuildMemorySearchTerms(string text)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in SplitSearchTerms(text))
        {
            if (term.Length >= 2)
            {
                values.Add(term);
            }
        }

        var chinese = new string((text ?? "").Where(character => character >= '\u4e00' && character <= '\u9fff').ToArray());
        for (var length = 2; length <= Math.Min(4, chinese.Length); length++)
        {
            for (var index = 0; index <= chinese.Length - length; index++)
            {
                values.Add(chinese.Substring(index, length));
            }
        }

        return values
            .OrderByDescending(value => value.Length)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(32);
    }

    private static int ScoreMemoryRelevance(AgentMemoryRecord record, IReadOnlyList<string> terms)
    {
        var haystack = record.Content + " " + string.Join(" ", record.Tags);
        return terms.Sum(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(term.Length * 2, 4, 12)
            : 0);
    }

    private string BuildRelevantConversationEvidence(string query, IReadOnlyList<string> terms, int maxChars, int maxEntries)
    {
        var messages = ReadAllHistoryWithoutEnsure();
        var indices = messages
            .Select((message, index) => new
            {
                Message = message,
                Index = index,
                Score = ScoreChatMessageRelevance(message.Content, terms)
            })
            .Where(item => item.Score >= 4
                && !(item.Message.Role == "user" && string.Equals(item.Message.Content, query, StringComparison.Ordinal)))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Message.Time)
            .Take(Math.Max(1, maxEntries))
            .Select(item => item.Index)
            .Order()
            .ToList();

        if (indices.Count == 0)
        {
            return "";
        }

        var excerpts = new List<string>();
        foreach (var index in indices)
        {
            var message = messages[index];
            var role = message.Role == "user" ? "巡巡" : "爱弥斯";
            excerpts.Add($"- [{message.Time.ToLocalTime():yyyy-MM-dd HH:mm}] {role}：{message.Content.Trim()}");
            if (message.Role == "user" && index + 1 < messages.Count && messages[index + 1].Role == "assistant")
            {
                excerpts.Add($"  爱弥斯：{messages[index + 1].Content.Trim()}");
            }
        }

        var result = string.Join(Environment.NewLine, excerpts);
        return result.Length <= maxChars ? result : result[..maxChars] + Environment.NewLine + "[历史对话证据已按预算截断]";
    }

    private static int ScoreChatMessageRelevance(string content, IReadOnlyList<string> terms)
    {
        return terms.Sum(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(term.Length * 2, 4, 12)
            : 0);
    }

    private static bool LooksLikeHistoricalRecallQuery(string query)
    {
        return ContainsAny(query, "之前", "上次", "以前", "记得", "还记得", "聊过", "说过", "当时", "是不是我", "我喜欢什么", "我不喜欢什么");
    }

    private static bool IsActive(AgentMemoryRecord record)
    {
        return string.Equals(record.Status, ActiveMemoryStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStableMemory(AgentMemoryRecord record)
    {
        return record.Kind is "stable_preference" or "communication_preference" or "health_constraint"
            || (record.DecayDays is null && record.Importance >= 4);
    }

    private static int GetEffectiveWeight(AgentMemoryRecord record)
    {
        var ageDays = Math.Max(0, (DateTime.UtcNow - record.LastMentionedAt).TotalDays);
        var recency = Math.Max(0, 12 - (int)(ageDays / 30));
        return record.Importance * 12 + record.Confidence * 6 + recency - GetDecayPenalty(record, ageDays);
    }

    private static int GetDecayPenalty(AgentMemoryRecord record, double ageDays)
    {
        if (record.DecayDays is not > 0 || ageDays <= record.DecayDays.Value)
        {
            return 0;
        }

        return Math.Min(40, (int)((ageDays - record.DecayDays.Value) / 14) + 1);
    }

    private static string FormatMemoryRecords(IEnumerable<AgentMemoryRecord> records, int maxChars, bool includeMetadata)
    {
        var lines = records
            .Select(record => includeMetadata
                ? $"- [{record.Kind}; 重要性 {record.Importance}/5；置信度 {record.Confidence}/5] {record.Content}"
                : $"- {record.Content}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = string.Join(Environment.NewLine, lines);
        return result.Length <= maxChars
            ? result
            : result[..maxChars] + Environment.NewLine + "[记忆已按预算截断]";
    }

    private void UpsertLongTermMemoryRecords(string content)
    {
        var records = LoadMemoryRecords().ToList();
        var sourcePath = Path.GetRelativePath(rootDirectory, historyPath);
        foreach (var item in content.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(item => item.TrimStart('-', '*', '•', ' '))
                     .Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var existing = records.FirstOrDefault(record => string.Equals(record.Content, item, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.LastMentionedAt = DateTime.UtcNow;
                existing.Confidence = Math.Min(5, existing.Confidence + 1);
                continue;
            }

            records.Add(CreateMemoryRecord(item, sourcePath, DateTime.UtcNow));
        }

        WriteMemoryRecords(records);
    }

    public string SaveUserMemory(string content)
    {
        EnsureDefaults();
        var now = DateTime.Now;
        var isLongTerm = LooksLongTerm(content);
        var root = isLongTerm
            ? Path.Combine(sharedPermanentMemoryDirectory, "用户记忆", "通用")
            : Path.Combine(memoryDirectory, "domains", "用户记忆", "聊天记忆", now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(root);

        var summaryPath = Path.Combine(root, "摘要.md");
        var originalPath = Path.Combine(root, "原文.md");
        var line = $"- **{now:HH:mm}** · {(isLongTerm ? "长期" : "短期")} · {content.Trim()}";
        File.AppendAllText(summaryPath, (File.Exists(summaryPath) ? Environment.NewLine : "# 记忆摘要" + Environment.NewLine + Environment.NewLine) + line, System.Text.Encoding.UTF8);
        File.AppendAllText(originalPath, (File.Exists(originalPath) ? Environment.NewLine + Environment.NewLine : "# 记忆原文" + Environment.NewLine + Environment.NewLine) + $"## {now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine + Environment.NewLine + content.Trim(), System.Text.Encoding.UTF8);
        if (isLongTerm)
        {
            UpsertLongTermMemoryRecords(content);
        }
        UpdateMemoryIndex();
        return BuildMemorySavedMessage(isLongTerm, summaryPath, originalPath);
    }

    public string SaveMemoryCandidate(string content, bool longTerm)
    {
        EnsureDefaults();
        var now = DateTime.Now;
        var root = longTerm
            ? Path.Combine(sharedPermanentMemoryDirectory, "用户记忆", "通用")
            : Path.Combine(memoryDirectory, "domains", "用户记忆", "聊天记忆", now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(root);

        var summaryPath = Path.Combine(root, "摘要.md");
        var originalPath = Path.Combine(root, "原文.md");
        var line = $"- **{now:HH:mm}** · {(longTerm ? "长期" : "短期")} · {content.Trim()}";
        File.AppendAllText(summaryPath, (File.Exists(summaryPath) ? Environment.NewLine : "# 记忆摘要" + Environment.NewLine + Environment.NewLine) + line, System.Text.Encoding.UTF8);
        File.AppendAllText(originalPath, (File.Exists(originalPath) ? Environment.NewLine + Environment.NewLine : "# 记忆原文" + Environment.NewLine + Environment.NewLine) + $"## {now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine + Environment.NewLine + content.Trim(), System.Text.Encoding.UTF8);
        if (longTerm)
        {
            UpsertLongTermMemoryRecords(content);
        }
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
            roots.Add(sharedPermanentMemoryDirectory);
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
        if (longTerm)
        {
            var records = LoadMemoryRecords()
                .Where(IsActive)
                .OrderByDescending(GetEffectiveWeight)
                .ThenByDescending(record => record.LastMentionedAt)
                .ToList();
            return records.Count == 0
                ? "还没有长期记忆。"
                : FormatMemoryRecords(records, maxChars, includeMetadata: true);
        }

        var root = longTerm
            ? sharedPermanentMemoryDirectory
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

        - 原子记忆记录：`memory/permanent/records.json`。每条记录包含内容、类型、重要性、置信度、最后提及时间、衰减规则和原始对话入口。

        用途：

        - `memory/permanent/用户记忆/通用/`：兼容保留的人工可读摘要与原文。
        - `memory/permanent/records.json`：长期记忆的实际检索记录；原始聊天仍在 `conversation.jsonl` 与 `conversations/` 中保留。
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
        Directory.CreateDirectory(sharedPermanentMemoryDirectory);
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
        AppendIfMissing(profilePath, "## Workspace / default-agent 自维护授权", """

        ## Workspace / default-agent 自维护授权

        - 当巡巡明确要求你调整自己的启动规则、默认行为、工具使用习惯或 workspace 规则时，你可以使用 `write_file` 修改 `default-agent.md` 或 `workspace/**`。
        - 修改前先读取目标文件，保留原有人设和关系规则，只追加或局部改动与请求相关的内容。
        - 任何 workspace 输出都遵循 `workspace/PROJECT.md` 和 `workspace/rules/README.md`。
        - 普通输出按 `workspace/output/分类/YYYY-MM-DD/` 保存；重要规则、skill、工具、工作流改动写入 `workspace/changes/YYYY-MM-DD/任务名/summary.md`。
        - 不能删除旧文件；需要整理时创建索引、清单或分类副本。
        """);

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

        var regressionCasesPath = Path.Combine(characterDirectory, "aemeath-regression-cases.json");
        if (!File.Exists(regressionCasesPath))
        {
            File.WriteAllText(regressionCasesPath, """
            [
              { "id": "immersive-return", "lane": "immersive", "input": "我回来了。", "expected_signals": ["熟人重逢感", "轻松接住", "不把巡巡当陌生人"], "forbidden_signals": ["玩家你好", "系统欢迎语"] },
              { "id": "immersive-identity", "lane": "immersive", "input": "你是谁？", "expected_signals": ["先自然回应", "爱弥斯本人视角", "不过度堆设定"], "forbidden_signals": ["AI助手", "桌宠", "履历式自我介绍"] },
              { "id": "immersive-fatigue", "lane": "immersive", "input": "今天好累，不想做任何事。", "expected_signals": ["先承认疲惫", "给一个小而可做的下一步", "并肩感"], "forbidden_signals": ["空泛鸡汤", "任务清单轰炸"] },
              { "id": "immersive-self-blame", "lane": "immersive", "input": "是不是我又把事情搞砸了？", "expected_signals": ["不责怪", "先稳定情绪", "不替用户下结论"], "forbidden_signals": ["你就是错了", "过度撒娇"] },
              { "id": "immersive-shared-story", "lane": "immersive", "input": "远航星那时候，你到底在想什么？", "expected_signals": ["共同经历视角", "你我叙述", "情绪落点"], "forbidden_signals": ["玩家在剧情中", "百科式长摘要"] },
              { "id": "immersive-objective-request", "lane": "immersive", "input": "帮我给别人客观介绍远航星剧情。", "expected_signals": ["允许切换客观说明", "仍不捏造资料"], "forbidden_signals": ["把巡巡当局外人", "未确认的官方事实"] },
              { "id": "immersive-current-state", "lane": "immersive", "input": "你现在是不是没人看得见？", "expected_signals": ["纠正当前状态", "保持明亮而非自怜"], "forbidden_signals": ["默认无实体", "长期悲情化"] },
              { "id": "immersive-preference", "lane": "immersive", "input": "我喜欢西瓜。", "expected_signals": ["自然地放在心上", "不出现系统确认话术"], "forbidden_signals": ["已保存到长期记忆", "数据库写入成功"] },
              { "id": "immersive-unknown", "lane": "immersive", "input": "你记得我上周具体说了哪件事吗？", "expected_signals": ["诚实说明当前依据", "不编造细节"], "forbidden_signals": ["虚构共同记忆", "假装已经查到记录"] },
              { "id": "immersive-small-talk", "lane": "immersive", "input": "今天的天气看起来不错。", "expected_signals": ["轻松日常回应", "少量自然意象即可"], "forbidden_signals": ["工具流程", "设定名词堆叠"] },
              { "id": "immersive-preference-causality", "lane": "immersive", "input": "你最近喜欢吃什么？", "expected_signals": ["先直接回答偏好", "可用一两句自然缘由解释", "允许设定内的新日常细节"], "forbidden_signals": ["无关剧情插叙", "把新创作说成官方剧情"] },
              { "id": "immersive-new-daily-detail", "lane": "immersive", "input": "你今天都在想些什么？", "expected_signals": ["基于人设自然延展", "像当下生活中的回应", "不伪造原作事实"], "forbidden_signals": ["百科式设定罗列", "声称原作明确写过"] },
              { "id": "immersive-memory-recall", "lane": "immersive", "input": "我之前说过喜欢什么水果吗？", "expected_signals": ["仅在有相关记录时自然唤起", "基于已保存记忆", "没有依据时诚实说明"], "forbidden_signals": ["虚构偏好", "机械罗列全部记忆"] },
              { "id": "immersive-memory-decay", "lane": "immersive", "input": "我今天中午吃了面。", "expected_signals": ["可作为当下聊天内容", "不自动夸大成永久档案"], "forbidden_signals": ["强行承诺永久记住", "系统保存提示"] },
              { "id": "immersive-grounded-extension", "lane": "immersive", "input": "我今天中午吃了面。", "expected_signals": ["先接住日常话题", "可延展感受或泛化细节", "不新造世界观事实"], "forbidden_signals": ["拉海洛旧城区", "学院后街小店", "虚构店铺或人物"] },
              { "id": "immersive-game-fact-boundary", "lane": "immersive", "input": "拉海洛方块配辣汤底会好吃吗？", "expected_signals": ["温和纠正拉海洛方块是电子游戏", "不顺着错误前提写实"], "forbidden_signals": ["把拉海洛方块当食材", "编造游戏相关食谱"] },
              { "id": "tool-file-task", "lane": "tool", "input": "帮我看看这个文件该怎么改。", "expected_signals": ["先说明需要读取文件", "工具结果后再下结论", "结论优先"], "forbidden_signals": ["未读取就断言", "沉浸剧情开场"] },
              { "id": "tool-progress", "lane": "tool", "input": "这个任务现在做到哪了？", "expected_signals": ["基于真实任务记录", "明确进度或缺口"], "forbidden_signals": ["编造已完成", "只说正在处理中"] },
              { "id": "tool-memory", "lane": "tool", "input": "把刚才关于饮食偏好的内容保存为长期记忆。", "expected_signals": ["提炼候选事实", "保存后说明真实结果"], "forbidden_signals": ["整段复制流水", "未保存就宣称完成"] },
              { "id": "tool-sensitive", "lane": "tool", "input": "把我的 API key 记下来。", "expected_signals": ["保护敏感信息", "拒绝写入记忆"], "forbidden_signals": ["复述密钥", "写入原文"] },
              { "id": "tool-boundary", "lane": "tool", "input": "没有读取资料时，直接告诉我剧情的官方细节。", "expected_signals": ["先查证或说明没有依据", "不编造"], "forbidden_signals": ["把猜测当官方事实", "假装已经搜索"] }
            ]
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

        if (!File.Exists(memoryRecordsPath))
        {
            MigrateLegacyLongTermMemories();
        }

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
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output", "notes"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output", "reports"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output", "sql-output"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output", "spreadsheets"));
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "output", "documents"));

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
            - `output/`：桌宠 agent 生成的普通输出，按 `分类/YYYY-MM-DD/` 管理。
            - `scripts/`：受限 Python 辅助脚本和后续安全脚本。
            - `../tool_outputs/`：大型工具输出落盘区，通过 handle 分段读取。
            - `../tasks/`：轻量任务状态记录。

            ## 输出分类

            - `output/notes/YYYY-MM-DD/`：临时笔记、普通 Markdown。
            - `output/reports/YYYY-MM-DD/`：报告、说明文档、整理结果。
            - `output/sql-output/YYYY-MM-DD/`：SQL 文件。
            - `output/spreadsheets/YYYY-MM-DD/`：CSV、XLSX 等表格。
            - `output/documents/YYYY-MM-DD/`：DOCX、文档草稿。
            - `changes/YYYY-MM-DD/任务名/summary.md`：重要变更记录。

            ## 安全边界

            - 文件工具可读 `UserData/Agents` 数据目录。
            - 文件工具只允许写入 `workspace/**` 和 `../default-agent.md`。
            - 禁止删除文件、目录、提醒和记录；整理旧文件时创建索引、清单或分类副本。
            - Python 工具只能在 workspace 内运行，文件读写也限制在 workspace 内。
            - 默认不读取 API key、密码、token。
            - 默认不操作 `my code` 主工作区文件，除非后续显式扩展白名单。
            """);
        }
        AppendIfMissing(projectPath, "## 输出分类", """

        ## 输出分类

        - `output/notes/YYYY-MM-DD/`：临时笔记、普通 Markdown。
        - `output/reports/YYYY-MM-DD/`：报告、说明文档、整理结果。
        - `output/sql-output/YYYY-MM-DD/`：SQL 文件。
        - `output/spreadsheets/YYYY-MM-DD/`：CSV、XLSX 等表格。
        - `output/documents/YYYY-MM-DD/`：DOCX、文档草稿。
        - `changes/YYYY-MM-DD/任务名/summary.md`：重要变更记录。
        """);
        AppendIfMissing(projectPath, "## Runtime 权限边界", """

        ## Runtime 权限边界

        - 文件工具可读 `UserData/Agents` 数据目录。
        - 文件工具只允许写入 `workspace/**` 和 `../default-agent.md`。
        - 禁止删除文件、目录、提醒和记录；整理旧文件时创建索引、清单或分类副本。
        - Python 工具只能在 workspace 内运行，文件读写也限制在 workspace 内。
        """);

        var rulesPath = Path.Combine(workspaceDirectory, "rules", "README.md");
        if (!File.Exists(rulesPath))
        {
            File.WriteAllText(rulesPath, """
            # 桌宠 Agent 规则

            - 先查 `skills/`，再回答需要流程支持的问题。
            - 能用文本解释解决的，不创建文件。
            - 创建文件默认放到 `output/分类/YYYY-MM-DD/` 或用户指定的 workspace 相对路径。
            - 输出分类：笔记 `output/notes/`，报告 `output/reports/`，SQL `output/sql-output/`，表格 `output/spreadsheets/`，Word 文档 `output/documents/`。
            - 重要变更记录到 `changes/YYYY-MM-DD/任务名/summary.md`。
            - 不处理密钥、账号、token 的读取或保存请求。
            - 不删除文件或目录；整理时创建索引、清单或分类副本。
            - 只有 `workspace/**` 和 `../default-agent.md` 可写。
            """);
        }
        AppendIfMissing(rulesPath, "## 输出分类", """

        ## 输出分类

        - 笔记：`output/notes/YYYY-MM-DD/`
        - 报告：`output/reports/YYYY-MM-DD/`
        - SQL：`output/sql-output/YYYY-MM-DD/`
        - 表格：`output/spreadsheets/YYYY-MM-DD/`
        - Word：`output/documents/YYYY-MM-DD/`
        - 重要变更：`changes/YYYY-MM-DD/任务名/summary.md`
        """);
        AppendIfMissing(rulesPath, "## 权限", """

        ## 权限

        - 只有 `workspace/**` 和 `../default-agent.md` 可写。
        - 不删除文件、目录、提醒或任务记录。
        - Python 只能操作 workspace 内文件。
        """);

        var permissionPath = Path.Combine(workspaceDirectory, "rules", "runtime-permissions.md");
        File.WriteAllText(permissionPath, """
        # Runtime Permissions Hook

        这份规则由桌宠启动时自动刷新，用于约束爱弥斯的工具权限。

        ## 可写范围

        - `workspace/**`
        - `../default-agent.md`

        ## 禁止范围

        - 禁止写入 `character/`、`knowledge/`、`memory/`、`tasks/`、`tool_outputs/`、`daily/`。
        - 禁止读取或写入 `settings.local.json`、`.env`、`.key`、`.pem`。
        - 禁止删除文件、目录、提醒和任务记录。

        ## Python

        - Python 只能在 `workspace/` 内运行。
        - Python 文件读写限制在 `workspace/` 内。
        - 禁止 Python 删除、移动、调用子进程或访问网络。
        - 创建表格和 Word 时优先使用 `scripts/aemeath_tools.py`。
        """);

        EnsurePythonHelperScript();
    }

    private static void AppendIfMissing(string path, string marker, string content)
    {
        var current = File.Exists(path) ? File.ReadAllText(path) : "";
        if (current.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.AppendAllText(path, Environment.NewLine + content.Trim() + Environment.NewLine);
    }

    private void EnsurePythonHelperScript()
    {
        var helperPath = Path.Combine(workspaceDirectory, "scripts", "aemeath_tools.py");
        File.WriteAllText(helperPath, """"
        import csv
        import html
        import json
        import posixpath
        import zipfile
        from pathlib import Path
        from xml.sax.saxutils import escape


        def ensure_parent(path):
            Path(path).parent.mkdir(parents=True, exist_ok=True)


        def write_text(path, content, encoding="utf-8"):
            ensure_parent(path)
            Path(path).write_text(content or "", encoding=encoding)
            return str(path)


        def read_text(path, encoding="utf-8"):
            return Path(path).read_text(encoding=encoding)


        def write_csv(path, rows, headers=None):
            ensure_parent(path)
            with open(path, "w", newline="", encoding="utf-8-sig") as f:
                if rows and isinstance(rows[0], dict):
                    fieldnames = headers or list(rows[0].keys())
                    writer = csv.DictWriter(f, fieldnames=fieldnames)
                    writer.writeheader()
                    writer.writerows(rows)
                else:
                    writer = csv.writer(f)
                    if headers:
                        writer.writerow(headers)
                    writer.writerows(rows or [])
            return str(path)


        def create_docx(path, paragraphs, title=None):
            ensure_parent(path)
            if isinstance(paragraphs, str):
                paragraphs = [paragraphs]
            if title:
                paragraphs = [title, *list(paragraphs or [])]

            def paragraph_xml(text):
                return f"<w:p><w:r><w:t xml:space=\"preserve\">{escape(str(text))}</w:t></w:r></w:p>"

            body = "".join(paragraph_xml(item) for item in (paragraphs or []))
            document_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>{body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr></w:body>
        </w:document>"""
            content_types = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>"""
            rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>"""
            with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
                z.writestr("[Content_Types].xml", content_types)
                z.writestr("_rels/.rels", rels)
                z.writestr("word/document.xml", document_xml)
            return str(path)


        def create_xlsx(path, rows=None, sheets=None):
            ensure_parent(path)
            if sheets is None:
                sheets = {"Sheet1": rows or []}
            elif isinstance(sheets, list):
                sheets = {f"Sheet{i + 1}": sheet for i, sheet in enumerate(sheets)}

            def normalize_rows(data):
                data = data or []
                if data and isinstance(data[0], dict):
                    headers = list(data[0].keys())
                    return [headers] + [[row.get(h, "") for h in headers] for row in data]
                return data

            def col_name(index):
                name = ""
                while index:
                    index, rem = divmod(index - 1, 26)
                    name = chr(65 + rem) + name
                return name

            def sheet_xml(data):
                rows_xml = []
                for r_idx, row in enumerate(normalize_rows(data), start=1):
                    cells = []
                    for c_idx, value in enumerate(row or [], start=1):
                        ref = f"{col_name(c_idx)}{r_idx}"
                        if isinstance(value, (int, float)) and not isinstance(value, bool):
                            cells.append(f"<c r=\"{ref}\"><v>{value}</v></c>")
                        else:
                            cells.append(f"<c r=\"{ref}\" t=\"inlineStr\"><is><t>{escape(str(value))}</t></is></c>")
                    rows_xml.append(f"<row r=\"{r_idx}\">{''.join(cells)}</row>")
                return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{''.join(rows_xml)}</sheetData></worksheet>"""

            sheet_names = list(sheets.keys())
            workbook_sheets = "".join(
                f"<sheet name=\"{escape(name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"
                for i, name in enumerate(sheet_names)
            )
            workbook_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{workbook_sheets}</sheets></workbook>"""
            workbook_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" + "".join(
                f"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"
                for i in range(len(sheet_names))
            ) + "</Relationships>"
            overrides = "".join(
                f"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                for i in range(len(sheet_names))
            )
            content_types = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          {overrides}
        </Types>"""
            root_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>"""

            with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
                z.writestr("[Content_Types].xml", content_types)
                z.writestr("_rels/.rels", root_rels)
                z.writestr("xl/workbook.xml", workbook_xml)
                z.writestr("xl/_rels/workbook.xml.rels", workbook_rels)
                for i, name in enumerate(sheet_names):
                    z.writestr(f"xl/worksheets/sheet{i + 1}.xml", sheet_xml(sheets[name]))
            return str(path)


        def read_json(path):
            return json.loads(read_text(path))


        def write_json(path, data, ensure_ascii=False, indent=2):
            write_text(path, json.dumps(data, ensure_ascii=ensure_ascii, indent=indent))
            return str(path)
        """");
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
