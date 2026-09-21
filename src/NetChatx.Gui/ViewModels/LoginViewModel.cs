using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Core;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly Func<AccountProfile, Task<bool>> _onLoginCallback;

    [ObservableProperty]
    private string _jid = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = "5222";

    [ObservableProperty]
    private bool _useDirectTls;

    [ObservableProperty]
    private bool _showAdvanced;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public LoginViewModel(Func<AccountProfile, Task<bool>> onLoginCallback)
    {
        _onLoginCallback = onLoginCallback;
    }

    [RelayCommand]
    public void ToggleAdvanced()
    {
        ShowAdvanced = !ShowAdvanced;
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Jid))
        {
            ErrorMessage = "Please enter your XMPP JID (e.g. user@example.com).";
            return;
        }

        if (!NetChatx.Core.Jid.TryParse(Jid.Trim(), out var parsedJid))
        {
            ErrorMessage = "Invalid JID format. Example: alice@xmpp.org";
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Please enter your password.";
            return;
        }

        var port = 5222;
        if (!string.IsNullOrWhiteSpace(Port) && !int.TryParse(Port.Trim(), out port))
        {
            ErrorMessage = "Port must be a valid number.";
            return;
        }

        IsConnecting = true;
        StatusMessage = "Connecting to XMPP server...";

        try
        {
            var profile = new AccountProfile
            {
                Jid = parsedJid.BareJid.ToString(),
                Password = Password,
                Host = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim(),
                Port = port,
                UseDirectTls = UseDirectTls,
                IsActive = true
            };

            var success = await _onLoginCallback(profile);
            if (!success && ErrorMessage is null)
            {
                ErrorMessage = "Failed to connect. Please check credentials or network.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsConnecting = false;
            StatusMessage = null;
        }
    }
}
