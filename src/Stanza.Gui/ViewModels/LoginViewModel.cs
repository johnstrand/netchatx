using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Core;
using Stanza.Gui.Services;
using Stanza.Storage.Models;

namespace Stanza.Gui.ViewModels;

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
    private bool _allowUntrustedCertificates;

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
            ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_EnterJid");
            return;
        }

        if (!Stanza.Core.Jid.TryParse(Jid.Trim(), out var parsedJid))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_InvalidJid");
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_EnterPassword");
            return;
        }

        var port = 5222;
        if (!string.IsNullOrWhiteSpace(Port) && !int.TryParse(Port.Trim(), out port))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_InvalidPort");
            return;
        }

        IsConnecting = true;
        StatusMessage = LocalizationManager.Instance.GetString("Login_Status_Connecting");

        try
        {
            var profile = new AccountProfile
            {
                Jid = parsedJid.BareJid.ToString(),
                Password = Password,
                Host = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim(),
                Port = port,
                UseDirectTls = UseDirectTls,
                AllowUntrustedCertificates = AllowUntrustedCertificates,
                IsActive = true
            };

            var success = await _onLoginCallback(profile);
            if (!success && ErrorMessage is null)
            {
                ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_Failed");
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
