using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly DatabaseContext _dbContext;
    private readonly AccountRepository _accountRepo;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _statusText = "Starting NetChatx...";

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
            StatusText = $"Auto-connecting to {activeAccount.Jid}...";
            var success = await ConnectWithProfileAsync(activeAccount);
            if (success) return;
        }

        SwitchToLogin();
    }

    public void SwitchToLogin()
    {
        CurrentView = new LoginViewModel(ConnectWithProfileAsync);
        StatusText = "Disconnected";
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
                Port = profile.Port
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
                StatusText = $"Connected as {client.BoundJid}";
            });

            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Connection failed: {ex.Message}";
            return false;
        }
    }
}
