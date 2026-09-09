using System.Collections.ObjectModel;
using NetChatx.Core;
using NetChatx.Storage.Models;

namespace NetChatx.Tui.Models;

public enum BufferType
{
    Console,
    DirectChat,
    GroupChat
}

public sealed class ChatBuffer
{
    public string Id { get; }
    public string Title { get; set; }
    public Jid? RemoteJid { get; }
    public BufferType Type { get; }
    public bool IsEncrypted { get; set; }
    public int UnreadCount { get; set; }
    public bool HasLoadedHistory { get; set; }
    public DateTimeOffset? OldestMessageTimestamp { get; set; }

    private readonly List<string> _displayLines = [];
    public IReadOnlyList<string> DisplayLines => _displayLines;

    public ChatBuffer(string id, string title, BufferType type, Jid? remoteJid = null)
    {
        Id = id;
        Title = title;
        Type = type;
        RemoteJid = remoteJid;
    }

    public void AddSystemMessage(string text)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _displayLines.Add($"[{timestamp}] * {text}");
    }

    public void AddChatMessage(ChatMessage msg)
    {
        if (!OldestMessageTimestamp.HasValue || msg.Timestamp < OldestMessageTimestamp.Value)
        {
            OldestMessageTimestamp = msg.Timestamp;
        }

        _displayLines.Add(FormatChatMessage(msg));
    }

    public void LoadHistory(IEnumerable<ChatMessage> messages)
    {
        _displayLines.Clear();
        foreach (var msg in messages)
        {
            if (!OldestMessageTimestamp.HasValue || msg.Timestamp < OldestMessageTimestamp.Value)
            {
                OldestMessageTimestamp = msg.Timestamp;
            }
            _displayLines.Add(FormatChatMessage(msg));
        }
        HasLoadedHistory = true;
    }

    public void PrependHistory(IEnumerable<ChatMessage> messages)
    {
        var formatted = new List<string>();
        foreach (var msg in messages)
        {
            if (!OldestMessageTimestamp.HasValue || msg.Timestamp < OldestMessageTimestamp.Value)
            {
                OldestMessageTimestamp = msg.Timestamp;
            }
            formatted.Add(FormatChatMessage(msg));
        }

        _displayLines.InsertRange(0, formatted);
    }

    public static string FormatChatMessage(ChatMessage msg)
    {
        string timestamp = msg.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        string sender = msg.Direction == MessageDirection.Outbound ? "Me" : (Jid.TryParse(msg.SenderJid, out var sj) ? (sj.Resource ?? sj.LocalPart ?? sj.Domain) : msg.SenderJid);
        string lockIcon = msg.IsEncrypted ? "🔒 " : "";
        string receiptIcon = msg.Direction == MessageDirection.Outbound ? (msg.IsRead ? " ✓✓" : " ✓") : "";

        return $"[{timestamp}] <{lockIcon}{sender}> {msg.Body}{receiptIcon}";
    }

    public void Clear()
    {
        _displayLines.Clear();
    }
}
