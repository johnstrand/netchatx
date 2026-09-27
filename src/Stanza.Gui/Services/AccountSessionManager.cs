using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.Services;

public sealed partial class AccountSessionManager : ObservableObject, IAsyncDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly AccountRepository _accountRepo;
    private bool _disposed;

    public ObservableCollection<AccountSession> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllAccountsSelected))]
    private AccountSession? _selectedSession;

    public bool IsAllAccountsSelected => SelectedSession == null;

    [ObservableProperty]
    private int _totalUnreadCount;

    public Func<AccountProfile, XmppClient>? ClientFactory { get; set; }

    public event Action<AccountSession>? SessionAdded;
    public event Action<AccountSession>? SessionRemoved;
    public event Action<AccountSession, AccountConnectionState>? SessionStateChanged;

    public AccountSessionManager(DatabaseContext dbContext, AccountRepository? accountRepo = null)
    {
        _dbContext = dbContext;
        _accountRepo = accountRepo ?? new AccountRepository(_dbContext);
    }

    public async Task InitializeAsync(bool autoConnect = true, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepo.GetAccountsAsync(cancellationToken).ConfigureAwait(false);

        var activeAccounts = accounts.Where(a => a.IsActive).ToList();
        var inactiveAccounts = accounts.Where(a => !a.IsActive).ToList();

        var sessionsToConnect = new List<AccountSession>();

        foreach (var profile in activeAccounts)
        {
            try
            {
                var client = ClientFactory?.Invoke(profile);
                var session = new AccountSession(profile, client);
                sessionsToConnect.Add(session);
            }
            catch (Exception)
            {
                // Profile might have invalid JID, create session with Error state if possible
            }
        }

        foreach (var profile in inactiveAccounts)
        {
            try
            {
                var client = ClientFactory?.Invoke(profile);
                var session = new AccountSession(profile, client)
                {
                    ConnectionState = AccountConnectionState.Disconnected
                };
                AttachSessionEvents(session);
                PostToUi(() => Sessions.Add(session));
            }
            catch { }
        }

        // Connect active accounts concurrently
        var connectTasks = sessionsToConnect.Select(async session =>
        {
            AttachSessionEvents(session);
            PostToUi(() => Sessions.Add(session));

            await session.InitializeXepsAsync().ConfigureAwait(false);
            if (autoConnect)
            {
                await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
        });

        await Task.WhenAll(connectTasks).ConfigureAwait(false);
    }

    public async Task<AccountSession> AddAccountAsync(AccountProfile profile, XmppClient? client = null, bool autoConnect = true, CancellationToken cancellationToken = default)
    {
        await _accountRepo.SaveAccountAsync(profile, cancellationToken).ConfigureAwait(false);

        var actualClient = client ?? ClientFactory?.Invoke(profile);
        var session = new AccountSession(profile, actualClient);
        AttachSessionEvents(session);

        PostToUi(() => Sessions.Add(session));

        if (profile.IsActive)
        {
            await session.InitializeXepsAsync().ConfigureAwait(false);
            if (autoConnect)
            {
                await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        SessionAdded?.Invoke(session);
        return session;
    }

    public async Task RemoveAccountAsync(string jid, CancellationToken cancellationToken = default)
    {
        var session = Sessions.FirstOrDefault(s => s.AccountJid.Equals(jid, StringComparison.OrdinalIgnoreCase));
        if (session is not null)
        {
            if (SelectedSession == session)
            {
                SelectedSession = null;
            }

            PostToUi(() => Sessions.Remove(session));
            await session.DisconnectAsync().ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            SessionRemoved?.Invoke(session);
        }

        await _accountRepo.DeleteAccountAsync(jid, cancellationToken).ConfigureAwait(false);
        UpdateTotalUnreadCount();
    }

    public async Task SetAccountActiveAsync(string jid, bool isActive, CancellationToken cancellationToken = default)
    {
        var session = Sessions.FirstOrDefault(s => s.AccountJid.Equals(jid, StringComparison.OrdinalIgnoreCase));
        if (session is null) return;

        session.Profile.IsActive = isActive;
        await _accountRepo.SaveAccountAsync(session.Profile, cancellationToken).ConfigureAwait(false);

        if (isActive)
        {
            if (session.Ping is null)
            {
                await session.InitializeXepsAsync().ConfigureAwait(false);
            }
            await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await session.DisconnectAsync().ConfigureAwait(false);
        }
    }

    public async Task UpdateAccountProfileAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        await _accountRepo.SaveAccountAsync(profile, cancellationToken).ConfigureAwait(false);

        var session = Sessions.FirstOrDefault(s => s.AccountJid.Equals(profile.Jid, StringComparison.OrdinalIgnoreCase));
        if (session is not null)
        {
            session.Profile = profile;
            session.RefreshProfileDisplay();
        }
    }

    public async Task BroadcastPresenceAsync(string show, string? status, int priority = 0)
    {
        var tasks = Sessions.Select(s => s.SendPresenceAsync(show, status, priority));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public AccountSession? GetSession(string accountJid)
    {
        var targetBare = Jid.TryParse(accountJid, out var jid) ? jid.BareJid.ToString() : accountJid;
        return Sessions.FirstOrDefault(s => s.AccountJid.Equals(targetBare, StringComparison.OrdinalIgnoreCase));
    }

    public void UpdateTotalUnreadCount()
    {
        TotalUnreadCount = Sessions.Sum(s => s.UnreadCount);
    }

    private void AttachSessionEvents(AccountSession session)
    {
        session.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(AccountSession.UnreadCount))
            {
                UpdateTotalUnreadCount();
            }
            else if (args.PropertyName == nameof(AccountSession.ConnectionState))
            {
                SessionStateChanged?.Invoke(session, session.ConnectionState);
            }
        };
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var session in Sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        Sessions.Clear();
    }
}
