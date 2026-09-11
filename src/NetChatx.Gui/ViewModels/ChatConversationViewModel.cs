using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Gui.Helpers;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Sharing;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class ChatConversationViewModel : ViewModelBase
{
    private readonly XmppClient? _client;
    private readonly MessageRepository _messageRepo;
    private readonly Xep0313MessageArchiveManagement? _mamManager;
    private readonly Xep0384OmemoManager? _omemoManager;
    private readonly Xep0363HttpFileUpload? _httpUploadManager;
    private readonly Xep0444Reactions? _reactionsManager;
    private readonly Xep0333ChatMarkers? _chatMarkers;
    private readonly Xep0085ChatStates? _chatStates;
    private readonly SettingsRepository? _settingsRepo;
    private readonly string _accountJid;

    private List<string> _quickEmojis = [.. EmojiData.DefaultQuickEmojis];
    private System.Threading.CancellationTokenSource? _remoteComposingCts;
    private System.Threading.CancellationTokenSource? _localPauseCts;
    private ChatState? _lastSentLocalState;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private Jid _remoteJid;

    [ObservableProperty]
    private bool _isGroupChat;

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private byte[]? _pendingImageBytes;

    [ObservableProperty]
    private Bitmap? _pendingImagePreview;

    [ObservableProperty]
    private string? _pendingImageFileName;

    [ObservableProperty]
    private string? _pendingImageSizeText;

    [ObservableProperty]
    private bool _hasPendingImage;

    [ObservableProperty]
    private bool _isRemoteComposing;

    [ObservableProperty]
    private bool _isComposing;

    [ObservableProperty]
    private bool _hasLoadedHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOlderButtonText))]
    private bool _isLoadingOlderHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncButtonText))]
    private bool _isSyncing;

    public string SyncButtonText => IsSyncing ? "Syncing... ⏳" : "Sync 🔄";

    [ObservableProperty]
    private DateTimeOffset? _oldestMessageTimestamp;

    public string LoadOlderButtonText => IsLoadingOlderHistory ? "Loading older messages..." : "▲ Load Older Messages";

    public ObservableCollection<MessageBubbleViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonIcon))]
    [NotifyPropertyChangedFor(nameof(SendButtonToolTip))]
    private bool _isEditingMessage;

    [ObservableProperty]
    private string? _editingMessageId;

    [ObservableProperty]
    private string? _editingMessagePreviewText;

    private string? _savedDraftText;

    public string SendButtonIcon => IsEditingMessage ? "✓" : "➤";
    public string SendButtonToolTip => IsEditingMessage ? "Save changes (Enter)" : "Send Message (Enter)";

    public event Action? EditStarted;

    public event Action? ScrollToBottomRequested;
    public event Action<ChatMessage>? MessageProcessed;

    public void RequestScrollToBottom()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ScrollToBottomRequested?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => ScrollToBottomRequested?.Invoke());
        }
    }

    public ChatConversationViewModel(
        string accountJid,
        string id,
        string title,
        Jid remoteJid,
        bool isGroupChat,
        MessageRepository messageRepo,
        XmppClient? client = null,
        Xep0313MessageArchiveManagement? mamManager = null,
        Xep0384OmemoManager? omemoManager = null,
        Xep0363HttpFileUpload? httpUploadManager = null,
        Xep0444Reactions? reactionsManager = null,
        Xep0333ChatMarkers? chatMarkers = null,
        Xep0085ChatStates? chatStates = null,
        SettingsRepository? settingsRepo = null)
    {
        _accountJid = accountJid;
        _id = id;
        _title = title;
        _remoteJid = remoteJid;
        _isGroupChat = isGroupChat;
        _messageRepo = messageRepo;
        _client = client;
        _mamManager = mamManager;
        _omemoManager = omemoManager;
        _httpUploadManager = httpUploadManager;
        _reactionsManager = reactionsManager;
        _chatMarkers = chatMarkers;
        _chatStates = chatStates;
        _settingsRepo = settingsRepo;
    }

    public void HandleRemoteChatState(ChatState state)
    {
        _remoteComposingCts?.Cancel();
        _remoteComposingCts = null;

        if (state == ChatState.Composing)
        {
            IsRemoteComposing = true;
            var cts = new System.Threading.CancellationTokenSource();
            _remoteComposingCts = cts;

            Task.Delay(TimeSpan.FromSeconds(10), cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    void Apply() => IsRemoteComposing = false;
                    if (Dispatcher.UIThread.CheckAccess()) Apply();
                    else Dispatcher.UIThread.Post(Apply);
                }
            }, TaskScheduler.Default);
        }
        else
        {
            IsRemoteComposing = false;
        }
    }

    partial void OnInputTextChanged(string value)
    {
        _localPauseCts?.Cancel();
        _localPauseCts = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (_lastSentLocalState == ChatState.Composing || _lastSentLocalState == ChatState.Paused)
            {
                SendLocalChatState(ChatState.Active);
            }
            return;
        }

        if (_lastSentLocalState != ChatState.Composing)
        {
            SendLocalChatState(ChatState.Composing);
        }

        var cts = new System.Threading.CancellationTokenSource();
        _localPauseCts = cts;

        Task.Delay(TimeSpan.FromSeconds(5), cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled && _lastSentLocalState == ChatState.Composing)
            {
                SendLocalChatState(ChatState.Paused);
            }
        }, TaskScheduler.Default);
    }

    private void SendLocalChatState(ChatState state)
    {
        if (_chatStates is null || _client is null || IsGroupChat) return;

        _lastSentLocalState = state;
        _ = Task.Run(async () =>
        {
            try
            {
                await _chatStates.SendChatStateAsync(RemoteJid, state);
            }
            catch
            {
                // Soft failure sending chat state
            }
        });
    }

    public async Task EnsureHistoryLoadedAsync()
    {
        if (!HasLoadedHistory)
        {
            await LoadHistoryAsync();
        }
        await MarkUnreadMessagesAsReadAsync();
    }

    public void MarkMessageAsRead(string stanzaOrMessageId)
    {
        void Apply()
        {
            foreach (var msg in Messages)
            {
                if (msg.Id == stanzaOrMessageId || msg.StanzaId == stanzaOrMessageId)
                {
                    msg.IsRead = true;
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    public async Task MarkUnreadMessagesAsReadAsync()
    {
        var markedIds = await _messageRepo.MarkUnreadMessagesAsReadAsync(_accountJid, RemoteJid.ToString());
        if (markedIds.Count == 0) return;

        void Apply()
        {
            foreach (var msg in Messages)
            {
                if (msg.Direction == MessageDirection.Inbound)
                {
                    msg.IsRead = true;
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }

        if (_chatMarkers is not null)
        {
            foreach (var id in markedIds)
            {
                try
                {
                    await _chatMarkers.SendDisplayedMarkerAsync(RemoteJid, id);
                }
                catch
                {
                    // Soft failure sending displayed marker
                }
            }
        }
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        if (_settingsRepo is not null)
        {
            try
            {
                _quickEmojis = await _settingsRepo.GetQuickEmojisAsync(_accountJid);
            }
            catch
            {
                // Fallback to default
            }
        }

        // 1. Immediately load whatever is cached in SQLite for responsive UI
        var dbMessages = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 50);
        Messages.Clear();
        foreach (var msg in dbMessages)
        {
            AddOrUpdateMessage(msg);
        }
        await LoadReactionsForCurrentMessagesAsync();
        UpdateDateHeaders();
        HasLoadedHistory = true;
        RequestScrollToBottom();

        // 2. Query MAM to sync latest messages from the server
        if (_mamManager is not null)
        {
            await SyncArchiveAsync();
        }
    }

    private static (string body, bool isEncrypted) ExtractMessageBody(MessageStanza m)
    {
        if (!string.IsNullOrEmpty(m.Body))
        {
            return (m.Body, false);
        }

        var encElem = m.RawElement.Element("encrypted", "eu.siacs.conversations.axolotl")
                   ?? m.RawElement.Element("encrypted", "urn:xmpp:omemo:2")
                   ?? m.RawElement.Element("encrypted", "urn:xmpp:omemo:1");
        if (encElem is not null)
        {
            return ("[Encrypted Message]", true);
        }

        return (string.Empty, false);
    }

    public static ChatMessage? ParseMamMessage(
        MamMessageItem item,
        string accountJid,
        Jid defaultRemoteJid,
        bool isGroupChat,
        XmppClient? client = null)
    {
        var m = item.Message;
        var (body, isEnc) = ExtractMessageBody(m);
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var originIdElem = m.RawElement.Element("origin-id", "urn:xmpp:sid:0");
        var stanzaIdElem = m.RawElement.Element("stanza-id", "urn:xmpp:sid:0");
        string? stanzaId = stanzaIdElem?.GetAttr("id") ?? m.Id;
        string? originId = originIdElem?.GetAttr("id");

        var parsedAccountJid = Jid.TryParse(accountJid, out var accJid) ? accJid : null;
        bool isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
                       || (m.From?.EqualsBare(client?.BoundJid) == true);

        Jid effectiveRemote;
        if (isGroupChat)
        {
            effectiveRemote = defaultRemoteJid;
        }
        else if (isFromSelf)
        {
            effectiveRemote = (m.To ?? defaultRemoteJid).BareJid;
        }
        else
        {
            effectiveRemote = (m.From ?? defaultRemoteJid).BareJid;
        }

        string senderJidStr = (m.From ?? (isFromSelf ? (parsedAccountJid ?? effectiveRemote) : effectiveRemote)).ToString();

        return new ChatMessage
        {
            Id = $"mam_{accountJid}_{effectiveRemote}_{item.ArchiveId}",
            AccountJid = accountJid,
            RemoteJid = effectiveRemote.ToString(),
            SenderJid = senderJidStr,
            Body = body,
            Direction = isFromSelf ? MessageDirection.Outbound : MessageDirection.Inbound,
            Timestamp = item.Timestamp,
            StanzaId = !string.IsNullOrEmpty(stanzaId) ? stanzaId : item.ArchiveId,
            OriginId = originId,
            IsEncrypted = isEnc,
            EncryptionType = isEnc ? "OMEMO" : null,
            RawXml = m.ToXmlString(indent: true)
        };
    }

    private ChatMessage? ParseMamMessage(MamMessageItem item)
    {
        return ParseMamMessage(item, _accountJid, RemoteJid, IsGroupChat, _client);
    }

    private async Task<List<ChatMessage>> ProcessMamMessagesAsync(IEnumerable<MamMessageItem> items)
    {
        var chatMsgs = new List<ChatMessage>();
        var parsedAccountJid = Jid.TryParse(_accountJid, out var parsedAcc) ? parsedAcc : null;
        foreach (var item in items)
        {
            var m = item.Message;
            bool isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
                           || (m.From?.EqualsBare(_client?.BoundJid) == true);

            if (Xep0444Reactions.TryExtractReaction(m.RawElement, isCarbonSent: isFromSelf, out var reactArgs))
            {
                await HandleIncomingReactionAsync(reactArgs);
                continue;
            }

            var chatMsg = ParseMamMessage(item);
            if (chatMsg is not null)
            {
                chatMsgs.Add(chatMsg);
                AddOrUpdateMessage(chatMsg);
            }
        }
        return chatMsgs;
    }

    [RelayCommand]
    public async Task SyncArchiveAsync()
    {
        if (IsSyncing || _mamManager is null) return;
        IsSyncing = true;

        try
        {
            Jid? archiveJid = IsGroupChat ? RemoteJid : null;
            Jid? withJid = IsGroupChat ? null : RemoteJid;

            var latestBubble = Messages.LastOrDefault();
            DateTimeOffset? startTimestamp = latestBubble?.Timestamp;

            string? beforeId = null;
            string? afterId = null;
            const int maxPages = 10;
            int pagesFetched = 0;

            while (pagesFetched < maxPages)
            {
                MamQueryResult mamResult;
                if (startTimestamp.HasValue)
                {
                    mamResult = await _mamManager.QueryArchiveAsync(
                        withJid: withJid,
                        archiveJid: archiveJid,
                        maxResults: 50,
                        start: startTimestamp.Value,
                        after: afterId);
                }
                else
                {
                    mamResult = await _mamManager.QueryArchiveAsync(
                        withJid: withJid,
                        archiveJid: archiveJid,
                        maxResults: 50,
                        before: beforeId);
                }

                pagesFetched++;

                if (mamResult.Messages.Count == 0)
                {
                    if (startTimestamp.HasValue && pagesFetched == 1)
                    {
                        // Fallback to querying the latest page with RSM <before/>
                        // in case the server ignores 'start', returns error, or clock drift exists
                        startTimestamp = null;
                        pagesFetched = 0;
                        continue;
                    }
                    break;
                }

                var chatMsgs = await ProcessMamMessagesAsync(mamResult.Messages);

                if (chatMsgs.Count > 0)
                {
                    await _messageRepo.SaveMessagesAsync(chatMsgs);
                }

                if (mamResult.IsComplete)
                    break;

                if (startTimestamp.HasValue)
                {
                    string? nextAfter = !string.IsNullOrEmpty(mamResult.LastId)
                        ? mamResult.LastId
                        : mamResult.Messages.LastOrDefault()?.ArchiveId;

                    if (string.IsNullOrEmpty(nextAfter) || nextAfter == afterId)
                        break;
                    afterId = nextAfter;
                }
                else
                {
                    string? nextBefore = !string.IsNullOrEmpty(mamResult.FirstId)
                        ? mamResult.FirstId
                        : mamResult.Messages.FirstOrDefault()?.ArchiveId;

                    if (string.IsNullOrEmpty(nextBefore) || nextBefore == beforeId)
                        break;
                    beforeId = nextBefore;
                }
            }

            await LoadReactionsForCurrentMessagesAsync();
            UpdateDateHeaders();
            RequestScrollToBottom();
        }
        catch
        {
            // Soft failure on MAM sync
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    public async Task LoadOlderHistoryAsync()
    {
        if (IsLoadingOlderHistory) return;
        IsLoadingOlderHistory = true;

        try
        {
            var oldestBubble = Messages.FirstOrDefault();
            if (oldestBubble is null) return;

            var oldestTimestamp = oldestBubble.Timestamp;
            var oldestStanzaId = oldestBubble.StanzaId;

            // 1. Fetch from SQLite before oldest timestamp
            var olderLocal = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 50, before: oldestTimestamp);

            // 2. If SQLite has fewer than 20 older messages, query MAM paging backwards
            if (olderLocal.Count < 20 && _mamManager is not null)
            {
                try
                {
                    // Pass the oldest known archive ID to RSM <before>{id}</before> to fetch preceding messages
                    var mamResult = await _mamManager.QueryArchiveAsync(
                        withJid: IsGroupChat ? null : RemoteJid,
                        archiveJid: IsGroupChat ? RemoteJid : null,
                        maxResults: 50,
                        before: oldestStanzaId,
                        end: string.IsNullOrEmpty(oldestStanzaId) ? oldestTimestamp : null);

                    var chatMsgs = await ProcessMamMessagesAsync(mamResult.Messages);

                    if (chatMsgs.Count > 0)
                    {
                        await _messageRepo.SaveMessagesAsync(chatMsgs);
                    }

                    // Reload older from SQLite after ingesting MAM
                    olderLocal = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 50, before: oldestTimestamp);
                }
                catch
                {
                    // Soft failure on MAM query
                }
            }

            foreach (var msg in olderLocal)
            {
                AddOrUpdateMessage(msg);
            }

            if (Messages.Count > 0)
            {
                OldestMessageTimestamp = Messages[0].Timestamp;
            }

            await LoadReactionsForCurrentMessagesAsync();
            UpdateDateHeaders();
        }
        finally
        {
            IsLoadingOlderHistory = false;
        }
    }

    public async Task LoadReactionsForCurrentMessagesAsync()
    {
        if (Messages.Count == 0) return;

        var messageIds = Messages.Select(m => m.Id).ToList();
        var rawReactions = await _messageRepo.GetReactionsForMessagesAsync(_accountJid, messageIds);

        var reactionsByMsgId = rawReactions.GroupBy(r => r.MessageId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var bubble in Messages)
        {
            if (reactionsByMsgId.TryGetValue(bubble.Id, out var msgReactions))
            {
                bubble.UpdateReactions(msgReactions, _accountJid);
            }
            else
            {
                bubble.UpdateReactions([], _accountJid);
            }
        }
    }

    public async Task ToggleReactionAsync(MessageBubbleViewModel bubble, string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;

        // Check if I already reacted with this emoji
        var mySenderJid = _client?.BoundJid.ToString() ?? _accountJid;
        var existingForMsg = await _messageRepo.GetReactionsForMessagesAsync(_accountJid, [bubble.Id]);

        var myCurrentEmojis = existingForMsg
            .Where(r => r.SenderJid.Equals(mySenderJid, StringComparison.OrdinalIgnoreCase) ||
                        r.SenderJid.StartsWith(_accountJid + "/", StringComparison.OrdinalIgnoreCase) ||
                        r.SenderJid.Equals(_accountJid, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Emoji)
            .Distinct()
            .ToList();

        if (myCurrentEmojis.Contains(emoji))
        {
            myCurrentEmojis.Remove(emoji);
        }
        else
        {
            myCurrentEmojis.Add(emoji);
        }

        // Save new reaction set in DB
        await _messageRepo.SaveReactionsAsync(_accountJid, RemoteJid.ToString(), bubble.Id, mySenderJid, myCurrentEmojis, isGroupChat: IsGroupChat);

        // Update UI
        var updatedReactions = await _messageRepo.GetReactionsForMessagesAsync(_accountJid, [bubble.Id]);
        bubble.UpdateReactions(updatedReactions, _accountJid);

        // Send XEP-0444 Reaction stanza over wire
        if (_reactionsManager is not null && _client is not null)
        {
            var targetId = !string.IsNullOrEmpty(bubble.StanzaId)
                ? bubble.StanzaId
                : (!string.IsNullOrEmpty(bubble.OriginId) ? bubble.OriginId : bubble.Id);
            var stanzaType = IsGroupChat ? MessageStanza.TypeGroupChat : MessageStanza.TypeChat;
            try
            {
                await _reactionsManager.SendReactionAsync(RemoteJid, targetId, myCurrentEmojis, stanzaType);
            }
            catch
            {
                // Soft failure over wire
            }
        }
    }

    public async Task HandleIncomingReactionAsync(ReactionEventArgs args)
    {
        var bubble = Messages.FirstOrDefault(m =>
            m.Id == args.TargetMessageId ||
            (!string.IsNullOrEmpty(m.StanzaId) && m.StanzaId == args.TargetMessageId) ||
            (!string.IsNullOrEmpty(m.OriginId) && m.OriginId == args.TargetMessageId));

        string targetId = bubble?.Id ?? args.TargetMessageId;
        string effectiveSenderJid = args.IsCarbonSent ? _accountJid : args.SenderJid.ToString();

        await _messageRepo.SaveReactionsAsync(
            _accountJid,
            RemoteJid.ToString(),
            targetId,
            effectiveSenderJid,
            args.Emojis,
            isGroupChat: IsGroupChat);

        if (bubble is not null)
        {
            var updatedReactions = await _messageRepo.GetReactionsForMessagesAsync(_accountJid, [bubble.Id]);
            PostToUi(() => bubble.UpdateReactions(updatedReactions, _accountJid));
        }
    }

    public void StageImageAttachment(byte[] imageBytes, string? fileName = null)
    {
        if (imageBytes is null || imageBytes.Length == 0) return;

        fileName ??= $"image_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png";
        double kb = imageBytes.Length / 1024.0;
        string sizeText = kb >= 1024 ? $"{kb / 1024.0:F1} MB" : $"{kb:F0} KB";

        Bitmap? previewBitmap = null;
        try
        {
            using var ms = new MemoryStream(imageBytes);
            previewBitmap = new Bitmap(ms);
        }
        catch
        {
            // Soft failure generating UI preview bitmap (e.g. headless unit tests or unsupported preview format)
        }

        PendingImagePreview?.Dispose();
        PendingImageBytes = imageBytes;
        PendingImageFileName = fileName;
        PendingImageSizeText = sizeText;
        PendingImagePreview = previewBitmap;
        HasPendingImage = true;
    }

    [RelayCommand]
    public void ClearPendingImage()
    {
        PendingImagePreview?.Dispose();
        PendingImagePreview = null;
        PendingImageBytes = null;
        PendingImageFileName = null;
        PendingImageSizeText = null;
        HasPendingImage = false;
    }

    public async Task<string?> UploadOrCacheImageAsync(byte[] imageBytes, string? fileName = null)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;

        fileName ??= $"image_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png";
        string? imageUrl = null;

        // 1. Try XEP-0363 HTTP File Upload if available
        if (_httpUploadManager is not null && _client is not null)
        {
            try
            {
                var domain = _client.Options.Jid.Domain;
                var serviceJid = await _httpUploadManager.DiscoverUploadServiceAsync(Jid.Parse(domain));
                if (serviceJid is not null)
                {
                    imageUrl = await _httpUploadManager.UploadBytesAsync(serviceJid, imageBytes, fileName, "image/png");
                }
            }
            catch
            {
                // Soft fallback to local media file
            }
        }

        // 2. Fallback: Save to local media cache directory and send file URI
        if (string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                var mediaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetChatx", "media");
                Directory.CreateDirectory(mediaDir);
                var id = Guid.NewGuid().ToString("N")[..8];
                var safeName = Path.GetFileName(fileName);
                var localPath = Path.Combine(mediaDir, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{id}_{safeName}");
                await File.WriteAllBytesAsync(localPath, imageBytes);
                imageUrl = new Uri(localPath).AbsoluteUri;
            }
            catch
            {
                // Soft failure saving media
            }
        }

        return imageUrl;
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (_client is null) return;

        // Check if we are currently editing an existing message
        if (IsEditingMessage && !string.IsNullOrEmpty(EditingMessageId))
        {
            string newText = InputText.Trim();
            string targetId = EditingMessageId;
            CancelEditingMessage();

            if (string.IsNullOrWhiteSpace(newText)) return;

            var targetBubble = Messages.FirstOrDefault(m =>
                m.Id == targetId || m.StanzaId == targetId || m.OriginId == targetId);

            var replaceStanza = new MessageStanza(
                to: RemoteJid,
                type: IsGroupChat ? MessageStanza.TypeGroupChat : MessageStanza.TypeChat,
                from: _client.BoundJid);

            replaceStanza.RawElement.Attr("xml:lang", "en");
            replaceStanza.RawElement.Attr("xmlns", "jabber:client");

            if (!IsGroupChat)
            {
                replaceStanza.RawElement.Child(new XmppElement("request", "urn:xmpp:receipts"));
                replaceStanza.RawElement.Child(new XmppElement("markable", "urn:xmpp:chat-markers:0"));
            }

            replaceStanza.Body = newText;
            replaceStanza.RawElement.Child(new XmppElement("replace", "urn:xmpp:message-correct:0").Attr("id", targetId));
            replaceStanza.RawElement.Child(new XmppElement("active", "http://jabber.org/protocol/chatstates"));

            if (_client.BoundJid is not null)
            {
                string byJid = _client.BoundJid.BareJid.ToString();
                if (!string.IsNullOrEmpty(byJid))
                {
                    replaceStanza.RawElement.Child(new XmppElement("stanza-id", "urn:xmpp:sid:0")
                        .Attr("by", byJid)
                        .Attr("id", replaceStanza.Id));
                }
            }
            replaceStanza.RawElement.Child(new XmppElement("origin-id", "urn:xmpp:sid:0").Attr("id", replaceStanza.Id));

            try
            {
                await _client.SendStanzaAsync(replaceStanza);

                if (targetBubble is not null)
                {
                    targetBubble.Body = newText;
                    targetBubble.IsEdited = true;
                    targetBubble.ReplaceId = replaceStanza.Id;
                    targetBubble.RawXml = replaceStanza.ToXmlString(indent: true);
                }

                await _messageRepo.UpdateMessageByReplaceIdAsync(_accountJid, targetId, newText, replaceStanza.Id);
            }
            catch
            {
                // Soft failure sending correction
            }

            _localPauseCts?.Cancel();
            _localPauseCts = null;
            SendLocalChatState(ChatState.Active);
            return;
        }

        bool hasText = !string.IsNullOrWhiteSpace(InputText);
        bool hasPendingImage = HasPendingImage && PendingImageBytes is not null && PendingImageBytes.Length > 0;

        if (!hasText && !hasPendingImage) return;

        byte[]? imageToSend = PendingImageBytes;
        string? imageFileName = PendingImageFileName;
        string textToSend = hasText ? InputText.Trim() : string.Empty;

        InputText = string.Empty;
        ClearPendingImage();

        string? imageUrl = null;
        if (imageToSend is not null && imageToSend.Length > 0)
        {
            imageUrl = await UploadOrCacheImageAsync(imageToSend, imageFileName);
        }

        string body;
        if (!string.IsNullOrEmpty(imageUrl))
        {
            body = !string.IsNullOrEmpty(textToSend)
                ? $"{imageUrl}\n{textToSend}"
                : imageUrl;
        }
        else
        {
            body = textToSend;
        }

        if (string.IsNullOrWhiteSpace(body)) return;

        var stanza = new MessageStanza(
            to: RemoteJid,
            type: IsGroupChat ? MessageStanza.TypeGroupChat : MessageStanza.TypeChat,
            from: _client.BoundJid);

        stanza.RawElement.Attr("xml:lang", "en");
        stanza.RawElement.Attr("xmlns", "jabber:client");

        if (!IsGroupChat)
        {
            stanza.RawElement.Child(new XmppElement("request", "urn:xmpp:receipts"));
            stanza.RawElement.Child(new XmppElement("markable", "urn:xmpp:chat-markers:0"));
        }

        if (!string.IsNullOrEmpty(imageUrl))
        {
            Xep0066OutOfBandData.AttachOobUrl(stanza.RawElement, imageUrl);
        }

        stanza.Body = body;

        stanza.RawElement.Child(new XmppElement("active", "http://jabber.org/protocol/chatstates"));

        if (_client.BoundJid is not null)
        {
            string byJid = _client.BoundJid.BareJid.ToString();
            if (!string.IsNullOrEmpty(byJid))
            {
                stanza.RawElement.Child(new XmppElement("stanza-id", "urn:xmpp:sid:0")
                    .Attr("by", byJid)
                    .Attr("id", stanza.Id));
            }
        }
        stanza.RawElement.Child(new XmppElement("origin-id", "urn:xmpp:sid:0").Attr("id", stanza.Id));

        var chatMsg = new ChatMessage
        {
            AccountJid = _accountJid,
            RemoteJid = RemoteJid.ToString(),
            SenderJid = _client.BoundJid?.ToString() ?? _accountJid,
            Body = body,
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow,
            IsEncrypted = IsEncrypted,
            EncryptionType = IsEncrypted ? "OMEMO" : null,
            StanzaId = stanza.Id,
            OriginId = stanza.Id
        };

        try
        {
            await _client.SendStanzaAsync(stanza);
            chatMsg.StanzaId = stanza.Id;
            chatMsg.RawXml = stanza.ToXmlString(indent: true);

            await _messageRepo.SaveMessageAsync(chatMsg);
            AddOrUpdateMessage(chatMsg);
            UpdateDateHeaders();
            RequestScrollToBottom();
        }
        catch
        {
            // Soft failure sending message stanza
        }

        _localPauseCts?.Cancel();
        _localPauseCts = null;
        SendLocalChatState(ChatState.Active);
    }

    public async Task SendImageAsync(byte[] imageBytes, string? fileName = null)
    {
        if (imageBytes is null || imageBytes.Length == 0 || _client is null) return;
        StageImageAttachment(imageBytes, fileName);
        await SendMessageAsync();
    }

    public void ReceiveMessage(ChatMessage msg)
    {
        void Apply()
        {
            AddOrUpdateMessage(msg);
            UpdateDateHeaders();
            RequestScrollToBottom();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    public void UpdateDateHeaders()
    {
        void Apply()
        {
            DateTime? lastDate = null;
            foreach (var bubble in Messages)
            {
                var msgDate = bubble.Timestamp.ToLocalTime().Date;
                if (lastDate != msgDate)
                {
                    bubble.ShowDateHeader = true;
                    bubble.DateHeader = MessageBubbleViewModel.FormatDateHeader(bubble.Timestamp);
                    lastDate = msgDate;
                }
                else
                {
                    bubble.ShowDateHeader = false;
                    bubble.DateHeader = null;
                }
            }
        }

        PostToUi(Apply);
    }

    public static bool IsSameMessage(MessageBubbleViewModel bubble, ChatMessage msg)
    {
        if (bubble.Id == msg.Id) return true;
        if (!string.IsNullOrEmpty(bubble.StanzaId) && !string.IsNullOrEmpty(msg.StanzaId) && bubble.StanzaId == msg.StanzaId)
        {
            return true;
        }
        if (!string.IsNullOrEmpty(bubble.OriginId) && !string.IsNullOrEmpty(msg.OriginId) && bubble.OriginId == msg.OriginId)
        {
            return true;
        }
        if (!string.IsNullOrEmpty(bubble.StanzaId) && bubble.StanzaId == msg.Id) return true;
        if (!string.IsNullOrEmpty(msg.StanzaId) && msg.StanzaId == bubble.Id) return true;

        // Content similarity within 60 seconds (when IDs are not available on both sides or differing MAM archive IDs)
        if (bubble.Direction == msg.Direction &&
            bubble.Body == msg.Body &&
            Math.Abs((bubble.Timestamp - msg.Timestamp).TotalSeconds) <= 60)
        {
            return true;
        }

        return false;
    }

    public bool ContainsMessage(ChatMessage msg)
    {
        return Messages.Any(m => IsSameMessage(m, msg));
    }

    [RelayCommand]
    public void ReplyToMessage(MessageBubbleViewModel message)
    {
        if (message is null) return;

        string textToQuote = !string.IsNullOrEmpty(message.Body) ? message.Body : (message.ImageUrl ?? string.Empty);
        if (string.IsNullOrWhiteSpace(textToQuote)) return;

        var sender = message.SenderName;
        var lines = textToQuote.Split(['\r', '\n'], StringSplitOptions.None);

        var quoteBuilder = new System.Text.StringBuilder();
        quoteBuilder.AppendLine($"> {sender}: {lines[0]}");
        for (int i = 1; i < lines.Length; i++)
        {
            quoteBuilder.AppendLine($"> {lines[i]}");
        }
        quoteBuilder.AppendLine();

        if (string.IsNullOrWhiteSpace(InputText))
        {
            InputText = quoteBuilder.ToString();
        }
        else
        {
            InputText = quoteBuilder.ToString() + InputText.TrimStart();
        }
    }

    [RelayCommand]
    public void StartEditingMessage(MessageBubbleViewModel message)
    {
        if (message is null || !message.IsOutbound) return;

        _savedDraftText = InputText;
        IsEditingMessage = true;
        EditingMessageId = message.StanzaId ?? message.OriginId ?? message.Id;
        EditingMessagePreviewText = !string.IsNullOrWhiteSpace(message.Body) ? message.Body : (message.ImageUrl ?? "Message");
        InputText = message.Body;

        EditStarted?.Invoke();
    }

    [RelayCommand]
    public void CancelEditingMessage()
    {
        IsEditingMessage = false;
        EditingMessageId = null;
        EditingMessagePreviewText = null;
        InputText = _savedDraftText ?? string.Empty;
        _savedDraftText = null;
    }

    public void StartEditingLastSentMessage()
    {
        var lastOutbound = Messages.LastOrDefault(m => m.IsOutbound);
        if (lastOutbound is not null)
        {
            StartEditingMessage(lastOutbound);
        }
    }

    [RelayCommand]
    public async Task DeleteMessageAsync(MessageBubbleViewModel message)
    {
        if (message is null) return;

        if (IsEditingMessage && (EditingMessageId == message.Id || EditingMessageId == message.StanzaId || EditingMessageId == message.OriginId))
        {
            CancelEditingMessage();
        }

        string targetId = message.StanzaId ?? message.OriginId ?? message.Id;

        // If outbound and client connected, send XEP-0424 retraction stanza
        if (message.IsOutbound && _client is not null)
        {
            try
            {
                var retractStanza = Xep0424MessageRetraction.CreateRetractionStanza(
                    RemoteJid,
                    targetId,
                    IsGroupChat ? MessageStanza.TypeGroupChat : MessageStanza.TypeChat);

                await _client.SendStanzaAsync(retractStanza);
            }
            catch
            {
                // Soft failure sending retraction stanza
            }
        }

        // Delete from database
        await _messageRepo.DeleteMessageAsync(_accountJid, targetId);
        if (!string.IsNullOrEmpty(message.Id) && message.Id != targetId)
        {
            await _messageRepo.DeleteMessageAsync(_accountJid, message.Id);
        }

        void RemoveFromList()
        {
            Messages.Remove(message);
            UpdateDateHeaders();
        }

        PostToUi(RemoveFromList);
    }

    public void HandleIncomingCorrection(string originalId, string newBody, string? newRawXml = null)
    {
        void Apply()
        {
            var targetBubble = Messages.FirstOrDefault(m =>
                m.Id == originalId || m.StanzaId == originalId || m.OriginId == originalId);
            if (targetBubble is not null)
            {
                targetBubble.Body = newBody;
                targetBubble.IsEdited = true;
                targetBubble.ReplaceId = originalId;
                if (!string.IsNullOrEmpty(newRawXml))
                {
                    targetBubble.RawXml = newRawXml;
                }
            }
        }

        PostToUi(Apply);
    }

    public void HandleIncomingRetraction(string targetId)
    {
        void Apply()
        {
            var targetBubble = Messages.FirstOrDefault(m =>
                m.Id == targetId || m.StanzaId == targetId || m.OriginId == targetId);
            if (targetBubble is not null)
            {
                Messages.Remove(targetBubble);
                UpdateDateHeaders();
            }
        }

        PostToUi(Apply);
    }

    public void AddOrUpdateMessage(ChatMessage msg)
    {
        void Apply()
        {
            var existing = Messages.FirstOrDefault(m => IsSameMessage(m, msg));
            if (existing is not null)
            {
                if (string.IsNullOrEmpty(existing.StanzaId) && !string.IsNullOrEmpty(msg.StanzaId))
                {
                    existing.StanzaId = msg.StanzaId;
                }
                if (string.IsNullOrEmpty(existing.OriginId) && !string.IsNullOrEmpty(msg.OriginId))
                {
                    existing.OriginId = msg.OriginId;
                }
                if (msg.IsRead && !existing.IsRead)
                {
                    existing.IsRead = true;
                }
                if (!string.IsNullOrEmpty(msg.ReplaceId) && existing.ReplaceId != msg.ReplaceId)
                {
                    existing.ReplaceId = msg.ReplaceId;
                    existing.IsEdited = true;
                    existing.Body = msg.Body;
                }
                return;
            }

            var bubble = MessageBubbleViewModel.FromChatMessage(msg, _accountJid, _settingsRepo, _quickEmojis);
            bubble.ToggleReactionHandler = (b, emoji) => ToggleReactionAsync(b, emoji);
            bubble.ReplyRequested = ReplyToMessage;
            bubble.EditRequested = StartEditingMessage;
            bubble.DeleteRequested = b => _ = DeleteMessageAsync(b);
            int index = 0;
            while (index < Messages.Count && Messages[index].Timestamp <= bubble.Timestamp)
            {
                index++;
            }
            Messages.Insert(index, bubble);

            if (!OldestMessageTimestamp.HasValue || bubble.Timestamp < OldestMessageTimestamp.Value)
            {
                OldestMessageTimestamp = bubble.Timestamp;
            }

            MessageProcessed?.Invoke(msg);
        }

        PostToUi(Apply);
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
