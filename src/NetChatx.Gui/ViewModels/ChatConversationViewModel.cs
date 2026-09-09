using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class ChatConversationViewModel : ViewModelBase
{
    private readonly XmppClient? _client;
    private readonly MessageRepository _messageRepo;
    private readonly Xep0313MessageArchiveManagement? _mamManager;
    private readonly Xep0384OmemoManager? _omemoManager;
    private readonly string _accountJid;

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
    private bool _isComposing;

    [ObservableProperty]
    private bool _hasLoadedHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOlderButtonText))]
    private bool _isLoadingOlderHistory;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private DateTimeOffset? _oldestMessageTimestamp;

    public string LoadOlderButtonText => IsLoadingOlderHistory ? "Loading older messages..." : "▲ Load Older Messages";

    public ObservableCollection<MessageBubbleViewModel> Messages { get; } = [];

    public event Action? ScrollToBottomRequested;

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
        Xep0384OmemoManager? omemoManager = null)
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
    }

    public async Task EnsureHistoryLoadedAsync()
    {
        if (HasLoadedHistory) return;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        // 1. Immediately load whatever is cached in SQLite for responsive UI
        var dbMessages = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 50);
        Messages.Clear();
        foreach (var msg in dbMessages)
        {
            AddOrUpdateMessage(msg);
        }
        UpdateDateHeaders();
        HasLoadedHistory = true;
        RequestScrollToBottom();

        // 2. Query MAM to sync latest messages from the server
        if (_mamManager is not null)
        {
            await SyncArchiveAsync();
        }
    }

    [RelayCommand]
    public async Task SyncArchiveAsync()
    {
        if (IsSyncing || _mamManager is null) return;
        IsSyncing = true;

        try
        {
            // Retrieve the most recent 50 messages from MAM
            // (Empty <before/> per XEP-0313 4.1.4 retrieves the latest page up to now)
            var mamResult = await _mamManager.QueryArchiveAsync(withJid: RemoteJid, maxResults: 50);

            foreach (var item in mamResult.Messages)
            {
                var m = item.Message;
                if (!string.IsNullOrEmpty(m.Body))
                {
                    var chatMsg = new ChatMessage
                    {
                        Id = $"mam_{_accountJid}_{RemoteJid}_{item.ArchiveId}",
                        AccountJid = _accountJid,
                        RemoteJid = RemoteJid.ToString(),
                        SenderJid = (m.From ?? RemoteJid).ToString(),
                        Body = m.Body,
                        Direction = (m.From?.EqualsBare(_client?.BoundJid) == true) ? MessageDirection.Outbound : MessageDirection.Inbound,
                        Timestamp = item.Timestamp,
                        StanzaId = item.ArchiveId
                    };
                    await _messageRepo.SaveMessageAsync(chatMsg);
                    AddOrUpdateMessage(chatMsg);
                }
            }

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
            var oldestTimestamp = Messages.Count > 0 ? Messages[0].Timestamp : OldestMessageTimestamp;
            if (!oldestTimestamp.HasValue) return;

            // 1. Fetch from SQLite before oldest timestamp
            var olderLocal = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 30, before: oldestTimestamp);

            // 2. If SQLite has fewer than 30 older messages, query MAM paging backwards
            if (olderLocal.Count < 30 && _mamManager is not null)
            {
                try
                {
                    // XEP-0313 Section 4.1.4: Page backwards with 'end' timestamp and empty <before/>
                    var mamResult = await _mamManager.QueryArchiveAsync(
                        withJid: RemoteJid,
                        maxResults: 30,
                        end: oldestTimestamp.Value);

                    foreach (var item in mamResult.Messages)
                    {
                        var m = item.Message;
                        if (!string.IsNullOrEmpty(m.Body))
                        {
                            var chatMsg = new ChatMessage
                            {
                                Id = $"mam_{_accountJid}_{RemoteJid}_{item.ArchiveId}",
                                AccountJid = _accountJid,
                                RemoteJid = RemoteJid.ToString(),
                                SenderJid = (m.From ?? RemoteJid).ToString(),
                                Body = m.Body,
                                Direction = (m.From?.EqualsBare(_client?.BoundJid) == true) ? MessageDirection.Outbound : MessageDirection.Inbound,
                                Timestamp = item.Timestamp,
                                StanzaId = item.ArchiveId
                            };
                            await _messageRepo.SaveMessageAsync(chatMsg);
                        }
                    }

                    // Reload older from SQLite after ingesting MAM
                    olderLocal = await _messageRepo.GetMessagesAsync(_accountJid, RemoteJid.ToString(), limit: 30, before: oldestTimestamp);
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

            UpdateDateHeaders();
        }
        finally
        {
            IsLoadingOlderHistory = false;
        }
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || _client is null) return;

        string textToSend = InputText.Trim();
        InputText = string.Empty;

        var chatMsg = new ChatMessage
        {
            AccountJid = _accountJid,
            RemoteJid = RemoteJid.ToString(),
            SenderJid = _client.BoundJid.ToString(),
            Body = textToSend,
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow,
            IsEncrypted = IsEncrypted,
            EncryptionType = IsEncrypted ? "OMEMO" : null
        };

        if (IsGroupChat)
        {
            var stanza = MessageStanza.CreateGroupChat(RemoteJid, textToSend);
            await _client.SendStanzaAsync(stanza);
            chatMsg.StanzaId = stanza.Id;
        }
        else
        {
            var stanza = MessageStanza.CreateChat(RemoteJid, textToSend);
            await _client.SendStanzaAsync(stanza);
            chatMsg.StanzaId = stanza.Id;
        }

        await _messageRepo.SaveMessageAsync(chatMsg);
        AddOrUpdateMessage(chatMsg);
        UpdateDateHeaders();
        RequestScrollToBottom();
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

    public static bool IsSameMessage(MessageBubbleViewModel bubble, ChatMessage msg)
    {
        if (bubble.Id == msg.Id) return true;
        if (!string.IsNullOrEmpty(bubble.StanzaId) && !string.IsNullOrEmpty(msg.StanzaId) && bubble.StanzaId == msg.StanzaId) return true;
        if (!string.IsNullOrEmpty(bubble.StanzaId) && bubble.StanzaId == msg.Id) return true;
        if (!string.IsNullOrEmpty(msg.StanzaId) && msg.StanzaId == bubble.Id) return true;

        // Content similarity within 60 seconds
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

    public void AddOrUpdateMessage(ChatMessage msg)
    {
        var existing = Messages.FirstOrDefault(m => IsSameMessage(m, msg));
        if (existing is not null)
        {
            if (string.IsNullOrEmpty(existing.StanzaId) && !string.IsNullOrEmpty(msg.StanzaId))
            {
                existing.StanzaId = msg.StanzaId;
            }
            if (msg.IsRead && !existing.IsRead)
            {
                existing.IsRead = true;
            }
            return;
        }

        var bubble = MessageBubbleViewModel.FromChatMessage(msg);
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
    }
}
