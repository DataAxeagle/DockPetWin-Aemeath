using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DockPetWin.Core.Agents;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DockPetWin;

public partial class AgentChatWindow : Window
{
    private static readonly Random StatusRandom = new();
    private readonly AgentStore store = new();
    private readonly AgentChatClient client = new();
    private readonly Action<string>? showPetBubble;
    private readonly Func<string, bool>? beforeUserMessage;
    private readonly Action<string, string>? afterAssistantReply;
    private readonly Func<string?>? extraSystemContextProvider;
    private CancellationTokenSource? activeRequest;
    private string assistantDisplayName = "爱弥斯";

    public AgentChatWindow(
        Action<string>? showPetBubble = null,
        Func<string, bool>? beforeUserMessage = null,
        Action<string, string>? afterAssistantReply = null,
        Func<string?>? extraSystemContextProvider = null)
    {
        this.showPetBubble = showPetBubble;
        this.beforeUserMessage = beforeUserMessage;
        this.afterAssistantReply = afterAssistantReply;
        this.extraSystemContextProvider = extraSystemContextProvider;
        InitializeComponent();
        Icon = AppImageLoader.TryLoad(AppImageLoader.AppIconPath);
        HeaderIconImage.Source = AppImageLoader.TryLoad(AppImageLoader.AppIconPath);
        Loaded += (_, _) => LoadConversation();
        Closed += (_, _) => activeRequest?.Cancel();
    }

    private void LoadConversation()
    {
        ConversationPanel.Children.Clear();
        var settings = store.LoadSettings();
        assistantDisplayName = string.IsNullOrWhiteSpace(settings.PetName) ? "爱弥斯" : settings.PetName.Trim();
        Title = $"{assistantDisplayName}对话";
        TitleText.Text = $"{assistantDisplayName}对话";
        StatusText.Text = $"已加载 {assistantDisplayName} Agent";

        foreach (var message in store.LoadRecentHistory(settings.MaxHistoryMessages * 2))
        {
            ShowExchangeBubble(
                message.Role == "user" ? "你" : assistantDisplayName,
                message.Content,
                alignRight: message.Role == "user");
        }

        InputBox.Focus();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || activeRequest is not null)
        {
            return;
        }

        var isFirstConversationMessage = store.LoadRecentHistory(1).Count == 0;
        InputBox.Clear();
        store.AppendHistory("user", text);
        ShowExchangeBubble("你", text, alignRight: true);
        if (beforeUserMessage?.Invoke(text) == true)
        {
            var localReply = $"{assistantDisplayName}点点头，继续忙自己的事。";
            store.AppendHistory("assistant", localReply);
            ShowExchangeBubble(assistantDisplayName, localReply, alignRight: false);
            showPetBubble?.Invoke(localReply);
            StatusText.Text = "继续小屋生活";
            InputBox.Focus();
            return;
        }

        SendButton.IsEnabled = false;
        StatusText.Text = BuildThinkingStatus(assistantDisplayName);
        activeRequest = new CancellationTokenSource();

        try
        {
            var settings = store.LoadSettings();
            var profile = store.BuildSystemPrompt(settings);
            var extraSystemContext = extraSystemContextProvider?.Invoke();
            if (!string.IsNullOrWhiteSpace(extraSystemContext))
            {
                profile += $"{Environment.NewLine}{Environment.NewLine}{extraSystemContext.Trim()}";
            }
            if (isFirstConversationMessage)
            {
                profile += $"{Environment.NewLine}{Environment.NewLine}{FirstConversationContext()}";
            }

            var history = store.LoadRecentHistory(settings.MaxHistoryMessages * 2);
            var reply = await client.SendWithToolsAsync(
                settings,
                profile,
                history,
                text,
                store,
                activeRequest.Token,
                trace => Dispatcher.Invoke(() =>
                {
                    if (!WantsVisibleToolTrace(text))
                    {
                        StatusText.Text = IsKnowledgeTool(trace.Tool)
                            ? BuildKnowledgeThinkingStatus(assistantDisplayName)
                            : BuildThinkingStatus(assistantDisplayName);
                        return;
                    }

                    ShowToolTrace(trace);
                }));
            store.AppendHistory("assistant", reply);
            ShowExchangeBubble(assistantDisplayName, reply, alignRight: false);
            showPetBubble?.Invoke(reply);
            afterAssistantReply?.Invoke(text, reply);
            var autoSavedPreference = await TryAutoSaveLongTermPreferenceAsync(settings, text, reply, activeRequest.Token);
            await TryCompressHistoryIfNeeded(settings, activeRequest.Token);
            StatusText.Text = autoSavedPreference ? "已回复；记下了一点偏好" : "已回复";
        }
        catch (Exception ex)
        {
            var error = $"[错误] {ex.Message}";
            store.AppendHistory("assistant", error);
            ShowExchangeBubble(assistantDisplayName, error, alignRight: false);
            StatusText.Text = "调用失败，请检查 API key / 网络 / 模型配置";
        }
        finally
        {
            activeRequest?.Dispose();
            activeRequest = null;
            SendButton.IsEnabled = true;
            InputBox.Focus();
        }
    }

    private static string FirstConversationContext()
    {
        return """
        # 首次对话开场语境

        这是当前用户第一次真正和爱弥斯对话。当前用户就是漂泊者，不是陌生玩家，也不是旁观第三方。
        默认背景：漂泊者从索拉里斯其他地方完成任务后回到拉海洛，和爱弥斯再次相见。爱弥斯已经认出漂泊者，也记得两人之间的关系。
        第一轮回复应自然承接“重逢”而不是自我介绍或询问用户是谁：可以带一点拉海洛、频率、归来、任务后的疲惫和终于听见彼此的感觉，但不要堆设定，不要写成长篇旁白。
        即便用户只是发一句普通问候，也要以爱弥斯自己的视角回应漂泊者已经回来这件事。
        """;
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }

    private void ClearConversation_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "会清空当前聊天上下文和主动读取的旧摘要。旧对话会保留在本地归档里，之后只有你明确要找旧聊天时才会再查。",
            "清除当前对话？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var message = store.ResetActiveConversationContext();
        ConversationPanel.Children.Clear();
        StatusText.Text = message;
        ShowExchangeBubble(assistantDisplayName, "嗯，我把这次频率重新归零了。旧记录还在本地，需要时你再叫我去翻。", alignRight: false);
        InputBox.Focus();
    }

    private void ShowExchangeBubble(string speaker, string text, bool alignRight)
    {
        var wrapper = new StackPanel
        {
            HorizontalAlignment = alignRight ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left,
            MaxWidth = 560,
            Margin = new Thickness(4, 4, 4, 10)
        };

        wrapper.Children.Add(new TextBlock
        {
            Text = speaker,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(123, 104, 121)),
            FontSize = 11,
            HorizontalAlignment = alignRight ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 8, 3)
        });

        wrapper.Children.Add(new Border
        {
            Background = alignRight
                ? new SolidColorBrush(WpfColor.FromRgb(224, 247, 255))
                : new SolidColorBrush(WpfColor.FromRgb(255, 240, 247)),
            BorderBrush = alignRight
                ? new SolidColorBrush(WpfColor.FromRgb(168, 218, 232))
                : new SolidColorBrush(WpfColor.FromRgb(232, 185, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 21,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(48, 42, 54))
            }
        });

        ConversationPanel.Children.Add(wrapper);
        ConversationScroll.ScrollToEnd();
    }

    private void ShowToolTrace(AgentToolTrace trace)
    {
        var status = trace.Ok ? "完成" : "失败";
        var accent = trace.Ok
            ? new SolidColorBrush(WpfColor.FromRgb(66, 135, 96))
            : new SolidColorBrush(WpfColor.FromRgb(180, 76, 76));
        var expander = new Expander
        {
            Header = $"工具 {trace.Round}: {trace.Tool} · {status}",
            IsExpanded = false,
            MaxWidth = 560,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Margin = new Thickness(4, 2, 4, 10),
            Foreground = accent,
            Background = new SolidColorBrush(WpfColor.FromRgb(255, 250, 253))
        };

        var body = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        body.Children.Add(new TextBlock
        {
            Text = trace.Summary,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(48, 42, 54)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });
        body.Children.Add(new TextBlock
        {
            Text = $"参数：{trace.ArgumentsJson}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(123, 104, 121)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var preview = string.IsNullOrWhiteSpace(trace.Preview) ? "(无内容)" : trace.Preview;
        if (!string.IsNullOrWhiteSpace(trace.Handle))
        {
            preview += $"{Environment.NewLine}{Environment.NewLine}handle: {trace.Handle}";
        }

        if (!string.IsNullOrWhiteSpace(trace.ErrorCode))
        {
            preview += $"{Environment.NewLine}{Environment.NewLine}error: {trace.ErrorCode}";
        }

        body.Children.Add(new TextBlock
        {
            Text = preview,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(48, 42, 54)),
            FontSize = 11
        });
        expander.Content = body;
        ConversationPanel.Children.Add(expander);
        ConversationScroll.ScrollToEnd();
    }

    private static string BuildThinkingStatus(string name)
    {
        var templates = new[]
        {
            "{0}正在思考...",
            "{0}正在整理脑内便签...",
            "{0}正在把话说顺一点...",
            "{0}正在赶 deadline...",
            "{0}正在对齐设定...",
            "{0}正在认真转圈..."
        };

        return string.Format(templates[StatusRandom.Next(templates.Length)], name);
    }

    private static string BuildKnowledgeThinkingStatus(string name)
    {
        var templates = new[]
        {
            "{0}正在翻设定...",
            "{0}正在查角色小抄...",
            "{0}正在捋世界观...",
            "{0}正在把人设对齐...",
            "{0}正在从知识库里找线索..."
        };

        return string.Format(templates[StatusRandom.Next(templates.Length)], name);
    }

    private static bool IsKnowledgeTool(string tool)
    {
        return string.Equals(tool, "search_knowledge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "read_knowledge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "knowledge_search", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "knowledge_read", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WantsVisibleToolTrace(string text)
    {
        return ContainsAny(
            text,
            "显示工具",
            "工具流程",
            "工具调用",
            "调用流程",
            "展开工具",
            "调试",
            "debug",
            "tool trace",
            "tool_call",
            "search_knowledge",
            "read_knowledge",
            "直接调用工具",
            "强制调用工具");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> TryAutoSaveLongTermPreferenceAsync(
        AgentChatSettings settings,
        string userMessage,
        string assistantReply,
        CancellationToken cancellationToken)
    {
        if (!LooksLikeLongTermPreference(userMessage))
        {
            return false;
        }

        try
        {
            var candidate = await client.ExtractAutoLongTermMemoryAsync(
                settings,
                store.ReadMemorySummary(longTerm: true, maxChars: 8000),
                store.LoadRecentHistory(settings.MaxHistoryMessages * 2),
                userMessage,
                assistantReply,
                cancellationToken);
            candidate = CleanAutoMemoryCandidate(candidate);
            if (string.IsNullOrWhiteSpace(candidate)
                || candidate.Contains("无值得保存", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("无", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            store.SaveMemoryCandidate(candidate, longTerm: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeLongTermPreference(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 4)
        {
            return false;
        }

        if (ContainsAny(text, "你喜欢", "爱弥斯喜欢", "你记得我什么", "我喜欢什么吗"))
        {
            return false;
        }

        return ContainsAny(
            text,
            "我喜欢",
            "我最喜欢",
            "我更喜欢",
            "我偏好",
            "我爱吃",
            "我爱喝",
            "我爱看",
            "我爱玩",
            "我不喜欢",
            "我讨厌",
            "我不吃",
            "我不能吃",
            "我过敏",
            "我的爱好",
            "我习惯",
            "我通常",
            "我希望你",
            "以后叫我",
            "可以叫我",
            "记住",
            "记得",
            "别忘",
            "以后要知道",
            "这些要记住");
    }

    private static string CleanAutoMemoryCandidate(string candidate)
    {
        var lines = candidate.Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("```", StringComparison.Ordinal))
            .Select(line => line.TrimStart('-', '*', '•', ' ', '\t'))
            .Where(line => !line.Contains("API key", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("token", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("密码", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        var cleaned = string.Join(Environment.NewLine, lines).Trim();
        return cleaned.Length <= 1000 ? cleaned : cleaned[..1000];
    }

    private async Task TryCompressHistoryIfNeeded(AgentChatSettings settings, CancellationToken cancellationToken)
    {
        var sessionUserMessages = store.CountSessionUserMessages();
        if (sessionUserMessages <= 0 || sessionUserMessages % 30 != 0)
        {
            return;
        }

        try
        {
            StatusText.Text = "正在压缩最近对话记忆...";
            var summary = await client.SummarizeAsync(
                settings,
                store.LoadRollingSummary(),
                store.LoadAllHistory().TakeLast(60).ToList(),
                cancellationToken);
            store.SaveRollingSummary(summary);
        }
        catch
        {
            StatusText.Text = "已回复；记忆压缩稍后再试";
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = store.RootDirectory,
            UseShellExecute = true
        });
    }
}
