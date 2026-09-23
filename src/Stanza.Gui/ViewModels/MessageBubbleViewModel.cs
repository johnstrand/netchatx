using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Core;
using Stanza.Gui.Converters;
using Stanza.Gui.Helpers;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.ViewModels;

public sealed partial class MessageBubbleViewModel : ViewModelBase, IDisposable
{
    public static readonly Regex ImageUrlRegex = new(
        @"https?://[^\s<>""]+?\.(?:png|jpe?g|gif|webp|bmp)(?:\?[^\s<>""]*)?|(?:https?://(?:media|c|www)\.tenor\.com/[^\s<>""]+)|(?:https?://(?:media\d*|i)\.giphy\.com/[^\s<>""]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private GifAnimationPlayer? _gifPlayer;

    public Func<MessageBubbleViewModel, string, Task>? ToggleReactionHandler { get; set; }

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string? _rawBody;

    [ObservableProperty]
    private bool _isActionMessage;

    [ObservableProperty]
    private string? _actionContent;

    [ObservableProperty]
    private string _displayText = string.Empty;

    partial void OnBodyChanged(string value)
    {
        _gifPlayer?.Dispose();
        _gifPlayer = null;
        ExtractImageUrl(value);
        ExtractLinks(value);
        UpdateDisplayText();
        if (HasImage)
        {
            _ = LoadThumbnailAsync();
        }
    }

    [ObservableProperty]
    private IReadOnlyList<string> _links = [];

    [ObservableProperty]
    private bool _hasLinks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    [NotifyPropertyChangedFor(nameof(IsOutbound))]
    [NotifyPropertyChangedFor(nameof(SenderDisplayName))]
    private MessageDirection _direction = MessageDirection.Outbound;

    public bool IsOutbound => Direction == MessageDirection.Outbound;

    [ObservableProperty]
    private bool _isEdited;

    [ObservableProperty]
    private string? _replaceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTime))]
    [NotifyPropertyChangedFor(nameof(FormattedDateTime))]
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;

    partial void OnTimestampChanged(DateTimeOffset value)
    {
        if (MergedMessages.Count <= 1)
        {
            LatestTimestamp = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTime))]
    [NotifyPropertyChangedFor(nameof(FormattedDateTime))]
    private DateTimeOffset _latestTimestamp = default;

    public List<ChatMessage> MergedMessages { get; } = [];
    public HashSet<string> MergedMessageIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SenderDisplayName))]
    private string _senderName = "Me";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    public bool HasAvatar => Avatar is not null;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private IBrush? _avatarBackgroundBrush;

    private string? _senderDisplayName;

    public string SenderDisplayName
    {
        get => !string.IsNullOrWhiteSpace(_senderDisplayName)
            ? _senderDisplayName
            : ResolveSenderDisplayName(SenderName, Direction);
        set
        {
            if (SetProperty(ref _senderDisplayName, value))
            {
                OnPropertyChanged(nameof(SenderDisplayName));
                if (IsActionMessage && !string.IsNullOrEmpty(RawBody))
                {
                    var actionText = RawBody.Length > 3 ? RawBody.Substring(3).Trim() : string.Empty;
                    Body = $"_{value} {actionText}_";
                }
            }
        }
    }

    [ObservableProperty]
    private string _remoteJid = string.Empty;

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private string? _encryptionType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    private bool _isRead;

    partial void OnIsReadChanged(bool value)
    {
        if (value)
        {
            foreach (var m in MergedMessages)
            {
                m.IsRead = true;
            }
        }
    }

    [ObservableProperty]
    private string? _stanzaId;

    [ObservableProperty]
    private string? _originId;

    [ObservableProperty]
    private bool _showDateHeader;

    [ObservableProperty]
    private string? _dateHeader;

    [ObservableProperty]
    private bool _showReadMarkerDivider;

    [ObservableProperty]
    private string? _readMarkerDividerText;

    [ObservableProperty]
    private string? _receiptTooltip;

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _isGif;

    [ObservableProperty]
    private bool _isOnlyImage;

    [ObservableProperty]
    private bool _isUnstyled;

    [ObservableProperty]
    private Bitmap? _imageThumbnail;

    [ObservableProperty]
    private bool _isLoadingImage;

    public ObservableCollection<ReactionCountViewModel> Reactions { get; } = [];

    [ObservableProperty]
    private EmojiPickerViewModel? _emojiPicker;

    public IReadOnlyList<string> QuickEmojis => EmojiPicker?.QuickEmojis.ToList() ?? [.. EmojiData.DefaultQuickEmojis];

    [ObservableProperty]
    private string? _rawXml;

    [ObservableProperty]
    private bool _isRawXmlVisible;

    public static bool Use24HourClock { get; set; } = true;
    public static bool ShowInlinePreviews { get; set; } = true;
    public static bool AutoDownloadMedia { get; set; } = true;

    public bool IsPreviewVisible => HasImage && ShowInlinePreviews;

    public IBrush BubbleBackground => Direction == MessageDirection.Outbound
        ? DirectionToBackgroundConverter.OutboundBrush
        : DirectionToBackgroundConverter.InboundBrush;

    public void RefreshBubbleStyle()
    {
        OnPropertyChanged(nameof(BubbleBackground));
    }

    public void RefreshTimeDisplay()
    {
        OnPropertyChanged(nameof(FormattedTime));
        OnPropertyChanged(nameof(FormattedDateTime));
    }

    public void RefreshPreviewVisibility()
    {
        UpdateDisplayText();
        OnPropertyChanged(nameof(IsPreviewVisible));
    }

    public void UpdateDisplayText(string? sourceBody = null)
    {
        var bodyToUse = !string.IsNullOrEmpty(sourceBody) ? sourceBody : Body;
        if (HasImage && ShowInlinePreviews && !string.IsNullOrEmpty(ImageUrl))
        {
            var cleaned = bodyToUse.Replace(ImageUrl, string.Empty);
            cleaned = Regex.Replace(cleaned, @"^\s*[\r\n]+|[\r\n]+\s*$", string.Empty).Trim();
            DisplayText = cleaned;
        }
        else
        {
            DisplayText = bodyToUse;
        }

        IsOnlyImage = HasImage && ShowInlinePreviews && string.IsNullOrWhiteSpace(DisplayText);
    }

    [RelayCommand]
    public void ToggleRawXml()
    {
        if (!IsRawXmlVisible)
        {
            UpdateRawXml();
        }
        IsRawXmlVisible = !IsRawXmlVisible;
    }

    public void UpdateRawXml()
    {
        if (MergedMessages.Count == 0)
        {
            return;
        }

        if (MergedMessages.Count == 1)
        {
            var single = MergedMessages[0];
            RawXml = !string.IsNullOrWhiteSpace(single.RawXml) ? single.RawXml : GenerateFallbackRawXml(single);
        }
        else
        {
            RawXml = string.Join("\n\n", MergedMessages.Select(m =>
                !string.IsNullOrWhiteSpace(m.RawXml) ? m.RawXml : GenerateFallbackRawXml(m)));
        }
    }

    public string FormattedTime
    {
        get
        {
            var time = LatestTimestamp != default ? LatestTimestamp : Timestamp;
            var local = time.ToLocalTime();
            var today = DateTime.Today;
            var timeFmt = Use24HourClock ? "HH:mm" : "h:mm tt";

            if (local.Date == today)
            {
                return local.ToString(timeFmt);
            }
            else if (local.Date == today.AddDays(-1))
            {
                return $"Yesterday {local.ToString(timeFmt)}";
            }
            else if (local.Year == today.Year)
            {
                return local.ToString($"MMM d, {timeFmt}");
            }
            else
            {
                return local.ToString($"yyyy-MM-dd {timeFmt}");
            }
        }
    }

    public string FormattedDateTime => (LatestTimestamp != default ? LatestTimestamp : Timestamp).ToLocalTime().ToString(Use24HourClock ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd h:mm tt");

    public string ReceiptIcon => Direction == MessageDirection.Outbound ? (IsRead ? "◈" : "◇") : string.Empty;

    [RelayCommand]
    public async Task QuickReactAsync(string emoji)
    {
        if (ToggleReactionHandler is not null && !string.IsNullOrWhiteSpace(emoji))
        {
            await ToggleReactionHandler(this, emoji);
        }
    }

    public void UpdateReactions(IEnumerable<MessageReaction> rawReactions, string currentAccountJid)
    {
        var grouped = rawReactions
            .GroupBy(r => r.Emoji)
            .Select(g => new
            {
                Emoji = g.Key,
                Count = g.Count(),
                IsReactedByMe = g.Any(r => r.SenderJid.Equals(currentAccountJid, StringComparison.OrdinalIgnoreCase) ||
                                           r.SenderJid.StartsWith(currentAccountJid + "/", StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        Reactions.Clear();
        foreach (var item in grouped)
        {
            Reactions.Add(new ReactionCountViewModel(item.Emoji, item.Count, item.IsReactedByMe, emoji => QuickReactAsync(emoji)));
        }
    }

    public Action<MessageBubbleViewModel>? ReplyRequested { get; set; }
    public Action<MessageBubbleViewModel>? EditRequested { get; set; }
    public Action<MessageBubbleViewModel>? DeleteRequested { get; set; }

    [RelayCommand]
    public void Reply()
    {
        ReplyRequested?.Invoke(this);
    }

    [RelayCommand]
    public void Edit()
    {
        EditRequested?.Invoke(this);
    }

    [RelayCommand]
    public void Delete()
    {
        DeleteRequested?.Invoke(this);
    }

    [RelayCommand]
    public async Task CopyTextAsync()
    {
        var textToCopy = !string.IsNullOrEmpty(Body) ? Body : (ImageUrl ?? string.Empty);
        if (string.IsNullOrEmpty(textToCopy)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(textToCopy);
                }
            }
        }
        catch
        {
            // Soft failure
        }
    }

    public void ExtractImageUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            HasImage = false;
            ImageUrl = null;
            IsGif = false;
            UpdateDisplayText(body);
            return;
        }

        var match = ImageUrlRegex.Match(body);
        if (match.Success)
        {
            ImageUrl = match.Value;
            HasImage = true;
            if (ImageUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                ImageUrl.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) ||
                ImageUrl.Contains("giphy.com", StringComparison.OrdinalIgnoreCase))
            {
                IsGif = true;
            }
        }
        else
        {
            HasImage = false;
            ImageUrl = null;
            IsGif = false;
        }
        UpdateDisplayText(body);
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ImageUrl) || ImageThumbnail is not null) return;
        IsLoadingImage = true;
        try
        {
            var (bmp, gifFrames) = await AsyncImageLoader.LoadImageOrGifAsync(ImageUrl);
            if (bmp is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _gifPlayer?.Dispose();
                    _gifPlayer = null;

                    ImageThumbnail = bmp;
                    IsLoadingImage = false;

                    if (gifFrames is not null && gifFrames.Count > 1)
                    {
                        IsGif = true;
                        _gifPlayer = new GifAnimationPlayer(gifFrames, nextFrame =>
                        {
                            ImageThumbnail = nextFrame;
                        });
                    }
                    else if (ImageUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                             ImageUrl.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) ||
                             ImageUrl.Contains("giphy.com", StringComparison.OrdinalIgnoreCase))
                    {
                        IsGif = true;
                    }
                });
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsLoadingImage = false);
            }
        }
        catch
        {
            Dispatcher.UIThread.Post(() => IsLoadingImage = false);
        }
    }

    public void ExtractOobImageUrl(string? rawXml)
    {
        if (HasImage || string.IsNullOrWhiteSpace(rawXml)) return;

        try
        {
            var elem = Stanza.Core.Xml.XmppElement.Parse(rawXml);
            var oobUrl = Stanza.Protocol.Xeps.Sharing.Xep0066OutOfBandData.ExtractOobUrl(elem);
            if (!string.IsNullOrWhiteSpace(oobUrl))
            {
                var match = ImageUrlRegex.Match(oobUrl);
                if (match.Success)
                {
                    ImageUrl = match.Value;
                    HasImage = true;
                    if (ImageUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                        ImageUrl.Contains("tenor.com", StringComparison.OrdinalIgnoreCase) ||
                        ImageUrl.Contains("giphy.com", StringComparison.OrdinalIgnoreCase))
                    {
                        IsGif = true;
                    }
                }
                UpdateDisplayText();
            }
        }
        catch
        {
            // Soft failure parsing raw XML for OOB
        }
    }

    public void ExtractStyling(string? rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml)) return;

        try
        {
            var elem = Stanza.Core.Xml.XmppElement.Parse(rawXml);
            if (Stanza.Protocol.Xeps.Messaging.Xep0393MessageStyling.IsUnstyled(elem))
            {
                IsUnstyled = true;
            }
        }
        catch
        {
            // Soft failure parsing raw XML for styling
        }
    }

    public void ExtractLinks(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            Links = [];
            HasLinks = false;
            return;
        }

        var segments = LinkParser.Parse(body);
        var foundLinks = segments
            .Where(s => s.IsLink && !string.IsNullOrEmpty(s.NavigateUri))
            .Select(s => s.NavigateUri!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Links = foundLinks;
        HasLinks = foundLinks.Count > 0;
    }

    [RelayCommand]
    public void OpenUrl(string? url)
    {
        var target = url ?? Links.FirstOrDefault();
        if (!string.IsNullOrEmpty(target))
        {
            UrlLauncher.OpenUrl(target);
        }
    }

    [RelayCommand]
    public async Task CopyLinkAsync(string? url)
    {
        var toCopy = url ?? Links.FirstOrDefault();
        if (!string.IsNullOrEmpty(toCopy))
        {
            await UrlLauncher.CopyToClipboardAsync(toCopy);
        }
    }

    [RelayCommand]
    public void OpenImage()
    {
        if (string.IsNullOrEmpty(ImageUrl)) return;
        UrlLauncher.OpenUrl(ImageUrl);
    }

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

    public static string GenerateFallbackRawXml(ChatMessage msg)
    {
        var elem = new Stanza.Core.Xml.XmppElement("message");
        if (!string.IsNullOrEmpty(msg.StanzaId))
            elem.Attr("id", msg.StanzaId);
        else if (!string.IsNullOrEmpty(msg.Id))
            elem.Attr("id", msg.Id);

        if (msg.Direction == MessageDirection.Outbound)
        {
            elem.Attr("from", msg.AccountJid);
            elem.Attr("to", msg.RemoteJid);
        }
        else
        {
            elem.Attr("from", msg.SenderJid);
            elem.Attr("to", msg.AccountJid);
        }

        elem.Attr("xml:lang", "en");
        elem.Attr("type", "chat");
        elem.Attr("xmlns", "jabber:client");

        if (!string.IsNullOrEmpty(msg.Body))
        {
            elem.Child("body", text: msg.Body);
        }

        if (msg.IsEncrypted)
        {
            elem.Child(new Stanza.Core.Xml.XmppElement("encrypted", "urn:xmpp:omemo:2"));
        }

        return elem.ToXmlString(indent: true);
    }

    public static MessageBubbleViewModel FromChatMessage(
        ChatMessage msg,
        string accountJid = "",
        SettingsRepository? settingsRepo = null,
        IEnumerable<string>? quickEmojis = null,
        string? senderDisplayName = null,
        Bitmap? avatar = null,
        string? initials = null,
        IBrush? avatarBackgroundBrush = null)
    {
        var effectiveSenderName = msg.Direction == MessageDirection.Outbound ? "Me" : msg.SenderJid;
        var effectiveSenderDisplayName = !string.IsNullOrWhiteSpace(senderDisplayName)
            ? senderDisplayName
            : ResolveSenderDisplayName(msg.SenderJid, msg.Direction);

        var isAction = msg.Body.StartsWith("/me ", StringComparison.OrdinalIgnoreCase) ||
                        msg.Body.Equals("/me", StringComparison.OrdinalIgnoreCase);

        var displayBody = msg.Body;
        string? actionText = null;

        if (isAction)
        {
            actionText = msg.Body.Length > 3 ? msg.Body.Substring(3).Trim() : string.Empty;
            displayBody = $"_{effectiveSenderDisplayName} {actionText}_";
        }

        var vm = new MessageBubbleViewModel
        {
            Id = msg.Id,
            RawBody = msg.Body,
            Body = displayBody,
            IsActionMessage = isAction,
            ActionContent = actionText,
            Direction = msg.Direction,
            Timestamp = msg.Timestamp,
            SenderName = effectiveSenderName,
            SenderDisplayName = effectiveSenderDisplayName,
            Avatar = avatar,
            Initials = !string.IsNullOrEmpty(initials) ? initials : Helpers.AvatarHelper.GetInitials(effectiveSenderDisplayName),
            AvatarBackgroundBrush = avatarBackgroundBrush ?? Helpers.AvatarHelper.GetAvatarColorBrush(msg.SenderJid),
            RemoteJid = msg.RemoteJid ?? string.Empty,
            IsEncrypted = msg.IsEncrypted,
            EncryptionType = msg.EncryptionType,
            IsRead = msg.IsRead,
            StanzaId = msg.StanzaId,
            OriginId = msg.OriginId,
            ReplaceId = msg.ReplaceId,
            IsEdited = !string.IsNullOrEmpty(msg.ReplaceId),
            RawXml = !string.IsNullOrWhiteSpace(msg.RawXml) ? msg.RawXml : GenerateFallbackRawXml(msg)
        };

        var emojisToUse = quickEmojis ?? EmojiData.DefaultQuickEmojis;
        vm.EmojiPicker = new EmojiPickerViewModel(accountJid, emojisToUse, emoji => vm.QuickReactAsync(emoji), settingsRepo);

        vm.AddMessageRecord(msg);
        vm.LatestTimestamp = msg.Timestamp;

        vm.ExtractImageUrl(msg.Body);
        if (!vm.HasImage)
        {
            vm.ExtractOobImageUrl(msg.RawXml);
        }
        vm.ExtractStyling(msg.RawXml);
        vm.ExtractLinks(msg.Body);
        if (vm.HasImage && ShowInlinePreviews && AutoDownloadMedia)
        {
            _ = vm.LoadThumbnailAsync();
        }

        return vm;
    }

    public static string ResolveSenderDisplayName(string? sender, MessageDirection direction = MessageDirection.Inbound)
    {
        if (direction == MessageDirection.Outbound)
        {
            return "Me";
        }

        if (string.IsNullOrWhiteSpace(sender))
        {
            return "Unknown";
        }

        if (string.Equals(sender, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return "Me";
        }

        if (Jid.TryParse(sender, out var jid))
        {
            if (!string.IsNullOrWhiteSpace(jid.LocalPart))
            {
                // In groupchats (conference/muc), the resource is often the participant nickname
                if ((jid.Domain.Contains("conference", StringComparison.OrdinalIgnoreCase) ||
                     jid.Domain.Contains("muc", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(jid.Resource))
                {
                    return jid.Resource;
                }

                return FormatNameFromJid(jid);
            }

            if (!string.IsNullOrWhiteSpace(jid.Resource))
            {
                return jid.Resource;
            }

            return jid.Domain;
        }

        return sender;
    }

    public static string FormatNameFromJid(Jid jid)
    {
        if (!string.IsNullOrWhiteSpace(jid.LocalPart))
        {
            var local = jid.LocalPart.Trim();
            if (local.Contains('.') || local.Contains('_'))
            {
                var parts = local.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
                return string.Join(" ", parts.Select(Capitalize));
            }

            return Capitalize(local);
        }

        if (!string.IsNullOrWhiteSpace(jid.Resource))
        {
            return jid.Resource;
        }

        return jid.Domain;
    }

    public static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length == 1) return text.ToUpperInvariant();
        if (text.Any(char.IsUpper) && text.Any(char.IsLower))
        {
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        return char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();
    }

    public bool ContainsMessageId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (Id == id || StanzaId == id || OriginId == id) return true;
        return MergedMessageIds.Contains(id);
    }

    public void AddMessageRecord(ChatMessage msg)
    {
        MergedMessages.Add(msg);
        if (!string.IsNullOrEmpty(msg.Id)) MergedMessageIds.Add(msg.Id);
        if (!string.IsNullOrEmpty(msg.StanzaId)) MergedMessageIds.Add(msg.StanzaId);
        if (!string.IsNullOrEmpty(msg.OriginId)) MergedMessageIds.Add(msg.OriginId);
    }

    public bool CanMergeWith(ChatMessage msg, bool enableMerging, int thresholdSeconds)
    {
        if (!enableMerging || thresholdSeconds <= 0) return false;

        // Must match message direction
        if (Direction != msg.Direction) return false;

        // For inbound, must match sender
        if (Direction == MessageDirection.Inbound &&
            !string.Equals(SenderName, msg.SenderJid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Must be on the same calendar day
        var timeToCompare = LatestTimestamp != default ? LatestTimestamp : Timestamp;
        if (timeToCompare.ToLocalTime().Date != msg.Timestamp.ToLocalTime().Date)
        {
            return false;
        }

        // Must be within threshold seconds
        var diffSeconds = Math.Abs((msg.Timestamp - timeToCompare).TotalSeconds);
        return diffSeconds <= thresholdSeconds;
    }

    public void MergeMessage(ChatMessage msg)
    {
        AddMessageRecord(msg);

        if (msg.Timestamp > LatestTimestamp)
        {
            LatestTimestamp = msg.Timestamp;
        }

        Body = string.Join("\n", MergedMessages.Select(m => m.Body).Where(b => !string.IsNullOrEmpty(b)));
        IsRead = MergedMessages.All(m => m.IsRead);
        UpdateRawXml();

        ExtractImageUrl(Body);
        ExtractLinks(Body);
        if (HasImage && ImageThumbnail is null && ShowInlinePreviews && AutoDownloadMedia)
        {
            _ = LoadThumbnailAsync();
        }
    }

    [RelayCommand]
    public async Task LoadMediaAsync()
    {
        if (HasImage && ImageThumbnail is null && !IsLoadingImage)
        {
            await LoadThumbnailAsync();
        }
    }

    public bool RemoveMessageById(string messageOrStanzaId)
    {
        var match = MergedMessages.FirstOrDefault(m =>
            m.Id == messageOrStanzaId ||
            m.StanzaId == messageOrStanzaId ||
            m.OriginId == messageOrStanzaId);

        if (match is not null)
        {
            MergedMessages.Remove(match);
            if (!string.IsNullOrEmpty(match.Id)) MergedMessageIds.Remove(match.Id);
            if (!string.IsNullOrEmpty(match.StanzaId)) MergedMessageIds.Remove(match.StanzaId);
            if (!string.IsNullOrEmpty(match.OriginId)) MergedMessageIds.Remove(match.OriginId);

            if (MergedMessages.Count > 0)
            {
                var first = MergedMessages[0];
                Id = first.Id;
                StanzaId = first.StanzaId;
                OriginId = first.OriginId;
                ReplaceId = first.ReplaceId;
                Timestamp = first.Timestamp;
                Body = string.Join("\n", MergedMessages.Select(m => m.Body).Where(b => !string.IsNullOrEmpty(b)));
                LatestTimestamp = MergedMessages.Max(m => m.Timestamp);
                IsRead = MergedMessages.All(m => m.IsRead);
                UpdateRawXml();
                ExtractImageUrl(Body);
                ExtractLinks(Body);
            }
            else
            {
                Id = string.Empty;
                StanzaId = null;
                OriginId = null;
                ReplaceId = null;
                LatestTimestamp = Timestamp;
                RawXml = null;
            }
            return true;
        }
        return false;
    }

    public void UpdateMessageRecord(ChatMessage msg)
    {
        var existing = MergedMessages.FirstOrDefault(m =>
            m.Id == msg.Id ||
            (!string.IsNullOrEmpty(m.StanzaId) && m.StanzaId == msg.StanzaId) ||
            (!string.IsNullOrEmpty(m.OriginId) && m.OriginId == msg.OriginId));

        if (existing is not null)
        {
            if (string.IsNullOrEmpty(existing.StanzaId) && !string.IsNullOrEmpty(msg.StanzaId))
            {
                existing.StanzaId = msg.StanzaId;
                MergedMessageIds.Add(msg.StanzaId);
            }
            if (string.IsNullOrEmpty(existing.OriginId) && !string.IsNullOrEmpty(msg.OriginId))
            {
                existing.OriginId = msg.OriginId;
                MergedMessageIds.Add(msg.OriginId);
            }
            if (msg.IsRead)
            {
                existing.IsRead = true;
            }
            if (!string.IsNullOrEmpty(msg.ReplaceId))
            {
                existing.ReplaceId = msg.ReplaceId;
                existing.Body = msg.Body;
                IsEdited = true;
                ReplaceId = msg.ReplaceId;
            }
        }

        if (string.IsNullOrEmpty(StanzaId) && !string.IsNullOrEmpty(msg.StanzaId))
        {
            StanzaId = msg.StanzaId;
        }
        if (string.IsNullOrEmpty(OriginId) && !string.IsNullOrEmpty(msg.OriginId))
        {
            OriginId = msg.OriginId;
        }
        if (msg.IsRead && !IsRead)
        {
            IsRead = MergedMessages.All(m => m.IsRead);
        }
        if (!string.IsNullOrEmpty(msg.ReplaceId) && ReplaceId != msg.ReplaceId)
        {
            ReplaceId = msg.ReplaceId;
            IsEdited = true;
            Body = string.Join("\n", MergedMessages.Select(m => m.Body).Where(b => !string.IsNullOrEmpty(b)));
            ExtractImageUrl(Body);
            ExtractLinks(Body);
        }
    }

    public void UpdateMessageContent(string originalId, string newBody, string? newRawXml)
    {
        var existing = MergedMessages.FirstOrDefault(m =>
            m.Id == originalId ||
            (!string.IsNullOrEmpty(m.StanzaId) && m.StanzaId == originalId) ||
            (!string.IsNullOrEmpty(m.OriginId) && m.OriginId == originalId));

        if (existing is not null)
        {
            existing.ReplaceId = originalId;
            existing.Body = newBody;
            if (!string.IsNullOrEmpty(newRawXml)) existing.RawXml = newRawXml;
        }

        IsEdited = true;
        ReplaceId = originalId;
        if (MergedMessages.Count > 0)
        {
            Body = string.Join("\n", MergedMessages.Select(m => m.Body).Where(b => !string.IsNullOrEmpty(b)));
            UpdateRawXml();
        }
        else
        {
            Body = newBody;
            if (!string.IsNullOrEmpty(newRawXml)) RawXml = newRawXml;
        }
        ExtractImageUrl(Body);
        ExtractLinks(Body);
    }

    public void Dispose()
    {
        _gifPlayer?.Dispose();
        _gifPlayer = null;
    }
}
