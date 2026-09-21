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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, (string Show, string? Status, int Priority)>> _contactResourcePresence = new(StringComparer.OrdinalIgnoreCase);

    private static int ShowScore(string show) => show switch
    {
        "chat" => 4,
        "available" or "online" => 3,
        "away" => 2,
        "dnd" => 1,
        "xa" => 0,
        _ => -1
    };

    private void ApplyPresenceToContact(ContactItemViewModel contact)
    {
        if (_contactResourcePresence.TryGetValue(contact.ContactJid, out var resources) && !resources.IsEmpty)
        {
            var best = resources.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            contact.PresenceShow = best.Show;
            contact.StatusMessage = best.Status;
        }
    }

    public string AppVersion => Helpers.AppVersionHelper.Version;

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

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(newValue.RemoteJid.ToString(), StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(c.ContactJid, out var cj) && cj.EqualsBare(newValue.RemoteJid)));
            if (contact is not null)
            {
                contact.UnreadCount = 0;
            }

            _ = newValue.EnsureHistoryLoadedAsync();

            if (!_isInitializingActiveChat && !string.IsNullOrWhiteSpace(AccountJid))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _settingsRepo.SetLastActiveChatAsync(AccountJid, newValue.RemoteJid.BareJid.ToString());
                    }
                    catch
                    {
                        // Soft failure saving active chat setting
                    }
                });
            }
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
            ApplyPresenceToContact(newContact);
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
    private bool _isInitializingActiveChat;

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
        _notificationService = notificationService ?? (NetChatx.Gui.Services.NotificationService.EnableNativeNotifications ? new NetChatx.Gui.Services.NotificationService() : new NetChatx.Gui.Services.NotificationService(dispatchNative: false));
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
            },
            onCloseActionChanged: closeAction =>
            {
                CloseAction = closeAction;
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
            if (_client.State == XmppClientState.Connected)
            {
                await _carbons.EnableAsync();
            }
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

        // Wire incoming presence
        _client.PresenceReceived += async pres =>
        {
            await HandleIncomingPresenceAsync(pres);
        };

        // Restore last presence mode and status message from settings per user preference
        string initialPresence = SettingsRepository.DefaultPresenceMode;
        try
        {
            initialPresence = await _settingsRepo.GetLastPresenceModeAsync(AccountJid);
            var savedStatus = await _settingsRepo.GetLastStatusMessageAsync(AccountJid);
            if (!string.IsNullOrWhiteSpace(savedStatus))
            {
                StatusMessage = savedStatus;
            }
        }
        catch
        {
            // Soft failure reading presence settings
        }

        UserPresence = initialPresence;

        // Send initial presence per RFC 6121 to signal availability and release queued offline messages
        try
        {
            await SetPresenceAsync(initialPresence);
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
            CloseAction = Settings.CloseAction;
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
            var item = ContactItemViewModel.FromRosterContact(c);
            ApplyPresenceToContact(item);
            Contacts.Add(item);
        }

        // Query roster from server per RFC 6121
        try
        {
            if (_client.State == XmppClientState.Connected)
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
                            var newItem = new ContactItemViewModel
                            {
                                AccountJid = AccountJid,
                                ContactJid = cJid,
                                Name = name,
                                Subscription = sub
                            };
                            ApplyPresenceToContact(newItem);
                            Contacts.Add(newItem);
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

        // Restore last active chat from settings, or fall back to the first contact
        bool restoredActiveChat = false;
        try
        {
            _isInitializingActiveChat = true;
            var lastActiveChat = await _settingsRepo.GetLastActiveChatAsync(AccountJid);
            if (!string.IsNullOrWhiteSpace(lastActiveChat))
            {
                var targetContact = Contacts.FirstOrDefault(c =>
                    c.ContactJid.Equals(lastActiveChat, StringComparison.OrdinalIgnoreCase) ||
                    (Jid.TryParse(c.ContactJid, out var cj) && Jid.TryParse(lastActiveChat, out var lj) && cj.EqualsBare(lj)));

                if (targetContact is not null)
                {
                    await SelectContactAsync(targetContact);
                    restoredActiveChat = true;
                }
                else
                {
                    var targetConv = Conversations.FirstOrDefault(c =>
                        c.Id.Equals(lastActiveChat, StringComparison.OrdinalIgnoreCase) ||
                        (Jid.TryParse(lastActiveChat, out var lastJid) && c.RemoteJid.EqualsBare(lastJid)));

                    if (targetConv is not null)
                    {
                        await SelectConversationAsync(targetConv);
                        restoredActiveChat = true;
                    }
                    else if (Jid.TryParse(lastActiveChat, out var parsedLastJid))
                    {
                        var conv = GetOrCreateConversation(parsedLastJid.BareJid.ToString(), parsedLastJid.BareJid.ToString(), parsedLastJid.BareJid, isGroupChat: false);
                        await SelectConversationAsync(conv);
                        restoredActiveChat = true;
                    }
                }
            }
        }
        catch
        {
            // Soft failure restoring active chat
        }
        finally
        {
            _isInitializingActiveChat = false;
        }

        if (!restoredActiveChat && Contacts.Count > 0)
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

    private async Task<bool> HandleSlashCommandAsync(ChatConversationViewModel conv, SlashCommandResult result)
    {
        switch (result.Type)
        {
            case SlashCommandResultType.ChangeStatus:
                if (!string.IsNullOrEmpty(result.StatusShow))
                {
                    StatusMessage = result.StatusMessage ?? StatusMessage;
                    await SetPresenceAsync(result.StatusShow);
                    conv.AddSystemMessage($"Status changed to {result.StatusShow}{(string.IsNullOrEmpty(StatusMessage) ? "" : $" ({StatusMessage})")}.");
                }
                return true;

            case SlashCommandResultType.SetTopic:
                if (conv.IsGroupChat && !string.IsNullOrEmpty(result.MessageText))
                {
                    conv.Title = result.MessageText;
                    conv.AddSystemMessage($"Group topic changed to: {result.MessageText}");
                }
                return true;

            case SlashCommandResultType.JoinRoom:
            case SlashCommandResultType.OpenChat:
                if (!string.IsNullOrEmpty(result.TargetJid))
                {
                    await SelectConversationByIdAsync(result.TargetJid);
                    if (!string.IsNullOrEmpty(result.MessageText) && ActiveConversation is not null)
                    {
                        ActiveConversation.InputText = result.MessageText;
                        await ActiveConversation.SendMessageAsync();
                    }
                }
                return true;

            case SlashCommandResultType.LeaveRoom:
                conv.AddSystemMessage($"Left room {conv.Title}.");
                Conversations.Remove(conv);
                if (ActiveConversation == conv)
                {
                    ActiveConversation = Conversations.FirstOrDefault();
                }
                return true;

            case SlashCommandResultType.ChangeNick:
                if (conv.IsGroupChat && !string.IsNullOrEmpty(result.MessageText))
                {
                    conv.AddSystemMessage($"Nickname changed to: {result.MessageText}");
                }
                return true;

            default:
                return false;
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

        newConv.SlashCommandHandler = async result => await HandleSlashCommandAsync(newConv, result);

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
            if (!string.IsNullOrWhiteSpace(AccountJid))
            {
                await _settingsRepo.SetLastPresenceModeAsync(AccountJid, show);
                await _settingsRepo.SetLastStatusMessageAsync(AccountJid, StatusMessage);
            }
        }
        catch
        {
            // Soft failure saving presence setting
        }

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
        _contactResourcePresence.Clear();
        foreach (var c in Contacts)
        {
            c.PresenceShow = "offline";
            c.StatusMessage = null;
        }
        await _client.DisconnectAsync();
        await _onDisconnectRequested();
    }

    internal Task HandleIncomingPresenceAsync(PresenceStanza presence)
    {
        var fromJid = presence.From;
        var senderBare = fromJid?.ToBareString();
        if (string.IsNullOrEmpty(senderBare))
        {
            return Task.CompletedTask;
        }

        // Ignore non-presence stanzas (e.g. subscribe, subscribed, unsubscribe, error) for status updates
        if (!string.IsNullOrEmpty(presence.Type) &&
            !string.Equals(presence.Type, PresenceStanza.TypeUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Check if this is our own presence being reflected back
        bool isOurOwnPresence = false;
        if (_client.BoundJid is not null)
        {
            isOurOwnPresence = fromJid is not null && (fromJid.Equals(_client.BoundJid) ||
                (string.IsNullOrEmpty(fromJid.Resource) && senderBare.Equals(_client.BoundJid.ToBareString(), StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            isOurOwnPresence = senderBare.Equals(AccountJid, StringComparison.OrdinalIgnoreCase);
        }

        if (isOurOwnPresence)
        {
            string selfShow;
            if (!presence.IsAvailable || string.Equals(presence.Type, PresenceStanza.TypeUnavailable, StringComparison.OrdinalIgnoreCase))
            {
                selfShow = "offline";
            }
            else
            {
                var rawShow = presence.Show?.Trim().ToLowerInvariant();
                selfShow = string.IsNullOrEmpty(rawShow) ? "available" : rawShow;
            }

            PostToUi(() =>
            {
                UserPresence = selfShow;
                if (presence.Status is not null)
                {
                    StatusMessage = presence.Status;
                }
            });
            return Task.CompletedTask;
        }

        var resource = fromJid?.Resource ?? string.Empty;
        var resourceMap = _contactResourcePresence.GetOrAdd(senderBare, _ => new(StringComparer.OrdinalIgnoreCase));

        string aggregateShow;
        string? aggregateStatus;

        if (!presence.IsAvailable || string.Equals(presence.Type, PresenceStanza.TypeUnavailable, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(resource))
            {
                resourceMap.TryRemove(resource, out _);
            }
            else
            {
                resourceMap.Clear();
            }

            if (resourceMap.IsEmpty)
            {
                aggregateShow = "offline";
                aggregateStatus = null;
            }
            else
            {
                var best = resourceMap.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
                aggregateShow = best.Show;
                aggregateStatus = best.Status;
            }
        }
        else
        {
            var rawShow = presence.Show?.Trim().ToLowerInvariant();
            var show = string.IsNullOrEmpty(rawShow) ? "available" : rawShow;
            var priority = presence.Priority ?? 0;

            resourceMap[resource] = (show, presence.Status, priority);

            var best = resourceMap.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            aggregateShow = best.Show;
            aggregateStatus = best.Status;
        }

        PostToUi(() =>
        {
            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(senderBare, StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.PresenceShow = aggregateShow;
                contact.StatusMessage = aggregateStatus;
            }
        });

        return Task.CompletedTask;
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
    public event Action? RequestHideWindow;
    public event Action? RequestExitApp;

    [ObservableProperty]
    private string _closeAction = SettingsRepository.DefaultCloseAction;

    [ObservableProperty]
    private bool _isClosePromptOpen;

    [ObservableProperty]
    private bool _rememberCloseChoice;

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

    public bool HandleWindowClosing(Action hideWindow, Action exitApp)
    {
        if (CloseAction == "Minimize")
        {
            hideWindow();
            return false;
        }
        else if (CloseAction == "Exit")
        {
            return true;
        }
        else
        {
            RememberCloseChoice = false;
            IsClosePromptOpen = true;
            return false;
        }
    }

    [RelayCommand]
    public async Task ChooseMinimizeToTrayAsync()
    {
        if (RememberCloseChoice)
        {
            CloseAction = "Minimize";
            await _settingsRepo.SetCloseActionAsync(AccountJid, "Minimize");
            Settings.CloseAction = "Minimize";
        }
        IsClosePromptOpen = false;
        RequestHideWindow?.Invoke();
    }

    [RelayCommand]
    public async Task ChooseExitAppAsync()
    {
        if (RememberCloseChoice)
        {
            CloseAction = "Exit";
            await _settingsRepo.SetCloseActionAsync(AccountJid, "Exit");
            Settings.CloseAction = "Exit";
        }
        IsClosePromptOpen = false;
        RequestExitApp?.Invoke();
    }

    [RelayCommand]
    public void CancelClosePrompt()
    {
        IsClosePromptOpen = false;
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
