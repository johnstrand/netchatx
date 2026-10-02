using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Stanza.Core;
using Stanza.Gui.ViewModels;
using Stanza.Gui.Views;
using Stanza.Protocol.Xeps.Registration;
using Stanza.Storage.Models;
using Xunit;

namespace Stanza.Gui.Tests;

public sealed class RegistrationViewModelTests
{
    [Fact]
    public void SwitchToRegister_WithFullJid_SplitsUsernameAndDomain()
    {
        var vm = new LoginViewModel(_ => Task.FromResult(true))
        {
            Jid = "alice@example.com"
        };

        vm.SwitchToRegisterCommand.Execute(null);

        Assert.True(vm.IsRegisterMode);
        Assert.Equal("alice", vm.RegisterUsername);
        Assert.Equal("example.com", vm.RegisterDomain);
    }

    [Fact]
    public void SwitchToLogin_PrefillsJidAndPassword()
    {
        var vm = new LoginViewModel(_ => Task.FromResult(true))
        {
            IsRegisterMode = true,
            RegisterUsername = "bob",
            RegisterDomain = "chat.org",
            RegisterPassword = "mypassword"
        };

        vm.SwitchToLoginCommand.Execute(null);

        Assert.False(vm.IsRegisterMode);
        Assert.Equal("bob@chat.org", vm.Jid);
        Assert.Equal("mypassword", vm.Password);
    }

    [Fact]
    public async Task Register_EmptyDomain_SetsErrorMessage()
    {
        var vm = new LoginViewModel(_ => Task.FromResult(true))
        {
            IsRegisterMode = true,
            RegisterDomain = "",
            RegisterUsername = "user",
            RegisterPassword = "pass",
            RegisterConfirmPassword = "pass"
        };

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Register_EmptyUsername_SetsErrorMessage()
    {
        var vm = new LoginViewModel(_ => Task.FromResult(true))
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "",
            RegisterPassword = "pass",
            RegisterConfirmPassword = "pass"
        };

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Register_PasswordMismatch_SetsErrorMessage()
    {
        var vm = new LoginViewModel(_ => Task.FromResult(true))
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "user",
            RegisterPassword = "pass1",
            RegisterConfirmPassword = "pass2"
        };

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("match", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_SuccessfulRegistration_InvokesLoginCallback()
    {
        AccountProfile? loggedInProfile = null;
        var vm = new LoginViewModel(
            profile =>
            {
                loggedInProfile = profile;
                return Task.FromResult(true);
            },
            (domain, sub, host, port, directTls, untrusted, ct) =>
            {
                return Task.FromResult(new RegistrationResult { IsSuccess = true });
            })
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "newbie",
            RegisterPassword = "secretpassword",
            RegisterConfirmPassword = "secretpassword",
            RegisterEmail = "newbie@example.com"
        };

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(loggedInProfile);
        Assert.Equal("newbie@example.com", loggedInProfile.Jid);
        Assert.Equal("secretpassword", loggedInProfile.Password);
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task Register_ReturnsCaptchaChallenge_PromptsUserForCaptcha()
    {
        var challenge = new CaptchaChallenge
        {
            ChallengeId = "chal-1",
            QuestionText = "What is 4 + 4?",
            AnswerFieldVar = "answers"
        };

        var callCount = 0;
        var vm = new LoginViewModel(
            profile => Task.FromResult(true),
            (domain, sub, host, port, directTls, untrusted, ct) =>
            {
                callCount++;
                if (string.IsNullOrEmpty(sub.CaptchaAnswer))
                {
                    return Task.FromResult(new RegistrationResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "CAPTCHA required",
                        CaptchaChallenge = challenge
                    });
                }
                if (sub.CaptchaAnswer == "8")
                {
                    return Task.FromResult(new RegistrationResult { IsSuccess = true });
                }
                return Task.FromResult(new RegistrationResult { IsSuccess = false, ErrorMessage = "Incorrect CAPTCHA" });
            })
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "captcha_user",
            RegisterPassword = "pass",
            RegisterConfirmPassword = "pass"
        };

        // First attempt: Server returns CAPTCHA challenge
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.True(vm.IsCaptchaRequired);
        Assert.Equal("What is 4 + 4?", vm.CaptchaQuestion);
        Assert.Equal(1, callCount);

        // Attempt without answer: Validation error
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);

        // Answer correctly and submit
        vm.CaptchaAnswer = "8";
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Register_ServerConflictError_SetsFriendlyErrorMessage()
    {
        var vm = new LoginViewModel(
            _ => Task.FromResult(true),
            (domain, sub, host, port, directTls, untrusted, ct) =>
            {
                return Task.FromResult(new RegistrationResult
                {
                    IsSuccess = false,
                    ErrorCondition = "conflict",
                    ErrorMessage = "The desired username is already taken on this server."
                });
            })
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "existing_user",
            RegisterPassword = "pass",
            RegisterConfirmPassword = "pass"
        };

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("already taken", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void LoginView_RegisterKeyboardHandlers_NavigationAndExecution()
    {
        var view = new LoginView();
        var window = new Window { Content = view };
        window.Show();

        var registrationExecuted = false;
        var vm = new LoginViewModel(
            _ => Task.FromResult(true),
            (domain, sub, host, port, directTls, untrusted, ct) =>
            {
                registrationExecuted = true;
                return Task.FromResult(new RegistrationResult { IsSuccess = true });
            })
        {
            IsRegisterMode = true,
            RegisterDomain = "example.com",
            RegisterUsername = "keyboard_user",
            RegisterPassword = "password",
            RegisterConfirmPassword = "password"
        };
        view.DataContext = vm;

        // Enter on Domain
        var domainArgs = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        view.OnRegisterDomainKeyDown(null, domainArgs);
        Assert.True(domainArgs.Handled);

        // Enter on Username
        var userArgs = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        view.OnRegisterUsernameKeyDown(null, userArgs);
        Assert.True(userArgs.Handled);

        // Enter on Password
        var passArgs = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        view.OnRegisterPasswordKeyDown(null, passArgs);
        Assert.True(passArgs.Handled);

        // Enter on Confirm Password triggers register
        var submitArgs = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        view.OnRegisterSubmitKeyDown(null, submitArgs);
        Assert.True(submitArgs.Handled);
        Assert.True(registrationExecuted);
    }
}
