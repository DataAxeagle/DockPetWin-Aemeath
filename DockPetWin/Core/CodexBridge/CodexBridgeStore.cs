using System.IO;
using System.Text.Json;

namespace DockPetWin.Core.CodexBridge;

public sealed class CodexBridgeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string rootDirectory;
    private readonly string inboxPath;
    private readonly string outboxPath;
    private int consumedInboxLines;

    public CodexBridgeStore()
    {
        rootDirectory = Path.Combine(AppContext.BaseDirectory, "UserData", "CodexBridge");
        inboxPath = Path.Combine(rootDirectory, "inbox.jsonl");
        outboxPath = Path.Combine(rootDirectory, "outbox.jsonl");
        Directory.CreateDirectory(rootDirectory);
        EnsureFile(inboxPath);
        EnsureFile(outboxPath);
        consumedInboxLines = CountLines(inboxPath);
    }

    public string RootDirectory => rootDirectory;
    public string InboxPath => inboxPath;
    public string OutboxPath => outboxPath;

    public IReadOnlyList<CodexBridgeMessage> ReadNewInboxMessages()
    {
        var lines = ReadAllLinesShared(inboxPath);
        if (consumedInboxLines >= lines.Count)
        {
            consumedInboxLines = lines.Count;
            return [];
        }

        var messages = new List<CodexBridgeMessage>();
        foreach (var line in lines.Skip(consumedInboxLines))
        {
            var message = TryParseMessage(line);
            if (message is not null && !string.IsNullOrWhiteSpace(message.Message))
            {
                messages.Add(message);
            }
        }

        consumedInboxLines = lines.Count;
        return messages;
    }

    public void MarkInboxConsumed()
    {
        consumedInboxLines = CountLines(inboxPath);
    }

    public void AppendOutboxQuestion(string message)
    {
        AppendJsonLine(outboxPath, new CodexBridgeMessage
        {
            Type = "user_message",
            Title = "Question for Codex",
            Message = message,
            Time = DateTime.UtcNow,
            Source = "DockPetWin"
        });
    }

    public static void AppendNotification(string inboxPath, string title, string message)
    {
        AppendJsonLine(inboxPath, new CodexBridgeMessage
        {
            Type = "task_done",
            Title = string.IsNullOrWhiteSpace(title) ? "Codex" : title,
            Message = message,
            Time = DateTime.UtcNow,
            Source = "Codex"
        });
    }

    private static CodexBridgeMessage? TryParseMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CodexBridgeMessage>(line, JsonOptions);
        }
        catch
        {
            return new CodexBridgeMessage
            {
                Title = "Codex",
                Message = line.Trim(),
                Time = DateTime.UtcNow,
                Source = "Codex"
            };
        }
    }

    private static void AppendJsonLine(string path, CodexBridgeMessage message)
    {
        EnsureFile(path);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(json);
    }

    private static int CountLines(string path)
    {
        return ReadAllLinesShared(path).Count;
    }

    private static IReadOnlyList<string> ReadAllLinesShared(string path)
    {
        EnsureFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void EnsureFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }
    }
}
