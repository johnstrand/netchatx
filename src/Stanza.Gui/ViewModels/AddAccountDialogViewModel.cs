using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Core;
using Stanza.Gui.Services;
using Stanza.Storage.Models;

namespace Stanza.Gui.ViewModels;

public sealed partial class AddAccountDialogViewModel : ViewModelBase
{
    private readonly Func<AccountProfile, Task<(bool Success, string? ErrorMessage)>> _onAddAccountCallback;
    private readonly Action _onCloseCallback;

    public static readonly IReadOnlyList<string> PresetColors =
    [
        "#00F0FF", // Cyan
        "#3B82F6", // Blue
        "#8B5CF6", // Violet
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#F43F5E"  // Rose
    ];

    [ObservableProperty]
    private string _jid = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _colorHex = "#00F0FF";

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

    public AddAccountDialogViewModel(
        Func<AccountProfile, Task<(bool Success, string? ErrorMessage)>> onAddAccountCallback,
        Action onCloseCallback)
    {
        _onAddAccountCallback = onAddAccountCallback;
        _onCloseCallback = onCloseCallback;
    }

    [RelayCommand]
    public void SelectColor(string color)
    {
        ColorHex = color;
    }

    [RelayCommand]
    public void ToggleAdvanced()
    {
        ShowAdvanced = !ShowAdvanced;
    }

    [RelayCommand]
    public void Close()
    {
        _onCloseCallback();
    }

    [RelayCommand]
    public async Task AddAccountAsync()
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

        try
        {
            var profile = new AccountProfile
            {
                Jid = parsedJid.BareJid.ToString(),
                Password = Password,
                Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
                ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? "#00F0FF" : ColorHex.Trim(),
                Host = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim(),
                Port = port,
                UseDirectTls = UseDirectTls,
                AllowUntrustedCertificates = AllowUntrustedCertificates,
                IsActive = true
            };

            var (success, error) = await _onAddAccountCallback(profile);
            if (success)
            {
                _onCloseCallback();
            }
            else
            {
                ErrorMessage = error ?? LocalizationManager.Instance.GetString("Login_Error_Failed");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
