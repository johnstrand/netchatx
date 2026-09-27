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
    private CancellationTokenSource? _reconnectCts;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly SemaphoreSlim _resumeLock = new(1, 1);
    private Task<bool>? _currentReconnectTask;

    private Action<Stanza.Protocol.Xeps.Avatars.AvatarChangedEventArgs>? _avatarUpdatedHandler;

    internal const int MaxReconnectAttempts = 10;
    internal const double InitialReconnectDelaySeconds = 2.0;
    internal const double MaxReconnectDelaySeconds = 60.0;
    internal const double JitterRatio = 0.20;

    internal Func<TimeSpan, CancellationToken, Task>? DelayProvider { get; set; }
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
        var session = _sessionManager?.GetSession(contact.AccountJid);
        var resDict = session?.ContactResourcePresence ?? _contactResourcePresence;
        if (resDict.TryGetValue(bare, out var resources) && !resources.IsEmpty)
        {
            var best = resources.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            contact.PresenceShow = best.Show;
            contact.StatusMessage = best.Status;

            var conv = _allConversations.FirstOrDefault(c =>
                c.AccountJid.Equals(contact.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                (c.RemoteJid.ToString().Equals(bare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(contact.ContactJid, out var cJid) && c.RemoteJid.EqualsBare(cJid))));
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
    private bool _isReconnecting;

    [ObservableProperty]
    private bool _canManualReconnect;

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
        NewChatSelectedSession = SelectedAccountSession ?? _sessionManager.Sessions.FirstOrDefault();
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

        var targetSession = SelectedAccountSession ?? NewChatSelectedSession ?? _sessionManager.Sessions.FirstOrDefault();
        var targetAccountJid = targetSession?.AccountJid ?? AccountJid;
        var targetClient = targetSession?.Client ?? _client;

        var bareJid = parsedJid.BareJid;
        var displayName = !string.IsNullOrWhiteSpace(NewChatDisplayName)
            ? NewChatDisplayName.Trim()
            : (!string.IsNullOrEmpty(bareJid.LocalPart) ? bareJid.LocalPart : bareJid.ToString());

        var existingContact = _allContacts.FirstOrDefault(c =>
            c.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) &&
            c.ContactJid.Equals(bareJid.ToString(), StringComparison.OrdinalIgnoreCase));

        if (existingContact is null)
        {
            var newContact = new ContactItemViewModel
            {
                AccountJid = targetAccountJid,
                ContactJid = bareJid.ToString(),
                Name = displayName,
                Subscription = "none",
                AccountLabel = targetSession?.DisplayName ?? targetAccountJid,
                AccountColorHex = targetSession?.ColorHex ?? "#00F0FF",
                ShowAccountBadge = IsAllAccountsSelected
            };
            ApplyPresenceToContact(newContact);
            _allContacts.Add(newContact);
            if (IsAllAccountsSelected || SelectedAccountSession?.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) == true)
            {
                PostToUi(() => Contacts.Add(newContact));
            }

            await _rosterRepo.UpsertContactsAsync([new RosterContact
            {
                AccountJid = targetAccountJid,
                ContactJid = bareJid.ToString(),
                Name = displayName,
                Subscription = "none"
            }]);

            if (targetClient.IsReady || targetClient.State == XmppClientState.Connected)
            {
                try
                {
                    var presence = new PresenceStanza
                    {
                        To = bareJid,
                        Type = "subscribe"
                    };
                    await targetClient.SendStanzaAsync(presence);
                }
                catch
                {
                    // Soft failure sending subscribe presence
                }
            }
        }

        var conv = GetOrCreateConversation(targetAccountJid, bareJid.ToString(), displayName, bareJid, isGroupChat: false);
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

    private readonly AccountSessionManager _sessionManager;
    public AccountSessionManager SessionManager => _sessionManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllAccountsSelected))]
    private AccountSession? _selectedAccountSession;

    public bool IsAllAccountsSelected => SelectedAccountSession == null;

    [ObservableProperty]
    private AccountSession? _newChatSelectedSession;

    [ObservableProperty]
    private bool _isAddAccountDialogOpen;

    [ObservableProperty]
    private AddAccountDialogViewModel? _addAccountDialog;

    private readonly List<ContactItemViewModel> _allContacts = [];
    private readonly List<ChatConversationViewModel> _allConversations = [];

    public MainChatViewModel(
        AccountSessionManager sessionManager,
        DatabaseContext dbContext,
        Func<Task> onDisconnectRequested,
        INotificationService? notificationService = null,
        ISystemResumeWatcher? resumeWatcher = null)
    {
        _sessionManager = sessionManager;
        var firstSession = sessionManager.Sessions.FirstOrDefault();
        _client = firstSession?.Client ?? new XmppClient(new XmppClientOptions { Jid = Jid.Parse("dummy@localhost"), Password = "" });
        _dbContext = dbContext;
        _onDisconnectRequested = onDisconnectRequested;
        _notificationService = notificationService ?? (Stanza.Gui.Services.NotificationService.EnableNativeNotifications ? new Stanza.Gui.Services.NotificationService() : new Stanza.Gui.Services.NotificationService(dispatchNative: false));
        _notificationService.WindowActiveChanged += active =>
        {
            OnPropertyChanged(nameof(IsWindowActive));
        };
        _resumeWatcher = resumeWatcher ?? new SystemResumeWatcher();
        _resumeWatcher.Resumed += async () => await HandleSystemResumeAsync();

        _sessionManager.SessionAdded += async s =>
        {
            WireSession(s);
            await LoadRosterForSessionAsync(s);
            ApplyAccountFilter();
        };
        _sessionManager.SessionRemoved += s =>
        {
            RemoveSessionData(s);
        };

        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);
        _settingsRepo = new SettingsRepository(_dbContext);
        _avatarRepo = new AvatarRepository(_dbContext);

        _accountJid = firstSession?.AccountJid ?? _client.Options.Jid.BareJid.ToString();
        _userBoundJid = firstSession?.Client.BoundJid.ToString() ?? _client.BoundJid.ToString();

        _selectedAccountSession = sessionManager.SelectedSession;
        _newChatSelectedSession = firstSession;

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
            },
            sessionManager: _sessionManager,
            accountRepo: _accountRepo,
            onOpenAddAccountRequested: OpenAddAccountDialog,
            messageRepo: _messageRepo);
    }

    public MainChatViewModel(
        XmppClient client,
        DatabaseContext dbContext,
        Func<Task> onDisconnectRequested,
        INotificationService? notificationService = null,
        ISystemResumeWatcher? resumeWatcher = null)
        : this(CreateSessionManagerForSingleClient(client, dbContext), dbContext, onDisconnectRequested, notificationService, resumeWatcher)
    {
    }

    private static AccountSessionManager CreateSessionManagerForSingleClient(XmppClient client, DatabaseContext dbContext)
    {
        var profile = new AccountProfile
        {
            Jid = client.Options.Jid.BareJid.ToString(),
            Password = client.Options.Password,
            Host = client.Options.Host,
            Port = client.Options.Port,
            UseDirectTls = client.Options.UseDirectTls,
            AllowUntrustedCertificates = client.Options.AllowUntrustedCertificates,
            IsActive = true
        };
        var session = new AccountSession(profile, client);
        var mgr = new AccountSessionManager(dbContext);
        mgr.Sessions.Add(session);
        mgr.SelectedSession = session;
        return mgr;
    }

    partial void OnSelectedAccountSessionChanged(AccountSession? value)
    {
        if (_sessionManager is not null)
        {
            _sessionManager.SelectedSession = value;
        }
        ApplyAccountFilter();
    }

    [RelayCommand]
    public void SelectAllAccounts()
    {
        SelectedAccountSession = null;
    }

    [RelayCommand]
    public void SelectAccountSession(AccountSession? session)
    {
        SelectedAccountSession = session;
    }

    [RelayCommand]
    public void OpenAddAccountDialog()
    {
        Help.Close();
        About.Close();
        AddAccountDialog = new AddAccountDialogViewModel(OnAddAccountFromDialogAsync, CloseAddAccountDialog);
        IsAddAccountDialogOpen = true;
    }

    [RelayCommand]
    public void CloseAddAccountDialog()
    {
        IsAddAccountDialogOpen = false;
        AddAccountDialog = null;
    }

    private async Task<(bool Success, string? ErrorMessage)> OnAddAccountFromDialogAsync(AccountProfile profile)
    {
        try
        {
            var session = await _sessionManager.AddAccountAsync(profile);
            WireSession(session);
            await LoadRosterForSessionAsync(session);
            ApplyAccountFilter();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void ApplyAccountFilter()
    {
        var isAll = IsAllAccountsSelected;
        var selectedJid = SelectedAccountSession?.AccountJid;

        Contacts.Clear();
        foreach (var contact in _allContacts)
        {
            if (isAll || contact.AccountJid.Equals(selectedJid, StringComparison.OrdinalIgnoreCase))
            {
                contact.ShowAccountBadge = isAll;
                Contacts.Add(contact);
            }
        }

        Conversations.Clear();
        foreach (var conv in _allConversations)
        {
            if (isAll || conv.AccountJid.Equals(selectedJid, StringComparison.OrdinalIgnoreCase))
            {
                conv.ShowAccountBadge = isAll;
                Conversations.Add(conv);
            }
        }

        if (!isAll && ActiveConversation is not null && !ActiveConversation.AccountJid.Equals(selectedJid, StringComparison.OrdinalIgnoreCase))
        {
            ActiveConversation = null;
        }
    }

    private void RemoveSessionData(AccountSession session)
    {
        _allContacts.RemoveAll(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase));
        _allConversations.RemoveAll(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase));
        ApplyAccountFilter();
    }

    public async Task LoadRosterForSessionAsync(AccountSession session)
    {
        var cachedContacts = await _rosterRepo.GetContactsAsync(session.AccountJid);
        foreach (var c in cachedContacts)
        {
            var item = ContactItemViewModel.FromRosterContact(c);
            item.AccountJid = session.AccountJid;
            item.AccountLabel = session.Profile.Label;
            item.AccountColorHex = session.ColorHex;
            item.ShowAccountBadge = IsAllAccountsSelected;
            ApplyPresenceToContact(item);
            if (_avatarCache.TryGetValue(item.ContactJid, out var av))
            {
                item.Avatar = av.Bitmap;
                item.AvatarHash = av.Hash;
            }
            if (!_allContacts.Any(x => x.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) && x.ContactJid.Equals(item.ContactJid, StringComparison.OrdinalIgnoreCase)))
            {
                _allContacts.Add(item);
            }
        }

        await RefreshRosterFromServerAsync(session);

        try
        {
            var summaries = await _messageRepo.GetContactSummariesAsync(session.AccountJid);
            foreach (var kvp in summaries)
            {
                var existing = _allContacts.FirstOrDefault(x =>
                    x.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                    x.ContactJid.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var newContact = new ContactItemViewModel
                    {
                        AccountJid = session.AccountJid,
                        AccountLabel = session.Profile.Label,
                        AccountColorHex = session.ColorHex,
                        ShowAccountBadge = IsAllAccountsSelected,
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
                    _allContacts.Add(newContact);
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

            foreach (var contact in _allContacts.Where(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (Jid.TryParse(contact.ContactJid, out var jid))
                {
                    var conv = GetOrCreateConversation(session.AccountJid, jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
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

        session.UnreadCount = _allContacts.Where(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase)).Sum(c => c.UnreadCount);
    }

    private void WireSession(AccountSession session)
    {
        session.Client.StateChanged += state =>
        {
            if (state == XmppClientState.Disconnected && !_isManualDisconnect)
            {
                PostToUi(() =>
                {
                    if (SelectedAccountSession == session || (IsAllAccountsSelected && session == _sessionManager.Sessions.FirstOrDefault()))
                    {
                        StatusMessage = $"{session.DisplayName}: Connection lost ⏳";
                    }
                });
            }
            else if (state == XmppClientState.Ready)
            {
                PostToUi(() =>
                {
                    if (SelectedAccountSession == session || (IsAllAccountsSelected && session == _sessionManager.Sessions.FirstOrDefault()))
                    {
                        StatusMessage = $"Connected as {session.Client.BoundJid}";
                    }
                });
            }
        };

        session.Client.MessageReceived += async msg =>
        {
            await HandleIncomingMessageAsync(session, msg);
        };

        session.Client.PresenceReceived += async pres =>
        {
            await HandleIncomingPresenceAsync(session, pres);
        };

        if (session.Carbons is not null)
        {
            session.Carbons.CarbonMessageReceived += async (msg, isSentByUs) =>
            {
                await HandleCarbonMessageAsync(session, msg, isSentByUs);
            };
        }

        if (session.Omemo is not null)
        {
            session.Omemo.MessageDecrypted += async dec =>
            {
                await HandleDecryptedMessageAsync(session, dec);
            };
        }

        if (session.Reactions is not null)
        {
            session.Reactions.ReactionReceived += async args =>
            {
                await HandleIncomingReactionAsync(session, args);
            };
        }

        if (session.Correction is not null)
        {
            session.Correction.MessageCorrected += async (msg, originalId) =>
            {
                await HandleMessageCorrectionAsync(session, msg, originalId);
            };
        }

        if (session.Retraction is not null)
        {
            session.Retraction.MessageRetracted += async (targetId, fromJid) =>
            {
                await HandleMessageRetractionAsync(session, targetId, fromJid);
            };
        }

        if (session.ChatStates is not null)
        {
            session.ChatStates.ChatStateReceived += (fromJid, chatState) =>
            {
                PostToUi(() =>
                {
                    var conv = _allConversations.FirstOrDefault(c =>
                        c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                        c.RemoteJid.EqualsBare(fromJid));
                    conv?.HandleRemoteChatState(chatState);
                });
            };
        }

        if (session.ChatMarkers is not null)
        {
            session.ChatMarkers.MarkerReceived += async (stanzaId, fromJid, markerType) =>
            {
                if (markerType is ChatMarkerType.Displayed or ChatMarkerType.Acknowledged)
                {
                    await HandleReadMarkerReceivedAsync(session, stanzaId, fromJid);
                }
            };
        }

        if (session.AvatarManager is not null)
        {
            session.AvatarManager.AvatarUpdated += async args =>
            {
                await HandleAvatarUpdatedAsync(session, args);
            };
        }
    }

    public async Task InitializeAsync()
    {
        // Setup default XEP managers
        var firstSession = _sessionManager?.Sessions.FirstOrDefault();
        if (firstSession is not null)
        {
            if (firstSession.Ping is null)
            {
                await firstSession.InitializeXepsAsync();
            }
            _ping = firstSession.Ping;
            _mam = firstSession.Mam;
            _omemo = firstSession.Omemo;
            _muc = firstSession.Muc;
            _httpUpload = firstSession.HttpUpload;
            _carbons = firstSession.Carbons;
            _reactions = firstSession.Reactions;
            _receipts = firstSession.Receipts;
            _chatMarkers = firstSession.ChatMarkers;
            _chatStates = firstSession.ChatStates;
            _stanzaIds = firstSession.StanzaIds;
            _oob = firstSession.Oob;
            _correction = firstSession.Correction;
            _retraction = firstSession.Retraction;
            _styling = firstSession.Styling;
            _avatarManager = firstSession.AvatarManager;
        }
        else
        {
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
        }

        // Load user avatar and cached avatars from SQLite
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
        catch { }

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
        catch { }

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
        catch { }
        UserPresence = initialPresence;

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
            OnPropertyChanged(nameof(MessageInputWatermark));
        }
        catch { }
        finally
        {
            _isInitializingSettings = false;
        }

        if (_sessionManager is not null)
        {
            foreach (var session in _sessionManager.Sessions)
            {
                if (session.Ping is null)
                {
                    await session.InitializeXepsAsync();
                }
                WireSession(session);
                await LoadRosterForSessionAsync(session);

                if (session.Client.IsReady && session.Carbons is not null)
                {
                    try { await session.Carbons.EnableAsync(); } catch { }
                }
            }

            ApplyAccountFilter();
            _sessionManager.UpdateTotalUnreadCount();
        }

        try
        {
            await SetPresenceAsync(initialPresence);
        }
        catch { }

        if (_sessionManager is not null)
        {
            foreach (var session in _sessionManager.Sessions)
            {
                if (session.ConnectionState == AccountConnectionState.Connected)
                {
                    _ = CatchUpAccountArchiveAsync(session);
                }
            }
        }

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
        Task<bool> task;
        await _reconnectLock.WaitAsync();
        try
        {
            if (_client.IsReady) return true;
            if (_currentReconnectTask is not null && !_currentReconnectTask.IsCompleted)
            {
                task = _currentReconnectTask;
            }
            else
            {
                task = _currentReconnectTask = PerformReconnectAsync();
            }
        }
        finally
        {
            _reconnectLock.Release();
        }

        return await task;
    }

    [RelayCommand]
    public async Task ManualReconnectAsync()
    {
        _isManualDisconnect = false;
        CanManualReconnect = false;
        await ReconnectAsync();
    }

    [RelayCommand]
    public Task RetryConnectionAsync() => ManualReconnectAsync();

    internal static TimeSpan CalculateBackoffDelay(
        int attempt,
        double initialDelaySeconds = InitialReconnectDelaySeconds,
        double maxDelaySeconds = MaxReconnectDelaySeconds,
        Random? random = null)
    {
        if (attempt < 1) attempt = 1;
        if (initialDelaySeconds <= 0) initialDelaySeconds = 1.0;
        if (maxDelaySeconds < initialDelaySeconds) maxDelaySeconds = initialDelaySeconds;

        double baseDelay = Math.Min(maxDelaySeconds, initialDelaySeconds * Math.Pow(2, attempt - 1));
        double jitterMultiplier = (1.0 - JitterRatio) + (random ?? Random.Shared).NextDouble() * (2.0 * JitterRatio);
        double delaySeconds = Math.Min(maxDelaySeconds, Math.Max(1.0, baseDelay * jitterMultiplier));
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (DelayProvider is not null)
        {
            return DelayProvider(duration, cancellationToken);
        }

        return Task.Delay(duration, cancellationToken);
    }

    private async Task<bool> PerformReconnectAsync()
    {
        if (_isManualDisconnect)
        {
            return false;
        }

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();
        var cancellationToken = _reconnectCts.Token;

        PostToUi(() =>
        {
            IsReconnecting = true;
            CanManualReconnect = false;
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

        if (_isManualDisconnect || cancellationToken.IsCancellationRequested)
        {
            PostToUi(() =>
            {
                IsReconnecting = false;
                CanManualReconnect = false;
            });
            return false;
        }

        int maxAttempts = MaxReconnectAttempts;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (_isManualDisconnect || cancellationToken.IsCancellationRequested)
            {
                PostToUi(() =>
                {
                    IsReconnecting = false;
                    CanManualReconnect = false;
                });
                return false;
            }

            bool connected = false;
            try
            {
                PostToUi(() =>
                {
                    StatusMessage = attempt == 1
                        ? "Reconnecting to server... ⏳"
                        : $"Reconnecting to server (attempt {attempt}/{maxAttempts})... ⏳";
                });

                await _client.ConnectAsync(cancellationToken);

                if (_client.IsReady)
                {
                    connected = true;
                    PostToUi(() =>
                    {
                        IsReconnecting = false;
                        CanManualReconnect = false;
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
            catch (OperationCanceledException) when (_isManualDisconnect || cancellationToken.IsCancellationRequested)
            {
                PostToUi(() =>
                {
                    IsReconnecting = false;
                    CanManualReconnect = false;
                });
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reconnect attempt {attempt} failed: {ex.Message}");
            }

            if (!connected)
            {
                try
                {
                    if (_client.State != XmppClientState.Disconnected)
                    {
                        await _client.DisconnectAsync();
                    }
                }
                catch { }
            }

            if (!connected && attempt < maxAttempts)
            {
                var delay = CalculateBackoffDelay(attempt);
                int totalSeconds = (int)Math.Max(1, Math.Round(delay.TotalSeconds));

                for (int remaining = totalSeconds; remaining > 0; remaining--)
                {
                    if (_isManualDisconnect || cancellationToken.IsCancellationRequested)
                    {
                        PostToUi(() =>
                        {
                            IsReconnecting = false;
                            CanManualReconnect = false;
                        });
                        return false;
                    }

                    PostToUi(() =>
                    {
                        StatusMessage = $"Reconnecting to server (attempt {attempt + 1}/{maxAttempts} in {remaining}s)... ⏳";
                    });

                    try
                    {
                        await DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    catch (OperationCanceledException) when (_isManualDisconnect || cancellationToken.IsCancellationRequested)
                    {
                        PostToUi(() =>
                        {
                            IsReconnecting = false;
                            CanManualReconnect = false;
                        });
                        return false;
                    }
                }
            }
        }

        if (_isManualDisconnect || cancellationToken.IsCancellationRequested)
        {
            PostToUi(() =>
            {
                IsReconnecting = false;
                CanManualReconnect = false;
            });
            return false;
        }

        PostToUi(() =>
        {
            IsReconnecting = false;
            CanManualReconnect = true;
            StatusMessage = "Connection failed (Offline). Reconnect manually";
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

    private async Task RefreshRosterFromServerAsync(AccountSession? session = null)
    {
        var targetSession = session ?? SelectedAccountSession ?? _sessionManager?.Sessions.FirstOrDefault();
        var client = targetSession?.Client ?? _client;
        var accountJid = targetSession?.AccountJid ?? AccountJid;

        try
        {
            if (client.IsReady || client.State == XmppClientState.Connected)
            {
                var rosterIq = IqStanza.CreateGet();
                rosterIq.RawElement.Child(new XmppElement("query", "jabber:iq:roster"));
                var result = await client.SendIqAsync(rosterIq);
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
                                AccountJid = accountJid,
                                ContactJid = cJid,
                                Name = name,
                                Subscription = sub
                            });

                            var existing = _allContacts.FirstOrDefault(x =>
                                x.AccountJid.Equals(accountJid, StringComparison.OrdinalIgnoreCase) &&
                                x.ContactJid.Equals(cJid, StringComparison.OrdinalIgnoreCase));
                            if (existing is null)
                            {
                                var newItem = new ContactItemViewModel
                                {
                                    AccountJid = accountJid,
                                    AccountLabel = targetSession?.Profile.Label,
                                    AccountColorHex = targetSession?.ColorHex,
                                    ShowAccountBadge = IsAllAccountsSelected,
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
                                _allContacts.Add(newItem);
                                if (IsAllAccountsSelected || SelectedAccountSession?.AccountJid.Equals(accountJid, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    PostToUi(() => Contacts.Add(newItem));
                                }
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

    public async Task CatchUpAccountArchiveAsync(AccountSession? session = null)
    {
        var client = session?.Client ?? _client;
        var mam = session?.Mam ?? _mam;
        var accJid = session?.AccountJid ?? AccountJid;

        if (mam is null || IsAccountSyncing || !client.IsReady) return;

        IsAccountSyncing = true;
        SyncStatusMessage = "Syncing messages... ⏳";

        try
        {
            var parsedAccountJid = Jid.TryParse(accJid, out var parsedAcc) ? parsedAcc : null;
            var latestTimestamp = await _messageRepo.GetLatestMessageTimestampAsync(accJid);
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
                            mamResult = await mam.QueryArchiveAsync(withJid: null, maxResults: 50, start: startTimestamp.Value, after: afterId);
                        }
                        else
                        {
                            mamResult = await mam.QueryArchiveAsync(withJid: null, maxResults: 50, before: beforeId);
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
                                   || (m.From?.EqualsBare(client.BoundJid) == true);

                    if (Xep0444Reactions.TryExtractReaction(m.RawElement, isCarbonSent: isFromSelf, out var reactArgs))
                    {
                        if (session is not null)
                        {
                            await HandleIncomingReactionAsync(session, reactArgs);
                        }
                        else
                        {
                            await HandleIncomingReactionAsync(reactArgs);
                        }
                        continue;
                    }

                    var defaultRemote = m.Type == MessageStanza.TypeGroupChat
                        ? (m.From ?? m.To)
                        : (isFromSelf ? m.To : m.From);
                    if (defaultRemote is null) continue;

                    var chatMsg = ChatConversationViewModel.ParseMamMessage(
                        item,
                        accJid,
                        defaultRemote.BareJid,
                        m.Type == MessageStanza.TypeGroupChat,
                        client);

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
                                conv = GetOrCreateConversation(accJid, parsedJid.BareJid.ToString(), contact?.DisplayName ?? remoteJidStr, parsedJid.BareJid, isGroupChat: false);
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
                                    AccountJid = accJid,
                                    AccountLabel = session?.Label,
                                    AccountColorHex = session?.ColorHex,
                                    ShowAccountBadge = IsAllAccountsSelected,
                                    ContactJid = remoteJidStr,
                                    Name = remoteJidStr,
                                    Subscription = "none",
                                    LastMessagePreview = conv?.LastMessageSnippet ?? lastMsg.Body,
                                    UnreadCount = (ActiveConversation?.RemoteJid.ToString() != remoteJidStr) ? inboundCount : 0
                                };
                                PostToUi(() => Contacts.Add(contact));
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
        var conv = GetOrCreateConversation(contact.AccountJid, jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
        conv.PresenceShow = contact.PresenceShow;
        ActiveConversation = conv;
        await conv.EnsureHistoryLoadedAsync();

        // Load OMEMO devices for contact
        OmemoDetails.ContactJid = contact.ContactJid;
        var devices = await _omemoRepo.GetSessionsAsync(contact.AccountJid, contact.ContactJid);
        var devVms = devices.Select(d => new OmemoDeviceItemViewModel(
            _omemoRepo,
            contact.AccountJid,
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

        var contact = _allContacts.FirstOrDefault(c =>
            c.AccountJid.Equals(conv.AccountJid, StringComparison.OrdinalIgnoreCase) &&
            c.ContactJid.Equals(conv.RemoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
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
                PostToUi(() => Conversations.Remove(conv));
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

    public ChatConversationViewModel GetOrCreateConversation(string accountJid, string id, string title, Jid remoteJid, bool isGroupChat)
    {
        var session = _sessionManager?.GetSession(accountJid);
        var targetAccountJid = session?.AccountJid ?? accountJid ?? AccountJid;

        var existing = _allConversations.FirstOrDefault(c =>
            c.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) &&
            c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if ((IsAllAccountsSelected || SelectedAccountSession?.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) == true) &&
                !Conversations.Contains(existing))
            {
                PostToUi(() => Conversations.Add(existing));
            }
            return existing;
        }

        var bare = remoteJid.ToBareString();
        var contact = _allContacts.FirstOrDefault(c =>
            c.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) &&
            (c.ContactJid.Equals(bare, StringComparison.OrdinalIgnoreCase) ||
            (Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(remoteJid))));

        if (!isGroupChat && (title == id || title == remoteJid.ToString()))
        {
            if (contact is not null && !string.IsNullOrWhiteSpace(contact.DisplayName))
            {
                title = contact.DisplayName;
            }
        }

        var clientToUse = session?.Client ?? _client;
        var mamToUse = session?.Mam ?? _mam;
        var omemoToUse = session?.Omemo ?? _omemo;
        var httpUploadToUse = session?.HttpUpload ?? _httpUpload;
        var reactionsToUse = session?.Reactions ?? _reactions;
        var chatMarkersToUse = session?.ChatMarkers ?? _chatMarkers;
        var chatStatesToUse = session?.ChatStates ?? _chatStates;

        var newConv = new ChatConversationViewModel(
            targetAccountJid,
            id,
            title,
            remoteJid,
            isGroupChat,
            _messageRepo,
            clientToUse,
            mamToUse,
            omemoToUse,
            httpUploadToUse,
            reactionsToUse,
            chatMarkersToUse,
            chatStatesToUse,
            _settingsRepo,
            ensureConnected: () => session is not null ? session.ConnectAsync() : EnsureConnectedAsync())
        {
            AccountLabel = session?.Profile.Label,
            AccountColorHex = session?.ColorHex,
            ShowAccountBadge = IsAllAccountsSelected,
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
                var msgContact = _allContacts.FirstOrDefault(c =>
                    c.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) &&
                    Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(newConv.RemoteJid));
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
                    var snipContact = _allContacts.FirstOrDefault(c =>
                        c.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) &&
                        Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(newConv.RemoteJid));
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

        var resDict = session?.ContactResourcePresence ?? _contactResourcePresence;
        if (resDict.TryGetValue(bare, out var resMap) && !resMap.IsEmpty)
        {
            var best = resMap.Values.OrderByDescending(r => r.Priority).ThenByDescending(r => ShowScore(r.Show)).First();
            newConv.PresenceShow = best.Show;
        }

        _allConversations.Add(newConv);
        if (IsAllAccountsSelected || SelectedAccountSession?.AccountJid.Equals(targetAccountJid, StringComparison.OrdinalIgnoreCase) == true)
        {
            PostToUi(() => Conversations.Add(newConv));
        }

        return newConv;
    }

    public ChatConversationViewModel GetOrCreateConversation(string id, string title, Jid remoteJid, bool isGroupChat)
        => GetOrCreateConversation(SelectedAccountSession?.AccountJid ?? AccountJid, id, title, remoteJid, isGroupChat);

    private async Task HandleReadMarkerReceivedAsync(AccountSession session, string stanzaId, Jid? fromJid)
    {
        if (string.IsNullOrEmpty(stanzaId)) return;

        await _messageRepo.MarkMessageAsReadAsync(session.AccountJid, stanzaId);

        string remoteJidStr = fromJid?.BareJid.ToString() ?? string.Empty;
        string participantJidStr = fromJid?.ToString() ?? remoteJidStr;

        if (!string.IsNullOrEmpty(remoteJidStr))
        {
            await _messageRepo.SaveReadMarkerAsync(session.AccountJid, remoteJidStr, participantJidStr, stanzaId);
        }

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = _allConversations.FirstOrDefault(c =>
                    c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                    c.RemoteJid.EqualsBare(fromJid));
                if (conv is not null)
                {
                    conv.MarkMessageAsRead(stanzaId);
                    conv.UpdateReadMarker(participantJidStr, stanzaId);
                }
            }
            else
            {
                foreach (var conv in _allConversations.Where(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase)))
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

    private Task HandleReadMarkerReceivedAsync(string stanzaId, Jid? fromJid)
        => HandleReadMarkerReceivedAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), stanzaId, fromJid);

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
            if (_sessionManager is not null && _sessionManager.Sessions.Count > 0)
            {
                await _sessionManager.BroadcastPresenceAsync(show, StatusMessage);
            }
            else
            {
                await _client.SendStanzaAsync(stanza);
            }
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
        _reconnectCts?.Cancel();
        PostToUi(() =>
        {
            IsReconnecting = false;
            CanManualReconnect = false;
        });
        _resumeWatcher.Dispose();
        _contactResourcePresence.Clear();
        foreach (var c in Contacts)
        {
            c.PresenceShow = "offline";
            c.StatusMessage = null;
        }

        if (_avatarUpdatedHandler is not null && _avatarManager is not null) _avatarManager.AvatarUpdated -= _avatarUpdatedHandler;

        if (_sessionManager is not null)
        {
            foreach (var s in _sessionManager.Sessions)
            {
                await s.DisconnectAsync();
            }
        }
        else
        {
            await _client.DisconnectAsync();
        }

        await _onDisconnectRequested();
    }

    internal Task HandleIncomingPresenceAsync(AccountSession session, PresenceStanza presence)
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

        var isOurOwnPresence = false;
        if (session.Client.BoundJid is not null)
        {
            isOurOwnPresence = fromJid is not null && (fromJid.Equals(session.Client.BoundJid) ||
                (string.IsNullOrEmpty(fromJid.Resource) && senderBare.Equals(session.Client.BoundJid.ToBareString(), StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            isOurOwnPresence = senderBare.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase);
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
                session.PresenceShow = selfShow;
                if (presence.Status is not null)
                {
                    session.StatusMessage = presence.Status;
                }
                if (SelectedAccountSession == session || IsAllAccountsSelected)
                {
                    UserPresence = selfShow;
                    if (presence.Status is not null)
                    {
                        StatusMessage = presence.Status;
                    }
                }
            });
            return Task.CompletedTask;
        }

        var resource = fromJid?.Resource ?? string.Empty;
        var resourceMap = session.ContactResourcePresence.GetOrAdd(senderBare, _ => new(StringComparer.OrdinalIgnoreCase));

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
            var contact = _allContacts.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                (c.ContactJid.Equals(senderBare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(c.ContactJid, out var cj) && Jid.TryParse(senderBare, out var sbJ) && cj.EqualsBare(sbJ))));
            if (contact is not null)
            {
                contact.PresenceShow = aggregateShow;
                contact.StatusMessage = aggregateStatus;
            }

            var conv = _allConversations.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                (c.RemoteJid.ToString().Equals(senderBare, StringComparison.OrdinalIgnoreCase) ||
                (Jid.TryParse(senderBare, out var sbJid) && c.RemoteJid.EqualsBare(sbJid))));
            if (conv is not null)
            {
                conv.PresenceShow = aggregateShow;
            }
        });

        return Task.CompletedTask;
    }

    internal Task HandleIncomingPresenceAsync(PresenceStanza presence)
        => HandleIncomingPresenceAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), presence);

    private async Task HandleIncomingReactionAsync(AccountSession session, ReactionEventArgs args)
    {
        PostToUi(async () =>
        {
            var conv = GetOrCreateConversation(session.AccountJid, args.RemoteJid.ToString(), args.RemoteJid.ToString(), args.RemoteJid, isGroupChat: args.IsGroupChat);
            await conv.HandleIncomingReactionAsync(args);
        });
    }

    private Task HandleIncomingReactionAsync(ReactionEventArgs args)
        => HandleIncomingReactionAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), args);

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

    private async Task HandleIncomingMessageAsync(AccountSession session, MessageStanza msg)
    {
        var replaceElem = msg.RawElement.Element("replace", "urn:xmpp:message-correct:0");
        if (replaceElem is not null)
        {
            var originalId = replaceElem.GetAttr("id");
            if (!string.IsNullOrEmpty(originalId))
            {
                await HandleMessageCorrectionAsync(session, msg, originalId);
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
                await HandleMessageRetractionAsync(session, targetId, msg.From);
                return;
            }
        }

        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(session.AccountJid);
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
        var isActiveConv = ActiveConversation?.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) == true &&
                           ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = session.AccountJid,
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

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && session.ChatMarkers is not null)
        {
            try
            {
                await session.ChatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(session.AccountJid, remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = _allContacts.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = conv.LastMessageSnippet;
                if ((ActiveConversation != conv) && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }

            if (direction == MessageDirection.Inbound)
            {
                if (ActiveConversation != conv)
                {
                    session.UnreadCount++;
                    _sessionManager.UpdateTotalUnreadCount();
                }

                var senderDisplayName = contact?.DisplayName ?? (sender.IsBare ? sender.LocalPart : sender.Resource) ?? sender.ToString();
                TriggerNotification(remote.ToString(), senderDisplayName, msg.Body, isEncrypted: false);
            }
        });
    }

    private Task HandleIncomingMessageAsync(MessageStanza msg)
        => HandleIncomingMessageAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), msg);

    private async Task HandleCarbonMessageAsync(AccountSession session, MessageStanza msg, bool isSentByUs)
    {
        var replaceElem = msg.RawElement.Element("replace", "urn:xmpp:message-correct:0");
        if (replaceElem is not null)
        {
            var originalId = replaceElem.GetAttr("id");
            if (!string.IsNullOrEmpty(originalId))
            {
                await HandleMessageCorrectionAsync(session, msg, originalId);
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
                await HandleMessageRetractionAsync(session, targetId, msg.From);
                return;
            }
        }

        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(session.AccountJid);
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
        var isActiveConv = ActiveConversation?.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) == true &&
                           ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = session.AccountJid,
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

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && session.ChatMarkers is not null)
        {
            try
            {
                await session.ChatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(session.AccountJid, remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = _allContacts.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = conv.LastMessageSnippet;
                if ((ActiveConversation != conv) && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }

            if (direction == MessageDirection.Inbound)
            {
                if (ActiveConversation != conv)
                {
                    session.UnreadCount++;
                    _sessionManager.UpdateTotalUnreadCount();
                }

                var senderDisplayName = contact?.DisplayName ?? (sender.IsBare ? sender.LocalPart : sender.Resource) ?? sender.ToString();
                TriggerNotification(remote.ToString(), senderDisplayName, msg.Body, isEncrypted: false);
            }
        });
    }

    private Task HandleCarbonMessageAsync(MessageStanza msg, bool isSentByUs)
        => HandleCarbonMessageAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), msg, isSentByUs);

    private async Task HandleDecryptedMessageAsync(AccountSession session, DecryptedOmemoMessage dec)
    {
        var remoteJid = dec.SenderJid.BareJid;
        var isActiveConv = ActiveConversation?.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) == true &&
                           ActiveConversation?.RemoteJid.EqualsBare(remoteJid) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = session.AccountJid,
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
            var conv = GetOrCreateConversation(session.AccountJid, remoteJid.ToString(), remoteJid.ToString(), remoteJid, isGroupChat: false);
            conv.IsEncrypted = true;
            conv.ReceiveMessage(chatMsg);

            var contact = _allContacts.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                c.ContactJid.Equals(remoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = conv.LastMessageSnippet;
                if (ActiveConversation != conv)
                {
                    contact.UnreadCount++;
                }
            }

            if (ActiveConversation != conv)
            {
                session.UnreadCount++;
                _sessionManager.UpdateTotalUnreadCount();
            }

            var senderDisplayName = contact?.DisplayName ?? dec.SenderJid.LocalPart ?? dec.SenderJid.ToString();
            TriggerNotification(remoteJid.ToString(), senderDisplayName, dec.PlaintextBody, isEncrypted: true);
        });
    }

    private Task HandleDecryptedMessageAsync(DecryptedOmemoMessage dec)
        => HandleDecryptedMessageAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), dec);

    private async Task HandleMessageCorrectionAsync(AccountSession session, MessageStanza msg, string originalId)
    {
        var accountJid = Jid.Parse(session.AccountJid);
        var sender = msg.From ?? accountJid;
        var isFromSelf = sender.EqualsBare(accountJid);
        var remote = isFromSelf ? (msg.To ?? accountJid).BareJid : sender.BareJid;
        var newBody = msg.Body ?? string.Empty;

        await _messageRepo.UpdateMessageByReplaceIdAsync(session.AccountJid, originalId, newBody, msg.Id);

        PostToUi(() =>
        {
            var conv = _allConversations.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                c.RemoteJid.EqualsBare(remote));
            conv?.HandleIncomingCorrection(originalId, newBody, msg.ToXmlString(indent: true));

            var contact = _allContacts.FirstOrDefault(c =>
                c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null && conv is not null)
            {
                contact.LastMessagePreview = conv.LastMessageSnippet;
            }
        });
    }

    private Task HandleMessageCorrectionAsync(MessageStanza msg, string originalId)
        => HandleMessageCorrectionAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), msg, originalId);

    private async Task HandleMessageRetractionAsync(AccountSession session, string targetId, Jid? fromJid)
    {
        await _messageRepo.DeleteMessageAsync(session.AccountJid, targetId);

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = _allConversations.FirstOrDefault(c =>
                    c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                    c.RemoteJid.EqualsBare(fromJid));
                conv?.HandleIncomingRetraction(targetId);

                var contact = _allContacts.FirstOrDefault(c =>
                    c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                    Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(fromJid));
                if (contact is not null && conv is not null)
                {
                    contact.LastMessagePreview = conv.LastMessageSnippet;
                }
            }
            else
            {
                foreach (var conv in _allConversations.Where(c => c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase)))
                {
                    conv.HandleIncomingRetraction(targetId);
                    var contact = _allContacts.FirstOrDefault(c =>
                        c.AccountJid.Equals(session.AccountJid, StringComparison.OrdinalIgnoreCase) &&
                        Jid.TryParse(c.ContactJid, out var cJid) && cJid.EqualsBare(conv.RemoteJid));
                    if (contact is not null)
                    {
                        contact.LastMessagePreview = conv.LastMessageSnippet;
                    }
                }
            }
        });
    }

    private Task HandleMessageRetractionAsync(string targetId, Jid? fromJid)
        => HandleMessageRetractionAsync(_sessionManager.GetSession(AccountJid) ?? _sessionManager.Sessions.First(), targetId, fromJid);

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

    internal Task HandleAvatarUpdatedAsync(AccountSession session, Stanza.Protocol.Xeps.Avatars.AvatarChangedEventArgs args) => HandleAvatarUpdatedAsync(args);
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

        foreach (var contact in _allContacts.Concat(Contacts).Distinct().Where(c => string.Equals(c.ContactJid, bareJid, StringComparison.OrdinalIgnoreCase) ||
            (targetJid is not null && Jid.TryParse(c.ContactJid, out var cj) && cj.EqualsBare(targetJid))))
        {
            contact.Avatar = bitmap;
            contact.AvatarHash = hash;
        }

        foreach (var conv in _allConversations.Concat(Conversations).Distinct().Where(cv => string.Equals(cv.RemoteJid.ToBareString(), bareJid, StringComparison.OrdinalIgnoreCase) ||
            (targetJid is not null && cv.RemoteJid.EqualsBare(targetJid))))
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
