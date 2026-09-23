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
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Gui.Helpers;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Protocol.Xeps.Omemo;
using Stanza.Protocol.Xeps.Sharing;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.ViewModels;

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
    private readonly Func<Task<bool>>? _ensureConnected;
    private readonly string _accountJid;

    private List<string> _quickEmojis = [.. EmojiData.DefaultQuickEmojis];
    public IReadOnlyList<string> QuickEmojis => _quickEmojis;

    private List<EmoticonMapping> _emoticonMappings = [.. SettingsRepository.DefaultEmoticonMappings];

    [ObservableProperty]
    private bool _autoReplaceEmoticons = SettingsRepository.DefaultAutoReplaceEmoticons;

    [ObservableProperty]
    private bool _showEmoticonBanner;

    public Action? OpenSettingsToChatRequested { get; set; }

    public void UpdateQuickEmojis(IEnumerable<string> emojis)
    {
        _quickEmojis = emojis.ToList();
        foreach (var msg in Messages)
        {
            msg.EmojiPicker?.UpdateQuickEmojis(_quickEmojis);
        }
    }

    public void UpdateEmoticonSettings(bool autoReplace, IEnumerable<EmoticonMapping> mappings)
    {
        AutoReplaceEmoticons = autoReplace;
        _emoticonMappings = mappings.ToList();
    }

    [RelayCommand]
    public async Task DismissEmoticonBannerAsync()
    {
        ShowEmoticonBanner = false;
        if (_settingsRepo is not null)
        {
            await _settingsRepo.SetEmoticonBannerDismissedAsync(_accountJid, true);
        }
    }

    [RelayCommand]
    public async Task OpenChatSettingsAndDismissBannerAsync()
    {
        await DismissEmoticonBannerAsync();
        OpenSettingsToChatRequested?.Invoke();
    }

    private readonly Dictionary<string, (string messageId, DateTimeOffset timestamp)> _participantReadMarkers = new(StringComparer.OrdinalIgnoreCase);

    private System.Threading.CancellationTokenSource? _remoteComposingCts;
    private System.Threading.CancellationTokenSource? _localPauseCts;
    private ChatState? _lastSentLocalState;
    private Task? _historyLoadingTask;
    private readonly Lock _historyLoadLock = new();

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
    [NotifyPropertyChangedFor(nameof(HasReplyingMessage))]
    private MessageBubbleViewModel? _replyingToMessage;

    public bool HasReplyingMessage => ReplyingToMessage != null;

    [RelayCommand]
    public void CancelReplyingMessage()
    {
        ReplyingToMessage = null;
    }

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
    [NotifyPropertyChangedFor(nameof(PendingImageHeader))]
    private bool _isPendingImageGif;

    public string PendingImageHeader => IsPendingImageGif ? "🎞️ GIF ready to send" : "📷 Image ready to send";

    [ObservableProperty]
    private bool _isRemoteComposing;

    [ObservableProperty]
    private bool _isComposing;

    [ObservableProperty]
    private bool _hasLoadedHistory;

    [ObservableProperty]
    private bool _isLoadingHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOlderButtonText))]
    [NotifyPropertyChangedFor(nameof(LoadOlderButtonIcon))]
    private bool _isLoadingOlderHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncButtonText))]
    [NotifyPropertyChangedFor(nameof(SyncButtonIcon))]
    private bool _isSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncButtonText))]
    private int _syncFetchedCount;

    public string SyncButtonText => IsSyncing
        ? (SyncFetchedCount > 0 ? $"Syncing... ({SyncFetchedCount}) ⏳" : "Syncing... ⏳")
        : "Sync 🔄";
    public string SyncButtonIcon => IsSyncing ? "⏳" : "🔄";

    [ObservableProperty]
    private DateTimeOffset? _oldestMessageTimestamp;

    public string LoadOlderButtonText => IsLoadingOlderHistory ? "Loading older messages..." : "▲ Load Older Messages";
    public string LoadOlderButtonIcon => IsLoadingOlderHistory ? "⏳" : "▲";

    public ObservableCollection<MessageBubbleViewModel> Messages { get; } = [];

    private bool _isLoadingSettings;

    [ObservableProperty]
    private bool _enableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;

    [ObservableProperty]
    private int _messageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;

    partial void OnEnableMessageMergingChanged(bool value)
    {
        if (!_isLoadingSettings && _settingsRepo is not null)
        {
            _ = _settingsRepo.SetMergeMessagesEnabledAsync(_accountJid, value);
        }
        if (!_isLoadingSettings)
        {
            RebuildMessageBubbles();
        }
    }

    partial void OnMessageMergeThresholdSecondsChanged(int value)
    {
        if (!_isLoadingSettings && _settingsRepo is not null)
        {
            _ = _settingsRepo.SetMergeMessagesThresholdSecondsAsync(_accountJid, value);
        }
        if (!_isLoadingSettings)
        {
            RebuildMessageBubbles();
        }
    }

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

    public Func<SlashCommandResult, Task<bool>>? SlashCommandHandler { get; set; }

    public void AddSystemMessage(string systemText)
    {
        var msg = new ChatMessage
        {
            AccountJid = _accountJid,
            RemoteJid = RemoteJid.ToString(),
            SenderJid = "System",
            Body = systemText,
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow,
            IsRead = true
        };

        PostToUi(() =>
        {
            var bubble = MessageBubbleViewModel.FromChatMessage(msg, _accountJid, _settingsRepo, _quickEmojis, "System");
            var index = 0;
            while (index < Messages.Count && Messages[index].Timestamp <= bubble.Timestamp)
            {
                index++;
            }
            Messages.Insert(index, bubble);
            UpdateDateHeaders();
            RequestScrollToBottom();
        });
    }

    public void ClearMessages()
    {
        PostToUi(() =>
        {
            Messages.Clear();
            UpdateDateHeaders();
        });
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
        SettingsRepository? settingsRepo = null,
        Func<Task<bool>>? ensureConnected = null)
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
        _ensureConnected = ensureConnected;
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
        if (HasLoadedHistory)
        {
            await MarkUnreadMessagesAsReadAsync();
            return;
        }

        Task task;
        lock (_historyLoadLock)
        {
            if (_historyLoadingTask is null || _historyLoadingTask.IsCompleted)
            {
                _historyLoadingTask = LoadHistoryAsync();
            }
            task = _historyLoadingTask;
        }

        await task;
        await MarkUnreadMessagesAsReadAsync();
    }

    public void MarkMessageAsRead(string stanzaOrMessageId)
    {
        void Apply()
        {
            var target = Messages.FirstOrDefault(m => m.ContainsMessageId(stanzaOrMessageId));
            if (target is not null)
            {
                target.IsRead = true;
                foreach (var msg in Messages)
                {
                    if (msg.Timestamp <= target.Timestamp && msg.IsOutbound)
                    {
                        msg.IsRead = true;
                    }
                }
            }
            else
            {
                foreach (var msg in Messages)
                {
                    if (msg.ContainsMessageId(stanzaOrMessageId))
                    {
                        msg.IsRead = true;
                    }
                }
            }
            UpdateReadMarkersOnBubbles();
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

    public void UpdateReadMarker(string participantJid, string stanzaOrMessageId, DateTimeOffset? timestamp = null)
    {
        var msg = Messages.FirstOrDefault(m => m.ContainsMessageId(stanzaOrMessageId));
        DateTimeOffset? ts = timestamp ?? msg?.Timestamp;
        string msgId = msg?.Id ?? stanzaOrMessageId;

        if (ts.HasValue)
        {
            _participantReadMarkers[participantJid] = (msgId, ts.Value);
            _ = _messageRepo.SaveReadMarkerAsync(_accountJid, RemoteJid.ToString(), participantJid, msgId, ts.Value);
            PostToUi(UpdateReadMarkersOnBubbles);
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await _messageRepo.SaveReadMarkerAsync(_accountJid, RemoteJid.ToString(), participantJid, msgId);
                var markers = await _messageRepo.GetReadMarkersAsync(_accountJid, RemoteJid.ToString());
                var matched = markers.FirstOrDefault(m => m.ParticipantJid.Equals(participantJid, StringComparison.OrdinalIgnoreCase));
                if (matched is not null)
                {
                    _participantReadMarkers[participantJid] = (matched.LastReadMessageId, matched.LastReadTimestamp);
                    PostToUi(UpdateReadMarkersOnBubbles);
                }
            });
        }
    }

    public async Task LoadReadMarkersAsync()
    {
        try
        {
            var dbMarkers = await _messageRepo.GetReadMarkersAsync(_accountJid, RemoteJid.ToString());
            foreach (var m in dbMarkers)
            {
                _participantReadMarkers[m.ParticipantJid] = (m.LastReadMessageId, m.LastReadTimestamp);
            }
            PostToUi(UpdateReadMarkersOnBubbles);
        }
        catch
        {
            // Soft failure loading read markers
        }
    }

    public void UpdateReadMarkersOnBubbles()
    {
        foreach (var b in Messages)
        {
            b.ShowReadMarkerDivider = false;
            b.ReadMarkerDividerText = null;
            b.ReceiptTooltip = null;
        }

        if (Messages.Count == 0) return;

        if (!IsGroupChat)
        {
            string remoteJidStr = RemoteJid.BareJid.ToString();
            string contactDisplayName = GetSenderDisplayName(remoteJidStr, MessageDirection.Inbound);

            DateTimeOffset? remoteReadTs = null;
            string? lastReadMsgId = null;

            foreach (var kvp in _participantReadMarkers)
            {
                if (remoteReadTs == null || kvp.Value.timestamp > remoteReadTs.Value)
                {
                    remoteReadTs = kvp.Value.timestamp;
                    lastReadMsgId = kvp.Value.messageId;
                }
            }

            var lastInbound = Messages.LastOrDefault(m => m.Direction == MessageDirection.Inbound);
            if (lastInbound != null && (remoteReadTs == null || lastInbound.Timestamp > remoteReadTs.Value))
            {
                remoteReadTs = lastInbound.Timestamp;
                lastReadMsgId = lastInbound.Id;
            }

            if (remoteReadTs.HasValue)
            {
                MessageBubbleViewModel? lastReadBubble = null;
                if (!string.IsNullOrEmpty(lastReadMsgId))
                {
                    lastReadBubble = Messages.FirstOrDefault(m => m.ContainsMessageId(lastReadMsgId));
                }

                if (lastReadBubble == null || lastReadBubble.Timestamp < remoteReadTs.Value)
                {
                    lastReadBubble = Messages.LastOrDefault(m => m.Timestamp <= remoteReadTs.Value);
                }

                if (lastReadBubble != null)
                {
                    int readIndex = Messages.IndexOf(lastReadBubble);
                    if (readIndex >= 0 && readIndex < Messages.Count - 1)
                    {
                        lastReadBubble.ShowReadMarkerDivider = true;
                        lastReadBubble.ReadMarkerDividerText = $"Read by {contactDisplayName}";
                    }
                }

                foreach (var bubble in Messages)
                {
                    if (bubble.IsOutbound)
                    {
                        if (bubble.Timestamp <= remoteReadTs.Value || bubble.IsRead)
                        {
                            bubble.IsRead = true;
                            bubble.ReceiptTooltip = $"Read by {contactDisplayName}";
                        }
                        else
                        {
                            bubble.ReceiptTooltip = "Delivered";
                        }
                    }
                }
            }
            else
            {
                foreach (var bubble in Messages)
                {
                    if (bubble.IsOutbound)
                    {
                        bubble.ReceiptTooltip = bubble.IsRead ? $"Read by {contactDisplayName}" : "Delivered";
                    }
                }
            }
        }
        else
        {
            var participantReadTimes = new Dictionary<string, (string name, DateTimeOffset ts)>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _participantReadMarkers)
            {
                string pJid = kvp.Key;
                string pName = GetSenderDisplayName(pJid, MessageDirection.Inbound);
                participantReadTimes[pJid] = (pName, kvp.Value.timestamp);
            }

            foreach (var msg in Messages)
            {
                if (msg.Direction == MessageDirection.Inbound && !string.IsNullOrEmpty(msg.SenderName))
                {
                    string pJid = msg.SenderName;
                    string pName = msg.SenderDisplayName;
                    if (!participantReadTimes.TryGetValue(pJid, out var existing) || msg.Timestamp > existing.ts)
                    {
                        participantReadTimes[pJid] = (pName, msg.Timestamp);
                    }
                }
            }

            if (participantReadTimes.Count > 0)
            {
                DateTimeOffset everyoneReadTs = participantReadTimes.Values.Min(v => v.ts);

                var everyoneReadBubble = Messages.LastOrDefault(m => m.Timestamp <= everyoneReadTs);
                if (everyoneReadBubble != null)
                {
                    int readIndex = Messages.IndexOf(everyoneReadBubble);
                    if (readIndex >= 0 && readIndex < Messages.Count - 1)
                    {
                        everyoneReadBubble.ShowReadMarkerDivider = true;
                        everyoneReadBubble.ReadMarkerDividerText = "Read by everyone";
                    }
                }

                foreach (var bubble in Messages)
                {
                    if (bubble.IsOutbound)
                    {
                        var readers = participantReadTimes.Values
                            .Where(p => p.ts >= bubble.Timestamp)
                            .Select(p => p.name)
                            .Distinct()
                            .ToList();

                        if (readers.Count == participantReadTimes.Count)
                        {
                            bubble.IsRead = true;
                            bubble.ReceiptTooltip = $"Read by everyone ({string.Join(", ", readers)})";
                        }
                        else if (readers.Count > 0)
                        {
                            bubble.IsRead = true;
                            bubble.ReceiptTooltip = $"Read by: {string.Join(", ", readers)}";
                        }
                        else
                        {
                            bubble.IsRead = false;
                            bubble.ReceiptTooltip = "Delivered";
                        }
                    }
                }
            }
            else
            {
                foreach (var bubble in Messages)
                {
                    if (bubble.IsOutbound)
                    {
                        bubble.ReceiptTooltip = bubble.IsRead ? "Read" : "Delivered";
                    }
                }
            }
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
        if (HasLoadedHistory) return;
        IsLoadingHistory = true;

        try
        {
            if (_settingsRepo is not null)
            {
                try
                {
                    _isLoadingSettings = true;
                    _quickEmojis = await _settingsRepo.GetQuickEmojisAsync(_accountJid);
                    EnableMessageMerging = await _settingsRepo.GetMergeMessagesEnabledAsync(_accountJid);
                    MessageMergeThresholdSeconds = await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(_accountJid);
                    AutoReplaceEmoticons = await _settingsRepo.GetAutoReplaceEmoticonsAsync(_accountJid);
                    _emoticonMappings = await _settingsRepo.GetEmoticonMappingsAsync(_accountJid);
                }
                catch
                {
                    // Fallback to default
                }
                finally
                {
                    _isLoadingSettings = false;
                }
            }

            // 1. Immediately load whatever is cached in SQLite for responsive UI
            var dbMessages = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 50);
            PostToUi(() =>
            {
                Messages.Clear();
                foreach (var msg in dbMessages)
                {
                    AddOrUpdateMessageInternal(msg);
                }
                UpdateDateHeaders();
                RequestScrollToBottom();
            });

            await LoadReadMarkersAsync();
            await LoadReactionsForCurrentMessagesAsync();
            HasLoadedHistory = true;

            // 2. Query MAM to sync latest messages from the server
            if (_mamManager is not null)
            {
                await SyncArchiveAsync();
            }
        }
        finally
        {
            IsLoadingHistory = false;
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
        var stanzaId = stanzaIdElem?.GetAttr("id") ?? m.Id;
        var originId = originIdElem?.GetAttr("id");

        var parsedAccountJid = Jid.TryParse(accountJid, out var accJid) ? accJid : null;
        var isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
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

        var senderJidStr = (m.From ?? (isFromSelf ? (parsedAccountJid ?? effectiveRemote) : effectiveRemote)).ToString();

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
            var isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
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
        SyncFetchedCount = 0;

        try
        {
            if (_ensureConnected is not null)
            {
                var connected = await _ensureConnected();
                if (!connected) return;
            }
            else if (_client is not null && !_client.IsReady)
            {
                return;
            }

            var archiveJid = IsGroupChat ? RemoteJid : null;
            var withJid = IsGroupChat ? null : RemoteJid;

            var latestBubble = Messages.LastOrDefault();
            var startTimestamp = latestBubble?.Timestamp;

            string? beforeId = null;
            string? afterId = null;
            var pagesFetched = 0;
            const int maxPages = 50;

            while (pagesFetched < maxPages)
            {
                MamQueryResult? mamResult = null;
                var retries = 3;
                while (retries > 0)
                {
                    try
                    {
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
                        break;
                    }
                    catch
                    {
                        retries--;
                        if (retries == 0) throw;
                        await Task.Delay(500 * (3 - retries));
                    }
                }

                if (mamResult is null || mamResult.Messages.Count == 0)
                {
                    break;
                }

                pagesFetched++;
                SyncFetchedCount += mamResult.Messages.Count;

                var chatMsgs = await ProcessMamMessagesAsync(mamResult.Messages);

                if (chatMsgs.Count > 0)
                {
                    await _messageRepo.SaveMessagesAsync(chatMsgs);
                }

                if (mamResult.IsComplete)
                    break;

                if (startTimestamp.HasValue)
                {
                    var nextAfter = !string.IsNullOrEmpty(mamResult.LastId)
                        ? mamResult.LastId
                        : mamResult.Messages.LastOrDefault()?.ArchiveId;

                    if (string.IsNullOrEmpty(nextAfter) || nextAfter == afterId)
                        break;
                    afterId = nextAfter;
                }
                else
                {
                    // Initial sync on empty conversation only loads the latest page.
                    // Older history is paged on demand via LoadOlderHistoryAsync.
                    break;
                }
            }

            if (SyncFetchedCount > 0)
            {
                await LoadReactionsForCurrentMessagesAsync();
                UpdateDateHeaders();
                RequestScrollToBottom();
            }
        }
        catch
        {
            // Soft failure on MAM sync
        }
        finally
        {
            IsSyncing = false;
            SyncFetchedCount = 0;
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

        var messageIds = Messages.SelectMany(m => m.MergedMessageIds.Count > 0 ? m.MergedMessageIds : [m.Id]).Distinct().ToList();
        var rawReactions = await _messageRepo.GetReactionsForMessagesAsync(_accountJid, messageIds);

        var reactionsByMsgId = rawReactions.GroupBy(r => r.MessageId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var bubble in Messages)
        {
            var bubbleReactions = new List<MessageReaction>();
            var idsToCheck = bubble.MergedMessageIds.Count > 0 ? bubble.MergedMessageIds : [bubble.Id];
            foreach (var id in idsToCheck)
            {
                if (reactionsByMsgId.TryGetValue(id, out var msgReactions))
                {
                    bubbleReactions.AddRange(msgReactions);
                }
            }
            bubble.UpdateReactions(bubbleReactions, _accountJid);
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

        var targetId = bubble?.Id ?? args.TargetMessageId;
        var effectiveSenderJid = args.IsCarbonSent ? _accountJid : args.SenderJid.ToString();

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

        var isGif = GifDecoder.IsGif(imageBytes) || (fileName?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) == true);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var ext = isGif ? "gif" : "png";
            fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{ext}";
        }
        else if (isGif && !fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.ChangeExtension(fileName, ".gif");
        }

        var kb = imageBytes.Length / 1024.0;
        var sizeText = kb >= 1024 ? $"{kb / 1024.0:F1} MB" : $"{kb:F0} KB";

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
        IsPendingImageGif = isGif;
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
        IsPendingImageGif = false;
        HasPendingImage = false;
    }

    public async Task<string?> UploadOrCacheImageAsync(byte[] imageBytes, string? fileName = null)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;

        var isGif = GifDecoder.IsGif(imageBytes) || (fileName?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) == true);
        var defaultExt = isGif ? "gif" : "png";
        var contentType = isGif ? "image/gif" : "image/png";

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{(isGif ? "gif" : "image")}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{defaultExt}";
        }
        else if (isGif && !fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.ChangeExtension(fileName, ".gif");
        }

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
                    imageUrl = await _httpUploadManager.UploadBytesAsync(serviceJid, imageBytes, fileName, contentType);
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
                var mediaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stanza", "media");
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
            var newText = InputText.Trim();
            var targetId = EditingMessageId;
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
                var byJid = _client.BoundJid.BareJid.ToString();
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
                    targetBubble.UpdateMessageContent(targetId, newText, replaceStanza.ToXmlString(indent: true));
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

        var hasText = !string.IsNullOrWhiteSpace(InputText);
        var hasPendingImage = HasPendingImage && PendingImageBytes is not null && PendingImageBytes.Length > 0;

        if (!hasText && !hasPendingImage) return;

        var imageToSend = PendingImageBytes;
        var imageFileName = PendingImageFileName;
        var textToSend = hasText ? InputText.Trim() : string.Empty;

        var wasEmoticonReplaced = false;
        if (AutoReplaceEmoticons && !string.IsNullOrEmpty(textToSend))
        {
            var (replacedText, replaced) = EmoticonReplacer.ReplaceEmoticons(textToSend, _emoticonMappings);
            if (replaced)
            {
                textToSend = replacedText;
                wasEmoticonReplaced = true;
            }
        }

        InputText = string.Empty;
        ClearPendingImage();
        CancelReplyingMessage();

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

        // Process slash command if no image attachment and message starts with / or //
        if (string.IsNullOrEmpty(imageUrl) && (body.StartsWith('/') || body.StartsWith("//")))
        {
            var commandResult = SlashCommandProcessor.Process(body, IsGroupChat);

            if (SlashCommandHandler is not null && await SlashCommandHandler(commandResult))
            {
                _localPauseCts?.Cancel();
                _localPauseCts = null;
                SendLocalChatState(ChatState.Active);
                return;
            }

            switch (commandResult.Type)
            {
                case SlashCommandResultType.Handled:
                    _localPauseCts?.Cancel();
                    _localPauseCts = null;
                    SendLocalChatState(ChatState.Active);
                    return;

                case SlashCommandResultType.SystemMessage:
                    if (!string.IsNullOrEmpty(commandResult.SystemOutput))
                    {
                        AddSystemMessage(commandResult.SystemOutput);
                    }
                    _localPauseCts?.Cancel();
                    _localPauseCts = null;
                    SendLocalChatState(ChatState.Active);
                    return;

                case SlashCommandResultType.ClearChat:
                    ClearMessages();
                    _localPauseCts?.Cancel();
                    _localPauseCts = null;
                    SendLocalChatState(ChatState.Active);
                    return;

                case SlashCommandResultType.SendMessage:
                    if (!string.IsNullOrEmpty(commandResult.MessageText))
                    {
                        body = commandResult.MessageText;
                    }
                    break;
            }
        }

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
            var byJid = _client.BoundJid.BareJid.ToString();
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

            if (wasEmoticonReplaced && _settingsRepo is not null)
            {
                var dismissed = await _settingsRepo.GetEmoticonBannerDismissedAsync(_accountJid);
                if (!dismissed)
                {
                    PostToUi(() => ShowEmoticonBanner = true);
                }
            }
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
            AddOrUpdateMessageInternal(msg);
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

    public void ReceiveMessages(IEnumerable<ChatMessage> msgs)
    {
        void Apply()
        {
            var anyAdded = false;
            foreach (var msg in msgs)
            {
                var existing = Messages.FirstOrDefault(m => IsSameMessage(m, msg));
                if (existing is not null)
                {
                    existing.UpdateMessageRecord(msg);
                    continue;
                }

                AddOrUpdateMessageInternal(msg);
                anyAdded = true;
            }

            if (anyAdded)
            {
                UpdateDateHeaders();
                RequestScrollToBottom();
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

        if (bubble.ContainsMessageId(msg.Id) ||
            (!string.IsNullOrEmpty(msg.StanzaId) && bubble.ContainsMessageId(msg.StanzaId)) ||
            (!string.IsNullOrEmpty(msg.OriginId) && bubble.ContainsMessageId(msg.OriginId)))
        {
            return true;
        }

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
        ReplyingToMessage = message;

        var textToQuote = !string.IsNullOrEmpty(message.Body) ? message.Body : (message.ImageUrl ?? string.Empty);
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

    public void InjectCodeBlock(string codeBlockMarkdown, int insertionIndex = -1)
    {
        if (string.IsNullOrEmpty(codeBlockMarkdown)) return;

        if (string.IsNullOrEmpty(InputText))
        {
            InputText = codeBlockMarkdown;
            return;
        }

        if (insertionIndex < 0 || insertionIndex > InputText.Length)
        {
            insertionIndex = InputText.Length;
        }

        var before = InputText[..insertionIndex];
        var after = InputText[insertionIndex..];

        var sb = new System.Text.StringBuilder();
        sb.Append(before);
        if (before.Length > 0 && !before.EndsWith('\n') && !before.EndsWith('\r'))
        {
            sb.Append('\n');
        }

        sb.Append(codeBlockMarkdown);

        if (after.Length > 0 && !after.StartsWith('\n') && !after.StartsWith('\r'))
        {
            sb.Append('\n');
        }
        sb.Append(after);

        InputText = sb.ToString();
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

        if (IsEditingMessage && (EditingMessageId == message.Id || EditingMessageId == message.StanzaId || EditingMessageId == message.OriginId || message.ContainsMessageId(EditingMessageId)))
        {
            CancelEditingMessage();
        }

        var idsToDelete = new List<string>();
        if (message.MergedMessages.Count > 0)
        {
            foreach (var m in message.MergedMessages)
            {
                var tid = m.StanzaId ?? m.OriginId ?? m.Id;
                idsToDelete.Add(tid);
                if (!string.IsNullOrEmpty(m.Id) && m.Id != tid) idsToDelete.Add(m.Id);
            }
        }
        else
        {
            var targetId = message.StanzaId ?? message.OriginId ?? message.Id;
            idsToDelete.Add(targetId);
            if (!string.IsNullOrEmpty(message.Id) && message.Id != targetId) idsToDelete.Add(message.Id);
        }

        // If outbound and client connected, send XEP-0424 retraction stanza
        if (message.IsOutbound && _client is not null)
        {
            foreach (var tid in idsToDelete.Distinct())
            {
                try
                {
                    var retractStanza = Xep0424MessageRetraction.CreateRetractionStanza(
                        RemoteJid,
                        tid,
                        IsGroupChat ? MessageStanza.TypeGroupChat : MessageStanza.TypeChat);

                    await _client.SendStanzaAsync(retractStanza);
                }
                catch
                {
                    // Soft failure sending retraction stanza
                }
            }
        }

        // Delete from database
        foreach (var tid in idsToDelete.Distinct())
        {
            await _messageRepo.DeleteMessageAsync(_accountJid, tid);
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
                m.Id == originalId || m.StanzaId == originalId || m.OriginId == originalId || m.ContainsMessageId(originalId));
            if (targetBubble is not null)
            {
                targetBubble.UpdateMessageContent(originalId, newBody, newRawXml);
            }
        }

        PostToUi(Apply);
    }

    public void HandleIncomingRetraction(string targetId)
    {
        void Apply()
        {
            var targetBubble = Messages.FirstOrDefault(m =>
                m.Id == targetId || m.StanzaId == targetId || m.OriginId == targetId || m.ContainsMessageId(targetId));
            if (targetBubble is not null)
            {
                if (targetBubble.MergedMessages.Count > 1)
                {
                    targetBubble.RemoveMessageById(targetId);
                    if (targetBubble.MergedMessages.Count == 0)
                    {
                        Messages.Remove(targetBubble);
                    }
                }
                else
                {
                    Messages.Remove(targetBubble);
                }
                UpdateDateHeaders();
            }
        }

        PostToUi(Apply);
    }

    public void AddOrUpdateMessage(ChatMessage msg)
    {
        PostToUi(() => AddOrUpdateMessageInternal(msg));
    }

    internal void AddOrUpdateMessageInternal(ChatMessage msg)
    {
        var existing = Messages.FirstOrDefault(m => IsSameMessage(m, msg));
        if (existing is not null)
        {
            existing.UpdateMessageRecord(msg);
            return;
        }

        if (EnableMessageMerging && MessageMergeThresholdSeconds > 0 && Messages.Count > 0)
        {
            var insertIdx = 0;
            while (insertIdx < Messages.Count && Messages[insertIdx].Timestamp <= msg.Timestamp)
            {
                insertIdx++;
            }

            if (insertIdx > 0)
            {
                var prevBubble = Messages[insertIdx - 1];
                if (prevBubble.CanMergeWith(msg, EnableMessageMerging, MessageMergeThresholdSeconds))
                {
                    prevBubble.MergeMessage(msg);
                    MessageProcessed?.Invoke(msg);
                    return;
                }
            }
        }

        var displayName = GetSenderDisplayName(msg);
        var bubble = MessageBubbleViewModel.FromChatMessage(msg, _accountJid, _settingsRepo, _quickEmojis, displayName);
        bubble.ToggleReactionHandler = (b, emoji) => ToggleReactionAsync(b, emoji);
        bubble.ReplyRequested = ReplyToMessage;
        bubble.EditRequested = StartEditingMessage;
        bubble.DeleteRequested = b => _ = DeleteMessageAsync(b);
        var index = 0;
        while (index < Messages.Count && Messages[index].Timestamp <= bubble.Timestamp)
        {
            index++;
        }
        Messages.Insert(index, bubble);

        if (!OldestMessageTimestamp.HasValue || bubble.Timestamp < OldestMessageTimestamp.Value)
        {
            OldestMessageTimestamp = bubble.Timestamp;
        }

        if (msg.Direction == MessageDirection.Inbound)
        {
            string pJid = IsGroupChat ? msg.SenderJid : RemoteJid.BareJid.ToString();
            _participantReadMarkers[pJid] = (msg.Id, msg.Timestamp);
            _ = _messageRepo.SaveReadMarkerAsync(_accountJid, RemoteJid.ToString(), pJid, msg.Id, msg.Timestamp);
        }

        UpdateReadMarkersOnBubbles();

        MessageProcessed?.Invoke(msg);
    }

    public string GetSenderDisplayName(ChatMessage msg) => GetSenderDisplayName(msg.SenderJid, msg.Direction);

    public string GetSenderDisplayName(string senderJid, MessageDirection direction)
    {
        if (direction == MessageDirection.Outbound)
        {
            return "Me";
        }

        if (IsGroupChat)
        {
            if (Jid.TryParse(senderJid, out var pJid) && !string.IsNullOrEmpty(pJid.Resource))
            {
                return pJid.Resource;
            }
        }
        else
        {
            // 1-on-1 chat: If Title is a friendly name (e.g. "Alice Cooper"), use it.
            // If Title is a JID (e.g. "alice@example.com"), format the JID name.
            if (!string.IsNullOrWhiteSpace(Title))
            {
                if (Title.Contains('@') && Jid.TryParse(Title, out var titleJid))
                {
                    return MessageBubbleViewModel.FormatNameFromJid(titleJid);
                }

                if (!Title.Contains(' ') && (Title.Contains('.') || Title.Contains('_')))
                {
                    var parts = Title.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
                    return string.Join(" ", parts.Select(MessageBubbleViewModel.Capitalize));
                }

                return Title;
            }
        }

        return MessageBubbleViewModel.ResolveSenderDisplayName(senderJid, direction);
    }

    partial void OnTitleChanged(string value)
    {
        if (!IsGroupChat && Messages.Count > 0)
        {
            foreach (var b in Messages)
            {
                if (b.Direction == MessageDirection.Inbound)
                {
                    b.SenderDisplayName = GetSenderDisplayName(b.SenderName, MessageDirection.Inbound);
                }
            }
        }
    }

    public void RebuildMessageBubbles()
    {
        void Apply()
        {
            if (Messages.Count == 0) return;

            var allMsgs = Messages
                .SelectMany(b => b.MergedMessages.Count > 0 ? b.MergedMessages : [new ChatMessage
                {
                    Id = b.Id,
                    AccountJid = _accountJid,
                    RemoteJid = RemoteJid.ToString(),
                    SenderJid = b.SenderName,
                    Body = b.Body,
                    Direction = b.Direction,
                    Timestamp = b.Timestamp,
                    IsRead = b.IsRead,
                    StanzaId = b.StanzaId,
                    OriginId = b.OriginId,
                    ReplaceId = b.ReplaceId,
                    RawXml = b.RawXml
                }])
                .OrderBy(m => m.Timestamp)
                .ToList();

            Messages.Clear();
            foreach (var msg in allMsgs)
            {
                AddOrUpdateMessage(msg);
            }
            UpdateDateHeaders();
            _ = LoadReactionsForCurrentMessagesAsync();
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
