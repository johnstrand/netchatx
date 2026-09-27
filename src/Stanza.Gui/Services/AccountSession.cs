using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Gui.Helpers;
using Stanza.Protocol.Xeps.Avatars;
using Stanza.Protocol.Xeps.Common;
using Stanza.Protocol.Xeps.Core;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Protocol.Xeps.Muc;
using Stanza.Protocol.Xeps.Omemo;
using Stanza.Protocol.Xeps.Privacy;
using Stanza.Protocol.Xeps.Registration;
using Stanza.Protocol.Xeps.Sharing;
using Stanza.Storage.Models;

namespace Stanza.Gui.Services;

public sealed partial class AccountSession : ObservableObject, IAsyncDisposable
{
    private bool _disposed;

    public AccountProfile Profile { get; set; }
    public XmppClient Client { get; }

    [ObservableProperty]
    private AccountConnectionState _connectionState = AccountConnectionState.Disconnected;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    [ObservableProperty]
    private string _presenceShow = "available";

    [ObservableProperty]
    private string _statusMessage = "Online with Stanza";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserAvatar))]
    private Bitmap? _userAvatar;

    [ObservableProperty]
    private string? _userAvatarHash;

    public bool HasUserAvatar => UserAvatar != null;

    public string AccountJid => Profile.Jid;

    public string? Label => Profile.Label;

    public string DisplayName => !string.IsNullOrWhiteSpace(Profile.Label) ? Profile.Label : Profile.Jid;

    public string ColorHex => !string.IsNullOrWhiteSpace(Profile.ColorHex) ? Profile.ColorHex : "#00F0FF";

    public IBrush AccountBrush
    {
        get
        {
            if (Color.TryParse(ColorHex, out var color))
            {
                return new SolidColorBrush(color);
            }
            return new SolidColorBrush(Color.Parse("#00F0FF"));
        }
    }

    public string Initials => AvatarHelper.GetInitials(DisplayName);

    public IBrush AvatarBackgroundBrush => AvatarHelper.GetAvatarColorBrush(DisplayName);

    // XEP managers
    public Xep0199Ping? Ping { get; private set; }
    public Xep0313MessageArchiveManagement? Mam { get; private set; }
    public Xep0384OmemoManager? Omemo { get; private set; }
    public Xep0045MultiUserChat? Muc { get; private set; }
    public Xep0363HttpFileUpload? HttpUpload { get; private set; }
    public Xep0280MessageCarbons? Carbons { get; private set; }
    public Xep0444Reactions? Reactions { get; private set; }
    public Xep0184MessageDeliveryReceipts? Receipts { get; private set; }
    public Xep0333ChatMarkers? ChatMarkers { get; private set; }
    public Xep0085ChatStates? ChatStates { get; private set; }
    public Xep0359StanzaIds? StanzaIds { get; private set; }
    public Xep0066OutOfBandData? Oob { get; private set; }
    public Xep0308LastMessageCorrection? Correction { get; private set; }
    public Xep0424MessageRetraction? Retraction { get; private set; }
    public Xep0393MessageStyling? Styling { get; private set; }
    public Xep0191Blocking? Blocking { get; private set; }
    public AvatarManager? AvatarManager { get; private set; }
    public Xep0077InBandRegistration? Registration { get; private set; }

    public ConcurrentDictionary<string, ConcurrentDictionary<string, (string Show, string? Status, int Priority)>> ContactResourcePresence { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AccountSession(AccountProfile profile, XmppClient? client = null)
    {
        Profile = profile;

        if (client is not null)
        {
            Client = client;
        }
        else
        {
            if (!Jid.TryParse(profile.Jid, out var jid))
            {
                throw new ArgumentException($"Invalid JID format: {profile.Jid}", nameof(profile));
            }

            var options = new XmppClientOptions
            {
                Jid = jid,
                Password = profile.Password,
                Host = profile.Host,
                Port = profile.Port,
                UseDirectTls = profile.UseDirectTls,
                AllowUntrustedCertificates = profile.AllowUntrustedCertificates
            };
            Client = new XmppClient(options);
        }

        Client.StateChanged += OnClientStateChanged;
    }

    private void OnClientStateChanged(XmppClientState state)
    {
        ConnectionState = state switch
        {
            XmppClientState.Ready => AccountConnectionState.Connected,
            XmppClientState.Connecting or XmppClientState.Connected => AccountConnectionState.Connecting,
            XmppClientState.Disconnected => AccountConnectionState.Disconnected,
            _ => ConnectionState
        };
    }

    public async Task InitializeXepsAsync()
    {
        Ping = new Xep0199Ping();
        Mam = new Xep0313MessageArchiveManagement();
        Omemo = new Xep0384OmemoManager();
        Muc = new Xep0045MultiUserChat();
        HttpUpload = new Xep0363HttpFileUpload();
        Carbons = new Xep0280MessageCarbons();
        Reactions = new Xep0444Reactions();
        Receipts = new Xep0184MessageDeliveryReceipts();
        ChatMarkers = new Xep0333ChatMarkers();
        ChatStates = new Xep0085ChatStates();
        StanzaIds = new Xep0359StanzaIds();
        Oob = new Xep0066OutOfBandData();
        Correction = new Xep0308LastMessageCorrection();
        Retraction = new Xep0424MessageRetraction();
        Styling = new Xep0393MessageStyling();
        Blocking = new Xep0191Blocking();
        AvatarManager = new AvatarManager();
        Registration = new Xep0077InBandRegistration();

        await Ping.AttachAsync(Client).ConfigureAwait(false);
        await Mam.AttachAsync(Client).ConfigureAwait(false);
        await Omemo.AttachAsync(Client).ConfigureAwait(false);
        await Muc.AttachAsync(Client).ConfigureAwait(false);
        await HttpUpload.AttachAsync(Client).ConfigureAwait(false);
        await Carbons.AttachAsync(Client).ConfigureAwait(false);
        await Reactions.AttachAsync(Client).ConfigureAwait(false);
        await Receipts.AttachAsync(Client).ConfigureAwait(false);
        await ChatMarkers.AttachAsync(Client).ConfigureAwait(false);
        await ChatStates.AttachAsync(Client).ConfigureAwait(false);
        await StanzaIds.AttachAsync(Client).ConfigureAwait(false);
        await Oob.AttachAsync(Client).ConfigureAwait(false);
        await Correction.AttachAsync(Client).ConfigureAwait(false);
        await Retraction.AttachAsync(Client).ConfigureAwait(false);
        await Styling.AttachAsync(Client).ConfigureAwait(false);
        await Blocking.AttachAsync(Client).ConfigureAwait(false);
        await AvatarManager.AttachAsync(Client).ConfigureAwait(false);
        await Registration.AttachAsync(Client).ConfigureAwait(false);
    }

    public async Task ChangePasswordAsync(string newPassword, CancellationToken ct = default)
    {
        if (Registration is null) throw new InvalidOperationException("Registration feature not initialized.");
        await Registration.ChangePasswordAsync(newPassword, ct).ConfigureAwait(false);
        Profile.Password = newPassword;
    }

    public async Task UnregisterAccountAsync(CancellationToken ct = default)
    {
        if (Registration is null) throw new InvalidOperationException("Registration feature not initialized.");
        await Registration.UnregisterAccountAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ConnectionState = AccountConnectionState.Connecting;
            LastErrorMessage = null;
            await Client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            ConnectionState = AccountConnectionState.Connected;
            return true;
        }
        catch (Exception ex)
        {
            ConnectionState = AccountConnectionState.Error;
            LastErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            ConnectionState = AccountConnectionState.Disconnected;
            await Client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }
    }

    public async Task SendPresenceAsync(string show, string? status, int priority = 0)
    {
        PresenceShow = show;
        StatusMessage = status ?? string.Empty;

        if (Client.State != XmppClientState.Ready)
            return;

        var presence = show switch
        {
            "away" => PresenceStanza.Available(show: PresenceStanza.ShowAway, status: status, priority: (sbyte)Math.Clamp(priority, -128, 127)),
            "dnd" => PresenceStanza.Available(show: PresenceStanza.ShowDnd, status: status, priority: (sbyte)Math.Clamp(priority, -128, 127)),
            "xa" => PresenceStanza.Available(show: PresenceStanza.ShowXa, status: status, priority: (sbyte)Math.Clamp(priority, -128, 127)),
            "chat" => PresenceStanza.Available(show: PresenceStanza.ShowChat, status: status, priority: (sbyte)Math.Clamp(priority, -128, 127)),
            _ => PresenceStanza.Available(status: status, priority: (sbyte)Math.Clamp(priority, -128, 127))
        };

        await Client.SendStanzaAsync(presence).ConfigureAwait(false);
    }

    public void RefreshProfileDisplay()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(AccountBrush));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(AvatarBackgroundBrush));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        Client.StateChanged -= OnClientStateChanged;

        try
        {
            await Client.DisposeAsync().ConfigureAwait(false);
        }
        catch { }
    }
}
