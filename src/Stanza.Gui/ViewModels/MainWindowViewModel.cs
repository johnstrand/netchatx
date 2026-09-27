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
    private readonly DatabaseContext _dbContext;
    private readonly AccountRepository _accountRepo;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _statusText = Services.LocalizationManager.Instance.GetString("App_Starting");

    public string AppVersion => Helpers.AppVersionHelper.Version;
    public string AppVersionDisplay => Helpers.AppVersionHelper.DisplayString;

    public MainWindowViewModel(DatabaseContext? dbContext = null)
    {
        _dbContext = dbContext ?? new DatabaseContext();
        _accountRepo = new AccountRepository(_dbContext);
    }

    public async Task InitializeAsync()
    {
        var accounts = await _accountRepo.GetAccountsAsync();
        var activeAccounts = accounts.Where(a => a.IsActive).ToList();

        if (activeAccounts.Count > 0)
        {
            StatusText = Services.LocalizationManager.Instance.GetString("App_Connecting", activeAccounts[0].Jid);
            var sessionManager = new AccountSessionManager(_dbContext, _accountRepo);
            await sessionManager.InitializeAsync();

            var chatVm = new MainChatViewModel(sessionManager, _dbContext, onDisconnectRequested: async () =>
            {
                Dispatcher.UIThread.Post(SwitchToLogin);
            });

            await chatVm.InitializeAsync();

            Dispatcher.UIThread.Post(() =>
            {
                CurrentView = chatVm;
                StatusText = Services.LocalizationManager.Instance.GetString("App_ConnectedAs", activeAccounts[0].Jid);
            });
            return;
        }

        SwitchToLogin();
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

            profile.IsActive = true;
            await _accountRepo.SaveAccountAsync(profile);

            var sessionManager = new AccountSessionManager(_dbContext, _accountRepo);
            await sessionManager.InitializeAsync();

            var chatVm = new MainChatViewModel(sessionManager, _dbContext, onDisconnectRequested: async () =>
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
}
