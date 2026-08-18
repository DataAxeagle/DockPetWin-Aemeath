using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DockPetWin.Core.Agents;

public sealed class AgentChatClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> SendAsync(
        AgentChatSettings settings,
        string systemPrompt,
        IReadOnlyList<AgentChatMessage> history,
        string userMessage,
        CancellationToken cancellationToken)
    {
        return await SendRawAsync(settings, systemPrompt, history, userMessage, cancellationToken);
    }

    public async Task<string> SendWithToolsAsync(
        AgentChatSettings settings,
        string systemPrompt,
        IReadOnlyList<AgentChatMessage> history,
        string userMessage,
        AgentStore store,
        CancellationToken cancellationToken,
        Action<AgentToolTrace>? onToolTrace = null,
        IEnumerable<string>? allowedTools = null)
    {
        if (!settings.EnableTools)
        {
            return await SendRawAsync(settings, systemPrompt, history, userMessage, cancellationToken);
        }

        var runner = new AgentToolRunner(store, allowedTools);
        if (allowedTools is null)
        {
            var pendingMemorySaveReply = TryHandlePendingMemorySave(userMessage, history, runner, onToolTrace);
            if (pendingMemorySaveReply is not null)
            {
                return pendingMemorySaveReply;
            }
        }

        var workingHistory = history.ToList();
        var loopMessages = history.Select(LoopMessage.FromAgentMessage).ToList();
        loopMessages.Add(LoopMessage.User(userMessage));
        var configuredMaxRounds = Math.Clamp(settings.MaxToolRounds, 1, 12);
        var skillMaxRounds = Math.Clamp(settings.MaxToolRounds + 4, 1, 16);
        var maxRounds = configuredMaxRounds;
        var toolTrace = new List<string>();
        var toolCallCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var routedSkillResult = allowedTools is null ? runner.TryAutoLoadSkillForMessage(userMessage) : null;
        if (routedSkillResult is not null)
        {
            maxRounds = skillMaxRounds;
            var toolResult = AgentToolRunner.FormatResult(routedSkillResult);
            toolTrace.Add($"## Skill Runtime: read_skill\n\n参数：auto_route\n\n结果：\n{toolResult}");
            onToolTrace?.Invoke(new AgentToolTrace
            {
                Round = 0,
                Tool = "read_skill",
                ArgumentsJson = "{\"source\":\"auto_route\"}",
                Ok = routedSkillResult.Ok,
                Summary = routedSkillResult.Summary,
                Preview = TrimTracePreview(routedSkillResult.Content),
                Handle = routedSkillResult.Handle,
                ErrorCode = routedSkillResult.ErrorCode
            });
            loopMessages.Add(LoopMessage.User("Skill runtime 已根据用户请求预加载 skill。以下内容是本轮必须遵循的 skill 上下文和运行状态：\n\n"
                + toolResult
                + "\n\n"
                + runner.BuildActiveSkillRuntimePrompt()));
        }

        for (var round = 0; round < maxRounds; round++)
        {
            var modelReply = await SendRawWithNativeToolsAsync(settings, systemPrompt, loopMessages, runner, cancellationToken);
            var reply = modelReply.Content.Trim();
            if (modelReply.ToolCalls.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(reply) && toolTrace.Count > 0)
                {
                    return await FinalizeToolAnswerAsync(
                        settings,
                        systemPrompt,
                        loopMessages.Select(item => new AgentChatMessage { Role = item.Role == "tool" ? "user" : item.Role, Content = item.Content ?? "", Time = DateTime.UtcNow }).ToList(),
                        userMessage,
                        toolTrace,
                        "模型在工具调用后返回了空回复。请基于已有工具结果直接给用户一个自然语言答复。",
                        cancellationToken);
                }

                if (AgentToolRunner.TryParseToolCall(reply, out var textCall))
                {
                    modelReply.ToolCalls.Add(new ModelToolCall
                    {
                        Id = $"text_tool_{round + 1}",
                        Name = textCall.Tool,
                        Arguments = textCall.Arguments
                    });
                }
                else if (LooksLikeUnexecutedToolNarration(reply, userMessage) && round < maxRounds - 1)
                {
                    loopMessages.Add(LoopMessage.Assistant(reply));
                    loopMessages.Add(LoopMessage.User(BuildToolNudge(reply)));
                    continue;
                }
            }

            if (modelReply.ToolCalls.Count == 0)
            {
                if (runner.TryBuildSkillComplianceNudge(out var skillNudge))
                {
                    if (round < maxRounds - 1)
                    {
                        loopMessages.Add(LoopMessage.Assistant(reply));
                        loopMessages.Add(LoopMessage.User(skillNudge));
                        continue;
                    }

                    return await FinalizeToolAnswerAsync(
                        settings,
                        systemPrompt,
                        loopMessages.Select(item => new AgentChatMessage { Role = item.Role == "tool" ? "user" : item.Role, Content = item.Content ?? "", Time = DateTime.UtcNow }).ToList(),
                        userMessage,
                        toolTrace,
                        "Skill runtime gate 尚未完成，不能假装已经按 skill 流程产出最终内容。请说明当前卡在什么 gate、已经读取了什么、还缺哪些文件或步骤，并让用户继续触发下一轮。",
                        cancellationToken);
                }

                return reply;
            }

            loopMessages.Add(LoopMessage.AssistantToolCalls(reply, modelReply.ToolCalls));
            var blockedAny = false;
            foreach (var toolCall in modelReply.ToolCalls)
            {
                var call = new AgentToolCall
                {
                    Tool = toolCall.Name,
                    Arguments = toolCall.Arguments
                };
                var signature = $"{call.Tool}:{CanonicalizeArguments(call.Arguments)}";
                toolCallCounts.TryGetValue(signature, out var seen);
                seen++;
                toolCallCounts[signature] = seen;

                AgentToolResult structuredResult;
                if (seen >= 3)
                {
                    blockedAny = true;
                    structuredResult = AgentToolResult.Error(
                        call.Tool,
                        "identical_tool_call",
                        $"这次完全相同的工具调用本轮已经出现 {seen} 次，已拦截。请更换参数、换工具，或基于已有结果回复用户。");
                }
                else
                {
                    try
                    {
                        structuredResult = runner.Run(call);
                    }
                    catch (Exception ex)
                    {
                        structuredResult = AgentToolResult.Error(call.Tool, "tool_exception", ex.Message);
                    }
                }

                var toolResult = AgentToolRunner.FormatResult(structuredResult);
                toolTrace.Add($"## 工具 {round + 1}: {call.Tool}\n\n参数：{JsonSerializer.Serialize(call.Arguments, JsonOptions)}\n\n结果：\n{toolResult}");
                onToolTrace?.Invoke(new AgentToolTrace
                {
                    Round = round + 1,
                    Tool = call.Tool,
                    ArgumentsJson = JsonSerializer.Serialize(call.Arguments, JsonOptions),
                    Ok = structuredResult.Ok,
                    Summary = structuredResult.Summary,
                    Preview = TrimTracePreview(structuredResult.Content),
                    Handle = structuredResult.Handle,
                    ErrorCode = structuredResult.ErrorCode
                });
                loopMessages.Add(LoopMessage.Tool(toolCall.Id, toolResult));
            }

            if (runner.HasActiveSkill)
            {
                maxRounds = Math.Max(maxRounds, skillMaxRounds);
            }

            if (round >= maxRounds - 2)
            {
                var gateWarning = runner.TryBuildSkillComplianceNudge(out var skillNudge)
                    ? $"{Environment.NewLine}{Environment.NewLine}但当前 skill gate 尚未完成，优先补齐 gate，不要跳过 skill 流程：{Environment.NewLine}{skillNudge}"
                    : "";
                var nearLimitPrompt = string.IsNullOrWhiteSpace(gateWarning)
                    ? "工具轮数快到上限。除非绝对必要，不要再调用工具；请基于已有工具结果给用户最终答复。"
                    : "工具轮数快到上限。当前仍有 skill gate 未完成时，必须优先补齐 gate，不要直接给最终答复。";
                loopMessages.Add(LoopMessage.User(nearLimitPrompt + gateWarning));
            }
            else if (blockedAny)
            {
                loopMessages.Add(LoopMessage.User("有重复工具调用已被拦截。请不要继续重复同一调用；更换参数、换工具，或直接给用户当前最好的答复。"));
            }
            else
            {
                var activeSkillPrompt = runner.BuildActiveSkillRuntimePrompt();
                var continuePrompt = "继续。若已有足够信息，请直接回复用户；只有缺关键事实时才继续调用工具。";
                loopMessages.Add(LoopMessage.User(string.IsNullOrWhiteSpace(activeSkillPrompt)
                    ? continuePrompt
                    : activeSkillPrompt + Environment.NewLine + Environment.NewLine + continuePrompt));
            }
        }

        var finalInstruction = runner.TryBuildSkillComplianceNudge(out var finalSkillNudge)
            ? "Skill runtime gate 尚未完成，不能假装已经按 skill 流程产出最终内容。请说明当前卡在什么 gate、已经读取了什么、还缺哪些文件或步骤，并让用户继续触发下一轮。" + Environment.NewLine + finalSkillNudge
            : "工具轮数已经用完。不要再调用工具，必须基于已有工具结果给用户一个当前最好的答复。";

        return await FinalizeToolAnswerAsync(
            settings,
            systemPrompt,
            loopMessages.Select(item => new AgentChatMessage { Role = item.Role == "tool" ? "user" : item.Role, Content = item.Content ?? "", Time = DateTime.UtcNow }).ToList(),
            userMessage,
            toolTrace,
            finalInstruction,
            cancellationToken);
    }

    private static string TrimTracePreview(string value)
    {
        var text = (value ?? "").Trim();
        return text.Length <= 800 ? text : text[..800] + "\n...";
    }

    private static string? TryHandleDirectToolIntent(string userMessage, AgentToolRunner runner)
    {
        var text = userMessage.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (IsToolListIntent(text))
        {
            var result = runner.Run(new AgentToolCall { Tool = "tool_specs" });
            return BuildToolSpecsReply(result);
        }

        if (IsSkillListIntent(text) && IsExplicitDirectToolShortcut(text))
        {
            var result = runner.Run(new AgentToolCall
            {
                Tool = "list_skills",
                Arguments = ExtractSkillQuery(text) is { Length: > 0 } query
                    ? new Dictionary<string, string> { ["query"] = query }
                    : []
            });
            return BuildSkillListReply(result);
        }

        if (IsSkillReadIntent(text) && IsExplicitDirectToolShortcut(text))
        {
            var skillName = ExtractSkillName(text);
            if (string.IsNullOrWhiteSpace(skillName))
            {
                var list = runner.Run(new AgentToolCall { Tool = "list_skills" });
                return "我需要先确定你要读哪个 skill。当前可用 skill 是：\n\n" + ExtractReadableContent(list);
            }

            var result = runner.Run(new AgentToolCall
            {
                Tool = "read_skill",
                Arguments = new Dictionary<string, string> { ["name"] = skillName }
            });
            return BuildSkillReadReply(skillName, result, text);
        }

        if (IsMemoryListIntent(text))
        {
            var result = runner.Run(new AgentToolCall
            {
                Tool = "list_memories",
                Arguments = new Dictionary<string, string> { ["type"] = ExtractMemoryType(text) }
            });
            return BuildMemoryListReply(result);
        }

        if (IsMemoryReadIntent(text))
        {
            var result = runner.Run(new AgentToolCall
            {
                Tool = "read_memory",
                Arguments = new Dictionary<string, string> { ["type"] = ExtractMemoryType(text) }
            });
            return BuildMemoryReadReply(ExtractMemoryType(text), result);
        }

        if (IsFileReadIntent(text) && IsExplicitDirectToolShortcut(text))
        {
            var fileQuery = ExtractFileQuery(text);
            if (string.IsNullOrWhiteSpace(fileQuery))
            {
                return "我需要知道你要读哪个文件。你可以直接说：读取 `workspace/output/文件名.md`。";
            }

            var result = runner.Run(new AgentToolCall
            {
                Tool = "read_file",
                Arguments = new Dictionary<string, string> { ["path"] = fileQuery }
            });
            return BuildFileReadReply(fileQuery, result);
        }

        if (IsFileFindIntent(text) && IsExplicitDirectToolShortcut(text))
        {
            var fileQuery = ExtractFileQuery(text);
            if (string.IsNullOrWhiteSpace(fileQuery))
            {
                return "我需要知道你要找什么文件名或关键词。你可以直接说：查找 `记忆备份`。";
            }

            var result = runner.Run(new AgentToolCall
            {
                Tool = "find_files",
                Arguments = new Dictionary<string, string> { ["query"] = fileQuery }
            });
            return BuildFileFindReply(fileQuery, result);
        }

        return null;
    }

    private static bool IsToolListIntent(string text)
    {
        return ContainsAny(text, "工具列表", "查看工具", "有哪些工具", "有什么工具", "tool list", "tool_specs");
    }

    private static bool IsSkillListIntent(string text)
    {
        return text.Contains("skill", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "有什么", "有哪些", "查看", "列出", "搜索", "查一下", "看一下")
            && !IsSkillReadIntent(text);
    }

    private static bool IsSkillReadIntent(string text)
    {
        return text.Contains("skill", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "读取", "读一下", "读一", "调用", "使用", "执行", "看看", "看一下", "激活");
    }

    private static bool IsFileReadIntent(string text)
    {
        return LooksLikeFileRequest(text)
            && ContainsAny(text, "读取", "读一下", "读一", "打开", "查看", "看一下", "看下")
            && !text.Contains("skill", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileFindIntent(string text)
    {
        return LooksLikeFileRequest(text)
            && ContainsAny(text, "找", "查找", "搜索", "有没有", "在哪", "路径", "列出")
            && !text.Contains("skill", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFileRequest(string text)
    {
        return text.Contains("文件", StringComparison.OrdinalIgnoreCase)
            || text.Contains("目录", StringComparison.OrdinalIgnoreCase)
            || text.Contains("workspace", StringComparison.OrdinalIgnoreCase)
            || text.Contains("output", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".md", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".txt", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".csv", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitDirectToolShortcut(string text)
    {
        return ContainsAny(text, "直接调用工具", "直接用工具", "用工具直接", "强制调用工具", "跳过AI", "跳过模型")
            || (ContainsAny(text, "工具") && ContainsAny(text, "直接", "调用", "执行"));
    }

    private static bool LooksLikeUnexecutedToolNarration(string reply, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        if (AgentToolRunner.TryParseToolCall(reply, out _))
        {
            return false;
        }

        var replyLooksLikeProgressOnly = LooksLikeProgressOnlyToolNarration(reply);
        var userMayNeedTool = LooksLikeFileRequest(userMessage)
            || userMessage.Contains("skill", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("记忆", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("搜索", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("查找", StringComparison.OrdinalIgnoreCase);

        return replyLooksLikeProgressOnly
            || (userMayNeedTool
            && ContainsAny(
                reply,
                "正在查找",
                "正在搜索",
                "正在列出",
                "正在查看",
                "我来查找",
                "我去查找",
                "我会查找",
                "让我查找",
                "我来列出",
                "我会列出",
                "正在读取",
                "我来读取",
                "我会读取",
                "准备调用",
                "需要调用工具"));
    }

    private static bool LooksLikeProgressOnlyToolNarration(string reply)
    {
        var normalized = reply.Trim();
        if (normalized.Length > 260)
        {
            return false;
        }

        var hasProgressPhrase = ContainsAny(
            normalized,
            "正在读取",
            "正在查找",
            "正在搜索",
            "正在列出",
            "正在查看",
            "正在调用",
            "先读一下",
            "先读取",
            "我先读",
            "我先查",
            "我先搜",
            "我来读",
            "我来查",
            "我去读",
            "我去查",
            "按步骤执行",
            "稍等",
            "等我");

        if (!hasProgressPhrase)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "skill",
            "SKILL.md",
            "工具",
            "文件",
            "目录",
            "记忆",
            "搜索",
            "查找",
            "读取",
            "列出",
            "调用");
    }

    private static string BuildToolNudge(string reply)
    {
        var lower = reply.ToLowerInvariant();
        var suggested = lower.Contains("skill") || lower.Contains("skill.md")
            ? "如果不知道 skill 名称，先调用 `list_skills`；确认后再调用 `read_skill`。"
            : "如果确实需要外部信息，必须调用一个可用工具。";

        return "你刚才只是说要使用工具，但没有输出工具调用，所以用户看起来会卡在“正在做”。"
            + suggested
            + " 如果不需要工具，就直接给最终答复。不要再说“正在查找/正在列出/正在读取/稍等”。";
    }

    private static bool IsMemoryListIntent(string text)
    {
        return ContainsAny(text, "记忆")
            && ContainsAny(text, "在哪", "保存在哪", "存在哪", "路径", "目录", "文件", "列出", "有哪些", "有什么", "查看列表");
    }

    private static bool IsMemoryReadIntent(string text)
    {
        return ContainsAny(text, "记忆")
            && ContainsAny(text, "读取", "读一下", "读一", "查看", "看一下", "看下", "总结", "摘要", "大纲")
            && !IsMemoryListIntent(text);
    }

    private static string? TryHandlePendingMemorySave(
        string userMessage,
        IReadOnlyList<AgentChatMessage> history,
        AgentToolRunner runner,
        Action<AgentToolTrace>? onToolTrace)
    {
        var type = ExtractExplicitMemoryChoice(userMessage);
        if (type == "all")
        {
            return null;
        }

        var prompt = FindLastPendingMemoryPrompt(history, userMessage);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var content = ExtractPendingMemoryContent(prompt);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = type,
            ["content"] = content
        };
        var result = runner.Run(new AgentToolCall
        {
            Tool = "save_memory",
            Arguments = args
        });

        onToolTrace?.Invoke(new AgentToolTrace
        {
            Round = 1,
            Tool = "save_memory",
            ArgumentsJson = JsonSerializer.Serialize(args, JsonOptions),
            Ok = result.Ok,
            Summary = result.Summary,
            Preview = TrimTracePreview(result.Content),
            Handle = result.Handle,
            ErrorCode = result.ErrorCode
        });

        if (!result.Ok)
        {
            return $"我刚刚尝试真实写入记忆，但工具返回失败：{result.ErrorMessage ?? result.Content}";
        }

        var label = type == "long" ? "长期" : "短期";
        return $"已真实保存到{label}记忆。\n\n{ExtractReadableContent(result)}";
    }

    private static string ExtractExplicitMemoryChoice(string text)
    {
        var normalized = text.Trim().Trim('。', '.', '！', '!', '，', ',', ' ');
        if (normalized.Equals("长期", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("长期记忆", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("long", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("permanent", StringComparison.OrdinalIgnoreCase)
            || normalized == "1")
        {
            return "long";
        }

        if (normalized.Equals("短期", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("短期记忆", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("short", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("recent", StringComparison.OrdinalIgnoreCase)
            || normalized == "2")
        {
            return "short";
        }

        if (ContainsAny(normalized, "保存到长期", "存长期", "长期保存"))
        {
            return "long";
        }

        if (ContainsAny(normalized, "保存到短期", "存短期", "短期保存"))
        {
            return "short";
        }

        return "all";
    }

    private static string? FindLastPendingMemoryPrompt(IReadOnlyList<AgentChatMessage> history, string userMessage)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];
            if (message.Role == "user" && string.Equals(message.Content.Trim(), userMessage.Trim(), StringComparison.Ordinal))
            {
                continue;
            }

            if (message.Role != "assistant")
            {
                continue;
            }

            var content = message.Content ?? "";
            if (LooksLikeMemoryChoicePrompt(content))
            {
                return content;
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                return null;
            }
        }

        return null;
    }

    private static bool LooksLikeMemoryChoicePrompt(string text)
    {
        return ContainsAny(text, "候选记忆", "要保存为", "保存为**长期记忆**", "保存为长期记忆")
            && text.Contains("长期", StringComparison.OrdinalIgnoreCase)
            && text.Contains("短期", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractPendingMemoryContent(string prompt)
    {
        var text = prompt.Replace("\r\n", "\n").Trim();
        var start = 0;
        foreach (var marker in new[] { "候选记忆内容", "候选记忆", "内容：" })
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                start = index + marker.Length;
                break;
            }
        }

        var candidate = text[start..];
        foreach (var marker in new[] { "\n请问", "\n要保存为", "\n回复", "请问要保存" })
        {
            var index = candidate.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                candidate = candidate[..index];
            }
        }

        var lines = candidate.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.Contains("长期记忆", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("短期记忆", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("回复", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.TrimStart(':', '：', '*', ' '))
            .ToList();
        var cleaned = string.Join(Environment.NewLine, lines).Trim();
        return cleaned.Length <= 3000 ? cleaned : cleaned[..3000] + "\n[内容过长，已截断]";
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractSkillQuery(string text)
    {
        if (text.Contains("记忆", StringComparison.OrdinalIgnoreCase)
            || text.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            return "memory";
        }

        return "";
    }

    private static string ExtractSkillName(string text)
    {
        var known = new[]
        {
            "desktop-memory",
            "project-harness-bootstrap",
            "skill-creator"
        };

        foreach (var name in known)
        {
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        if (text.Contains("记忆", StringComparison.OrdinalIgnoreCase)
            || text.Contains("memory", StringComparison.OrdinalIgnoreCase)
            || text.Contains("memory-saver", StringComparison.OrdinalIgnoreCase))
        {
            return "desktop-memory";
        }

        if (text.Contains("harness", StringComparison.OrdinalIgnoreCase)
            || text.Contains("项目骨架", StringComparison.OrdinalIgnoreCase)
            || text.Contains("项目架构", StringComparison.OrdinalIgnoreCase))
        {
            return "project-harness-bootstrap";
        }

        if (text.Contains("创建skill", StringComparison.OrdinalIgnoreCase)
            || text.Contains("skill creator", StringComparison.OrdinalIgnoreCase)
            || text.Contains("skill-creator", StringComparison.OrdinalIgnoreCase))
        {
            return "skill-creator";
        }

        var markerIndex = text.IndexOf("skill", StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            return "";
        }

        var before = text[..markerIndex]
            .Split([' ', '`', '，', '。', '、', ':', '：', '"', '\'', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return before?.Trim() ?? "";
    }

    private static string ExtractMemoryType(string text)
    {
        if (text.Contains("长期", StringComparison.OrdinalIgnoreCase)
            || text.Contains("大纲", StringComparison.OrdinalIgnoreCase)
            || text.Contains("启动", StringComparison.OrdinalIgnoreCase)
            || text.Contains("permanent", StringComparison.OrdinalIgnoreCase)
            || text.Contains("long", StringComparison.OrdinalIgnoreCase))
        {
            return "long";
        }

        if (text.Contains("短期", StringComparison.OrdinalIgnoreCase)
            || text.Contains("最近", StringComparison.OrdinalIgnoreCase)
            || text.Contains("近期", StringComparison.OrdinalIgnoreCase)
            || text.Contains("short", StringComparison.OrdinalIgnoreCase))
        {
            return "short";
        }

        return "all";
    }

    private static string ExtractFileQuery(string text)
    {
        var quoted = ExtractQuotedToken(text);
        if (!string.IsNullOrWhiteSpace(quoted))
        {
            return quoted;
        }

        var knownExtensions = new[] { ".md", ".txt", ".csv", ".json", ".log" };
        foreach (var extension in knownExtensions)
        {
            var index = text.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index;
                while (start > 0 && !IsFileTokenBoundary(text[start - 1]))
                {
                    start--;
                }

                var end = index + extension.Length;
                while (end < text.Length && !IsFileTokenBoundary(text[end]))
                {
                    end++;
                }

                return text[start..end].Trim().Trim('"', '`', '“', '”', '。', '，');
            }
        }

        var cleaned = text
            .Replace("帮我", "", StringComparison.OrdinalIgnoreCase)
            .Replace("你", "", StringComparison.OrdinalIgnoreCase)
            .Replace("去", "", StringComparison.OrdinalIgnoreCase)
            .Replace("一下", "", StringComparison.OrdinalIgnoreCase)
            .Replace("读取", "", StringComparison.OrdinalIgnoreCase)
            .Replace("读", "", StringComparison.OrdinalIgnoreCase)
            .Replace("打开", "", StringComparison.OrdinalIgnoreCase)
            .Replace("查看", "", StringComparison.OrdinalIgnoreCase)
            .Replace("看", "", StringComparison.OrdinalIgnoreCase)
            .Replace("查找", "", StringComparison.OrdinalIgnoreCase)
            .Replace("搜索", "", StringComparison.OrdinalIgnoreCase)
            .Replace("找", "", StringComparison.OrdinalIgnoreCase)
            .Replace("有没有", "", StringComparison.OrdinalIgnoreCase)
            .Replace("文件", "", StringComparison.OrdinalIgnoreCase)
            .Replace("目录", "", StringComparison.OrdinalIgnoreCase)
            .Replace("内容", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return cleaned.Trim('`', '"', '“', '”', '。', '，', '：', ':');
    }

    private static string ExtractQuotedToken(string text)
    {
        foreach (var pair in new[] { ('`', '`'), ('"', '"'), ('“', '”') })
        {
            var start = text.IndexOf(pair.Item1);
            if (start < 0)
            {
                continue;
            }

            var end = text.IndexOf(pair.Item2, start + 1);
            if (end > start + 1)
            {
                return text[(start + 1)..end].Trim();
            }
        }

        return "";
    }

    private static bool IsFileTokenBoundary(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch is '`' or '"' or '\'' or '“' or '”' or '，' or '。' or '、' or ':' or '：';
    }

    private static string BuildToolSpecsReply(AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"读取工具列表失败：{result.ErrorMessage}";
        }

        return "当前可用工具如下：\n\n"
            + "- `tool_specs`：查看工具说明和参数。\n"
            + "- `list_skills`：搜索桌宠 workspace 内的 skill。\n"
            + "- `read_skill`：读取并激活某个 skill 的 SKILL.md。\n"
            + "- `list_skill_files` / `find_skill_files` / `read_skill_file`：读取当前激活 skill 自带资料。\n"
            + "- `list_memories`：列出长期/短期记忆文件路径。\n"
            + "- `read_memory`：读取长期/短期记忆摘要。\n"
            + "- `search_knowledge`：搜索桌宠知识库里的游戏设定、角色资料、剧情摘要。\n"
            + "- `read_knowledge`：读取桌宠知识库里的指定资料文件。\n"
            + "- `list_files`：列出 Agents 数据目录内文件。\n"
            + "- `read_file`：读取 Agents 数据目录内文本文件。\n"
            + "- `handle_read`：读取大输出 handle 的片段。\n"
            + "- `write_file`：写入 Agents 数据目录内文本文件。\n"
            + "- `create_task`：记录轻量任务状态。\n\n"
            + "如果你要测试 skill，直接说“读取 desktop-memory 这个 skill”。";
    }

    private static string BuildSkillListReply(AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"读取 skill 列表失败：{result.ErrorMessage}";
        }

        return "我查到当前可用 skill 是：\n\n" + ExtractReadableContent(result);
    }

    private static string BuildSkillReadReply(string requestedName, AgentToolResult result, string originalText)
    {
        if (!result.Ok)
        {
            return $"读取 `{requestedName}` 失败：{result.ErrorMessage}";
        }

        var content = ExtractReadableContent(result);
        var note = ContainsAny(originalText, "只读", "只读取", "别执行", "不要执行", "不执行")
            ? "我只读取说明，没有执行保存、写入或其他动作。"
            : "我已经读取说明；如果这个 skill 需要后续动作，我会按它的流程继续前先说明。";

        return $"已读取 `{requestedName}` 的 SKILL.md。\n\n{note}\n\n{TrimForChat(content, 5000)}";
    }

    private static string BuildFileFindReply(string query, AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"查找 `{query}` 失败：{result.ErrorMessage}";
        }

        return $"查找 `{query}` 的结果：\n\n{ExtractReadableContent(result)}";
    }

    private static string BuildFileReadReply(string query, AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"读取 `{query}` 失败：{result.ErrorMessage}";
        }

        return $"已读取 `{query}`：\n\n{TrimForChat(ExtractReadableContent(result), 6000)}";
    }

    private static string BuildMemoryListReply(AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"读取记忆列表失败：{result.ErrorMessage}";
        }

        return "记忆文件在这里：\n\n" + ExtractReadableContent(result);
    }

    private static string BuildMemoryReadReply(string type, AgentToolResult result)
    {
        if (!result.Ok)
        {
            return $"读取记忆失败：{result.ErrorMessage}";
        }

        var label = type == "long" ? "长期记忆" : type == "short" ? "短期记忆" : "全部记忆";
        return $"已读取{label}摘要：\n\n{TrimForChat(ExtractReadableContent(result), 6000)}";
    }

    private static string ExtractReadableContent(AgentToolResult result)
    {
        var content = string.IsNullOrWhiteSpace(result.Content) ? result.Summary : result.Content;
        if (!string.IsNullOrWhiteSpace(result.Handle))
        {
            content += $"{Environment.NewLine}{Environment.NewLine}[完整内容 handle: {result.Handle}]";
        }

        return content.Trim();
    }

    private static string TrimForChat(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars] + "\n\n[内容较长，后面已省略。需要继续看可以让我读取 handle 或指定段落。]";
    }

    private async Task<string> FinalizeToolAnswerAsync(
        AgentChatSettings settings,
        string systemPrompt,
        IReadOnlyList<AgentChatMessage> workingHistory,
        string originalUserMessage,
        IReadOnlyList<string> toolTrace,
        string instruction,
        CancellationToken cancellationToken)
    {
        var trace = string.Join(Environment.NewLine + Environment.NewLine, toolTrace.TakeLast(8));
        var finalPrompt = $"""
        {instruction}

        用户原始请求：
        {originalUserMessage}

        已执行工具结果：
        {trace}

        回复要求：
        - 用中文。
        - 不要输出工具 JSON。
        - 不要提“工具调用次数达到上限”。
        - 如果信息不足，说明已经查到什么、还缺什么，并给出下一步。
        """;

        var reply = await SendRawAsync(settings, systemPrompt, workingHistory, finalPrompt, cancellationToken);
        if (AgentToolRunner.TryParseToolCall(reply, out _))
        {
            return "我已经查到一部分信息，但还没有形成完整结论。你可以让我继续查，或者把问题缩小一点。";
        }

        return reply;
    }

    private async Task<ModelReply> SendRawWithNativeToolsAsync(
        AgentChatSettings settings,
        string systemPrompt,
        IReadOnlyList<LoopMessage> loopMessages,
        AgentToolRunner runner,
        CancellationToken cancellationToken)
    {
        var apiKey = settings.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"还没有配置 DeepSeek API key。请在 {settings.ApiKeyEnv} 环境变量中设置，或填写 UserData/Agents/settings.local.json 里的 api_key。");
        }

        var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/chat/completions";
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        messages.AddRange(loopMessages.Select(ToApiMessage));

        var payload = new
        {
            model = settings.Model,
            messages,
            tools = runner.GetModelTools(),
            tool_choice = "auto",
            temperature = settings.Temperature,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepSeek API 调用失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
        var message = result?.Choices?.FirstOrDefault()?.Message
            ?? throw new InvalidOperationException("DeepSeek API 没有返回可用回复。");
        var reply = new ModelReply { Content = message.Content ?? "" };
        if (message.ToolCalls is not null)
        {
            foreach (var toolCall in message.ToolCalls)
            {
                var name = toolCall.Function?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Dictionary<string, string> args;
                try
                {
                    args = AgentToolRunner.ParseArgumentJson(toolCall.Function?.Arguments ?? "{}");
                }
                catch
                {
                    args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["_raw_arguments"] = toolCall.Function?.Arguments ?? ""
                    };
                }

                reply.ToolCalls.Add(new ModelToolCall
                {
                    Id = string.IsNullOrWhiteSpace(toolCall.Id) ? $"call_{reply.ToolCalls.Count + 1}" : toolCall.Id,
                    Name = name,
                    Arguments = args
                });
            }
        }

        return reply;
    }

    private static object ToApiMessage(LoopMessage message)
    {
        if (message.Role == "assistant" && message.ToolCalls.Count > 0)
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrWhiteSpace(message.Content) ? null : message.Content,
                tool_calls = message.ToolCalls.Select(toolCall => new
                {
                    id = toolCall.Id,
                    type = "function",
                    function = new
                    {
                        name = toolCall.Name,
                        arguments = JsonSerializer.Serialize(toolCall.Arguments, JsonOptions)
                    }
                }).ToList()
            };
        }

        if (message.Role == "tool")
        {
            return new
            {
                role = "tool",
                tool_call_id = message.ToolCallId,
                content = message.Content ?? ""
            };
        }

        return new
        {
            role = message.Role,
            content = message.Content ?? ""
        };
    }

    private static string CanonicalizeArguments(IReadOnlyDictionary<string, string> arguments)
    {
        return JsonSerializer.Serialize(
            arguments.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            JsonOptions);
    }

    private async Task<string> SendRawAsync(
        AgentChatSettings settings,
        string systemPrompt,
        IReadOnlyList<AgentChatMessage> history,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var apiKey = settings.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"还没有配置 DeepSeek API key。请在 {settings.ApiKeyEnv} 环境变量中设置，或填写 UserData/Agents/settings.local.json 里的 api_key。");
        }

        var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/chat/completions";
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        messages.AddRange(history.Select(item => new { role = item.Role, content = item.Content }));
        messages.Add(new { role = "user", content = userMessage });

        var payload = new
        {
            model = settings.Model,
            messages,
            temperature = settings.Temperature,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepSeek API 调用失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
        return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
            ?? throw new InvalidOperationException("DeepSeek API 没有返回可用回复。");
    }

    public Task<string> SummarizeAsync(
        AgentChatSettings settings,
        string existingSummary,
        IReadOnlyList<AgentChatMessage> history,
        CancellationToken cancellationToken)
    {
        var transcript = string.Join(
            Environment.NewLine,
            history.Select(message => $"{message.Role}: {message.Content}"));
        var prompt = $"""
        请把以下爱弥斯对话压缩成长期可复用的大纲摘要。

        要求：
        - 使用中文。
        - 保留用户偏好、事实、进行中的任务、重要约定。
        - 忽略寒暄和重复内容。
        - 输出 Markdown，控制在 800 字以内。

        现有摘要：
        {existingSummary}

        新对话：
        {transcript}
        """;

        return SendAsync(settings, "你是对话记忆压缩器，只输出摘要。", [], prompt, cancellationToken);
    }

    public Task<string> ExtractMemoryCandidateAsync(
        AgentChatSettings settings,
        string memorySkill,
        string currentSessionSummary,
        IReadOnlyList<AgentChatMessage> visibleHistory,
        CancellationToken cancellationToken)
    {
        var transcript = string.Join(
            Environment.NewLine,
            visibleHistory.Select(message => $"{message.Role}: {message.Content}"));
        var prompt = $"""
        用户触发了“保存记忆”。

        请严格按桌宠记忆 skill 执行，从当前对话框可见聊天记录、当前会话压缩摘要中，提炼真正值得保存的一条候选记忆。

        桌宠记忆 skill：
        {memorySkill}

        规则：
        - 不要只保存用户刚刚说“保存记忆”的那句话。
        - 优先保存用户长期偏好、称呼、项目规则、重要约定、后续复用价值高的事实。
        - 忽略寒暄、重复确认、一次性闲聊、无复用价值内容。
        - 不要保存 API key、token、密码。
        - 如果没有值得保存的内容，只输出：无值得保存的记忆。
        - 如果有，输出 1-5 条 Markdown bullet，每条尽量简洁。

        当前会话压缩摘要：
        {currentSessionSummary}

        当前可见对话：
        {transcript}
        """;

        return SendAsync(settings, "你是桌宠记忆提炼器，只输出候选记忆或“无值得保存的记忆”。", [], prompt, cancellationToken);
    }

    public Task<string> ExtractAutoLongTermMemoryAsync(
        AgentChatSettings settings,
        string existingLongTermMemory,
        IReadOnlyList<AgentChatMessage> recentHistory,
        string userMessage,
        string assistantReply,
        CancellationToken cancellationToken)
    {
        var recent = string.Join(
            Environment.NewLine,
            recentHistory.TakeLast(8).Select(message => $"{message.Role}: {message.Content}"));
        var prompt = $"""
        请判断这轮对话里是否出现了应自动写入长期用户记忆的内容。

        适合保存为长期记忆：
        - 用户稳定的饮食喜好、娱乐爱好、称呼偏好、沟通习惯、雷点、过敏/禁忌。
        - 用户明确或自然表达“以后要记得”的偏好或个人设定。
        - 能在未来多次对话中帮助爱弥斯更懂用户的事实。

        不要保存：
        - 一次性的当天状态、随口情绪、普通寒暄。
        - 对爱弥斯的提问，例如“你喜欢什么”。
        - 已经在现有长期记忆里表达过的重复内容。
        - API key、token、密码、隐私密钥。

        输出要求：
        - 如果不值得保存，只输出：无值得保存的记忆。
        - 如果值得保存，输出 1-3 条 Markdown bullet。
        - 每条都改写成长期稳定事实，例如“用户喜欢吃辣，但不喜欢香菜。”
        - 不要解释判断过程，不要输出保存路径。

        现有长期记忆摘要：
        {existingLongTermMemory}

        最近对话：
        {recent}

        当前用户消息：
        {userMessage}

        爱弥斯回复：
        {assistantReply}
        """;

        return SendAsync(settings, "你是爱弥斯的长期记忆守门员，只输出候选记忆或“无值得保存的记忆”。", [], prompt, cancellationToken);
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<ResponseToolCall>? ToolCalls { get; set; }
    }

    private sealed class ResponseToolCall
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("function")]
        public ResponseToolFunction? Function { get; set; }
    }

    private sealed class ResponseToolFunction
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }

    private sealed class ModelReply
    {
        public string Content { get; set; } = "";

        public List<ModelToolCall> ToolCalls { get; } = [];
    }

    private sealed class ModelToolCall
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LoopMessage
    {
        public string Role { get; init; } = "user";

        public string? Content { get; init; }

        public string? ToolCallId { get; init; }

        public List<ModelToolCall> ToolCalls { get; init; } = [];

        public static LoopMessage FromAgentMessage(AgentChatMessage message)
        {
            return new LoopMessage { Role = message.Role, Content = message.Content };
        }

        public static LoopMessage User(string content)
        {
            return new LoopMessage { Role = "user", Content = content };
        }

        public static LoopMessage Assistant(string content)
        {
            return new LoopMessage { Role = "assistant", Content = content };
        }

        public static LoopMessage AssistantToolCalls(string content, IReadOnlyList<ModelToolCall> toolCalls)
        {
            return new LoopMessage
            {
                Role = "assistant",
                Content = content,
                ToolCalls = toolCalls.ToList()
            };
        }

        public static LoopMessage Tool(string toolCallId, string content)
        {
            return new LoopMessage { Role = "tool", ToolCallId = toolCallId, Content = content };
        }
    }
}
