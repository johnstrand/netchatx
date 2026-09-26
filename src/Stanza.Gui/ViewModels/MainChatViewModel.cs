using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Gui.Converters;
using Stanza.Gui.Helpers;
using Stanza.Protocol.Xeps.Common;
using Stanza.Protocol.Xeps.Core;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Protocol.Xeps.Muc;
using Stanza.Protocol.Xeps.Omemo;
using Stanza.Protocol.Xeps.Sharing;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Stanza.Gui.Services;

namespace Stanza.Gui.ViewModels;

public sealed partial class MainChatViewModel : ViewModelBase
{
    private readonly XmppClient _client;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly RosterRepository _rosterRepo;
    private readonly AccountRepository _accountRepo;
    private readonly OmemoRepository _omemoRepo;
    private readonly SettingsRepository _settingsRepo;
    private readonly AvatarRepository _avatarRepo;
    private Stanza.Protocol.Xeps.Avatars.AvatarManager? _avatarManager;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Hash, Avalonia.Media.Imaging.Bitmap Bitmap)> _avatarCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<Task> _onDisconnectRequested;
    private readonly INotificationService _notificationService;
    private readonly ISystemResumeWatcher _resumeWatcher;

    private Xep0199Ping? _ping;
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

    private bool _isManualDisconnect;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly SemaphoreSlim _resumeLock = new(1, 1);
    private Task<bool>? _currentReconnectTask;

    private Action<Stanza.Protocol.Xeps.Avatars.AvatarChangedEventArgs>? _avatarUpdatedHandler;
    private Action<XmppClientState>? _stateChangedHandler;
    private Action<MessageStanza, string>? _messageCorrectedHandler;
    private Action<string, Jid?>? _messageRetractedHandler;
    private Action<Jid, ChatState>? _chatStateReceivedHandler;
    private Action<string, Jid?>? _receiptReceivedHandler;
    private Action<string, Jid?, ChatMarkerType>? _markerReceivedHandler;
    private Func<MessageStanza, Task>? _messageReceivedHandler;
    private Action<MessageStanza, bool>? _carbonMessageReceivedHandler;
    private Action<DecryptedOmemoMessage>? _messageDecryptedHandler;
    private Action<ReactionEventArgs>? _reactionReceivedHandler;
    private Func<PresenceStanza, Task>? _presenceReceivedHandler;
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
        var bare = Jid.TryParse(contact.ContactJid, out var cj) ? cj.ToBareString() : contact.ContactJid;
        if (_contactResourcePresence.TryGetValue(bare, out var resources) && !resources.IsEmpty)
        {
            var best = resources.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            contact.PresenceShow = best.Show;
            contact.StatusMessage = best.Status;

            var conv = Conversations.FirstOrDefault(c =>
                c.RemoteJid.ToString().Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(contact.ContactJid, out var cJid) && c.RemoteJid.EqualsBare(cJid)));
            if (conv is not null)
            {
                conv.PresenceShow = best.Show;
            }
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
    private string _statusMessage = "Online with Stanza";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserAvatar))]
    private Avalonia.Media.Imaging.Bitmap? _userAvatar;

    [ObservableProperty]
    private string? _userAvatarHash;

    public bool HasUserAvatar => UserAvatar != null;

    public string UserInitials => Helpers.AvatarHelper.GetInitials(UserBoundJid ?? AccountJid);

    public Avalonia.Media.IBrush UserAvatarBackgroundBrush => Helpers.AvatarHelper.GetAvatarColorBrush(UserBoundJid ?? AccountJid);

    [ObservableProperty]
    private bool _isAccountSyncing;

    [ObservableProperty]
    private string _syncStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateHeader))]
    private ChatConversationViewModel? _activeConversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateHeader))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleTooltip))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleIcon))]
    private bool _isSidebarOpen = true;

    public bool ShowEmptyStateHeader => !IsSidebarOpen && ActiveConversation == null;

    public string SidebarToggleTooltip => IsSidebarOpen
        ? LocalizationManager.Instance.GetString("Sidebar_Collapse_Tooltip")
        : LocalizationManager.Instance.GetString("Sidebar_Restore_Tooltip");

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
    private HelpViewModel _help = new();

    [ObservableProperty]
    private AboutViewModel _about = new();

    [RelayCommand]
    public void OpenHelp()
    {
        Settings.Close();
        CodeBlockEditor.Cancel();
        IsNewChatDialogOpen = false;
        About.Close();
        Help.Open();
    }

    [RelayCommand]
    public void CloseHelp()
    {
        Help.Close();
    }

    [RelayCommand]
    public void OpenAbout()
    {
        Settings.Close();
        CodeBlockEditor.Cancel();
        IsNewChatDialogOpen = false;
        Help.Close();
        About.Open();
    }

    [RelayCommand]
    public void CloseAbout()
    {
        About.Close();
    }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _searchResultsHeader = LocalizationManager.Instance.GetString("Search_Results");

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
            0 => LocalizationManager.Instance.GetString("Search_NoResults", query),
            1 => LocalizationManager.Instance.GetString("Search_OneResult", query),
            _ => LocalizationManager.Instance.GetString("Search_MultipleResults", SearchResults.Count, query)
        };
    }

    [RelayCommand]
    public void CloseSearch()
    {
        IsSearching = false;
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SearchResultsHeader = LocalizationManager.Instance.GetString("Search_Results");
    }

    [RelayCommand]
    public async Task SelectSearchResultAsync(MessageBubbleViewModel? result)
    {
        if (result is null) return;

        var targetJidStr = !string.IsNullOrEmpty(result.RemoteJid) ? result.RemoteJid : result.SenderName;
        if (Jid.TryParse(targetJidStr, out var parsedTarget))
        {
            var bare = parsedTarget.BareJid;
            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(bare.ToString(), StringComparison.OrdinalIgnoreCase));
            var title = contact?.DisplayName ?? bare.ToString();
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
        Help.Close();
        About.Close();
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
        var displayName = !string.IsNullOrWhiteSpace(NewChatDisplayName)
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

            if (_client.IsReady || _client.State == XmppClientState.Connected)
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
        Help.Close();
        About.Close();
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
        ? LocalizationManager.Instance.GetString("Chat_Watermark_SendOnEnter")
        : LocalizationManager.Instance.GetString("Chat_Watermark_CtrlSendOnEnter");

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
        INotificationService? notificationService = null,
        ISystemResumeWatcher? resumeWatcher = null)
    {
        _client = client;
        _dbContext = dbContext;
        _onDisconnectRequested = onDisconnectRequested;
        _notificationService = notificationService ?? (Stanza.Gui.Services.NotificationService.EnableNativeNotifications ? new Stanza.Gui.Services.NotificationService() : new Stanza.Gui.Services.NotificationService(dispatchNative: false));
        _notificationService.WindowActiveChanged += active =>
        {
            OnPropertyChanged(nameof(IsWindowActive));
        };
        _resumeWatcher = resumeWatcher ?? new SystemResumeWatcher();
        _resumeWatcher.Resumed += async () => await HandleSystemResumeAsync();

        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);
        _settingsRepo = new SettingsRepository(_dbContext);
        _avatarRepo = new AvatarRepository(_dbContext);

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
                if (Settings is not null)
                {
                    DirectionToForegroundConverter.SetColors(Settings.OutboundBubbleTextColor, Settings.InboundBubbleTextColor);
                }
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
            },
            onEmoticonSettingsChanged: (autoReplace, mappings) =>
            {
                foreach (var conv in Conversations)
                {
                    conv.UpdateEmoticonSettings(autoReplace, mappings);
                }
            },
            onEvaluateExpressionsChanged: evaluateExpressions =>
            {
                foreach (var conv in Conversations)
                {
                    conv.UpdateExpressionSettings(evaluateExpressions);
                }
            },
            onAvatarChanged: async (bytes, mime) => await SetUserAvatarFromBytesAsync(bytes, mime),
            onAvatarRemoved: async () => await RemoveUserAvatarAsync(),
            onAvatarSyncRequested: async () => await SyncOwnAvatarFromServerAsync(),
            onLanguageChanged: lang =>
            {
                OnPropertyChanged(nameof(SidebarToggleTooltip));
                OnPropertyChanged(nameof(MessageInputWatermark));
                if (!IsSearching)
                {
                    SearchResultsHeader = LocalizationManager.Instance.GetString("Search_Results");
                }
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
        _ping = new Xep0199Ping();

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
        await _ping.AttachAsync(_client);

        _avatarManager = new Stanza.Protocol.Xeps.Avatars.AvatarManager();
        await _avatarManager.AttachAsync(_client);
        _avatarUpdatedHandler = async args =>
        {
            await HandleAvatarUpdatedAsync(args);
        };
        _avatarManager.AvatarUpdated += _avatarUpdatedHandler;

        _stateChangedHandler = state =>
        {
            if (state == XmppClientState.Disconnected && !_isManualDisconnect)
            {
                PostToUi(() =>
                {
                    StatusMessage = "Connection lost. Reconnecting... ⏳";
                });
                _ = ReconnectAsync().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result)
                    {
                        _ = CatchUpAccountArchiveAsync();
                    }
                });
            }
            else if (state == XmppClientState.Ready)
            {
                PostToUi(() =>
                {
                    StatusMessage = $"Connected as {_client.BoundJid}";
                });
            }
        };
        _client.StateChanged += _stateChangedHandler;

        _messageCorrectedHandler = async (msg, originalId) =>
        {
            await HandleMessageCorrectionAsync(msg, originalId);
        };
        _correction.MessageCorrected += _messageCorrectedHandler;

        _messageRetractedHandler = async (targetId, fromJid) =>
        {
            await HandleMessageRetractionAsync(targetId, fromJid);
        };
        _retraction.MessageRetracted += _messageRetractedHandler;

        _chatStateReceivedHandler = (fromJid, state) =>
        {
            PostToUi(() =>
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.HandleRemoteChatState(state);
            });
        };
        _chatStates.ChatStateReceived += _chatStateReceivedHandler;

        _receiptReceivedHandler = (stanzaId, fromJid) =>
        {
            // XEP-0184 Delivery Receipts confirm delivery to the recipient's client,
            // but do not indicate that the message was read or displayed.
            // Do not mark message as read on delivery receipt.
        };
        _receipts.ReceiptReceived += _receiptReceivedHandler;

        _markerReceivedHandler = async (stanzaId, fromJid, markerType) =>
        {
            // XEP-0333 Chat Markers: Only Displayed and Acknowledged indicate the message has been read.
            // Received indicates delivery only.
            if (markerType is ChatMarkerType.Displayed or ChatMarkerType.Acknowledged)
            {
                await HandleReadMarkerReceivedAsync(stanzaId, fromJid);
            }
        };
        _chatMarkers.MarkerReceived += _markerReceivedHandler;

        try
        {
            if (_client.IsReady || _client.State == XmppClientState.Connected)
            {
                await _carbons.EnableAsync();
            }
        }
        catch
        {
            // Soft failure if server does not support carbons
        }

        // Wire incoming messages
        _messageReceivedHandler = async msg =>
        {
            await HandleIncomingMessageAsync(msg);
        };
        _client.MessageReceived += _messageReceivedHandler;

        // Wire carbon copy messages
        _carbonMessageReceivedHandler = async (msg, isSentByUs) =>
        {
            await HandleCarbonMessageAsync(msg, isSentByUs);
        };
        _carbons.CarbonMessageReceived += _carbonMessageReceivedHandler;

        // Wire OMEMO decrypted messages
        _messageDecryptedHandler = async dec =>
        {
            await HandleDecryptedMessageAsync(dec);
        };
        _omemo.MessageDecrypted += _messageDecryptedHandler;

        // Wire reactions
        _reactionReceivedHandler = async args =>
        {
            await HandleIncomingReactionAsync(args);
        };
        _reactions.ReactionReceived += _reactionReceivedHandler;

        // Wire incoming presence
        _presenceReceivedHandler = async pres =>
        {
            await HandleIncomingPresenceAsync(pres);
        };
        _client.PresenceReceived += _presenceReceivedHandler;

        // Restore last presence mode and status message from settings per user preference
        var initialPresence = SettingsRepository.DefaultPresenceMode;
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

        // Load user avatar and all cached avatars from SQLite before sending initial presence
        try
        {
            var myAvatar = await _avatarRepo.GetAvatarAsync(AccountJid);
            if (myAvatar is not null)
            {
                var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(myAvatar.Data);
                if (bmp is not null)
                {
                    UserAvatar = bmp;
                    UserAvatarHash = myAvatar.Hash;
                    Settings.UserAvatar = bmp;
                    Settings.UserAvatarHash = myAvatar.Hash;
                    _avatarCache[AccountJid] = (myAvatar.Hash, bmp);
                    _avatarManager?.SetCurrentAvatarHash(myAvatar.Hash);
                }
            }
        }
        catch
        {
            // Soft failure loading user avatar
        }

        try
        {
            var allAvatars = await _avatarRepo.GetAllAvatarsAsync();
            foreach (var (jid, rec) in allAvatars)
            {
                var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(rec.Data);
                if (bmp is not null)
                {
                    _avatarCache[jid] = (rec.Hash, bmp);
                }
            }
        }
        catch
        {
            // Soft failure loading avatar cache
        }

        UserPresence = initialPresence;

        // Asynchronously check and sync own avatar from server if missing locally or updated remotely
        _ = SyncOwnAvatarFromServerAsync();

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
            DirectionToForegroundConverter.SetColors(Settings.OutboundBubbleTextColor, Settings.InboundBubbleTextColor);
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
            if (_avatarCache.TryGetValue(item.ContactJid, out var av))
            {
                item.Avatar = av.Bitmap;
                item.AvatarHash = av.Hash;
            }
            Contacts.Add(item);
        }

        // Query roster from server per RFC 6121
        await RefreshRosterFromServerAsync();

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
                    var newContact = new ContactItemViewModel
                    {
                        AccountJid = AccountJid,
                        ContactJid = kvp.Key,
                        Name = kvp.Key,
                        Subscription = "none",
                        UnreadCount = kvp.Value.unreadCount
                    };
                    if (_avatarCache.TryGetValue(newContact.ContactJid, out var av))
                    {
                        newContact.Avatar = av.Bitmap;
                        newContact.AvatarHash = av.Hash;
                    }
                    Contacts.Add(newContact);
                }
                else
                {
                    existing.UnreadCount = kvp.Value.unreadCount;
                    if (existing.Avatar is null && _avatarCache.TryGetValue(existing.ContactJid, out var av))
                    {
                        existing.Avatar = av.Bitmap;
                        existing.AvatarHash = av.Hash;
                    }
                }
            }

            // Populate Conversations so the "Chats" tab immediately displays existing chats
            foreach (var contact in Contacts)
            {
                if (Jid.TryParse(contact.ContactJid, out var jid))
                {
                    var conv = GetOrCreateConversation(jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
                    if (summaries.TryGetValue(jid.BareJid.ToString(), out var summary) || summaries.TryGetValue(contact.ContactJid, out summary))
                    {
                        conv.UpdateSnippet(summary.lastPreview, summary.lastDirection, summary.lastSenderJid, summary.lastTimestamp);
                        contact.LastMessagePreview = conv.LastMessageSnippet;
                    }
                }
            }
        }
        catch
        {
            // Soft failure loading contact summaries
        }

        // Send initial presence per RFC 6121 to signal availability and release queued offline messages
        try
        {
            await SetPresenceAsync(initialPresence);
        }
        catch
        {
            // Soft failure on initial presence
        }

        // Trigger background account-wide archive catch-up (XEP-0313 MAM) for any missed messages
        _ = CatchUpAccountArchiveAsync();

        // Restore last active chat from settings, or fall back to the first contact
        var restoredActiveChat = false;
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

            if (!restoredActiveChat && Contacts.Count > 0)
            {
                await SelectContactAsync(Contacts[0]);
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
    }

    public async Task<bool> CheckConnectionHealthAsync(TimeSpan? timeout = null)
    {
        if (!_client.IsReady || _ping is null) return false;
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
            await _ping.PingAsync(timeout: timeout ?? TimeSpan.FromSeconds(3), ct: cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> EnsureConnectedAsync()
    {
        if (_client.IsReady)
        {
            return Task.FromResult(true);
        }

        return ReconnectAsync();
    }

    public async Task<bool> ReconnectAsync()
    {
        await _reconnectLock.WaitAsync();
        try
        {
            if (_client.IsReady) return true;
            if (_currentReconnectTask is not null && !_currentReconnectTask.IsCompleted)
            {
                return await _currentReconnectTask;
            }

            _currentReconnectTask = PerformReconnectAsync();
            return await _currentReconnectTask;
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private async Task<bool> PerformReconnectAsync()
    {
        PostToUi(() =>
        {
            StatusMessage = "Reconnecting to server... ⏳";
        });

        try
        {
            if (_client.State != XmppClientState.Disconnected)
            {
                await _client.DisconnectAsync();
            }
        }
        catch { }

        int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                PostToUi(() =>
                {
                    StatusMessage = attempt == 1
                        ? "Reconnecting to server... ⏳"
                        : $"Reconnecting to server (attempt {attempt}/{maxAttempts})... ⏳";
                });

                await _client.ConnectAsync();

                if (_client.IsReady)
                {
                    PostToUi(() =>
                    {
                        StatusMessage = $"Connected as {_client.BoundJid}";
                    });

                    if (_carbons is not null)
                    {
                        try { await _carbons.EnableAsync(); } catch { }
                    }

                    try { await SetPresenceAsync(UserPresence); } catch { }

                    await RefreshRosterFromServerAsync();

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reconnect attempt {attempt} failed: {ex.Message}");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(1000 * attempt);
                }
            }
        }

        PostToUi(() =>
        {
            StatusMessage = "Connection lost (Offline)";
        });
        return false;
    }

    public async Task HandleSystemResumeAsync()
    {
        if (!await _resumeLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine("System resume event triggered. Checking connection health...");

            bool isHealthy = false;
            if (_client.IsReady)
            {
                isHealthy = await CheckConnectionHealthAsync(TimeSpan.FromSeconds(3));
            }

            if (!isHealthy)
            {
                System.Diagnostics.Debug.WriteLine("Connection unresponsive after sleep. Reconnecting...");
                var reconnected = await ReconnectAsync();
                if (!reconnected) return;
            }

            System.Diagnostics.Debug.WriteLine("Connected after resume. Syncing messages...");
            _ = CatchUpAccountArchiveAsync();
        }
        finally
        {
            _resumeLock.Release();
        }
    }

    private async Task RefreshRosterFromServerAsync()
    {
        try
        {
            if (_client.IsReady || _client.State == XmppClientState.Connected)
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
                        var cJid = item.GetAttr("jid");
                        var name = item.GetAttr("name");
                        var sub = item.GetAttr("subscription") ?? "none";
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
                                if (_avatarCache.TryGetValue(newItem.ContactJid, out var av))
                                {
                                    newItem.Avatar = av.Bitmap;
                                    newItem.AvatarHash = av.Hash;
                                }
                                Contacts.Add(newItem);
                            }
                            else
                            {
                                existing.Name = name;
                                existing.Subscription = sub;
                                ApplyPresenceToContact(existing);
                                if (existing.Avatar is null && _avatarCache.TryGetValue(existing.ContactJid, out var av))
                                {
                                    existing.Avatar = av.Bitmap;
                                    existing.AvatarHash = av.Hash;
                                }
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
    }

    public async Task CatchUpAccountArchiveAsync()
    {
        if (_mam is null || IsAccountSyncing || !_client.IsReady) return;

        IsAccountSyncing = true;
        SyncStatusMessage = "Syncing messages... ⏳";

        try
        {
            var parsedAccountJid = Jid.TryParse(AccountJid, out var parsedAcc) ? parsedAcc : null;
            var latestTimestamp = await _messageRepo.GetLatestMessageTimestampAsync(AccountJid);
            var startTimestamp = latestTimestamp;
            string? beforeId = null;
            string? afterId = null;
            var pagesFetched = 0;
            var totalFetchedCount = 0;
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
                            mamResult = await _mam.QueryArchiveAsync(withJid: null, maxResults: 50, start: startTimestamp.Value, after: afterId);
                        }
                        else
                        {
                            mamResult = await _mam.QueryArchiveAsync(withJid: null, maxResults: 50, before: beforeId);
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
                totalFetchedCount += mamResult.Messages.Count;
                SyncStatusMessage = $"Syncing messages... ({totalFetchedCount} received)";

                var chatMsgs = new System.Collections.Generic.List<ChatMessage>();
                foreach (var item in mamResult.Messages)
                {
                    var m = item.Message;
                    var isFromSelf = (m.From is not null && parsedAccountJid is not null && m.From.EqualsBare(parsedAccountJid))
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
                        var groups = chatMsgs.GroupBy(m => m.RemoteJid, StringComparer.OrdinalIgnoreCase);
                        foreach (var group in groups)
                        {
                            var remoteJidStr = group.Key;
                            var conv = Conversations.FirstOrDefault(c => Jid.TryParse(remoteJidStr, out var rJid) && c.RemoteJid.EqualsBare(rJid));
                            if (conv is not null)
                            {
                                conv.ReceiveMessages(group);
                            }
                            else if (ActiveConversation?.RemoteJid.ToString().Equals(remoteJidStr, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                ActiveConversation.ReceiveMessages(group);
                                conv = ActiveConversation;
                            }

                            var lastMsg = group.Last();
                            var inboundCount = group.Count(m => m.Direction == MessageDirection.Inbound);
                            var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && Jid.TryParse(remoteJidStr, out var rJid) && cJid.EqualsBare(rJid));
                            if (conv is null && Jid.TryParse(remoteJidStr, out var parsedJid))
                            {
                                conv = GetOrCreateConversation(parsedJid.BareJid.ToString(), contact?.DisplayName ?? remoteJidStr, parsedJid.BareJid, isGroupChat: false);
                            }
                            conv?.UpdateLastMessageSnippetAndTime(lastMsg);

                            if (contact is not null)
                            {
                                contact.LastMessagePreview = conv?.LastMessageSnippet ?? lastMsg.Body;
                                if (ActiveConversation?.RemoteJid.ToString() != remoteJidStr)
                                {
                                    contact.UnreadCount += inboundCount;
                                }
                            }
                            else
                            {
                                contact = new ContactItemViewModel
                                {
                                    AccountJid = AccountJid,
                                    ContactJid = remoteJidStr,
                                    Name = remoteJidStr,
                                    Subscription = "none",
                                    LastMessagePreview = conv?.LastMessageSnippet ?? lastMsg.Body,
                                    UnreadCount = (ActiveConversation?.RemoteJid.ToString() != remoteJidStr) ? inboundCount : 0
                                };
                                Contacts.Add(contact);
                            }
                        }
                    });
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
                    // Initial account catchup when no messages exist locally only needs the latest page
                    break;
                }
            }
        }
        catch
        {
            // Soft failure on account archive catch-up
        }
        finally
        {
            IsAccountSyncing = false;
            SyncStatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    public async Task SelectContactAsync(ContactItemViewModel contact)
    {
        if (!Jid.TryParse(contact.ContactJid, out var jid)) return;

        contact.UnreadCount = 0;
        ApplyPresenceToContact(contact);
        var conv = GetOrCreateConversation(jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
        conv.PresenceShow = contact.PresenceShow;
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
            var title = contact?.DisplayName ?? parsedJid.BareJid.ToString();
            var newConv = GetOrCreateConversation(parsedJid.BareJid.ToString(), title, parsedJid.BareJid, isGroupChat: false);
            await SelectConversationAsync(newConv);
        }
    }

    public void TriggerNotification(string remoteJid, string senderDisplayName, string previewText, bool isEncrypted)
    {
        var isChatActiveAndFocused = IsWindowActive && (ActiveConversation?.Id == remoteJid || (Jid.TryParse(remoteJid, out var rj) && ActiveConversation?.RemoteJid.EqualsBare(rj) == true));

        if (isChatActiveAndFocused)
        {
            // Do not spam OS notifications if the user is actively viewing this conversation in the foreground window
            return;
        }

        if (Settings.NotificationPopupsEnabled)
        {
            var title = isEncrypted ? $"🔒 {senderDisplayName}" : senderDisplayName;
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

        var bare = remoteJid.ToBareString();
        var contact = Contacts.FirstOrDefault(c =>
            c.ContactJid.Equals(bare, StringComparison.OrdinalIgnoreCase) ||
            (Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(remoteJid)));

        if (!isGroupChat && (title == id || title == remoteJid.ToString()))
        {
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
            _settingsRepo,
            ensureConnected: EnsureConnectedAsync)
        {
            EnableMessageMerging = EnableMessageMerging,
            MessageMergeThresholdSeconds = MessageMergeThresholdSeconds,
            EvaluateExpressions = Settings.EvaluateExpressions
        };

        newConv.SlashCommandHandler = async result => await HandleSlashCommandAsync(newConv, result);
        newConv.OpenSettingsToChatRequested = () =>
        {
            Settings.SelectedTabIndex = 1; // Tab 2: "💬 Chat"
            Settings.Open();
        };

        newConv.MessageProcessed += msg =>
        {
            PostToUi(() =>
            {
                var msgContact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(newConv.RemoteJid));
                if (msgContact is not null)
                {
                    msgContact.LastMessagePreview = newConv.LastMessageSnippet;
                }
            });
        };

        newConv.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatConversationViewModel.LastMessageSnippet))
            {
                PostToUi(() =>
                {
                    var snipContact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(newConv.RemoteJid));
                    if (snipContact is not null)
                    {
                        snipContact.LastMessagePreview = newConv.LastMessageSnippet;
                    }
                });
            }
        };
        if (_avatarCache.TryGetValue(bare, out var av))
        {
            newConv.Avatar = av.Bitmap;
            newConv.AvatarHash = av.Hash;
        }

        if (contact is not null)
        {
            newConv.PresenceShow = contact.PresenceShow;
            if (newConv.Avatar is null && contact.Avatar is not null)
            {
                newConv.Avatar = contact.Avatar;
                newConv.AvatarHash = contact.AvatarHash;
            }
        }

        if (_contactResourcePresence.TryGetValue(bare, out var resMap) && !resMap.IsEmpty)
        {
            var best = resMap.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            newConv.PresenceShow = best.Show;
        }

        Conversations.Add(newConv);
        return newConv;
    }

    private async Task HandleReadMarkerReceivedAsync(string stanzaId, Jid? fromJid)
    {
        if (string.IsNullOrEmpty(stanzaId)) return;

        await _messageRepo.MarkMessageAsReadAsync(AccountJid, stanzaId);

        string remoteJidStr = fromJid?.BareJid.ToString() ?? string.Empty;
        string participantJidStr = fromJid?.ToString() ?? remoteJidStr;

        if (!string.IsNullOrEmpty(remoteJidStr))
        {
            await _messageRepo.SaveReadMarkerAsync(AccountJid, remoteJidStr, participantJidStr, stanzaId);
        }

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                if (conv is not null)
                {
                    conv.MarkMessageAsRead(stanzaId);
                    conv.UpdateReadMarker(participantJidStr, stanzaId);
                }
            }
            else
            {
                foreach (var conv in Conversations)
                {
                    if (conv.Messages.Any(m => m.ContainsMessageId(stanzaId)))
                    {
                        conv.MarkMessageAsRead(stanzaId);
                        conv.UpdateReadMarker(conv.RemoteJid.ToString(), stanzaId);
                        break;
                    }
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
        _isManualDisconnect = true;
        _resumeWatcher.Dispose();
        _contactResourcePresence.Clear();
        foreach (var c in Contacts)
        {
            c.PresenceShow = "offline";
            c.StatusMessage = null;
        }

        if (_avatarUpdatedHandler is not null && _avatarManager is not null) _avatarManager.AvatarUpdated -= _avatarUpdatedHandler;
        if (_stateChangedHandler is not null) _client.StateChanged -= _stateChangedHandler;
        if (_messageCorrectedHandler is not null && _correction is not null) _correction.MessageCorrected -= _messageCorrectedHandler;
        if (_messageRetractedHandler is not null && _retraction is not null) _retraction.MessageRetracted -= _messageRetractedHandler;
        if (_chatStateReceivedHandler is not null && _chatStates is not null) _chatStates.ChatStateReceived -= _chatStateReceivedHandler;
        if (_receiptReceivedHandler is not null && _receipts is not null) _receipts.ReceiptReceived -= _receiptReceivedHandler;
        if (_markerReceivedHandler is not null && _chatMarkers is not null) _chatMarkers.MarkerReceived -= _markerReceivedHandler;
        if (_messageReceivedHandler is not null) _client.MessageReceived -= _messageReceivedHandler;
        if (_carbonMessageReceivedHandler is not null && _carbons is not null) _carbons.CarbonMessageReceived -= _carbonMessageReceivedHandler;
        if (_messageDecryptedHandler is not null && _omemo is not null) _omemo.MessageDecrypted -= _messageDecryptedHandler;
        if (_reactionReceivedHandler is not null && _reactions is not null) _reactions.ReactionReceived -= _reactionReceivedHandler;
        if (_presenceReceivedHandler is not null) _client.PresenceReceived -= _presenceReceivedHandler;

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
            !string.Equals(presence.Type, PresenceStanza.TypeUnavailable, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(presence.Type, "available", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Check if this is our own presence being reflected back
        var isOurOwnPresence = false;
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
            var contact = Contacts.FirstOrDefault(c =>
                c.ContactJid.Equals(senderBare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(c.ContactJid, out var cj) && Jid.TryParse(senderBare, out var sbJ) && cj.EqualsBare(sbJ)));
            if (contact is not null)
            {
                contact.PresenceShow = aggregateShow;
                contact.StatusMessage = aggregateStatus;
            }

            var conv = Conversations.FirstOrDefault(c =>
                c.RemoteJid.ToString().Equals(senderBare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(senderBare, out var sbJid) && c.RemoteJid.EqualsBare(sbJid)));
            if (conv is not null)
            {
                conv.PresenceShow = aggregateShow;
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
            var originalId = replaceElem.GetAttr("id");
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
            var targetId = retractElem.GetAttr("id");
            if (!string.IsNullOrEmpty(targetId))
            {
                await HandleMessageRetractionAsync(targetId, msg.From);
                return;
            }
        }

        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(AccountJid);
        var sender = msg.From ?? accountJid;

        var isFromSelf = sender.EqualsBare(accountJid);
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

        var isGroup = msg.Type == MessageStanza.TypeGroupChat;
        var isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

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
                contact.LastMessagePreview = conv.LastMessageSnippet;
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
            var originalId = replaceElem.GetAttr("id");
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
            var targetId = retractElem.GetAttr("id");
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

        var isGroup = msg.Type == MessageStanza.TypeGroupChat;
        var sender = msg.From ?? (isSentByUs ? accountJid : remote);
        var isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

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
                contact.LastMessagePreview = conv.LastMessageSnippet;
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
        var isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remoteJid) == true;

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
                contact.LastMessagePreview = conv.LastMessageSnippet;
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
        var isFromSelf = sender.EqualsBare(accountJid);
        var remote = isFromSelf ? (msg.To ?? accountJid).BareJid : sender.BareJid;
        var newBody = msg.Body ?? string.Empty;

        await _messageRepo.UpdateMessageByReplaceIdAsync(AccountJid, originalId, newBody, msg.Id);

        PostToUi(() =>
        {
            var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(remote));
            conv?.HandleIncomingCorrection(originalId, newBody, msg.ToXmlString(indent: true));

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null && conv is not null)
            {
                contact.LastMessagePreview = conv.LastMessageSnippet;
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

                var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(fromJid));
                if (contact is not null && conv is not null)
                {
                    contact.LastMessagePreview = conv.LastMessageSnippet;
                }
            }
            else
            {
                foreach (var conv in Conversations)
                {
                    conv.HandleIncomingRetraction(targetId);
                    var contact = Contacts.FirstOrDefault(c => Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(conv.RemoteJid));
                    if (contact is not null)
                    {
                        contact.LastMessagePreview = conv.LastMessageSnippet;
                    }
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
        Help.Close();
        About.Close();

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

    [RelayCommand]
    public void OpenProfileSettings()
    {
        Settings.SelectedTabIndex = 4; // Tab: 👤 Profile
        Settings.Open();
    }

    public async Task SetUserAvatarFromBytesAsync(byte[] bytes, string mimeType = "image/png")
    {
        var hash = Helpers.AvatarHelper.ComputeSha1(bytes);
        await _avatarRepo.SaveAvatarAsync(AccountJid, hash, mimeType, bytes);
        var bitmap = Helpers.AvatarHelper.CreateBitmapFromBytes(bytes);

        PostToUi(() =>
        {
            UserAvatar = bitmap;
            UserAvatarHash = hash;
            Settings.UserAvatar = bitmap;
            Settings.UserAvatarHash = hash;
        });

        if (bitmap is not null)
        {
            _avatarCache[AccountJid] = (hash, bitmap);
        }

        if (_avatarManager is not null)
        {
            await _avatarManager.PublishAvatarAsync(bytes, mimeType);
        }
    }

    public async Task RemoveUserAvatarAsync()
    {
        await _avatarRepo.DeleteAvatarAsync(AccountJid);
        _avatarCache.TryRemove(AccountJid, out _);

        PostToUi(() =>
        {
            UserAvatar = null;
            UserAvatarHash = null;
            Settings.UserAvatar = null;
            Settings.UserAvatarHash = null;
        });

        if (_avatarManager is not null)
        {
            await _avatarManager.ClearAvatarAsync();
        }
    }

    internal async Task HandleAvatarUpdatedAsync(Stanza.Protocol.Xeps.Avatars.AvatarChangedEventArgs args)
    {
        var bareJid = args.Jid.ToBareString();
        if (string.IsNullOrEmpty(bareJid)) return;

        if (args.IsCleared)
        {
            await _avatarRepo.DeleteAvatarAsync(bareJid);
            _avatarCache.TryRemove(bareJid, out _);

            PostToUi(() =>
            {
                ApplyAvatarToContactAndConversation(bareJid, null, null);
            });
            return;
        }

        if (string.IsNullOrEmpty(args.Hash)) return;

        // If data is directly provided in event args
        if (args.Data is not null && args.Data.Length > 0)
        {
            var mime = !string.IsNullOrWhiteSpace(args.MimeType) ? args.MimeType : "image/png";
            await _avatarRepo.SaveAvatarAsync(bareJid, args.Hash, mime, args.Data);
            var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(args.Data);
            if (bmp is not null)
            {
                _avatarCache[bareJid] = (args.Hash, bmp);
                PostToUi(() =>
                {
                    ApplyAvatarToContactAndConversation(bareJid, bmp, args.Hash);
                });
                return;
            }
        }

        // Check in-memory cache
        if (_avatarCache.TryGetValue(bareJid, out var cached) &&
            string.Equals(cached.Hash, args.Hash, StringComparison.OrdinalIgnoreCase))
        {
            PostToUi(() =>
            {
                ApplyAvatarToContactAndConversation(bareJid, cached.Bitmap, args.Hash);
            });
            return;
        }

        // Check SQLite by hash
        var existingRecord = await _avatarRepo.GetAvatarByHashAsync(args.Hash);
        if (existingRecord is not null)
        {
            var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(existingRecord.Data);
            if (bmp is not null)
            {
                await _avatarRepo.SaveAvatarAsync(bareJid, args.Hash, existingRecord.MimeType, existingRecord.Data);
                _avatarCache[bareJid] = (args.Hash, bmp);
                PostToUi(() =>
                {
                    ApplyAvatarToContactAndConversation(bareJid, bmp, args.Hash);
                });
                return;
            }
        }

        // Fetch from network
        if (_avatarManager is not null)
        {
            var fetchResult = await _avatarManager.FetchAvatarAsync(args.Jid, args.Hash);
            if (fetchResult is not null)
            {
                await _avatarRepo.SaveAvatarAsync(bareJid, fetchResult.Hash, fetchResult.MimeType, fetchResult.Data);
                var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(fetchResult.Data);
                if (bmp is not null)
                {
                    _avatarCache[bareJid] = (fetchResult.Hash, bmp);
                    PostToUi(() =>
                    {
                        ApplyAvatarToContactAndConversation(bareJid, bmp, fetchResult.Hash);
                    });
                }
            }
        }
    }

    public async Task SyncOwnAvatarFromServerAsync(CancellationToken ct = default)
    {
        if (_avatarManager is null) return;

        try
        {
            var result = await _avatarManager.SyncOwnAvatarAsync(ct).ConfigureAwait(false);
            if (result is not null && result.Data.Length > 0)
            {
                if (!string.Equals(UserAvatarHash, result.Hash, StringComparison.OrdinalIgnoreCase) || UserAvatar is null)
                {
                    await _avatarRepo.SaveAvatarAsync(AccountJid, result.Hash, result.MimeType, result.Data, ct).ConfigureAwait(false);
                    var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(result.Data);
                    if (bmp is not null)
                    {
                        _avatarCache[AccountJid] = (result.Hash, bmp);

                        PostToUi(() =>
                        {
                            ApplyAvatarToContactAndConversation(AccountJid, bmp, result.Hash);
                            Settings.AvatarStatusMessage = "Avatar synced from server!";
                        });

                        try
                        {
                            await SetPresenceAsync(UserPresence).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Soft failure broadcasting presence
                        }
                    }
                }
            }
        }
        catch
        {
            // Soft failure syncing own avatar
        }
    }

    private void ApplyAvatarToContactAndConversation(string bareJid, Avalonia.Media.Imaging.Bitmap? bitmap, string? hash)
    {
        Jid.TryParse(bareJid, out var targetJid);

        if (string.Equals(AccountJid, bareJid, StringComparison.OrdinalIgnoreCase) ||
            (targetJid is not null && Jid.TryParse(AccountJid, out var myJid) && myJid.EqualsBare(targetJid)))
        {
            UserAvatar = bitmap;
            UserAvatarHash = hash;
            Settings.UserAvatar = bitmap;
            Settings.UserAvatarHash = hash;
            _avatarManager?.SetCurrentAvatarHash(hash);
        }

        var contact = Contacts.FirstOrDefault(c => string.Equals(c.ContactJid, bareJid, StringComparison.OrdinalIgnoreCase) ||
            (targetJid is not null && Jid.TryParse(c.ContactJid, out var cj) && cj.EqualsBare(targetJid)));
        if (contact is not null)
        {
            contact.Avatar = bitmap;
            contact.AvatarHash = hash;
        }

        var conv = Conversations.FirstOrDefault(cv => string.Equals(cv.RemoteJid.ToBareString(), bareJid, StringComparison.OrdinalIgnoreCase) ||
            (targetJid is not null && cv.RemoteJid.EqualsBare(targetJid)));
        if (conv is not null)
        {
            conv.Avatar = bitmap;
            conv.AvatarHash = hash;
        }
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
