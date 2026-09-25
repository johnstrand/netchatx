using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Core;
using Stanza.Core.Client;
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

    private XmppClient? _client;

    public MainWindowViewModel(DatabaseContext? dbContext = null)
    {
        _dbContext = dbContext ?? new DatabaseContext();
        _accountRepo = new AccountRepository(_dbContext);
    }

    public async Task InitializeAsync()
    {
        var accounts = await _accountRepo.GetAccountsAsync();
        var activeAccount = accounts.FirstOrDefault(a => a.IsActive) ?? accounts.FirstOrDefault();

        if (activeAccount is not null)
        {
            StatusText = Services.LocalizationManager.Instance.GetString("App_Connecting", activeAccount.Jid);
            var success = await ConnectWithProfileAsync(activeAccount);
            if (success) return;
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
            if (!Jid.TryParse(profile.Jid, out var jid))
                return false;

            var options = new XmppClientOptions
            {
                Jid = jid,
                Password = profile.Password,
                Host = profile.Host,
                Port = profile.Port,
                UseDirectTls = profile.UseDirectTls,
                AllowUntrustedCertificates = profile.AllowUntrustedCertificates
            };

            var client = new XmppClient(options);
            await client.ConnectAsync();
            _client = client;

            await _accountRepo.SaveAccountAsync(profile);

            var chatVm = new MainChatViewModel(client, _dbContext, onDisconnectRequested: async () =>
            {
                Dispatcher.UIThread.Post(SwitchToLogin);
            });

            await chatVm.InitializeAsync();

            Dispatcher.UIThread.Post(() =>
            {
                CurrentView = chatVm;
                StatusText = Services.LocalizationManager.Instance.GetString("App_ConnectedAs", client.BoundJid);
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
