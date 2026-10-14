using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Gui.Services;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private DatabaseContext? _dbContext;
    private AccountRepository? _accountRepo;
    private readonly Func<AccountProfile, XmppClient>? _clientFactory;
    private readonly Func<DatabaseContext> _databaseContextFactory;
    private readonly object _storageInitializationLock = new();
    private Task? _storageInitializationTask;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _statusText = Services.LocalizationManager.Instance.GetString("App_Starting");

    public string AppVersion => Helpers.AppVersionHelper.Version;
    public string AppVersionDisplay => Helpers.AppVersionHelper.DisplayString;

    public MainWindowViewModel(
        DatabaseContext? dbContext = null,
        Func<AccountProfile, XmppClient>? clientFactory = null,
        Func<DatabaseContext>? databaseContextFactory = null)
    {
        _dbContext = dbContext;
        _accountRepo = dbContext is null ? null : new AccountRepository(dbContext);
        _clientFactory = clientFactory;
        _databaseContextFactory = databaseContextFactory ?? (() => new DatabaseContext());
    }

    public async Task InitializeAsync()
    {
        try
        {
            await EnsureStorageInitializedAsync();
            var dbContext = _dbContext!;
            var accountRepo = _accountRepo!;
            var accounts = await Task.Run(() => accountRepo.GetAccountsAsync());
            var activeAccounts = accounts.Where(a => a.IsActive).ToList();

            if (activeAccounts.Count == 0)
            {
                SwitchToLogin();
                return;
            }

            var sessionManager = new AccountSessionManager(dbContext, accountRepo)
            {
                ClientFactory = _clientFactory
            };
            await Task.Run(() => sessionManager.InitializeAsync(autoConnect: false));

            var chatVm = new MainChatViewModel(sessionManager, dbContext, onDisconnectRequested: async () =>
            {
                Dispatcher.UIThread.Post(SwitchToLogin);
            });

            // Display the local chat UI before any network connection is attempted so an
            // unavailable server or stalled network cannot leave startup on a blank window.
            CurrentView = chatVm;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await chatVm.InitializeAsync();

            var sessions = sessionManager.Sessions.ToArray();
            StatusText = Services.LocalizationManager.Instance.GetString("App_Connecting", activeAccounts[0].Jid);
            _ = Task.Run(() => ConnectActiveSessionsAndUpdateStatusAsync(sessionManager, sessions));
        }
        catch (Exception ex)
        {
            SwitchToLogin();
            StatusText = Services.LocalizationManager.Instance.GetString("App_ConnectionFailed", ex.Message);
        }
    }

    public void SwitchToLogin()
    {
        CurrentView = new LoginViewModel(ConnectWithProfileAsync);
        StatusText = Services.LocalizationManager.Instance.GetString("App_Disconnected");
    }

    public async Task<bool> ConnectWithProfileAsync(AccountProfile profile)
    {
        try
        {
            if (!Jid.TryParse(profile.Jid, out _))
                return false;

            await EnsureStorageInitializedAsync();
            var dbContext = _dbContext!;
            var accountRepo = _accountRepo!;
            profile.IsActive = true;
            await Task.Run(() => accountRepo.SaveAccountAsync(profile));

            var sessionManager = new AccountSessionManager(dbContext, accountRepo)
            {
                ClientFactory = _clientFactory
            };
            await Task.Run(() => sessionManager.InitializeAsync());

            var chatVm = new MainChatViewModel(sessionManager, dbContext, onDisconnectRequested: async () =>
            {
                Dispatcher.UIThread.Post(SwitchToLogin);
            });

            await chatVm.InitializeAsync();

            Dispatcher.UIThread.Post(() =>
            {
                CurrentView = chatVm;
                StatusText = Services.LocalizationManager.Instance.GetString("App_ConnectedAs", profile.Jid);
            });

            return true;
        }
        catch (Exception ex)
        {
            StatusText = Services.LocalizationManager.Instance.GetString("App_ConnectionFailed", ex.Message);
            return false;
        }
    }

    private async Task ConnectActiveSessionsAndUpdateStatusAsync(
        AccountSessionManager sessionManager,
        IReadOnlyList<AccountSession> sessions)
    {
        string statusText;
        try
        {
            await sessionManager.ConnectSessionsAsync(sessions).ConfigureAwait(false);

            var connectedSession = sessions.FirstOrDefault(session =>
                session.ConnectionState == AccountConnectionState.Connected);
            if (connectedSession is not null)
            {
                statusText = Services.LocalizationManager.Instance.GetString("App_ConnectedAs", connectedSession.AccountJid);
            }
            else
            {
                var failedSession = sessions.FirstOrDefault(session =>
                    session.ConnectionState == AccountConnectionState.Error && !string.IsNullOrWhiteSpace(session.LastErrorMessage));
                statusText = failedSession is null
                    ? Services.LocalizationManager.Instance.GetString("App_Disconnected")
                    : Services.LocalizationManager.Instance.GetString("App_ConnectionFailed", failedSession.LastErrorMessage!);
            }
        }
        catch (Exception ex)
        {
            statusText = Services.LocalizationManager.Instance.GetString("App_ConnectionFailed", ex.Message);
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentView is MainChatViewModel)
            {
                StatusText = statusText;
            }
        });
    }

    private Task EnsureStorageInitializedAsync()
    {
        if (_accountRepo is not null)
        {
            return Task.CompletedTask;
        }

        lock (_storageInitializationLock)
        {
            return _storageInitializationTask ??= InitializeStorageAsync();
        }
    }

    private async Task InitializeStorageAsync()
    {
        var dbContext = await Task.Run(_databaseContextFactory).ConfigureAwait(false);
        _dbContext = dbContext;
        _accountRepo = new AccountRepository(dbContext);
    }
}
