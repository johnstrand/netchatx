using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.ViewModels;

public sealed partial class MessageBubbleViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    private MessageDirection _direction = MessageDirection.Outbound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTime))]
    [NotifyPropertyChangedFor(nameof(FormattedDateTime))]
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private string _senderName = "Me";

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private string? _encryptionType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    private bool _isRead;

    [ObservableProperty]
    private string? _stanzaId;

    [ObservableProperty]
    private bool _showDateHeader;

    [ObservableProperty]
    private string? _dateHeader;

    public string FormattedTime
    {
        get
        {
            var local = Timestamp.ToLocalTime();
            var today = DateTime.Today;

            if (local.Date == today)
            {
                return local.ToString("HH:mm");
            }
            else if (local.Date == today.AddDays(-1))
            {
                return $"Yesterday {local:HH:mm}";
            }
            else if (local.Year == today.Year)
            {
                return local.ToString("MMM d, HH:mm");
            }
            else
            {
                return local.ToString("yyyy-MM-dd HH:mm");
            }
        }
    }

    public string FormattedDateTime => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ReceiptIcon => Direction == MessageDirection.Outbound ? (IsRead ? "✓✓" : "✓") : string.Empty;

    public static string FormatDateHeader(DateTimeOffset dto)
    {
        var local = dto.ToLocalTime();
        var today = DateTime.Today;

        if (local.Date == today)
            return "Today";
        if (local.Date == today.AddDays(-1))
            return "Yesterday";
        if (local.Year == today.Year)
            return local.ToString("MMMM d");

        return local.ToString("MMMM d, yyyy");
    }

    public static MessageBubbleViewModel FromChatMessage(ChatMessage msg)
    {
        return new MessageBubbleViewModel
        {
            Id = msg.Id,
            Body = msg.Body,
            Direction = msg.Direction,
            Timestamp = msg.Timestamp,
            SenderName = msg.Direction == MessageDirection.Outbound ? "Me" : msg.SenderJid,
            IsEncrypted = msg.IsEncrypted,
            EncryptionType = msg.EncryptionType,
            IsRead = msg.IsRead,
            StanzaId = msg.StanzaId
        };
    }
}
