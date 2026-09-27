using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Core;
using Stanza.Gui.Services;
using Stanza.Protocol.Xeps.Registration;
using Stanza.Storage.Models;

namespace Stanza.Gui.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    public delegate Task<RegistrationResult> RegistrationHandler(
        string domain,
        RegistrationSubmission submission,
        string? host,
        int port,
        bool useDirectTls,
        bool allowUntrustedCertificates,
        CancellationToken cancellationToken);

    private readonly Func<AccountProfile, Task<bool>> _onLoginCallback;
    private readonly RegistrationHandler? _registrationHandler;

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

    // --- Registration properties ---

    [ObservableProperty]
    private bool _isRegisterMode;

    [ObservableProperty]
    private string _registerDomain = string.Empty;

    [ObservableProperty]
    private string _registerUsername = string.Empty;

    [ObservableProperty]
    private string _registerPassword = string.Empty;

    [ObservableProperty]
    private string _registerConfirmPassword = string.Empty;

    [ObservableProperty]
    private string _registerEmail = string.Empty;

    [ObservableProperty]
    private bool _isCaptchaRequired;

    [ObservableProperty]
    private string? _captchaInstructions;

    [ObservableProperty]
    private string? _captchaQuestion;

    [ObservableProperty]
    private string _captchaAnswer = string.Empty;

    [ObservableProperty]
    private Bitmap? _captchaImage;

    private CaptchaChallenge? _currentCaptchaChallenge;

    public LoginViewModel(Func<AccountProfile, Task<bool>> onLoginCallback)
        : this(onLoginCallback, null)
    {
    }

    public LoginViewModel(
        Func<AccountProfile, Task<bool>> onLoginCallback,
        RegistrationHandler? registrationHandler)
    {
        _onLoginCallback = onLoginCallback;
        _registrationHandler = registrationHandler;
    }

    [RelayCommand]
    public void ToggleAdvanced()
    {
        ShowAdvanced = !ShowAdvanced;
    }

    [RelayCommand]
    public void SwitchToRegister()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!string.IsNullOrWhiteSpace(Jid))
        {
            var parts = Jid.Trim().Split('@', 2);
            if (parts.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(RegisterUsername))
                    RegisterUsername = parts[0];
                if (string.IsNullOrWhiteSpace(RegisterDomain))
                    RegisterDomain = parts[1];
            }
            else if (string.IsNullOrWhiteSpace(RegisterDomain))
            {
                RegisterDomain = Jid.Trim();
            }
        }

        IsRegisterMode = true;
    }

    [RelayCommand]
    public void SwitchToLogin()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!string.IsNullOrWhiteSpace(RegisterUsername) && !string.IsNullOrWhiteSpace(RegisterDomain))
        {
            Jid = $"{RegisterUsername.Trim()}@{RegisterDomain.Trim()}";
        }
        if (!string.IsNullOrEmpty(RegisterPassword))
        {
            Password = RegisterPassword;
        }

        IsRegisterMode = false;
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

    [RelayCommand]
    public async Task RegisterAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(RegisterDomain))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Register_Error_EnterDomain");
            return;
        }

        if (string.IsNullOrWhiteSpace(RegisterUsername))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Register_Error_EnterUsername");
            return;
        }

        if (string.IsNullOrEmpty(RegisterPassword))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Register_Error_EnterPassword");
            return;
        }

        if (RegisterPassword != RegisterConfirmPassword)
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Register_Error_PasswordsMismatch");
            return;
        }

        if (IsCaptchaRequired && string.IsNullOrWhiteSpace(CaptchaAnswer))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Register_Error_EnterCaptcha");
            return;
        }

        var port = 5222;
        if (!string.IsNullOrWhiteSpace(Port) && !int.TryParse(Port.Trim(), out port))
        {
            ErrorMessage = LocalizationManager.Instance.GetString("Login_Error_InvalidPort");
            return;
        }

        IsConnecting = true;
        StatusMessage = LocalizationManager.Instance.GetString("Register_Status_Submitting");

        try
        {
            var domain = RegisterDomain.Trim();
            var host = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim();

            var submission = new RegistrationSubmission
            {
                Username = RegisterUsername.Trim(),
                Password = RegisterPassword,
                Email = string.IsNullOrWhiteSpace(RegisterEmail) ? null : RegisterEmail.Trim(),
                CaptchaAnswer = IsCaptchaRequired ? CaptchaAnswer.Trim() : null,
                CaptchaChallenge = _currentCaptchaChallenge
            };

            var result = _registrationHandler != null
                ? await _registrationHandler(domain, submission, host, port, UseDirectTls, AllowUntrustedCertificates, CancellationToken.None)
                : await Xep0077InBandRegistration.RegisterAccountAsync(domain, submission, host, port, UseDirectTls, AllowUntrustedCertificates, ct: CancellationToken.None);

            if (result.RequiresCaptcha)
            {
                IsCaptchaRequired = true;
                _currentCaptchaChallenge = result.CaptchaChallenge;
                CaptchaInstructions = result.CaptchaChallenge?.Instructions;
                CaptchaQuestion = result.CaptchaChallenge?.QuestionText ?? result.ErrorMessage;

                if (result.CaptchaChallenge?.ImageData is not null)
                {
                    try
                    {
                        using var ms = new MemoryStream(result.CaptchaChallenge.ImageData);
                        CaptchaImage = new Bitmap(ms);
                    }
                    catch
                    {
                        // Ignore invalid image
                    }
                }

                ErrorMessage = result.ErrorMessage;
                return;
            }

            if (!result.IsSuccess)
            {
                ErrorMessage = result.ErrorMessage ?? LocalizationManager.Instance.GetString("Login_Error_Failed");
                return;
            }

            // Success! Connect with newly registered credentials
            StatusMessage = LocalizationManager.Instance.GetString("Register_Success");
            Jid = $"{RegisterUsername.Trim()}@{domain}";
            Password = RegisterPassword;

            var profile = new AccountProfile
            {
                Jid = Jid,
                Password = Password,
                Host = host,
                Port = port,
                UseDirectTls = UseDirectTls,
                AllowUntrustedCertificates = AllowUntrustedCertificates,
                IsActive = true
            };

            var success = await _onLoginCallback(profile);
            if (!success && ErrorMessage is null)
            {
                // If direct login fails, switch to login view with pre-filled credentials
                IsRegisterMode = false;
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
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                StatusMessage = null;
            }
        }
    }
}
