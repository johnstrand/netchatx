using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Gui.Converters;
using NetChatx.Gui.Helpers;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Muc;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Sharing;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using NetChatx.Gui.Services;

namespace NetChatx.Gui.ViewModels;

public sealed partial class MainChatViewModel : ViewModelBase
{
    private readonly XmppClient _client;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly RosterRepository _rosterRepo;
    private readonly AccountRepository _accountRepo;
    private readonly OmemoRepository _omemoRepo;
    private readonly SettingsRepository _settingsRepo;
    private readonly Func<Task> _onDisconnectRequested;
    private readonly INotificationService _notificationService;

    private Xep0313MessageArchiveManagement? _mam;
    private Xep0384OmemoManager? _omemo;
    private Xep0045MultiUserChat? _muc;
    private Xep0363HttpFileUpload? _httpUpload;
    private Xep0280MessageCarbons? _carbons;
    private Xep0444Reactions? _reactions;
    private Xep0184MessageDeliveryReceipts? _receipts;
    private Xep0333ChatMarkers? _chatMarkers;
    private Xep0085ChatStates? _chatStates;
    private Xep0359StanzaIds? _stanzaIds;
    private Xep0066OutOfBandData? _oob;
    private Xep0308LastMessageCorrection? _correction;
    private Xep0424MessageRetraction? _retraction;
    private Xep0393MessageStyling? _styling;

    [ObservableProperty]
    private string _accountJid;

    [ObservableProperty]
    private string _userBoundJid;

    [ObservableProperty]
    private string _userPresence = "available";

    [ObservableProperty]
    private string _statusMessage = "Online with NetChatx";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateHeader))]
    private ChatConversationViewModel? _activeConversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateHeader))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleTooltip))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleIcon))]
    private bool _isSidebarOpen = true;

    public bool ShowEmptyStateHeader => !IsSidebarOpen && ActiveConversation == null;

    public string SidebarToggleTooltip => IsSidebarOpen ? "Collapse sidebar (Ctrl+B)" : "Restore sidebar (Ctrl+B)";

    public string SidebarToggleIcon => IsSidebarOpen ? "◀" : "▶";

    partial void OnActiveConversationChanged(ChatConversationViewModel? oldValue, ChatConversationViewModel? newValue)
    {
        CodeBlockEditor.Cancel();

        if (newValue is not null)
        {
            _notificationService.StopFlashing();

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(newValue.RemoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.UnreadCount = 0;
            }

            _ = newValue.EnsureHistoryLoadedAsync();
        }
    }

    [ObservableProperty]
    private CodeBlockEditorViewModel _codeBlockEditor = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _searchResultsHeader = "Search Results";

    [RelayCommand]
    public async Task ExecuteSearchAsync()
    {
        var query = SearchQuery?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            CloseSearch();
            return;
        }

        IsSearching = true;
        SearchResults.Clear();

        var messages = await _messageRepo.SearchMessagesAsync(AccountJid, query, limit: 50);
        foreach (var msg in messages)
        {
            string? displayName = null;
            if (msg.Direction == MessageDirection.Outbound)
            {
                displayName = "Me";
            }
            else
            {
                var contact = Contacts.FirstOrDefault(c =>
                    c.ContactJid.Equals(msg.RemoteJid, StringComparison.OrdinalIgnoreCase) ||
                    c.ContactJid.Equals(msg.SenderJid, StringComparison.OrdinalIgnoreCase));
                if (contact is not null && !string.IsNullOrWhiteSpace(contact.DisplayName))
                {
                    displayName = contact.DisplayName;
                }
            }

            var bubble = MessageBubbleViewModel.FromChatMessage(msg, AccountJid, _settingsRepo, EmojiData.DefaultQuickEmojis, displayName);
            SearchResults.Add(bubble);
        }

        SearchResultsHeader = SearchResults.Count switch
        {
            0 => $"No results for \"{query}\"",
            1 => $"1 result for \"{query}\"",
            _ => $"{SearchResults.Count} results for \"{query}\""
        };
    }

    [RelayCommand]
    public void CloseSearch()
    {
        IsSearching = false;
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SearchResultsHeader = "Search Results";
    }

    [RelayCommand]
    public async Task SelectSearchResultAsync(MessageBubbleViewModel? result)
    {
        if (result is null) return;

        string targetJidStr = !string.IsNullOrEmpty(result.RemoteJid) ? result.RemoteJid : result.SenderName;
        if (Jid.TryParse(targetJidStr, out var parsedTarget))
        {
            var bare = parsedTarget.BareJid;
            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(bare.ToString(), StringComparison.OrdinalIgnoreCase));
            string title = contact?.DisplayName ?? bare.ToString();
            var conv = GetOrCreateConversation(bare.ToString(), title, bare, isGroupChat: false);
            ActiveConversation = conv;
            await conv.EnsureHistoryLoadedAsync();
        }

        IsSearching = false;
    }

    [ObservableProperty]
    private bool _isNewChatDialogOpen;

    [ObservableProperty]
    private string _newChatJid = string.Empty;

    [ObservableProperty]
    private string _newChatDisplayName = string.Empty;

    [ObservableProperty]
    private string _newChatErrorMessage = string.Empty;

    [RelayCommand]
    public void OpenNewChatDialog()
    {
        NewChatJid = string.Empty;
        NewChatDisplayName = string.Empty;
        NewChatErrorMessage = string.Empty;
        IsNewChatDialogOpen = true;
    }

    [RelayCommand]
    public void CancelNewChatDialog()
    {
        IsNewChatDialogOpen = false;
        NewChatErrorMessage = string.Empty;
    }

    [RelayCommand]
    public async Task ConfirmNewChatAsync()
    {
        NewChatErrorMessage = string.Empty;
        var rawJid = NewChatJid?.Trim();
        if (string.IsNullOrWhiteSpace(rawJid))
        {
            NewChatErrorMessage = "Please enter a contact JID.";
            return;
        }

        if (!Jid.TryParse(rawJid, out var parsedJid))
        {
            NewChatErrorMessage = "Invalid JID format (e.g. user@example.com).";
            return;
        }

        var bareJid = parsedJid.BareJid;
        string displayName = !string.IsNullOrWhiteSpace(NewChatDisplayName)
            ? NewChatDisplayName.Trim()
            : (!string.IsNullOrEmpty(bareJid.LocalPart) ? bareJid.LocalPart : bareJid.ToString());

        var existingContact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(bareJid.ToString(), StringComparison.OrdinalIgnoreCase));
        if (existingContact is null)
        {
            var newContact = new ContactItemViewModel
            {
                AccountJid = AccountJid,
                ContactJid = bareJid.ToString(),
                Name = displayName,
                Subscription = "none"
            };
            Contacts.Add(newContact);

            await _rosterRepo.UpsertContactsAsync([new RosterContact
            {
                AccountJid = AccountJid,
                ContactJid = bareJid.ToString(),
                Name = displayName,
                Subscription = "none"
            }]);

            if (_client.State == XmppClientState.Connected)
            {
                try
                {
                    var presence = new PresenceStanza
                    {
                        To = bareJid,
                        Type = "subscribe"
                    };
                    await _client.SendStanzaAsync(presence);
                }
                catch
                {
                    // Soft failure sending subscribe presence
                }
            }
        }

        var conv = GetOrCreateConversation(bareJid.ToString(), displayName, bareJid, isGroupChat: false);
        ActiveConversation = conv;
        await conv.EnsureHistoryLoadedAsync();
        IsNewChatDialogOpen = false;
    }

    [ObservableProperty]
    private bool _isDetailsOpen;

    private bool _isInitializingSettings;

    [ObservableProperty]
    private bool _enableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;

    [ObservableProperty]
    private int _messageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;

    partial void OnEnableMessageMergingChanged(bool value)
    {
        if (!_isInitializingSettings)
        {
            _ = _settingsRepo.SetMergeMessagesEnabledAsync(AccountJid, value);
            if (Settings is not null && Settings.EnableMessageMerging != value)
            {
                Settings.EnableMessageMerging = value;
            }
        }
        foreach (var conv in Conversations)
        {
            conv.EnableMessageMerging = value;
        }
    }

    partial void OnMessageMergeThresholdSecondsChanged(int value)
    {
        if (!_isInitializingSettings)
        {
            _ = _settingsRepo.SetMergeMessagesThresholdSecondsAsync(AccountJid, value);
            if (Settings is not null && Settings.MessageMergeThresholdSeconds != value)
            {
                Settings.MessageMergeThresholdSeconds = value;
            }
        }
        foreach (var conv in Conversations)
        {
            conv.MessageMergeThresholdSeconds = value;
        }
    }

    [RelayCommand]
    public void OpenChatSettings()
    {
        Settings.Open();
    }

    [ObservableProperty]
    private SettingsViewModel _settings;

    [ObservableProperty]
    private string _chatFontFamily = SettingsRepository.DefaultFontFamily;

    [ObservableProperty]
    private double _chatFontSize = SettingsRepository.DefaultFontSize;

    [ObservableProperty]
    private bool _sendOnEnter = SettingsRepository.DefaultSendOnEnter;

    [ObservableProperty]
    private int _chatInputMaxLines = SettingsRepository.DefaultChatInputMaxLines;

    public string MessageInputWatermark => SendOnEnter
        ? "Type a message... (Enter to send, Shift+Enter for newline, or ``` for code)"
        : "Type a message... (Ctrl+Enter to send, Enter for newline, or ``` for code)";

    public INotificationService NotificationService => _notificationService;

    public bool IsWindowActive
    {
        get => _notificationService.IsWindowActive;
        set
        {
            _notificationService.IsWindowActive = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private OmemoDetailsViewModel _omemoDetails = new();

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];
    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];
    public ObservableCollection<MessageBubbleViewModel> SearchResults { get; } = [];

    public MainChatViewModel(
        XmppClient client,
        DatabaseContext dbContext,
        Func<Task> onDisconnectRequested,
        INotificationService? notificationService = null)
    {
        _client = client;
        _dbContext = dbContext;
        _onDisconnectRequested = onDisconnectRequested;
        _notificationService = notificationService ?? new NotificationService();
        _notificationService.WindowActiveChanged += active =>
        {
            OnPropertyChanged(nameof(IsWindowActive));
        };

        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);
        _settingsRepo = new SettingsRepository(_dbContext);

        _accountJid = client.Options.Jid.BareJid.ToString();
        _userBoundJid = client.BoundJid.ToString();

        _settings = new SettingsViewModel(
            _settingsRepo,
            _accountJid,
            onBubbleMergeChanged: (enabled, threshold) =>
            {
                _enableMessageMerging = enabled;
                _messageMergeThresholdSeconds = threshold;
                OnPropertyChanged(nameof(EnableMessageMerging));
                OnPropertyChanged(nameof(MessageMergeThresholdSeconds));
                foreach (var conv in Conversations)
                {
                    conv.EnableMessageMerging = enabled;
                    conv.MessageMergeThresholdSeconds = threshold;
                }
            },
            onPopupsChanged: enabled =>
            {
            },
            onFlashingChanged: enabled =>
            {
                if (!enabled)
                {
                    _notificationService.StopFlashing();
                }
            },
            onTypographyChanged: (font, size) =>
            {
                ChatFontFamily = font;
                ChatFontSize = size;
            },
            onSendOnEnterChanged: sendOnEnter =>
            {
                SendOnEnter = sendOnEnter;
                OnPropertyChanged(nameof(MessageInputWatermark));
            },
            onUse24HourClockChanged: use24h =>
            {
                MessageBubbleViewModel.Use24HourClock = use24h;
                foreach (var conv in Conversations)
                {
                    foreach (var msg in conv.Messages)
                    {
                        msg.RefreshTimeDisplay();
                    }
                }
            },
            onMediaSettingsChanged: (showPreviews, autoDownload) =>
            {
                MessageBubbleViewModel.ShowInlinePreviews = showPreviews;
                MessageBubbleViewModel.AutoDownloadMedia = autoDownload;
                foreach (var conv in Conversations)
                {
                    foreach (var msg in conv.Messages)
                    {
                        msg.RefreshPreviewVisibility();
                    }
                }
            },
            onThemeChanged: (theme, accent) =>
            {
            },
            onQuickEmojisChanged: quickEmojis =>
            {
                foreach (var conv in Conversations)
                {
                    conv.UpdateQuickEmojis(quickEmojis);
                }
            },
            onBubbleColorChanged: (outColor, inColor) =>
            {
                DirectionToBackgroundConverter.SetColors(outColor, inColor);
                foreach (var conv in Conversations)
                {
                    foreach (var msg in conv.Messages)
                    {
                        msg.RefreshBubbleStyle();
                    }
                }
            },
            onChatInputMaxLinesChanged: maxLines =>
            {
                ChatInputMaxLines = maxLines;
            });
    }

    public async Task InitializeAsync()
    {
        // Setup XEP managers
        _mam = new Xep0313MessageArchiveManagement();
        _omemo = new Xep0384OmemoManager();
        _muc = new Xep0045MultiUserChat();
        _httpUpload = new Xep0363HttpFileUpload();
        _carbons = new Xep0280MessageCarbons();
        _reactions = new Xep0444Reactions();
        _receipts = new Xep0184MessageDeliveryReceipts();
        _chatMarkers = new Xep0333ChatMarkers();
        _chatStates = new Xep0085ChatStates();
        _stanzaIds = new Xep0359StanzaIds();
        _oob = new Xep0066OutOfBandData();
        _correction = new Xep0308LastMessageCorrection();
        _retraction = new Xep0424MessageRetraction();
        _styling = new Xep0393MessageStyling();

        await _mam.AttachAsync(_client);
        await _omemo.AttachAsync(_client);
        await _muc.AttachAsync(_client);
        await _httpUpload.AttachAsync(_client);
        await _carbons.AttachAsync(_client);
        await _reactions.AttachAsync(_client);
        await _receipts.AttachAsync(_client);
        await _chatMarkers.AttachAsync(_client);
        await _chatStates.AttachAsync(_client);
        await _stanzaIds.AttachAsync(_client);
        await _oob.AttachAsync(_client);
        await _correction.AttachAsync(_client);
        await _retraction.AttachAsync(_client);
        await _styling.AttachAsync(_client);

        _correction.MessageCorrected += async (msg, originalId) =>
        {
            await HandleMessageCorrectionAsync(msg, originalId);
        };

        _retraction.MessageRetracted += async (targetId, fromJid) =>
        {
            await HandleMessageRetractionAsync(targetId, fromJid);
        };

        _chatStates.ChatStateReceived += (fromJid, state) =>
        {
            PostToUi(() =>
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.HandleRemoteChatState(state);
            });
        };

        _receipts.ReceiptReceived += (stanzaId, fromJid) =>
        {
            // XEP-0184 Delivery Receipts confirm delivery to the recipient's client,
            // but do not indicate that the message was read or displayed.
            // Do not mark message as read on delivery receipt.
        };

        _chatMarkers.MarkerReceived += async (stanzaId, fromJid, markerType) =>
        {
            // XEP-0333 Chat Markers: Only Displayed and Acknowledged indicate the message has been read.
            // Received indicates delivery only.
            if (markerType is ChatMarkerType.Displayed or ChatMarkerType.Acknowledged)
            {
                await HandleReadMarkerReceivedAsync(stanzaId, fromJid);
            }
        };

        try
        {
            await _carbons.EnableAsync();
        }
        catch
        {
            // Soft failure if server does not support carbons
        }

        // Wire incoming messages
        _client.MessageReceived += async msg =>
        {
            await HandleIncomingMessageAsync(msg);
        };

        // Wire carbon copy messages
        _carbons.CarbonMessageReceived += async (msg, isSentByUs) =>
        {
            await HandleCarbonMessageAsync(msg, isSentByUs);
        };

        // Wire OMEMO decrypted messages
        _omemo.MessageDecrypted += async dec =>
        {
            await HandleDecryptedMessageAsync(dec);
        };

        // Wire reactions
        _reactions.ReactionReceived += async args =>
        {
            await HandleIncomingReactionAsync(args);
        };

        // Send initial presence per RFC 6121 to signal availability and release queued offline messages
        try
        {
            await SetPresenceAsync("available");
        }
        catch
        {
            // Soft failure on initial presence
        }

        // Load account settings
        try
        {
            _isInitializingSettings = true;
            await Settings.LoadSettingsAsync();
            EnableMessageMerging = Settings.EnableMessageMerging;
            MessageMergeThresholdSeconds = Settings.MessageMergeThresholdSeconds;
            ChatFontFamily = Settings.EffectiveFontFamily;
            ChatFontSize = Settings.FontSize;
            SendOnEnter = Settings.SendOnEnter;
            ChatInputMaxLines = Settings.ChatInputMaxLines;
            MessageBubbleViewModel.Use24HourClock = Settings.Use24HourClock;
            MessageBubbleViewModel.ShowInlinePreviews = Settings.ShowInlinePreviews;
            MessageBubbleViewModel.AutoDownloadMedia = Settings.AutoDownloadMedia;
            DirectionToBackgroundConverter.SetColors(Settings.OutboundBubbleColor, Settings.InboundBubbleColor);
            foreach (var conv in Conversations)
            {
                foreach (var msg in conv.Messages)
                {
                    msg.RefreshBubbleStyle();
                }
            }
            OnPropertyChanged(nameof(MessageInputWatermark));
        }
        catch
        {
            // Soft failure loading settings
        }
        finally
        {
            _isInitializingSettings = false;
        }

        // Load cached contacts from SQLite
        var cachedContacts = await _rosterRepo.GetContactsAsync(AccountJid);
        foreach (var c in cachedContacts)
        {
            Contacts.Add(ContactItemViewModel.FromRosterContact(c));
        }

        // Query roster from server per RFC 6121
        try
        {
            var rosterIq = IqStanza.CreateGet();
            rosterIq.RawElement.Child(new XmppElement("query", "jabber:iq:roster"));
            var result = await _client.SendIqAsync(rosterIq);
            var queryElem = result.RawElement.Element("query", "jabber:iq:roster");
            if (queryElem is not null)
            {
                var contactsToUpsert = new System.Collections.Generic.List<RosterContact>();
                foreach (var item in queryElem.Elements("item"))
                {
                    string? cJid = item.GetAttr("jid");
                    string? name = item.GetAttr("name");
                    string sub = item.GetAttr("subscription") ?? "none";
                    if (!string.IsNullOrEmpty(cJid))
                    {
                        contactsToUpsert.Add(new RosterContact
                        {
                            AccountJid = AccountJid,
                            ContactJid = cJid,
                            Name = name,
                            Subscription = sub
                        });

                        var existing = Contacts.FirstOrDefault(x => x.ContactJid.Equals(cJid, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                        {
                            Contacts.Add(new ContactItemViewModel
                            {
                                AccountJid = AccountJid,
                                ContactJid = cJid,
                                Name = name,
                                Subscription = sub
                            });
                        }
                        else
                        {
                            existing.Name = name;
                            existing.Subscription = sub;
                        }
                    }
                }

                if (contactsToUpsert.Count > 0)
                {
                    await _rosterRepo.UpsertContactsAsync(contactsToUpsert);
                }
            }
        }
        catch
        {
            // Soft failure for roster fetch
        }

        // Populate unread badges and last previews from local SQLite database,
        // and ensure any contacts with existing message history appear in Contacts and Conversations
        try
        {
            var summaries = await _messageRepo.GetContactSummariesAsync(AccountJid);
            foreach (var kvp in summaries)
            {
                var existing = Contacts.FirstOrDefault(x => x.ContactJid.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    Contacts.Add(new ContactItemViewModel
                    {
                        AccountJid = AccountJid,
                        ContactJid = kvp.Key,
                        Name = kvp.Key,
                        Subscription = "none",
                        UnreadCount = kvp.Value.unreadCount,
                        LastMessagePreview = kvp.Value.lastPreview
                    });
                }
                else
                {
                    existing.UnreadCount = kvp.Value.unreadCount;
                    existing.LastMessagePreview = kvp.Value.lastPreview;
                }
            }

            // Populate Conversations so the "Chats" tab immediately displays existing chats
            foreach (var contact in Contacts)
            {
                if (Jid.TryParse(contact.ContactJid, out var jid))
                {
                    GetOrCreateConversation(jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
                }
            }
        }
        catch
        {
            // Soft failure loading contact summaries
        }

        // Trigger background account-wide archive catch-up (XEP-0313 MAM) for any missed messages
        _ = CatchUpAccountArchiveAsync();

        // If contacts exist, select the first one by default
        if (Contacts.Count > 0)
        {
            await SelectContactAsync(Contacts[0]);
        }
    }

    public async Task CatchUpAccountArchiveAsync()
    {
        if (_mam is null) return;

        try
        {
            var parsedAccountJid = Jid.TryParse(AccountJid, out var parsedAcc) ? parsedAcc : null;
            var latestTimestamp = await _messageRepo.GetLatestMessageTimestampAsync(AccountJid);
            DateTimeOffset? startTimestamp = latestTimestamp;
            string? beforeId = null;
            string? afterId = null;
            const int maxPages = 10;
            int pagesFetched = 0;

            while (pagesFetched < maxPages)
            {
                MamQueryResult mamResult;
                if (startTimestamp.HasValue)
                {
                    mamResult = await _mam.QueryArchiveAsync(withJid: null, maxResults: 50, start: startTimestamp.Value, after: afterId);
                }
                else
                {
                    mamResult = await _mam.QueryArchiveAsync(withJid: null, maxResults: 50, before: beforeId);
                }

                pagesFetched++;

                if (mamResult.Messages.Count == 0)
                {
                    if (startTimestamp.HasValue && pagesFetched == 1)
                    {
                        // Fallback to querying latest page with RSM <before/> in case start filter yields nothing
                        startTimestamp = null;
                        pagesFetched = 0;
                        continue;
                    }
                    break;
                }

                var chatMsgs = new System.Collections.Generic.List<ChatMessage>();
                foreach (var item in mamResult.Messages)
                {
                    var m = item.Message;
                    bool isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
                                   || (m.From?.EqualsBare(_client.BoundJid) == true);

                    if (Xep0444Reactions.TryExtractReaction(m.RawElement, isCarbonSent: isFromSelf, out var reactArgs))
                    {
                        await HandleIncomingReactionAsync(reactArgs);
                        continue;
                    }

                    var defaultRemote = m.Type == MessageStanza.TypeGroupChat
                        ? (m.From ?? m.To)
                        : (isFromSelf ? m.To : m.From);
                    if (defaultRemote is null) continue;

                    var chatMsg = ChatConversationViewModel.ParseMamMessage(
                        item,
                        AccountJid,
                        defaultRemote.BareJid,
                        m.Type == MessageStanza.TypeGroupChat,
                        _client);

                    if (chatMsg is not null)
                    {
                        chatMsgs.Add(chatMsg);
                    }
                }

                if (chatMsgs.Count > 0)
                {
                    await _messageRepo.SaveMessagesAsync(chatMsgs);

                    PostToUi(() =>
                    {
                        foreach (var msg in chatMsgs)
                        {
                            var conv = Conversations.FirstOrDefault(c => Jid.TryParse(msg.RemoteJid, out var rJid) && c.RemoteJid.EqualsBare(rJid));
                            if (conv is not null)
                            {
                                conv.ReceiveMessage(msg);
                            }
                            else if (ActiveConversation?.RemoteJid.ToString().Equals(msg.RemoteJid, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                ActiveConversation.ReceiveMessage(msg);
                            }

                            var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && Jid.TryParse(msg.RemoteJid, out var rJid) && cJid.EqualsBare(rJid));
                            if (contact is not null)
                            {
                                contact.LastMessagePreview = msg.Body;
                                if (ActiveConversation?.RemoteJid.ToString() != msg.RemoteJid && msg.Direction == MessageDirection.Inbound)
                                {
                                    contact.UnreadCount++;
                                }
                            }
                            else
                            {
                                contact = new ContactItemViewModel
                                {
                                    AccountJid = AccountJid,
                                    ContactJid = msg.RemoteJid,
                                    Name = msg.RemoteJid,
                                    Subscription = "none",
                                    LastMessagePreview = msg.Body,
                                    UnreadCount = (ActiveConversation?.RemoteJid.ToString() != msg.RemoteJid && msg.Direction == MessageDirection.Inbound) ? 1 : 0
                                };
                                Contacts.Add(contact);
                                if (Jid.TryParse(msg.RemoteJid, out var parsedJid))
                                {
                                    GetOrCreateConversation(parsedJid.BareJid.ToString(), contact.DisplayName, parsedJid.BareJid, isGroupChat: false);
                                }
                            }
                        }
                    });
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
        }
        catch
        {
            // Soft failure on account archive catch-up
        }
    }

    [RelayCommand]
    public async Task SelectContactAsync(ContactItemViewModel contact)
    {
        if (!Jid.TryParse(contact.ContactJid, out var jid)) return;

        contact.UnreadCount = 0;
        var conv = GetOrCreateConversation(jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
        ActiveConversation = conv;
        await conv.EnsureHistoryLoadedAsync();

        // Load OMEMO devices for contact
        OmemoDetails.ContactJid = contact.ContactJid;
        var devices = await _omemoRepo.GetSessionsAsync(AccountJid, contact.ContactJid);
        var devVms = devices.Select(d => new OmemoDeviceItemViewModel(
            _omemoRepo,
            AccountJid,
            contact.ContactJid,
            (uint)d.DeviceId,
            $"{d.DeviceId:X8}",
            (OmemoTrustState)d.TrustState
        ));
        OmemoDetails.LoadDevices(devVms);
    }

    [RelayCommand]
    public async Task SelectConversationAsync(ChatConversationViewModel conv)
    {
        ActiveConversation = conv;
        _notificationService.StopFlashing();

        var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(conv.RemoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
        if (contact is not null)
        {
            contact.UnreadCount = 0;
        }

        await conv.EnsureHistoryLoadedAsync();
    }

    public async Task SelectConversationByIdAsync(string conversationId)
    {
        if (!Jid.TryParse(conversationId, out var parsedJid)) return;

        var conv = Conversations.FirstOrDefault(c => c.Id.Equals(conversationId, StringComparison.OrdinalIgnoreCase)
                                                  || c.RemoteJid.EqualsBare(parsedJid));
        if (conv is not null)
        {
            await SelectConversationAsync(conv);
        }
        else
        {
            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(conversationId, StringComparison.OrdinalIgnoreCase)
                                                    || (Jid.TryParse(c.ContactJid, out var cj) && cj.EqualsBare(parsedJid)));
            string title = contact?.DisplayName ?? parsedJid.BareJid.ToString();
            var newConv = GetOrCreateConversation(parsedJid.BareJid.ToString(), title, parsedJid.BareJid, isGroupChat: false);
            await SelectConversationAsync(newConv);
        }
    }

    public void TriggerNotification(string remoteJid, string senderDisplayName, string previewText, bool isEncrypted)
    {
        bool isChatActiveAndFocused = IsWindowActive && (ActiveConversation?.Id == remoteJid || (Jid.TryParse(remoteJid, out var rj) && ActiveConversation?.RemoteJid.EqualsBare(rj) == true));

        if (isChatActiveAndFocused)
        {
            // Do not spam OS notifications if the user is actively viewing this conversation in the foreground window
            return;
        }

        if (Settings.NotificationPopupsEnabled)
        {
            string title = isEncrypted ? $"🔒 {senderDisplayName}" : senderDisplayName;
            _notificationService.ShowSystemNotification(title, previewText);
        }

        if (Settings.IconFlashingEnabled)
        {
            _notificationService.FlashWindow();
        }
    }

    public ChatConversationViewModel GetOrCreateConversation(string id, string title, Jid remoteJid, bool isGroupChat)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        if (!isGroupChat && (title == id || title == remoteJid.ToString()))
        {
            var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(remoteJid));
            if (contact is not null && !string.IsNullOrWhiteSpace(contact.DisplayName))
            {
                title = contact.DisplayName;
            }
        }

        var newConv = new ChatConversationViewModel(
            AccountJid,
            id,
            title,
            remoteJid,
            isGroupChat,
            _messageRepo,
            _client,
            _mam,
            _omemo,
            _httpUpload,
            _reactions,
            _chatMarkers,
            _chatStates,
            _settingsRepo)
        {
            EnableMessageMerging = EnableMessageMerging,
            MessageMergeThresholdSeconds = MessageMergeThresholdSeconds
        };

        newConv.MessageProcessed += msg =>
        {
            PostToUi(() =>
            {
                var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(newConv.RemoteJid));
                if (contact is not null)
                {
                    contact.LastMessagePreview = msg.Body;
                }
            });
        };

        Conversations.Add(newConv);
        return newConv;
    }

    private async Task HandleReadMarkerReceivedAsync(string stanzaId, Jid? fromJid)
    {
        if (string.IsNullOrEmpty(stanzaId)) return;

        await _messageRepo.MarkMessageAsReadAsync(AccountJid, stanzaId);

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.MarkMessageAsRead(stanzaId);
            }
            else
            {
                foreach (var conv in Conversations)
                {
                    conv.MarkMessageAsRead(stanzaId);
                }
            }
        });
    }

    [RelayCommand]
    public void ToggleDetails()
    {
        IsDetailsOpen = !IsDetailsOpen;
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    [RelayCommand]
    public void CollapseSidebar()
    {
        IsSidebarOpen = false;
    }

    [RelayCommand]
    public void RestoreSidebar()
    {
        IsSidebarOpen = true;
    }

    [RelayCommand]
    public async Task SetPresenceAsync(string show)
    {
        UserPresence = show;
        var stanza = show switch
        {
            "away" => PresenceStanza.Available(show: PresenceStanza.ShowAway, status: StatusMessage),
            "dnd" => PresenceStanza.Available(show: PresenceStanza.ShowDnd, status: StatusMessage),
            "xa" => PresenceStanza.Available(show: PresenceStanza.ShowXa, status: StatusMessage),
            _ => PresenceStanza.Available(status: StatusMessage)
        };

        try
        {
            await _client.SendStanzaAsync(stanza);
        }
        catch
        {
            // Soft failure on presence send error
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        await _onDisconnectRequested();
    }

    private async Task HandleIncomingReactionAsync(ReactionEventArgs args)
    {
        PostToUi(async () =>
        {
            var conv = GetOrCreateConversation(args.RemoteJid.ToString(), args.RemoteJid.ToString(), args.RemoteJid, isGroupChat: args.IsGroupChat);
            await conv.HandleIncomingReactionAsync(args);
        });
    }

    private static DateTimeOffset ExtractDelayTimestamp(MessageStanza msg)
    {
        var delayElem = msg.RawElement.Element("delay", "urn:xmpp:delay")
                     ?? msg.RawElement.Element("delay")
                     ?? msg.RawElement.Element("x", "jabber:x:delay");
        if (delayElem?.GetAttr("stamp") is string stampStr && DateTimeOffset.TryParse(stampStr, out var parsedStamp))
        {
            return parsedStamp;
        }
        return DateTimeOffset.UtcNow;
    }

    private async Task HandleIncomingMessageAsync(MessageStanza msg)
    {
        var replaceElem = msg.RawElement.Element("replace", "urn:xmpp:message-correct:0");
        if (replaceElem is not null)
        {
            string? originalId = replaceElem.GetAttr("id");
            if (!string.IsNullOrEmpty(originalId))
            {
                await HandleMessageCorrectionAsync(msg, originalId);
                return;
            }
        }

        var retractElem = msg.RawElement.Element("retract", "urn:xmpp:message-retract:0")
                       ?? msg.RawElement.Element("retract", "urn:xmpp:message-retract:1");
        if (retractElem is not null)
        {
            string? targetId = retractElem.GetAttr("id");
            if (!string.IsNullOrEmpty(targetId))
            {
                await HandleMessageRetractionAsync(targetId, msg.From);
                return;
            }
        }

        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(AccountJid);
        var sender = msg.From ?? accountJid;

        bool isFromSelf = sender.EqualsBare(accountJid);
        Jid remote;
        MessageDirection direction;

        if (isFromSelf)
        {
            remote = (msg.To ?? accountJid).BareJid;
            direction = MessageDirection.Outbound;
        }
        else
        {
            remote = msg.Type == MessageStanza.TypeGroupChat ? sender.BareJid : sender.BareJid;
            direction = MessageDirection.Inbound;
        }

        bool isGroup = msg.Type == MessageStanza.TypeGroupChat;
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = direction,
            Timestamp = ExtractDelayTimestamp(msg),
            StanzaId = msg.Id,
            RawXml = msg.ToXmlString(indent: true),
            IsRead = direction == MessageDirection.Inbound && isActiveConv
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && _chatMarkers is not null)
        {
            try
            {
                await _chatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = msg.Body;
                if (ActiveConversation?.Id != remote.ToString() && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }

            if (direction == MessageDirection.Inbound)
            {
                var senderDisplayName = contact?.DisplayName ?? (sender.IsBare ? sender.LocalPart : sender.Resource) ?? sender.ToString();
                TriggerNotification(remote.ToString(), senderDisplayName, msg.Body, isEncrypted: false);
            }
        });
    }

    private async Task HandleCarbonMessageAsync(MessageStanza msg, bool isSentByUs)
    {
        var replaceElem = msg.RawElement.Element("replace", "urn:xmpp:message-correct:0");
        if (replaceElem is not null)
        {
            string? originalId = replaceElem.GetAttr("id");
            if (!string.IsNullOrEmpty(originalId))
            {
                await HandleMessageCorrectionAsync(msg, originalId);
                return;
            }
        }

        var retractElem = msg.RawElement.Element("retract", "urn:xmpp:message-retract:0")
                       ?? msg.RawElement.Element("retract", "urn:xmpp:message-retract:1");
        if (retractElem is not null)
        {
            string? targetId = retractElem.GetAttr("id");
            if (!string.IsNullOrEmpty(targetId))
            {
                await HandleMessageRetractionAsync(targetId, msg.From);
                return;
            }
        }

        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(AccountJid);
        Jid remote;
        MessageDirection direction;

        if (isSentByUs)
        {
            remote = (msg.To ?? accountJid).BareJid;
            direction = MessageDirection.Outbound;
        }
        else
        {
            remote = (msg.From ?? accountJid).BareJid;
            direction = MessageDirection.Inbound;
        }

        bool isGroup = msg.Type == MessageStanza.TypeGroupChat;
        var sender = msg.From ?? (isSentByUs ? accountJid : remote);
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = direction,
            Timestamp = ExtractDelayTimestamp(msg),
            StanzaId = msg.Id,
            RawXml = msg.ToXmlString(indent: true),
            IsRead = direction == MessageDirection.Inbound && isActiveConv
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && _chatMarkers is not null)
        {
            try
            {
                await _chatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = msg.Body;
                if (ActiveConversation?.Id != remote.ToString() && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }

            if (direction == MessageDirection.Inbound)
            {
                var senderDisplayName = contact?.DisplayName ?? (sender.IsBare ? sender.LocalPart : sender.Resource) ?? sender.ToString();
                TriggerNotification(remote.ToString(), senderDisplayName, msg.Body, isEncrypted: false);
            }
        });
    }

    private async Task HandleDecryptedMessageAsync(DecryptedOmemoMessage dec)
    {
        var remoteJid = dec.SenderJid.BareJid;
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remoteJid) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remoteJid.ToString(),
            SenderJid = dec.SenderJid.ToString(),
            Body = dec.PlaintextBody,
            Direction = MessageDirection.Inbound,
            Timestamp = ExtractDelayTimestamp(dec.OriginalStanza),
            IsEncrypted = true,
            EncryptionType = "OMEMO",
            RawXml = dec.OriginalStanza.ToXmlString(indent: true),
            IsRead = isActiveConv
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remoteJid.ToString(), remoteJid.ToString(), remoteJid, isGroupChat: false);
            conv.IsEncrypted = true;
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = dec.PlaintextBody;
                if (ActiveConversation?.Id != remoteJid.ToString())
                {
                    contact.UnreadCount++;
                }
            }

            var senderDisplayName = contact?.DisplayName ?? dec.SenderJid.LocalPart ?? dec.SenderJid.ToString();
            TriggerNotification(remoteJid.ToString(), senderDisplayName, dec.PlaintextBody, isEncrypted: true);
        });
    }

    private async Task HandleMessageCorrectionAsync(MessageStanza msg, string originalId)
    {
        var accountJid = Jid.Parse(AccountJid);
        var sender = msg.From ?? accountJid;
        bool isFromSelf = sender.EqualsBare(accountJid);
        Jid remote = isFromSelf ? (msg.To ?? accountJid).BareJid : sender.BareJid;
        string newBody = msg.Body ?? string.Empty;

        await _messageRepo.UpdateMessageByReplaceIdAsync(AccountJid, originalId, newBody, msg.Id);

        PostToUi(() =>
        {
            var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(remote));
            conv?.HandleIncomingCorrection(originalId, newBody, msg.ToXmlString(indent: true));

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = newBody;
            }
        });
    }

    private async Task HandleMessageRetractionAsync(string targetId, Jid? fromJid)
    {
        await _messageRepo.DeleteMessageAsync(AccountJid, targetId);

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.HandleIncomingRetraction(targetId);
            }
            else
            {
                foreach (var conv in Conversations)
                {
                    conv.HandleIncomingRetraction(targetId);
                }
            }
        });
    }

    public event Action? CodeBlockInjected;

    [RelayCommand]
    public void OpenCodeBlockEditor()
    {
        OpenCodeBlockEditor(initialCode: string.Empty);
    }

    public void OpenCodeBlockEditor(string initialCode = "", string? languageHint = null, int? insertionIndex = null)
    {
        if (ActiveConversation is null) return;

        CodeBlockEditor.Open(initialCode, languageHint, markdown =>
        {
            ActiveConversation.InjectCodeBlock(markdown, insertionIndex ?? -1);
            PostToUi(() =>
            {
                CodeBlockInjected?.Invoke();
            });
        });
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
