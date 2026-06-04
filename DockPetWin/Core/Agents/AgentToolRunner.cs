using System.IO;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DockPetWin.Core.Settings;

namespace DockPetWin.Core.Agents;

public sealed class AgentToolRunner
{
    private readonly AgentStore store;
    private ActiveSkillContext? activeSkill;
    private int toolStep;
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private const int MaxSkillDiscoveryDepth = 8;

    public AgentToolRunner(AgentStore store)
    {
        this.store = store;
    }

    public bool HasActiveSkill => activeSkill is not null;

    public static bool TryParseToolCall(string text, out AgentToolCall call)
    {
        call = new AgentToolCall();
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var toolProperty = "";
            var tool = GetJsonString(root, "tool");
            if (!string.IsNullOrWhiteSpace(tool))
            {
                toolProperty = "tool";
            }
            else
            {
                tool = GetJsonString(root, "name");
                toolProperty = !string.IsNullOrWhiteSpace(tool) ? "name" : toolProperty;
            }

            if (string.IsNullOrWhiteSpace(tool))
            {
                tool = GetJsonString(root, "function");
                toolProperty = !string.IsNullOrWhiteSpace(tool) ? "function" : toolProperty;
            }

            if (string.IsNullOrWhiteSpace(tool))
            {
                tool = GetJsonString(root, "action");
                toolProperty = !string.IsNullOrWhiteSpace(tool) ? "action" : toolProperty;
            }

            if (string.IsNullOrWhiteSpace(tool))
            {
                return false;
            }

            call.Tool = tool.Trim();
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGetObject(root, "arguments", out var arguments)
                || TryGetObject(root, "args", out arguments)
                || TryGetObject(root, "params", out arguments)
                || TryGetObject(root, "input", out arguments))
            {
                AddObjectArguments(arguments, args);
            }

            foreach (var property in root.EnumerateObject())
            {
                if (IsEnvelopeProperty(property.Name, toolProperty))
                {
                    continue;
                }

                args[property.Name] = JsonElementToString(property.Value);
            }

            call.Arguments = args;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AgentToolResult Run(AgentToolCall call)
    {
        var tool = call.Tool.Trim().ToLowerInvariant();
        toolStep++;
        try
        {
            return tool switch
            {
                "tool_specs" => ToolSpecs(),
                "list_skills" or "list_skill" or "search_skills" or "skill_search" => ListSkills(GetArg(call, "query")),
                "read_skill" or "readskill" or "skill_read" or "load_skill" or "invoke_skill" or "use_skill" or "call_skill" => ReadSkill(GetFirstArg(call, "name", "skill", "skill_name", "skillName", "query")),
                "list_skill_files" or "skill_files" or "skill_file_list" => ListSkillFiles(GetFirstArg(call, "path", "directory", "dir")),
                "find_skill_files" or "find_skill_file" or "skill_file_search" => FindSkillFiles(GetFirstArg(call, "query", "name", "filename", "file", "path"), GetFirstArg(call, "path", "directory", "dir")),
                "read_skill_file" or "skill_file_read" => ReadSkillFile(GetFirstArg(call, "path", "file", "name"), ParseInt(GetFirstArg(call, "max_chars", "maxChars", "limit"), 12000)),
                "list_memories" or "list_memory" or "memory_list" => ListMemories(GetFirstArg(call, "type", "scope", "kind")),
                "read_memory" or "read_memories" or "memory_read" => ReadMemory(GetFirstArg(call, "type", "scope", "kind"), ParseInt(GetFirstArg(call, "max_chars", "maxChars", "limit"), 12000)),
                "save_memory" or "memory_save" or "remember" => SaveMemory(call),
                "search_knowledge" or "knowledge_search" => SearchKnowledge(
                    GetFirstArg(call, "query", "q", "keyword"),
                    ParseInt(GetFirstArg(call, "max_results", "maxResults", "limit"), 8)),
                "read_knowledge" or "knowledge_read" => ReadKnowledge(
                    GetFirstArg(call, "path", "file", "name", "query"),
                    ParseInt(GetFirstArg(call, "max_chars", "maxChars", "limit"), 12000)),
                "list_files" => ListFiles(GetArg(call, "path")),
                "find_files" or "find_file" or "search_files" or "file_search" => FindFiles(GetFirstArg(call, "query", "name", "filename", "file", "path"), GetArg(call, "path")),
                "read_file" => ReadFile(GetArg(call, "path")),
                "web_search" or "search_web" or "news_search" => WebSearch(
                    GetFirstArg(call, "query", "q", "keyword"),
                    GetFirstArg(call, "kind", "type")),
                "handle_read" => HandleRead(
                    GetArg(call, "handle"),
                    ParseInt(GetArg(call, "offset"), 0),
                    ParseInt(GetFirstArg(call, "max_chars", "maxChars", "limit"), 4000)),
                "write_file" => WriteFile(GetArg(call, "path"), GetArg(call, "content")),
                "python_execute" or "run_python" or "python" => PythonExecute(
                    GetFirstArg(call, "code", "script", "python"),
                    GetFirstArg(call, "path", "file", "script_path"),
                    ParseInt(GetFirstArg(call, "timeout_seconds", "timeoutSeconds", "timeout"), 30)),
                "list_reminders" or "reminder_list" => ListReminders(),
                "upsert_reminder" or "save_reminder" or "create_reminder" or "edit_reminder" => UpsertReminder(call),
                "delete_reminder" or "remove_reminder" => AgentToolResult.Error("delete_reminder", "delete_disabled", "Startup Permission Hook 已禁用删除权限。请到设置界面手动删除提醒，或让爱弥斯创建整理清单。"),
                "list_task_runs" or "list_scheduled_runs" or "task_run_list" => ListTaskRuns(ParseInt(GetFirstArg(call, "limit", "max"), 20)),
                "read_task_run" or "read_scheduled_run" or "task_run_read" => ReadTaskRun(GetFirstArg(call, "path", "file", "output_path")),
                "create_task" => CreateTask(
                    GetFirstArg(call, "title", "name"),
                    GetFirstArg(call, "detail", "description", "content"),
                    GetArg(call, "status")),
                _ => AgentToolResult.Error(tool, "unknown_tool", $"工具不存在：{call.Tool}。请先调用 tool_specs 查看可用工具。")
            };
        }
        catch (Exception ex)
        {
            return AgentToolResult.Error(tool, "tool_exception", ex.Message);
        }
    }

    public string RunText(AgentToolCall call)
    {
        return FormatResult(Run(call));
    }

    public AgentToolResult? TryAutoLoadSkillForMessage(string userMessage)
    {
        var route = FindSkillRoute(userMessage);
        if (route is null)
        {
            return null;
        }

        return ReadSkill(route.Value.Name, $"Skill runtime 根据用户请求自动路由：{route.Value.Reason}");
    }

    public bool TryBuildSkillComplianceNudge(out string nudge)
    {
        nudge = "";
        if (activeSkill is null)
        {
            return false;
        }

        var missingStartup = activeSkill.GetMissingStartupFiles()
            .Take(5)
            .ToList();
        if (missingStartup.Count > 0)
        {
            nudge = "Skill runtime gate 检查到当前已激活 skill `" + activeSkill.Name + "`，但启动阶段文件还没有读取。"
                + Environment.NewLine
                + "必须先调用 `read_skill_file` 读取这些 startup 文件，再继续后续步骤："
                + Environment.NewLine
                + string.Join(Environment.NewLine, missingStartup.Select(path => $"- {path}"));
            return true;
        }

        var missingRequired = activeSkill.GetMissingRequiredFiles()
            .Take(5)
            .ToList();
        if (missingRequired.Count == 0)
        {
            if (activeSkill.RequiresIndexSelectedFile && !activeSkill.HasReadIndexSelectedContentFile())
            {
                nudge = "Skill runtime gate 检查到当前 skill 要求先读索引并根据索引选择对应资料，但还没有读取任何索引指向的具体内容文件。"
                    + Environment.NewLine
                    + "请先根据已读取的 index/索引内容，使用 `find_skill_files` 或 `read_skill_file` 读取本次任务对应的资料文件，再继续最终答复。"
                    + (string.IsNullOrWhiteSpace(activeSkill.IndexSelectionReason) ? "" : Environment.NewLine + "触发原因：" + activeSkill.IndexSelectionReason);
                return true;
            }

            var staleFinalReview = activeSkill.GetMissingOrStaleFinalReviewFiles()
                .Take(5)
                .ToList();
            if (staleFinalReview.Count == 0)
            {
                return false;
            }

            nudge = "Skill runtime gate 检查到当前 skill 要求最终输出前复读规范，但这些 final review 文件尚未读取，或读取时间早于后续资料读取。"
                + Environment.NewLine
                + "请现在重新调用 `read_skill_file` 读取这些文件，然后再给最终答复："
                + Environment.NewLine
                + string.Join(Environment.NewLine, staleFinalReview.Select(path => $"- {path}"));
            return true;
        }

        nudge = "Skill runtime 检查到当前已激活 skill `" + activeSkill.Name + "`，但还有 SKILL.md 标记的必读资料没有读取。"
            + Environment.NewLine
            + "请先调用 `read_skill_file` 读取这些路径，再继续最终答复："
            + Environment.NewLine
            + string.Join(Environment.NewLine, missingRequired.Select(path => $"- {path}"));
        return true;
    }

    public string BuildActiveSkillRuntimePrompt()
    {
        if (activeSkill is null)
        {
            return "";
        }

        var required = activeSkill.RequiredFiles.Count == 0
            ? "- 无显式必读文件。"
            : string.Join(Environment.NewLine, activeSkill.RequiredFiles.Select(path => $"- {path}"));
        var suggested = activeSkill.SuggestedFiles.Count == 0
            ? "- 无额外建议文件。"
            : string.Join(Environment.NewLine, activeSkill.SuggestedFiles.Take(12).Select(path => $"- {path}"));
        var startup = activeSkill.StartupFiles.Count == 0
            ? "- 无 startup gate。"
            : string.Join(Environment.NewLine, activeSkill.StartupFiles.Select(path => $"- {path}"));
        var finalReview = activeSkill.FinalReviewFiles.Count == 0
            ? "- 无 final review gate。"
            : string.Join(Environment.NewLine, activeSkill.FinalReviewFiles.Select(path => $"- {path}"));
        var readFiles = activeSkill.ReadFiles.Count == 0
            ? "- 本轮尚未读取 skill-local 文件。"
            : string.Join(Environment.NewLine, activeSkill.ReadFiles.Order(StringComparer.OrdinalIgnoreCase).Take(20).Select(path => $"- {path}"));
        var indexGate = activeSkill.RequiresIndexSelectedFile
            ? $"- enabled: true{Environment.NewLine}- rule: 读完 index 后，必须继续读取至少一个 index 指向的具体内容文件。"
            : "- enabled: false";

        return $"""
        # Skill Runtime State

        - active_skill: {activeSkill.Name}
        - skill_root: `{GetRelativeWorkspacePath(activeSkill.RootPath)}`
        - rule: 当前请求已自动进入 skill runtime。后续相对路径默认以 active skill root 解析。
        - read_tool: 读取当前 skill 自带资料时，优先使用 `read_skill_file`；列目录用 `list_skill_files`；查找用 `find_skill_files`。
        - gate_rule: 没有通过 startup、required、index-selected-content、final-review gates 前，不要给最终答复。

        ## Startup Gate Files

        {startup}

        ## Required Skill Files

        {required}

        ## Index Selected Content Gate

        {indexGate}

        ## Final Review Gate Files

        {finalReview}

        ## Suggested Skill Files

        {suggested}

        ## Skill Files Read In This Turn

        {readFiles}
        """;
    }

    public static string FormatResult(AgentToolResult result)
    {
        return JsonSerializer.Serialize(result, ResultJsonOptions);
    }

    public IReadOnlyList<object> GetModelTools()
    {
        return BuildToolSpecs()
            .Select(spec =>
            {
                var properties = new Dictionary<string, object>();
                var required = new List<string>();
                foreach (var argument in spec.Arguments)
                {
                    properties[argument.Key] = new
                    {
                        type = "string",
                        description = argument.Value
                    };
                    if (argument.Value.Contains("必填", StringComparison.OrdinalIgnoreCase))
                    {
                        required.Add(argument.Key);
                    }
                }

                return (object)new
                {
                    type = "function",
                    function = new
                    {
                        name = spec.Name,
                        description = spec.Description,
                        parameters = new
                        {
                            type = "object",
                            properties,
                            required
                        }
                    }
                };
            })
            .ToList();
    }

    public static Dictionary<string, string> ParseArgumentJson(string json)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return args;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            AddObjectArguments(document.RootElement, args);
        }

        return args;
    }

    private AgentToolResult ToolSpecs()
    {
        var specs = BuildToolSpecs();
        return AgentToolResult.Success("tool_specs", "已返回工具 schema。", JsonSerializer.Serialize(specs, ResultJsonOptions));
    }

    private static AgentToolSpec[] BuildToolSpecs()
    {
        return
        [
            new AgentToolSpec
            {
                Name = "tool_specs",
                Description = "查看所有可用工具、参数和权限。",
                Arguments = [],
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "list_skills",
                Description = "按关键词搜索桌宠 workspace/skills 内的 skill。",
                Arguments = new() { ["query"] = "可选。关键词，为空则列出前 30 个。" },
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_skill",
                Description = "读取并激活指定 skill 的 SKILL.md。用户说调用、使用、执行某个 skill 时，先用 list_skills 找到名称，再用 read_skill 读取，然后按 SKILL.md 指令继续回答或操作。别名 load_skill 也可用。",
                Arguments = new() { ["name"] = "必填。skill 目录名，例如 desktop-memory。" },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "list_skill_files",
                Description = "列出当前已激活 skill 目录内的文件。读取 skill 自带 knowledge/references/rules/assets/templates 时优先用它，不要混用桌宠人设 knowledge。",
                Arguments = new() { ["path"] = "可选。当前 skill 内相对路径，例如 knowledge 或 references。" },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "find_skill_files",
                Description = "在当前已激活 skill 目录内递归查找文件或文件夹。用于根据 SKILL.md 的路径提示找到 skill-local 资料。",
                Arguments = new()
                {
                    ["query"] = "必填。文件名或路径关键词，例如 checklist.md、deal、knowledge。",
                    ["path"] = "可选。当前 skill 内限定搜索目录，例如 knowledge。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_skill_file",
                Description = "读取当前已激活 skill 目录内的文本资料，并记录为已读。SKILL.md 提到 knowledge/、references/、rules/、schemas/、checklist.md 等相对路径时优先使用。",
                Arguments = new()
                {
                    ["path"] = "必填。当前 skill 内相对路径，例如 knowledge/index.md、references/schema.md、checklist.md。",
                    ["max_chars"] = "可选。最多读取字符数，默认 12000。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "list_memories",
                Description = "列出桌宠已保存的长期/短期记忆文件，并显示相对路径。",
                Arguments = new() { ["type"] = "可选。all、long、short；默认 all。" },
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_memory",
                Description = "读取桌宠长期或短期记忆摘要。长期记忆会在启动时自动加载；短期记忆需要时用此工具读取。",
                Arguments = new()
                {
                    ["type"] = "必填。long 或 short。",
                    ["max_chars"] = "可选。最多读取字符数，默认 12000。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "save_memory",
                Description = "把 AI 提炼后的候选记忆真实保存到桌宠记忆文件。用户表达记住、保存、记录、沉淀、以后记得等意图时，由 AI 判断是否调用；不要只用自然语言声称已保存。",
                Arguments = new()
                {
                    ["content"] = "必填。要保存的候选记忆，必须是提炼后的内容，不要包含询问长期/短期的提示话术。",
                    ["type"] = "必填。long 或 short。长期稳定偏好/设定/规则用 long；临时上下文、当天任务、近期状态用 short。"
                },
                ReturnsHandle = false,
                WriteAccess = true
            },
            new AgentToolSpec
            {
                Name = "search_knowledge",
                Description = "搜索桌宠知识库 knowledge/，用于游戏设定、角色背景、剧情摘要、台词风格、世界观、飞行雪绒歌曲、专有名词等大资料的按需检索。系统提示中已有 knowledge/index.md 作为路由图；索引不够明确时用此工具搜索，不要全量读取 knowledge。",
                Arguments = new()
                {
                    ["query"] = "必填。关键词，例如 角色名、阵营、剧情名、术语。",
                    ["max_results"] = "可选。最多返回结果数，默认 8。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_knowledge",
                Description = "读取桌宠知识库 knowledge/ 中的指定资料文件。优先按系统提示中的 knowledge/index.md 路由读取明确路径；不明确时先用 search_knowledge 找到相对路径，再读取最相关文件。",
                Arguments = new()
                {
                    ["path"] = "必填。knowledge 相对路径或文件名，例如 characters/aemeath.md。",
                    ["max_chars"] = "可选。最多读取字符数，默认 12000。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "list_files",
                Description = "列出 UserData/Agents 内的目录。",
                Arguments = new() { ["path"] = "可选。相对路径，例如 workspace/output。" },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "find_files",
                Description = "递归查找 UserData/Agents 内的文件或文件夹。用户说找文件、搜索文件、有没有某文件时优先使用。",
                Arguments = new()
                {
                    ["query"] = "必填。文件名或路径关键词，例如 爱弥斯记忆备份.md。",
                    ["path"] = "可选。限定搜索目录，例如 workspace/output。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_file",
                Description = "读取 UserData/Agents 内的文本文件。支持相对路径和文件名模糊匹配，大文件会返回 handle。",
                Arguments = new() { ["path"] = "必填。相对路径，例如 workspace/PROJECT.md。" },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "web_search",
                Description = "联网搜索新闻或网页摘要。默认优先使用 Tavily；未配置 Tavily key 时退回 Bing RSS。定时任务涉及新闻、资讯、最近动态时优先使用。",
                Arguments = new()
                {
                    ["query"] = "必填。搜索关键词。",
                    ["kind"] = "可选。news 或 web，默认 news。"
                },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "handle_read",
                Description = "读取大型工具输出 handle 的片段。",
                Arguments = new()
                {
                    ["handle"] = "必填。上一次工具返回的 handle。",
                    ["offset"] = "可选。起始字符位置，默认 0。",
                    ["max_chars"] = "可选。最多读取字符数，默认 4000，最大 12000。"
                },
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "write_file",
                Description = "写入 workspace/** 或 default-agent.md。受 Startup Permission Hook 限制，不能写入其他 Agents 目录。",
                Arguments = new()
                {
                    ["path"] = "可选。相对路径；为空则写入 workspace/output/notes/YYYY-MM-DD/桌宠笔记-时间.md。允许 workspace/** 或 default-agent.md。",
                    ["content"] = "必填。要写入的文本内容。"
                },
                ReturnsHandle = false,
                WriteAccess = true
            },
            new AgentToolSpec
            {
                Name = "python_execute",
                Description = "在 workspace 内执行受限 Python。用于简单数据处理、整理 output、生成/修改 CSV/XLSX/DOCX。文件访问限制在 workspace 内，禁用删除、子进程和网络。",
                Arguments = new()
                {
                    ["code"] = "可选。要执行的 Python 代码。可导入 scripts/aemeath_tools.py 中的 create_xlsx/create_docx/write_csv 等工具。",
                    ["path"] = "可选。workspace 内已有 .py 脚本路径。code 为空时读取这个脚本。",
                    ["timeout_seconds"] = "可选。超时秒数，默认 30，最大 120。"
                },
                ReturnsHandle = true,
                WriteAccess = true
            },
            new AgentToolSpec
            {
                Name = "list_reminders",
                Description = "列出桌宠当前提醒事项，包括间隔、固定文案和 AI 文案开关。",
                Arguments = [],
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "upsert_reminder",
                Description = "新增或编辑桌宠提醒事项。用户要求新增、修改喝水提醒、走动提醒或其他提醒时使用。",
                Arguments = new()
                {
                    ["id"] = "可选。提醒 ID；编辑已有提醒时优先使用，例 water、movement、custom-focus。",
                    ["title"] = "可选。提醒名称。",
                    ["category"] = "可选。类型，例 water、movement、custom。",
                    ["enabled"] = "可选。true/false。",
                    ["action_type"] = "可选。reminder 或 agent_task。需要定时执行搜索/整理/写文件时用 agent_task。",
                    ["schedule_type"] = "可选。interval、daily、weekly、monthly。",
                    ["interval_minutes"] = "可选。提醒间隔，单位分钟。",
                    ["time_of_day"] = "可选。每日/每周/每月执行时间，格式 HH:mm。",
                    ["days_of_week"] = "可选。每周执行日，例 mon,wed,fri 或 周一,周三。",
                    ["day_of_month"] = "可选。每月第几天执行，1-31。",
                    ["fixed_message"] = "可选。固定提醒文字。",
                    ["use_ai_message"] = "可选。是否启用 AI 自写提醒文案，true/false。",
                    ["ai_prompt"] = "可选。AI 文案提示词。",
                    ["task_prompt"] = "可选。agent_task 的任务说明，例 搜索今天 AI 新闻并总结 5 条。",
                    ["save_output"] = "可选。是否保存任务结果到 output，默认 true。",
                    ["output_directory"] = "可选。保存目录，默认 workspace/output/scheduled-tasks。"
                },
                ReturnsHandle = false,
                WriteAccess = true
            },
            new AgentToolSpec
            {
                Name = "delete_reminder",
                Description = "已禁用。Startup Permission Hook 禁止爱弥斯执行删除操作；需要删除提醒请让用户到设置界面手动处理。",
                Arguments = new() { ["id"] = "必填。提醒 ID、名称或类型。" },
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "list_task_runs",
                Description = "列出最近的定时 agent_task 运行记录，包含状态、工具次数和输出文件路径。",
                Arguments = new() { ["limit"] = "可选。返回条数，默认 20。" },
                ReturnsHandle = false,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "read_task_run",
                Description = "读取某一次定时任务运行记录 Markdown。先用 list_task_runs 找到 output_path。",
                Arguments = new() { ["path"] = "必填。运行记录路径，支持绝对路径或 UserData/Agents 相对路径。" },
                ReturnsHandle = true,
                WriteAccess = false
            },
            new AgentToolSpec
            {
                Name = "create_task",
                Description = "记录轻量任务状态，用于后续提醒和追踪；不会真正执行后台命令。",
                Arguments = new()
                {
                    ["title"] = "必填。任务标题。",
                    ["detail"] = "可选。任务说明。",
                    ["status"] = "可选。open、waiting_user、done 等。"
                },
                ReturnsHandle = false,
                WriteAccess = true
            }
        ];
    }

    private AgentToolResult ListMemories(string type)
    {
        var normalized = NormalizeMemoryType(type);
        var content = normalized switch
        {
            "long" => store.ListMemoryFiles(longTermOnly: true),
            "short" => store.ListMemoryFiles(shortTermOnly: true),
            _ => store.ListMemoryFiles()
        };
        var label = normalized switch
        {
            "long" => "长期记忆",
            "short" => "短期记忆",
            _ => "全部记忆"
        };
        return AgentToolResult.Success("list_memories", $"已列出{label}。", content);
    }

    private AgentToolResult ReadMemory(string type, int maxChars)
    {
        var normalized = NormalizeMemoryType(type);
        if (normalized == "all")
        {
            var allContent = "# 长期记忆\n\n"
                + store.ReadMemorySummary(longTerm: true, maxChars / 2)
                + Environment.NewLine + Environment.NewLine
                + "# 短期记忆\n\n"
                + store.ReadMemorySummary(longTerm: false, maxChars / 2);
            return BuildContentResult("read_memory", "已读取全部记忆摘要。", allContent);
        }

        var content = store.ReadMemorySummary(normalized == "long", maxChars);
        var label = normalized == "long" ? "长期记忆" : "短期记忆";
        return BuildContentResult("read_memory", $"已读取{label}摘要。", content);
    }

    private AgentToolResult SaveMemory(AgentToolCall call)
    {
        var content = GetFirstArg(call, "content", "memory", "text", "summary");
        if (string.IsNullOrWhiteSpace(content))
        {
            return AgentToolResult.Error("save_memory", "missing_argument", "缺少参数：content。请先提炼要保存的记忆内容。");
        }

        var type = NormalizeMemoryType(GetFirstArg(call, "type", "scope", "kind"));
        if (type == "all")
        {
            return AgentToolResult.Error("save_memory", "missing_argument", "缺少参数：type。请使用 long 或 short；不确定时先询问用户。");
        }

        var savedMessage = store.SaveMemoryCandidate(CleanMemoryContent(content), type == "long");
        return AgentToolResult.Success("save_memory", $"已保存{(type == "long" ? "长期" : "短期")}记忆。", savedMessage);
    }

    private AgentToolResult SearchKnowledge(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Error("search_knowledge", "missing_argument", "缺少参数：query。");
        }

        var content = store.SearchKnowledge(query, maxResults);
        return BuildContentResult("search_knowledge", $"已搜索知识库：{query}。", content);
    }

    private AgentToolResult ReadKnowledge(string path, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AgentToolResult.Error("read_knowledge", "missing_argument", "缺少参数：path。请先用 search_knowledge 搜索。");
        }

        var content = store.ReadKnowledge(path, maxChars);
        if (content.StartsWith("知识库文件不存在", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("找到多个知识库文件", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("缺少知识库路径", StringComparison.OrdinalIgnoreCase))
        {
            return AgentToolResult.Error("read_knowledge", "knowledge_not_found", content);
        }

        return BuildContentResult("read_knowledge", $"已读取知识库：{path}。", content);
    }

    private AgentToolResult ListSkills(string query)
    {
        var rows = new List<string>();
        foreach (var skill in EnumerateSkills())
        {
            var haystack = $"{skill.Name}\n{skill.Description}";
            if (!string.IsNullOrWhiteSpace(query)
                && !haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add($"- {skill.Name}: {skill.Description} ({skill.File})");
            if (rows.Count >= 30)
            {
                break;
            }
        }

        var content = rows.Count == 0 ? "没有找到匹配 skill。" : string.Join(Environment.NewLine, rows);
        return AgentToolResult.Success("list_skills", rows.Count == 0 ? "没有找到匹配 skill。" : $"找到 {rows.Count} 个 skill。", content);
    }

    private AgentToolResult ReadSkill(string name, string activationReason = "")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AgentToolResult.Error("read_skill", "missing_argument", "缺少参数：name。请用 {\"tool\":\"read_skill\",\"arguments\":{\"name\":\"desktop-memory\"}}。");
        }

        if (string.Equals(name, "memory-saver", StringComparison.OrdinalIgnoreCase))
        {
            return AgentToolResult.Error("read_skill", "skill_disabled", "旧 memory-saver 已从桌宠可调用 skill 中下线。请使用 desktop-memory。");
        }

        var match = FindSkill(name);
        if (match.File is not null)
        {
            activeSkill = BuildActiveSkillContext(match.Name, match.File);
            toolStep = 0;
            var reason = string.IsNullOrWhiteSpace(activationReason)
                ? ""
                : $"{Environment.NewLine}- activation_reason: {activationReason}";
            return BuildContentResult(
                "read_skill",
                $"已读取并激活 skill：{match.Name}。请按该 SKILL.md 的流程继续完成用户请求。",
                BuildSkillReadContent(activeSkill, reason),
                14000);
        }

        var candidates = FindSkillCandidates(name);
        var suffix = candidates.Count > 0
            ? $"{Environment.NewLine}可能是：{string.Join("、", candidates)}。请用候选名称重新调用 read_skill。"
            : "";
        return AgentToolResult.Error("read_skill", "skill_not_found", $"没有找到 skill：{name}{suffix}");
    }

    private ActiveSkillContext BuildActiveSkillContext(string skillName, string skillFile)
    {
        var skillRoot = Path.GetDirectoryName(skillFile) ?? store.SkillDirectory;
        var skillMarkdown = SafeRead(skillFile, 30000);
        var gates = DiscoverSkillRuntimeGates(skillRoot, skillMarkdown);
        var required = DiscoverSkillFiles(skillRoot, skillMarkdown, requiredOnly: true);
        foreach (var path in gates.StartupFiles.Concat(gates.FinalReviewFiles))
        {
            AddDistinct(required, path);
        }

        var suggested = DiscoverSkillFiles(skillRoot, skillMarkdown, requiredOnly: false)
            .Where(path => !required.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new ActiveSkillContext
        {
            Name = skillName,
            RootPath = skillRoot,
            SkillFile = skillFile,
            SkillMarkdown = skillMarkdown,
            RequiredFiles = required,
            SuggestedFiles = suggested,
            StartupFiles = gates.StartupFiles,
            FinalReviewFiles = gates.FinalReviewFiles,
            RequiresIndexSelectedFile = gates.RequiresIndexSelectedFile,
            IndexSelectionReason = gates.IndexSelectionReason
        };
    }

    private string BuildSkillReadContent(ActiveSkillContext context, string activationReason)
    {
        var skillRoot = context.RootPath;
        var relativeRoot = GetRelativeWorkspacePath(skillRoot);
        var localEntries = Directory.Exists(skillRoot)
            ? Directory.EnumerateFileSystemEntries(skillRoot)
                .Where(entry => !Path.GetFileName(entry).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .Select(entry =>
                {
                    var kind = Directory.Exists(entry) ? "[dir]" : "[file]";
                    return $"- {kind} {GetRelativeWorkspacePath(entry)}";
                })
                .ToList()
            : [];
        var localFilesText = localEntries.Count == 0
            ? "- 未发现额外本地资料。"
            : string.Join(Environment.NewLine, localEntries);
        var requiredFiles = context.RequiredFiles.Count == 0
            ? "- 未从 SKILL.md 识别出显式必读文件。"
            : string.Join(Environment.NewLine, context.RequiredFiles.Select(path => $"- {path}"));
        var suggestedFiles = context.SuggestedFiles.Count == 0
            ? "- 未从 SKILL.md 或常见目录识别出额外建议文件。"
            : string.Join(Environment.NewLine, context.SuggestedFiles.Take(20).Select(path => $"- {path}"));
        var startupFiles = context.StartupFiles.Count == 0
            ? "- 未识别到 startup gate。"
            : string.Join(Environment.NewLine, context.StartupFiles.Select(path => $"- {path}"));
        var finalReviewFiles = context.FinalReviewFiles.Count == 0
            ? "- 未识别到 final review gate。"
            : string.Join(Environment.NewLine, context.FinalReviewFiles.Select(path => $"- {path}"));
        var indexSelectedGate = context.RequiresIndexSelectedFile
            ? "- 已启用：读完 index 后必须继续读取至少一个 index 指向的具体内容文件。"
            : "- 未启用。";

        return $"""
        # Active Skill Context

        - skill_name: {context.Name}
        - skill_root: `{relativeRoot}`
        {activationReason}
        - path_rule: 本 skill 的相对路径都优先以 `skill_root` 为根目录解析。比如 SKILL.md 写 `knowledge/foo.md`，应读取 `{relativeRoot}/knowledge/foo.md`，不是爱弥斯人设知识库里的 `knowledge/foo.md`。
        - read_rule: 读取 skill 自带资料时，优先使用 `list_skill_files`、`find_skill_files`、`read_skill_file`。只有当 SKILL.md 明确要求读取桌宠人设/世界观 knowledge 时，才使用 `search_knowledge` 或 `read_knowledge`。
        - compliance_rule: `Required Skill Files` 中的文件在最终答复前必须读取；如果文件不存在或不适用，必须说明卡点。
        - gate_rule: 必须先通过 startup gate，再读索引对应内容；如果有 final review gate，最终答复前必须重新读取这些规范文件。

        ## Local Skill Files

        {localFilesText}

        ## Startup Gate Files

        {startupFiles}

        ## Required Skill Files

        {requiredFiles}

        ## Index Selected Content Gate

        {indexSelectedGate}

        ## Final Review Gate Files

        {finalReviewFiles}

        ## Suggested Skill Files

        {suggestedFiles}

        # SKILL.md

        {context.SkillMarkdown}
        """;
    }

    private AgentToolResult ListSkillFiles(string path)
    {
        if (activeSkill is null)
        {
            return AgentToolResult.Error("list_skill_files", "no_active_skill", "当前没有激活 skill。请先调用 read_skill 或 load_skill。");
        }

        var directory = ResolveActiveSkillPath(path);
        if (!Directory.Exists(directory))
        {
            return AgentToolResult.Error("list_skill_files", "directory_not_found", $"目录不存在：{GetActiveSkillRelativePath(directory)}");
        }

        var rows = Directory.EnumerateFileSystemEntries(directory)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(120)
            .Select(entry =>
            {
                var kind = Directory.Exists(entry) ? "[dir]" : "[file]";
                return $"- {kind} {GetActiveSkillRelativePath(entry)}";
            });
        return BuildContentResult("list_skill_files", $"已列出当前 skill 目录：{GetActiveSkillRelativePath(directory)}。", string.Join(Environment.NewLine, rows));
    }

    private AgentToolResult FindSkillFiles(string query, string path)
    {
        if (activeSkill is null)
        {
            return AgentToolResult.Error("find_skill_files", "no_active_skill", "当前没有激活 skill。请先调用 read_skill 或 load_skill。");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Error("find_skill_files", "missing_argument", "缺少参数：query。");
        }

        var root = ResolveActiveSkillPath(path);
        if (File.Exists(root))
        {
            root = Path.GetDirectoryName(root) ?? activeSkill.RootPath;
        }

        if (!Directory.Exists(root))
        {
            return AgentToolResult.Error("find_skill_files", "directory_not_found", $"目录不存在：{GetActiveSkillRelativePath(root)}");
        }

        var normalizedQuery = query.Trim().Trim('"', '`');
        var rows = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(entry => !IsSensitivePath(entry))
            .Where(entry =>
            {
                var relative = GetActiveSkillRelativePath(entry);
                return Path.GetFileName(entry).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || relative.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .Select(entry =>
            {
                var kind = Directory.Exists(entry) ? "[dir]" : "[file]";
                return $"- {kind} {GetActiveSkillRelativePath(entry)}";
            })
            .ToList();

        var content = rows.Count == 0
            ? $"没有在当前 skill 中找到匹配文件：{normalizedQuery}"
            : string.Join(Environment.NewLine, rows);
        return BuildContentResult("find_skill_files", rows.Count == 0 ? "没有找到匹配 skill 文件。" : $"找到 {rows.Count} 个 skill 文件匹配项。", content);
    }

    private AgentToolResult ReadSkillFile(string path, int maxChars)
    {
        if (activeSkill is null)
        {
            return AgentToolResult.Error("read_skill_file", "no_active_skill", "当前没有激活 skill。请先调用 read_skill 或 load_skill。");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return AgentToolResult.Error("read_skill_file", "missing_argument", "缺少参数：path。");
        }

        var file = ResolveActiveSkillPath(path);
        if (!File.Exists(file))
        {
            var candidates = FindActiveSkillFileCandidates(path).ToList();
            if (candidates.Count == 1)
            {
                file = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                var rows = candidates
                    .Take(8)
                    .Select(candidate => $"- {GetActiveSkillRelativePath(candidate)}");
                return AgentToolResult.Error(
                    "read_skill_file",
                    "ambiguous_file",
                    $"当前 skill 中找到多个匹配文件，请指定更完整路径：{Environment.NewLine}{string.Join(Environment.NewLine, rows)}");
            }
            else
            {
                return AgentToolResult.Error("read_skill_file", "file_not_found", $"当前 skill 文件不存在：{GetActiveSkillRelativePath(file)}");
            }
        }

        if (IsSensitivePath(file))
        {
            return AgentToolResult.Error("read_skill_file", "sensitive_file", "该文件可能包含 API key、token 或密钥，文件工具已拦截。");
        }

        var relativePath = NormalizeSkillRelativePath(GetActiveSkillRelativePath(file));
        activeSkill.MarkFileRead(relativePath, toolStep);
        var content = File.ReadAllText(file, Encoding.UTF8);
        var limit = Math.Clamp(maxChars, 1000, 30000);
        if (content.Length > limit)
        {
            content = content[..limit] + $"{Environment.NewLine}{Environment.NewLine}[文件内容较长，本次已按 max_chars 截断。需要更多内容时调大 max_chars 再读。]";
        }

        return BuildContentResult("read_skill_file", $"已读取当前 skill 文件：{GetActiveSkillRelativePath(file)}。", content, 14000);
    }

    private AgentToolResult ListFiles(string path)
    {
        var directory = ResolveWorkspacePath(path);
        if (!Directory.Exists(directory))
        {
            return AgentToolResult.Error("list_files", "directory_not_found", $"目录不存在：{GetRelativeWorkspacePath(directory)}");
        }

        var rows = Directory.EnumerateFileSystemEntries(directory)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(entry =>
            {
                var info = File.GetAttributes(entry).HasFlag(FileAttributes.Directory) ? "[dir]" : "[file]";
                return $"- {info} {GetRelativeWorkspacePath(entry)}";
            });
        return BuildContentResult("list_files", $"已列出目录：{GetRelativeWorkspacePath(directory)}。", string.Join(Environment.NewLine, rows));
    }

    private AgentToolResult FindFiles(string query, string path)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Error("find_files", "missing_argument", "缺少参数：query。");
        }

        var root = ResolveWorkspacePath(path);
        if (File.Exists(root))
        {
            root = Path.GetDirectoryName(root) ?? store.RootDirectory;
        }

        if (!Directory.Exists(root))
        {
            return AgentToolResult.Error("find_files", "directory_not_found", $"目录不存在：{GetRelativeWorkspacePath(root)}");
        }

        var normalizedQuery = query.Trim().Trim('"', '`');
        var rows = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(entry => !IsSensitivePath(entry))
            .Where(entry =>
            {
                var relative = GetRelativeWorkspacePath(entry);
                return Path.GetFileName(entry).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || relative.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(entry =>
            {
                var kind = Directory.Exists(entry) ? "[dir]" : "[file]";
                return $"- {kind} {GetRelativeWorkspacePath(entry)}";
            })
            .ToList();

        var content = rows.Count == 0
            ? $"没有找到匹配文件：{normalizedQuery}"
            : string.Join(Environment.NewLine, rows);
        return BuildContentResult("find_files", rows.Count == 0 ? "没有找到匹配文件。" : $"找到 {rows.Count} 个匹配项。", content);
    }

    private AgentToolResult ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AgentToolResult.Error("read_file", "missing_argument", "缺少参数：path。");
        }

        var file = ResolveWorkspacePath(path);
        if (!File.Exists(file))
        {
            var candidates = FindFileCandidates(path).ToList();
            if (candidates.Count == 1)
            {
                file = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                var rows = candidates
                    .Take(8)
                    .Select(candidate => $"- {GetRelativeWorkspacePath(candidate)}");
                return AgentToolResult.Error(
                    "read_file",
                    "ambiguous_file",
                    $"找到多个匹配文件，请指定更完整路径：{Environment.NewLine}{string.Join(Environment.NewLine, rows)}");
            }
            else
            {
                return AgentToolResult.Error("read_file", "file_not_found", $"文件不存在：{GetRelativeWorkspacePath(file)}");
            }
        }

        if (IsSensitivePath(file))
        {
            return AgentToolResult.Error("read_file", "sensitive_file", "该文件可能包含 API key、token 或密钥，文件工具已拦截。");
        }

        return BuildContentResult("read_file", $"已读取文件：{GetRelativeWorkspacePath(file)}。", File.ReadAllText(file, Encoding.UTF8));
    }

    private AgentToolResult WebSearch(string query, string kind)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Error("web_search", "missing_argument", "缺少参数：query。");
        }

        try
        {
            var settings = store.LoadSettings();
            if (string.Equals(settings.SearchProvider, "tavily", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.ResolveTavilyApiKey()))
            {
                return TavilySearch(query.Trim(), kind, settings);
            }

            var isNews = !string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase);
            var url = isNews
                ? $"https://www.bing.com/news/search?q={Uri.EscapeDataString(query)}&format=rss"
                : $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&format=rss";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DockPetWin/1.0");
            var xml = client.GetStringAsync(url).GetAwaiter().GetResult();
            var document = XDocument.Parse(xml);
            var rows = document.Descendants("item")
                .Take(8)
                .Select((item, index) =>
                {
                    var title = WebUtility.HtmlDecode(item.Element("title")?.Value ?? "").Trim();
                    var link = item.Element("link")?.Value?.Trim() ?? "";
                    var pubDate = item.Element("pubDate")?.Value?.Trim() ?? "";
                    var description = WebUtility.HtmlDecode(item.Element("description")?.Value ?? "").Trim();
                    return $"{index + 1}. {title}\n   时间：{pubDate}\n   链接：{link}\n   摘要：{description}";
                })
                .Where(row => !string.IsNullOrWhiteSpace(row))
                .ToList();
            var content = rows.Count == 0
                ? $"没有搜索到结果：{query}"
                : string.Join(Environment.NewLine + Environment.NewLine, rows);
            return BuildContentResult("web_search", $"已搜索：{query}。", content);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Error("web_search", "search_failed", $"搜索失败：{ex.Message}");
        }
    }

    private AgentToolResult TavilySearch(string query, string kind, AgentChatSettings settings)
    {
        var isNews = !string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ResolveTavilyApiKey());
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockPetWin/1.0");

        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["topic"] = isNews ? "news" : "general",
            ["search_depth"] = "advanced",
            ["max_results"] = 8,
            ["include_answer"] = true,
            ["include_raw_content"] = false
        };

        var requestJson = JsonSerializer.Serialize(payload, ResultJsonOptions);
        using var response = client.PostAsync(
            "https://api.tavily.com/search",
            new StringContent(requestJson, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return AgentToolResult.Error("web_search", "tavily_failed", $"Tavily 搜索失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var rows = new List<string>();
        if (root.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.String)
        {
            var answer = answerElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(answer))
            {
                rows.Add($"综合回答：{answer}");
            }
        }

        if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
        {
            var index = 1;
            foreach (var item in resultsElement.EnumerateArray().Take(8))
            {
                var title = GetJsonString(item, "title");
                var url = GetJsonString(item, "url");
                var publishedDate = GetJsonString(item, "published_date");
                var content = GetJsonString(item, "content");
                var dateLine = string.IsNullOrWhiteSpace(publishedDate) ? "" : $"\n   时间：{publishedDate}";
                rows.Add($"{index}. {title}{dateLine}\n   链接：{url}\n   摘要：{content}");
                index++;
            }
        }

        var contentText = rows.Count == 0
            ? $"Tavily 没有搜索到结果：{query}"
            : string.Join(Environment.NewLine + Environment.NewLine, rows);
        return BuildContentResult("web_search", $"已用 Tavily 搜索：{query}。", contentText);
    }

    private AgentToolResult HandleRead(string handle, int offset, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return AgentToolResult.Error("handle_read", "missing_argument", "缺少参数：handle。");
        }

        var content = store.ReadToolOutput(handle, offset, maxChars);
        return AgentToolResult.Success("handle_read", $"已读取 handle：{handle}。", content);
    }

    private AgentToolResult WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine("workspace", "output", "notes", DateTime.Now.ToString("yyyy-MM-dd"), $"桌宠笔记-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        }

        var file = ResolveWorkspacePath(path);
        GuardWritablePath(file);
        if (Directory.Exists(file))
        {
            file = Path.Combine(file, $"桌宠笔记-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content ?? "", Encoding.UTF8);
        return AgentToolResult.Success("write_file", $"已写入：{GetRelativeWorkspacePath(file)}。", $"已写入：{GetRelativeWorkspacePath(file)}");
    }

    private AgentToolResult PythonExecute(string code, string scriptPath, int timeoutSeconds)
    {
        try
        {
            var workspaceRoot = Path.GetFullPath(store.WorkspaceDirectory);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "scripts", "runtime", "python-runs"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "output"));

            if (string.IsNullOrWhiteSpace(code))
            {
                if (string.IsNullOrWhiteSpace(scriptPath))
                {
                    return AgentToolResult.Error("python_execute", "missing_argument", "缺少参数：code 或 path。");
                }

                var scriptFile = ResolveWorkspacePath(scriptPath);
                GuardPythonWorkspacePath(scriptFile);
                if (!File.Exists(scriptFile))
                {
                    return AgentToolResult.Error("python_execute", "script_not_found", $"Python 脚本不存在：{GetRelativeWorkspacePath(scriptFile)}");
                }

                code = File.ReadAllText(scriptFile, Encoding.UTF8);
            }

            var policyError = ValidatePythonCode(code);
            if (!string.IsNullOrWhiteSpace(policyError))
            {
                return AgentToolResult.Error("python_execute", "policy_blocked", policyError);
            }

            var script = BuildPythonSandboxPrelude(workspaceRoot) + Environment.NewLine + code;
            var runtimeScript = Path.Combine(workspaceRoot, "scripts", "runtime", "python-runs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.py");
            File.WriteAllText(runtimeScript, script, Encoding.UTF8);

            var python = ResolvePythonExecutable();
            if (python is null)
            {
                return AgentToolResult.Error("python_execute", "python_not_found", "没有找到可用 Python。请安装 Python 3，或后续把便携 Python 放到 workspace/python/python.exe 或程序目录 python/python.exe。");
            }

            var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 30 : timeoutSeconds, 1, 120));
            var startInfo = new ProcessStartInfo
            {
                FileName = python.Value.FileName,
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in python.Value.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
            startInfo.ArgumentList.Add(runtimeScript);
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONPATH"] = Path.Combine(workspaceRoot, "scripts");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return AgentToolResult.Error("python_execute", "process_start_failed", "Python 进程启动失败。");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures; timeout error is enough for the model.
                }

                return AgentToolResult.Error("python_execute", "timeout", $"Python 执行超过 {timeout.TotalSeconds:0} 秒，已中止。");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            var output = $"""
            exit_code: {process.ExitCode}
            script: {GetRelativeWorkspacePath(runtimeScript)}

            stdout:
            {stdout.Trim()}

            stderr:
            {stderr.Trim()}
            """;

            if (process.ExitCode != 0)
            {
                return BuildContentResult("python_execute", $"Python 执行失败，exit_code={process.ExitCode}。", output, 12000);
            }

            return BuildContentResult("python_execute", "Python 执行完成。", output, 12000);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Error("python_execute", "tool_exception", ex.Message);
        }
    }

    private AgentToolResult CreateTask(string title, string detail, string status)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return AgentToolResult.Error("create_task", "missing_argument", "缺少参数：title。");
        }

        var id = store.AppendTask(title, detail, status);
        return AgentToolResult.Success("create_task", $"已记录任务：{id}。", $"任务 {id} 已记录。");
    }

    private AgentToolResult ListReminders()
    {
        var settings = new SettingsStore().Load();
        var rows = settings.Reminders
            .Select(reminder =>
                $"- {reminder.Id}: {reminder.Title} | action={reminder.ActionType} | schedule={reminder.ScheduleType} | type={reminder.Category} | enabled={reminder.Enabled} | interval={reminder.IntervalSeconds / 60:0.##}min | time={reminder.TimeOfDay} | weekly={reminder.DaysOfWeek} | month_day={reminder.DayOfMonth} | ai={reminder.UseAiMessage} | fixed={reminder.FixedMessage} | task={reminder.TaskPrompt}");
        var content = settings.Reminders.Count == 0 ? "当前没有提醒事项。" : string.Join(Environment.NewLine, rows);
        return AgentToolResult.Success("list_reminders", "已列出桌宠提醒事项。", content);
    }

    private AgentToolResult ListTaskRuns(int limit)
    {
        var indexPath = Path.Combine(store.TasksDirectory, "scheduled-runs", "index.jsonl");
        if (!File.Exists(indexPath))
        {
            return AgentToolResult.Success("list_task_runs", "还没有定时任务运行记录。", "还没有定时任务运行记录。");
        }

        var rows = File.ReadLines(indexPath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(Math.Clamp(limit, 1, 100))
            .Select(FormatTaskRunIndexLine)
            .Reverse()
            .ToList();
        var content = rows.Count == 0 ? "还没有定时任务运行记录。" : string.Join(Environment.NewLine, rows);
        return AgentToolResult.Success("list_task_runs", $"已列出最近 {rows.Count} 条定时任务运行记录。", content);
    }

    private AgentToolResult ReadTaskRun(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AgentToolResult.Error("read_task_run", "missing_argument", "缺少参数：path。请先调用 list_task_runs 获取 output_path。");
        }

        var root = Path.GetFullPath(store.RootDirectory);
        var file = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return AgentToolResult.Error("read_task_run", "outside_workspace", "只能读取 UserData/Agents 内的任务记录。");
        }

        if (!File.Exists(file))
        {
            return AgentToolResult.Error("read_task_run", "file_not_found", $"任务记录不存在：{path}");
        }

        return BuildContentResult("read_task_run", $"已读取任务记录：{Path.GetRelativePath(root, file)}。", File.ReadAllText(file, Encoding.UTF8));
    }

    private static string FormatTaskRunIndexLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var title = GetJsonString(root, "title");
            var status = GetJsonString(root, "status");
            var started = GetJsonString(root, "started_at");
            var tools = root.TryGetProperty("tool_count", out var toolElement) ? toolElement.ToString() : "0";
            var output = GetJsonString(root, "output_path");
            return $"- {started} | {status} | {title} | tools={tools} | output_path={output}";
        }
        catch
        {
            return "- " + line;
        }
    }

    private AgentToolResult UpsertReminder(AgentToolCall call)
    {
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        settings.ReminderSchemaInitialized = true;

        var id = GetFirstArg(call, "id", "name", "title");
        var title = GetFirstArg(call, "title", "name");
        var category = GetFirstArg(call, "category", "type", "kind");
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(title))
        {
            return AgentToolResult.Error("upsert_reminder", "missing_argument", "缺少参数：id 或 title。");
        }

        var reminder = settings.Reminders.FirstOrDefault(item =>
            MatchesReminder(item, id)
            || (!string.IsNullOrWhiteSpace(title) && MatchesReminder(item, title)));
        if (reminder is null)
        {
            reminder = new ReminderSettings
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"custom-{DateTime.Now:yyyyMMddHHmmss}" : id,
                Title = string.IsNullOrWhiteSpace(title) ? id : title,
                Category = string.IsNullOrWhiteSpace(category) ? "custom" : category,
                Enabled = true,
                ActionType = "reminder",
                ScheduleType = "interval",
                IntervalSeconds = 60 * 60,
                FixedMessage = $"{settings.UserSalutation}，休息一下吧。"
            };
            settings.Reminders.Add(reminder);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            reminder.Title = title;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            reminder.Category = category;
        }

        var enabled = GetFirstArg(call, "enabled", "is_enabled");
        if (!string.IsNullOrWhiteSpace(enabled))
        {
            reminder.Enabled = ParseBool(enabled, reminder.Enabled);
        }

        var actionType = GetFirstArg(call, "action_type", "action", "mode");
        if (!string.IsNullOrWhiteSpace(actionType))
        {
            reminder.ActionType = NormalizeActionType(actionType);
        }

        var scheduleType = GetFirstArg(call, "schedule_type", "schedule", "repeat");
        if (!string.IsNullOrWhiteSpace(scheduleType))
        {
            reminder.ScheduleType = NormalizeScheduleType(scheduleType);
        }

        var interval = GetFirstArg(call, "interval_minutes", "minutes", "interval", "interval_min");
        if (TryParseDouble(interval, out var minutes))
        {
            reminder.IntervalSeconds = minutes * 60;
        }

        var fixedMessage = GetFirstArg(call, "fixed_message", "message", "content", "text");
        if (!string.IsNullOrWhiteSpace(fixedMessage))
        {
            reminder.FixedMessage = fixedMessage;
        }

        var useAiMessage = GetFirstArg(call, "use_ai_message", "ai_message", "ai", "use_ai");
        if (!string.IsNullOrWhiteSpace(useAiMessage))
        {
            reminder.UseAiMessage = ParseBool(useAiMessage, reminder.UseAiMessage);
        }

        var aiPrompt = GetFirstArg(call, "ai_prompt", "prompt");
        if (!string.IsNullOrWhiteSpace(aiPrompt))
        {
            reminder.AiPrompt = aiPrompt;
        }

        var timeOfDay = GetFirstArg(call, "time_of_day", "time", "at");
        if (!string.IsNullOrWhiteSpace(timeOfDay))
        {
            reminder.TimeOfDay = timeOfDay;
        }

        var daysOfWeek = GetFirstArg(call, "days_of_week", "weekdays", "days");
        if (!string.IsNullOrWhiteSpace(daysOfWeek))
        {
            reminder.DaysOfWeek = daysOfWeek;
        }

        var dayOfMonth = GetFirstArg(call, "day_of_month", "month_day");
        if (int.TryParse(dayOfMonth, out var parsedDay))
        {
            reminder.DayOfMonth = parsedDay;
        }

        var taskPrompt = GetFirstArg(call, "task_prompt", "task", "job", "instruction");
        if (!string.IsNullOrWhiteSpace(taskPrompt))
        {
            reminder.TaskPrompt = taskPrompt;
        }

        var saveOutput = GetFirstArg(call, "save_output", "save");
        if (!string.IsNullOrWhiteSpace(saveOutput))
        {
            reminder.SaveOutput = ParseBool(saveOutput, reminder.SaveOutput);
        }

        var outputDirectory = GetFirstArg(call, "output_directory", "output_dir", "output_path");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            reminder.OutputDirectory = outputDirectory;
        }

        SyncLegacyReminderIntervals(settings);
        settingsStore.Save(settings);
        return AgentToolResult.Success(
            "upsert_reminder",
            $"已保存提醒：{reminder.Title}。",
            $"提醒 `{reminder.Id}` 已保存：action={reminder.ActionType}, schedule={reminder.ScheduleType}, enabled={reminder.Enabled}, interval={reminder.IntervalSeconds / 60:0.##}min, time={reminder.TimeOfDay}, ai={reminder.UseAiMessage}。设置窗口会显示这项配置，提醒调度会在下一次轮询时生效。");
    }

    private AgentToolResult DeleteReminder(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return AgentToolResult.Error("delete_reminder", "missing_argument", "缺少参数：id。");
        }

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        settings.ReminderSchemaInitialized = true;
        var removed = settings.Reminders.RemoveAll(item => MatchesReminder(item, id));
        if (removed == 0)
        {
            return AgentToolResult.Error("delete_reminder", "not_found", $"没有找到提醒：{id}。");
        }

        SyncLegacyReminderIntervals(settings);
        settingsStore.Save(settings);
        return AgentToolResult.Success("delete_reminder", $"已删除 {removed} 个提醒。", $"已删除匹配 `{id}` 的提醒。");
    }

    private static bool MatchesReminder(ReminderSettings reminder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(reminder.Id, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reminder.Title, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reminder.Category, value, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeActionType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("task", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("job", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("工作", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("任务", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("搜索", StringComparison.OrdinalIgnoreCase))
        {
            return "agent_task";
        }

        return "reminder";
    }

    private static string NormalizeScheduleType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("daily", StringComparison.OrdinalIgnoreCase) || normalized.Contains("每天", StringComparison.OrdinalIgnoreCase) || normalized.Contains("每日", StringComparison.OrdinalIgnoreCase))
        {
            return "daily";
        }

        if (normalized.Contains("weekly", StringComparison.OrdinalIgnoreCase) || normalized.Contains("每周", StringComparison.OrdinalIgnoreCase))
        {
            return "weekly";
        }

        if (normalized.Contains("monthly", StringComparison.OrdinalIgnoreCase) || normalized.Contains("每月", StringComparison.OrdinalIgnoreCase))
        {
            return "monthly";
        }

        return "interval";
    }

    private static void SyncLegacyReminderIntervals(AppSettings settings)
    {
        var water = settings.Reminders.FirstOrDefault(item =>
            string.Equals(item.Id, "water", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Category, "water", StringComparison.OrdinalIgnoreCase));
        if (water is not null)
        {
            settings.WaterReminderIntervalSeconds = water.IntervalSeconds;
        }

        var movement = settings.Reminders.FirstOrDefault(item =>
            string.Equals(item.Id, "movement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Category, "movement", StringComparison.OrdinalIgnoreCase));
        if (movement is not null)
        {
            settings.MovementReminderIntervalSeconds = movement.IntervalSeconds;
        }
    }

    private AgentToolResult BuildContentResult(string tool, string summary, string content, int inlineLimit = 6000)
    {
        if (content.Length <= inlineLimit)
        {
            return AgentToolResult.Success(tool, summary, content);
        }

        var handle = store.SaveToolOutput(tool, content);
        var preview = content[..inlineLimit] + $"{Environment.NewLine}{Environment.NewLine}[内容过长，完整内容已保存为 handle：{handle}。需要更多内容时调用 handle_read。]";
        return AgentToolResult.Success(tool, $"{summary} 内容较长，已生成 handle：{handle}。", preview, handle);
    }

    private (string Name, string Reason)? FindSkillRoute(string userMessage)
    {
        var text = (userMessage ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var skills = EnumerateSkills().ToList();
        if (skills.Count == 0)
        {
            return null;
        }

        var explicitSkillIntent = ContainsAny(text, "skill", "技能", "能力")
            && ContainsAny(text, "调用", "使用", "执行", "读取", "激活", "跑一下", "用一下", "按");
        var taskIntent = ContainsAny(text, "帮我", "生成", "写", "查询", "统计", "创建", "处理", "执行", "整理", "保存", "记住", "分析", "检查", "修复");
        var best = skills
            .Select(skill => (skill, score: ScoreSkillRoute(text, skill.Name, skill.Description, explicitSkillIntent, taskIntent)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ToList();
        if (best.Count == 0)
        {
            return null;
        }

        var top = best[0];
        if (top.score < 3)
        {
            return null;
        }

        if (best.Count > 1 && best[1].score >= top.score - 1)
        {
            return null;
        }

        return (top.skill.Name, $"匹配分数 {top.score}，命中 skill 描述/名称。");
    }

    private static int ScoreSkillRoute(string userMessage, string skillName, string description, bool explicitSkillIntent, bool taskIntent)
    {
        var score = 0;
        var normalizedUser = NormalizeSkillName(userMessage);
        var normalizedName = NormalizeSkillName(skillName);
        if (!string.IsNullOrWhiteSpace(normalizedName)
            && normalizedUser.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            score += explicitSkillIntent ? 12 : 8;
        }

        if (userMessage.Contains(skillName, StringComparison.OrdinalIgnoreCase))
        {
            score += explicitSkillIntent ? 12 : 8;
        }

        var haystack = $"{skillName} {description}";
        foreach (var term in ExtractRouteTerms(userMessage).Take(20))
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += term.Length >= 3 ? 3 : 2;
            }
        }

        foreach (var term in ExtractRouteTerms(description).Take(40))
        {
            if (userMessage.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += term.Length >= 3 ? 3 : 2;
            }
        }

        if (!taskIntent && !explicitSkillIntent)
        {
            score -= 4;
        }

        return score;
    }

    private static IEnumerable<string> ExtractRouteTerms(string text)
    {
        foreach (Match match in Regex.Matches(text ?? "", @"[\p{IsCJKUnifiedIdeographs}A-Za-z0-9_\-]+"))
        {
            var value = match.Value.Trim('-', '_');
            if (value.Length is >= 2 and <= 18)
            {
                yield return value;
            }
        }
    }

    private sealed class SkillRuntimeGates
    {
        public List<string> StartupFiles { get; } = [];

        public List<string> FinalReviewFiles { get; } = [];

        public bool RequiresIndexSelectedFile { get; set; }

        public string IndexSelectionReason { get; set; } = "";
    }

    private static SkillRuntimeGates DiscoverSkillRuntimeGates(string skillRoot, string skillMarkdown)
    {
        var gates = new SkillRuntimeGates();
        var lines = (skillMarkdown ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var inRuntimeGates = false;
        var currentBucket = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (Regex.IsMatch(line, @"^#{1,6}\s+"))
            {
                inRuntimeGates = line.Contains("runtime", StringComparison.OrdinalIgnoreCase)
                    && (line.Contains("gate", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("gates", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("门禁", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("流程", StringComparison.OrdinalIgnoreCase));
                currentBucket = "";
                continue;
            }

            if (inRuntimeGates)
            {
                var normalizedLine = line.TrimStart('-', '*', ' ').Trim();
                if (normalizedLine.StartsWith("startup", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("start", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("before_start", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("启动", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("开始", StringComparison.OrdinalIgnoreCase))
                {
                    currentBucket = "startup";
                    foreach (var path in ExtractSkillPathMentions(normalizedLine))
                    {
                        AddExistingSkillPath(skillRoot, gates.StartupFiles, path);
                    }
                    continue;
                }

                if (normalizedLine.StartsWith("before_final", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("final_review", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("before_answer", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("输出前", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("最终", StringComparison.OrdinalIgnoreCase))
                {
                    currentBucket = "final";
                    foreach (var path in ExtractSkillPathMentions(normalizedLine))
                    {
                        AddExistingSkillPath(skillRoot, gates.FinalReviewFiles, path);
                    }
                    continue;
                }

                if (normalizedLine.StartsWith("must_use_index", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("index_selected_content", StringComparison.OrdinalIgnoreCase)
                    || normalizedLine.StartsWith("索引选择", StringComparison.OrdinalIgnoreCase))
                {
                    if (ContainsAny(normalizedLine, "true", "yes", "1", "必须", "启用"))
                    {
                        gates.RequiresIndexSelectedFile = true;
                        gates.IndexSelectionReason = normalizedLine;
                    }
                    continue;
                }

                if (currentBucket == "startup" || currentBucket == "final")
                {
                    foreach (var path in ExtractSkillPathMentions(normalizedLine))
                    {
                        AddExistingSkillPath(skillRoot, currentBucket == "startup" ? gates.StartupFiles : gates.FinalReviewFiles, path);
                    }
                }
            }

            var isFinalReviewLine = ContainsAny(line, "最终输出前", "输出前", "生成前", "答复前", "回复前", "before final", "before answer", "final review");
            var isStartupLine = ContainsAny(line, "先读取", "先读", "先看", "先打开", "第一步", "启动时", "开始时", "read first", "first read", "startup");
            foreach (var path in ExtractSkillPathMentions(line))
            {
                if (isFinalReviewLine)
                {
                    AddExistingSkillPath(skillRoot, gates.FinalReviewFiles, path);
                }
                else if (isStartupLine)
                {
                    AddExistingSkillPath(skillRoot, gates.StartupFiles, path);
                }
            }

            if (!gates.RequiresIndexSelectedFile && LooksLikeIndexRoutingInstruction(line))
            {
                gates.RequiresIndexSelectedFile = true;
                gates.IndexSelectionReason = line.Length <= 120 ? line : line[..120] + "...";
            }
        }

        if (!gates.RequiresIndexSelectedFile
            && gates.StartupFiles.Any(path => Path.GetFileName(path).Equals("index.md", StringComparison.OrdinalIgnoreCase))
            && ContainsAny(skillMarkdown ?? "", "对应文件", "相关文件", "根据索引", "根据 index", "索引中", "index 中"))
        {
            gates.RequiresIndexSelectedFile = true;
            gates.IndexSelectionReason = "startup gate 包含 index.md，且 SKILL.md 描述了按索引定位对应文件。";
        }

        return gates;
    }

    private static bool LooksLikeIndexRoutingInstruction(string line)
    {
        var hasIndex = ContainsAny(line, "index", "索引", "目录");
        var hasRoutingVerb = ContainsAny(line, "根据", "对应", "找到", "选择", "定位", "匹配", "路由");
        var hasReadVerb = ContainsAny(line, "读取", "读", "打开", "查看");
        return hasIndex && hasRoutingVerb && hasReadVerb;
    }

    private static void AddExistingSkillPath(string skillRoot, List<string> list, string path)
    {
        if (IsExistingSkillTextFile(skillRoot, path))
        {
            AddDistinct(list, NormalizeSkillRelativePath(path));
        }
    }

    private static List<string> DiscoverSkillFiles(string skillRoot, string skillMarkdown, bool requiredOnly)
    {
        var results = new List<string>();
        foreach (var line in (skillMarkdown ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var required = ContainsAny(line, "必须", "必需", "先读取", "先读", "输出前", "生成前", "required", "must", "before");
            if (requiredOnly != required)
            {
                continue;
            }

            foreach (var path in ExtractSkillPathMentions(line))
            {
                if (IsExistingSkillTextFile(skillRoot, path))
                {
                    AddDistinct(results, NormalizeSkillRelativePath(path));
                }
            }
        }

        if (!requiredOnly)
        {
            foreach (var path in DiscoverCommonSkillIndexFiles(skillRoot))
            {
                AddDistinct(results, path);
            }
        }

        if (requiredOnly)
        {
            var checklist = Path.Combine(skillRoot, "checklist.md");
            if (File.Exists(checklist))
            {
                AddDistinct(results, "checklist.md");
            }
        }

        return results;
    }

    private static IEnumerable<string> ExtractSkillPathMentions(string text)
    {
        foreach (Match match in Regex.Matches(text ?? "", @"(?<![\w.-])((?:knowledge|references|rules|schemas|schema|assets|templates|examples|scripts)[\\/][^\s`""'，。；；,;:：)）\]]+|checklist\.md|[A-Za-z0-9_\-]+\.md)"))
        {
            var path = match.Groups[1].Value.Trim().Trim('.', ',', ';', ':', '，', '。', '；', '：', ')', '）', ']');
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path.Replace('\\', '/');
            }
        }
    }

    private static IEnumerable<string> DiscoverCommonSkillIndexFiles(string skillRoot)
    {
        foreach (var relative in new[]
        {
            "knowledge/index.md",
            "references/index.md",
            "rules/index.md",
            "schemas/index.md",
            "schema/index.md",
            "README.md"
        })
        {
            if (File.Exists(Path.Combine(skillRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                yield return relative;
            }
        }
    }

    private static bool IsExistingSkillTextFile(string skillRoot, string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(skillRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(skillRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(candidate) && IsTextLikePath(candidate);
    }

    private static bool IsTextLikePath(string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext)
            || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }

    private string ResolveActiveSkillPath(string path)
    {
        if (activeSkill is null)
        {
            throw new InvalidOperationException("当前没有激活 skill。");
        }

        var root = Path.GetFullPath(activeSkill.RootPath);
        var trimmed = (path ?? "").Trim().Trim('"', '`').Replace('/', Path.DirectorySeparatorChar);
        var target = string.IsNullOrWhiteSpace(trimmed)
            ? root
            : Path.GetFullPath(Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(root, trimmed));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径超出当前 skill 目录，已拦截。");
        }

        GuardSensitivePath(target);
        return target;
    }

    private string GetActiveSkillRelativePath(string path)
    {
        if (activeSkill is null)
        {
            return path;
        }

        return NormalizeSkillRelativePath(Path.GetRelativePath(activeSkill.RootPath, path));
    }

    private IEnumerable<string> FindActiveSkillFileCandidates(string query)
    {
        if (activeSkill is null)
        {
            yield break;
        }

        var normalizedQuery = (query ?? "").Trim().Trim('"', '`').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(activeSkill.RootPath, "*", SearchOption.AllDirectories))
        {
            if (IsSensitivePath(file))
            {
                continue;
            }

            var relative = GetActiveSkillRelativePath(file);
            if (Path.GetFileName(file).Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || relative.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || relative.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static string NormalizeSkillRelativePath(string path)
    {
        return (path ?? "").Trim().Trim('"', '`').Replace('\\', '/').TrimStart('/');
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveWorkspacePath(string path)
    {
        var baseRoot = Path.GetFullPath(store.RootDirectory);
        var normalized = NormalizeAgentPath(path, baseRoot, Path.GetFullPath(store.WorkspaceDirectory));
        var target = string.IsNullOrWhiteSpace(normalized)
            ? baseRoot
            : Path.GetFullPath(Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(baseRoot, normalized));
        if (!target.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径超出桌宠 Agents 数据目录，已拦截。");
        }

        GuardSensitivePath(target);

        return target;
    }

    private static string NormalizeAgentPath(string path, string agentsRoot, string workspaceRoot)
    {
        var trimmed = (path ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "";
        }

        trimmed = trimmed.Replace('/', Path.DirectorySeparatorChar);
        var agentsPrefix = agentsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (trimmed.StartsWith(agentsPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, agentsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (trimmed.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var agentsMarker = $"agents{Path.DirectorySeparatorChar}";
        var agentsMarkerIndex = trimmed.IndexOf(agentsMarker, StringComparison.OrdinalIgnoreCase);
        if (agentsMarkerIndex >= 0)
        {
            return trimmed[(agentsMarkerIndex + agentsMarker.Length)..];
        }

        var marker = $"workspace{Path.DirectorySeparatorChar}";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            return trimmed[markerIndex..];
        }

        return trimmed;
    }

    private static void GuardSensitivePath(string path)
    {
        if (IsSensitivePath(path))
        {
            throw new InvalidOperationException("该文件可能包含 API key、token 或密钥，文件工具已拦截。请通过设置窗口修改配置。");
        }
    }

    private void GuardWritablePath(string path)
    {
        var target = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspaceRoot = Path.GetFullPath(store.WorkspaceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var profilePath = Path.GetFullPath(store.ProfilePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isWorkspace = string.Equals(target, workspaceRoot, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var isDefaultAgent = string.Equals(target, profilePath, StringComparison.OrdinalIgnoreCase);
        if (!isWorkspace && !isDefaultAgent)
        {
            throw new InvalidOperationException("Startup Permission Hook 已拦截：只能写入 workspace/** 或 default-agent.md。");
        }

        var knowledgeRoot = Path.GetFullPath(store.KnowledgeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(target, knowledgeRoot, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(knowledgeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("knowledge 知识库是只读资料区，桌宠不能自行修改世界观、人设或台词资料。请让用户手动维护这些文件。");
        }
    }

    private void GuardPythonWorkspacePath(string path)
    {
        var target = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspaceRoot = Path.GetFullPath(store.WorkspaceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(target, workspaceRoot, StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Python 只能读取和写入 workspace 内的文件。");
        }

        GuardSensitivePath(target);
    }

    private static string ValidatePythonCode(string code)
    {
        var blocked = new[]
        {
            "subprocess",
            "socket",
            "requests",
            "urllib",
            "http.client",
            "ctypes",
            "multiprocessing",
            "__import__",
            "importlib",
            "eval(",
            "exec(",
            "os.system",
            "os.popen",
            "os.remove",
            "os.unlink",
            "os.rmdir",
            "shutil.rmtree",
            "Path.unlink",
            ".unlink(",
            ".rmdir("
        };

        foreach (var token in blocked)
        {
            if ((code ?? "").Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return $"Python 代码触发权限 hook 拦截：包含被禁用的片段 `{token}`。";
            }
        }

        return "";
    }

    private static (string FileName, string[] Arguments)? ResolvePythonExecutable()
    {
        var candidates = new[]
        {
            (Path.Combine(AppContext.BaseDirectory, "python", "python.exe"), Array.Empty<string>()),
            (Path.Combine(AppContext.BaseDirectory, "UserData", "Agents", "workspace", "python", "python.exe"), Array.Empty<string>()),
            ("python", Array.Empty<string>()),
            ("py", new[] { "-3" })
        };

        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate.Item1) && !File.Exists(candidate.Item1))
            {
                continue;
            }

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = candidate.Item1,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var arg in candidate.Item2)
                {
                    info.ArgumentList.Add(arg);
                }
                info.ArgumentList.Add("--version");
                using var process = Process.Start(info);
                if (process is null)
                {
                    continue;
                }
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    continue;
                }
                if (process.ExitCode == 0)
                {
                    return (candidate.Item1, candidate.Item2);
                }
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static string BuildPythonSandboxPrelude(string workspaceRoot)
    {
        var rootLiteral = JsonSerializer.Serialize(Path.GetFullPath(workspaceRoot));
        return $$"""
        import builtins
        import io
        import os
        import pathlib
        import sys

        _AEMEATH_WORKSPACE_ROOT = os.path.realpath({{rootLiteral}})
        _AEMEATH_WORKSPACE_SCRIPTS = os.path.join(_AEMEATH_WORKSPACE_ROOT, "scripts")
        if _AEMEATH_WORKSPACE_SCRIPTS not in sys.path:
            sys.path.insert(0, _AEMEATH_WORKSPACE_SCRIPTS)
        _AEMEATH_ORIGINAL_OPEN = builtins.open

        def _aemeath_resolve_path(file):
            raw = os.fspath(file)
            path = raw if os.path.isabs(raw) else os.path.join(_AEMEATH_WORKSPACE_ROOT, raw)
            real = os.path.realpath(path)
            if real != _AEMEATH_WORKSPACE_ROOT and not real.startswith(_AEMEATH_WORKSPACE_ROOT + os.sep):
                raise PermissionError("Aemeath Python sandbox: path outside workspace is blocked: " + raw)
            return real

        def _aemeath_open(file, mode="r", *args, **kwargs):
            real = _aemeath_resolve_path(file)
            if any(flag in mode for flag in ("w", "a", "x", "+")):
                parent = os.path.dirname(real)
                if parent:
                    os.makedirs(parent, exist_ok=True)
            return _AEMEATH_ORIGINAL_OPEN(real, mode, *args, **kwargs)

        def _aemeath_blocked(*args, **kwargs):
            raise PermissionError("Aemeath Python sandbox: deletion, process and unrestricted mutation operations are disabled.")

        builtins.open = _aemeath_open
        io.open = _aemeath_open
        pathlib.Path.open = lambda self, mode="r", *args, **kwargs: _aemeath_open(str(self), mode, *args, **kwargs)
        def _aemeath_path_write_text(self, data, encoding=None, errors=None, newline=None):
            with _aemeath_open(str(self), "w", encoding=encoding or "utf-8", errors=errors, newline=newline) as f:
                return f.write(data)
        def _aemeath_path_write_bytes(self, data):
            with _aemeath_open(str(self), "wb") as f:
                return f.write(data)
        pathlib.Path.write_text = _aemeath_path_write_text
        pathlib.Path.write_bytes = _aemeath_path_write_bytes
        os.remove = _aemeath_blocked
        os.unlink = _aemeath_blocked
        os.rmdir = _aemeath_blocked
        os.removedirs = _aemeath_blocked
        os.rename = _aemeath_blocked
        os.replace = _aemeath_blocked
        os.system = _aemeath_blocked
        os.popen = _aemeath_blocked
        try:
            import shutil
            shutil.rmtree = _aemeath_blocked
            shutil.move = _aemeath_blocked
        except Exception:
            pass

        """;
    }

    private static bool IsSensitivePath(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "settings.local.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ".env", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase);
    }

    private string GetRelativeWorkspacePath(string path)
    {
        return Path.GetRelativePath(store.RootDirectory, path);
    }

    private static string GetArg(AgentToolCall call, string name)
    {
        return call.Arguments.TryGetValue(name, out var value) ? value : "";
    }

    private static string GetFirstArg(AgentToolCall call, params string[] names)
    {
        foreach (var name in names)
        {
            if (call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value, out parsed)
            || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed);
    }

    private static bool ParseBool(string value, bool fallback)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized is "true" or "1" or "yes" or "y" or "on" or "enable" or "enabled" or "开启" or "启用" or "是")
        {
            return true;
        }

        if (normalized is "false" or "0" or "no" or "n" or "off" or "disable" or "disabled" or "关闭" or "禁用" or "否")
        {
            return false;
        }

        return fallback;
    }

    private static string NormalizeMemoryType(string type)
    {
        var normalized = (type ?? "").Trim().ToLowerInvariant();
        if (normalized.Contains("长期", StringComparison.OrdinalIgnoreCase)
            || normalized is "long" or "permanent" or "长期记忆")
        {
            return "long";
        }

        if (normalized.Contains("短期", StringComparison.OrdinalIgnoreCase)
            || normalized is "short" or "domain" or "domains" or "recent" or "短期记忆")
        {
            return "short";
        }

        return "all";
    }

    private static string CleanMemoryContent(string content)
    {
        var text = content.Trim();
        var endMarkers = new[]
        {
            "请问要保存",
            "请问要将",
            "要保存为",
            "回复「长期」",
            "回复“长期”"
        };
        foreach (var marker in endMarkers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                text = text[..index].Trim();
            }
        }

        return text;
    }

    private (string Name, string? File) FindSkill(string name)
    {
        var all = EnumerateSkills().ToList();
        var exact = all.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact.File is not null)
        {
            return (exact.Name, exact.File);
        }

        var normalizedName = NormalizeSkillName(name);
        var normalized = all.FirstOrDefault(item => NormalizeSkillName(item.Name) == normalizedName);
        if (normalized.File is not null)
        {
            return (normalized.Name, normalized.File);
        }

        var contains = all
            .Where(item => item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                || name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)
                || NormalizeSkillName(item.Name).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)
                || normalizedName.Contains(NormalizeSkillName(item.Name), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return contains.Count == 1 ? (contains[0].Name, contains[0].File) : ("", null);
    }

    private IEnumerable<string> FindFileCandidates(string query)
    {
        var normalizedQuery = (query ?? "").Trim().Trim('"', '`');
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            yield break;
        }

        var root = Path.GetFullPath(store.RootDirectory);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (IsSensitivePath(file))
            {
                continue;
            }

            var relative = GetRelativeWorkspacePath(file);
            if (Path.GetFileName(file).Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || relative.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || relative.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private IReadOnlyList<string> FindSkillCandidates(string name)
    {
        var normalizedName = NormalizeSkillName(name);
        return EnumerateSkills()
            .Where(item => item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                || NormalizeSkillName(item.Name).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private IEnumerable<(string Name, string File, string Description)> EnumerateSkills()
    {
        foreach (var root in GetSkillRoots().Where(Directory.Exists))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in EnumerateSkillsInDirectory(root, 0, visited))
            {
                yield return skill;
            }
        }
    }

    private IEnumerable<(string Name, string File, string Description)> EnumerateSkillsInDirectory(
        string directory,
        int depth,
        HashSet<string> visited)
    {
        if (depth > MaxSkillDiscoveryDepth)
        {
            yield break;
        }

        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch
        {
            yield break;
        }

        if (!visited.Add(fullDirectory))
        {
            yield break;
        }

        var skillFile = Path.Combine(fullDirectory, "SKILL.md");
        if (File.Exists(skillFile))
        {
            var fallbackName = new DirectoryInfo(fullDirectory).Name;
            if (!TryParseSkillMetadata(skillFile, fallbackName, out var name, out var description))
            {
                yield break;
            }

            if (!string.Equals(name, "memory-saver", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fallbackName, "memory-saver", StringComparison.OrdinalIgnoreCase))
            {
                yield return (name, skillFile, description);
            }

            yield break;
        }

        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(fullDirectory)
                .Where(child => !Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var child in childDirectories)
        {
            foreach (var skill in EnumerateSkillsInDirectory(child, depth + 1, visited))
            {
                yield return skill;
            }
        }
    }

    private static bool TryParseSkillMetadata(string skillFile, string fallbackName, out string name, out string description)
    {
        name = fallbackName;
        description = "无描述";

        string text;
        try
        {
            text = SafeRead(skillFile, 30000);
        }
        catch
        {
            return false;
        }

        var frontMatterName = ExtractFrontMatterValue(text, "name");
        if (!string.IsNullOrWhiteSpace(frontMatterName))
        {
            name = frontMatterName;
        }
        else
        {
            var headingName = ExtractMarkdownTitle(text);
            if (!string.IsNullOrWhiteSpace(headingName))
            {
                name = headingName;
            }
        }

        var frontMatterDescription = ExtractFrontMatterValue(text, "description");
        description = string.IsNullOrWhiteSpace(frontMatterDescription)
            ? ExtractDescription(text)
            : frontMatterDescription;

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? "无描述" : description.Trim();
        return !string.IsNullOrWhiteSpace(name);
    }

    private static string NormalizeSkillName(string value)
    {
        return new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool IsEnvelopeProperty(string name, string toolProperty)
    {
        return string.Equals(name, "tool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, toolProperty, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "arguments", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "args", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "params", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "input", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetObject(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetJsonString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return JsonElementToString(property.Value);
            }
        }

        return null;
    }

    private static void AddObjectArguments(JsonElement element, Dictionary<string, string> args)
    {
        foreach (var property in element.EnumerateObject())
        {
            args[property.Name] = JsonElementToString(property.Value);
        }
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => element.GetRawText()
        };
    }

    private static string SafeRead(string path, int maxChars)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return text.Length <= maxChars ? text : text[..maxChars] + "\n\n[内容过长，已截断]";
    }

    private static string ExtractFrontMatterValue(string text, string key)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return "";
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return "";
        }

        var endMarker = trimmed.IndexOf("\n---", firstLineEnd + 1, StringComparison.Ordinal);
        if (endMarker < 0)
        {
            return "";
        }

        var frontMatter = trimmed[(firstLineEnd + 1)..endMarker];
        foreach (var line in frontMatter.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var field = line[..colon].Trim();
            if (!string.Equals(field, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return UnquoteScalar(line[(colon + 1)..].Trim());
        }

        return "";
    }

    private static string ExtractMarkdownTitle(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return "";
    }

    private static string UnquoteScalar(string value)
    {
        if (value.Length >= 2
            && ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                || (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))))
        {
            return value[1..^1].Trim();
        }

        return value;
    }

    private static string ExtractDescription(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                return line["description:".Length..].Trim().Trim('"');
            }
        }

        return "无描述";
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : "";
    }

    private IReadOnlyList<string> GetSkillRoots()
    {
        return [store.SkillDirectory];
    }
}
