using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stanza.Gui.ViewModels;

namespace Stanza.Gui.Views;

public partial class LoginView : UserControl
{
    private TextBox? _jidTextBox;
    private TextBox? _passwordTextBox;
    private TextBox? _registerDomainTextBox;
    private TextBox? _registerUsernameTextBox;
    private TextBox? _registerPasswordTextBox;
    private TextBox? _registerConfirmPasswordTextBox;
    private TextBox? _captchaAnswerTextBox;

    public LoginView()
    {
        InitializeComponent();

        _jidTextBox = this.FindControl<TextBox>("JidTextBox");
        _passwordTextBox = this.FindControl<TextBox>("PasswordTextBox");
        _registerDomainTextBox = this.FindControl<TextBox>("RegisterDomainTextBox");
        _registerUsernameTextBox = this.FindControl<TextBox>("RegisterUsernameTextBox");
        _registerPasswordTextBox = this.FindControl<TextBox>("RegisterPasswordTextBox");
        _registerConfirmPasswordTextBox = this.FindControl<TextBox>("RegisterConfirmPasswordTextBox");
        _captchaAnswerTextBox = this.FindControl<TextBox>("CaptchaAnswerTextBox");

        if (_jidTextBox is not null)
        {
            _jidTextBox.AddHandler(InputElement.KeyDownEvent, OnJidKeyDown, RoutingStrategies.Tunnel);
        }

        if (_passwordTextBox is not null)
        {
            _passwordTextBox.AddHandler(InputElement.KeyDownEvent, OnPasswordKeyDown, RoutingStrategies.Tunnel);
        }

        if (_registerDomainTextBox is not null)
        {
            _registerDomainTextBox.AddHandler(InputElement.KeyDownEvent, OnRegisterDomainKeyDown, RoutingStrategies.Tunnel);
        }

        if (_registerUsernameTextBox is not null)
        {
            _registerUsernameTextBox.AddHandler(InputElement.KeyDownEvent, OnRegisterUsernameKeyDown, RoutingStrategies.Tunnel);
        }

        if (_registerPasswordTextBox is not null)
        {
            _registerPasswordTextBox.AddHandler(InputElement.KeyDownEvent, OnRegisterPasswordKeyDown, RoutingStrategies.Tunnel);
        }

        if (_registerConfirmPasswordTextBox is not null)
        {
            _registerConfirmPasswordTextBox.AddHandler(InputElement.KeyDownEvent, OnRegisterSubmitKeyDown, RoutingStrategies.Tunnel);
        }

        if (_captchaAnswerTextBox is not null)
        {
            _captchaAnswerTextBox.AddHandler(InputElement.KeyDownEvent, OnRegisterSubmitKeyDown, RoutingStrategies.Tunnel);
        }
    }

    internal TextBox? JidTextBoxControl => _jidTextBox;
    internal TextBox? PasswordTextBoxControl => _passwordTextBox;
    internal TextBox? RegisterDomainTextBoxControl => _registerDomainTextBox;
    internal TextBox? RegisterUsernameTextBoxControl => _registerUsernameTextBox;
    internal TextBox? RegisterPasswordTextBoxControl => _registerPasswordTextBox;
    internal TextBox? RegisterConfirmPasswordTextBoxControl => _registerConfirmPasswordTextBox;
    internal TextBox? CaptchaAnswerTextBoxControl => _captchaAnswerTextBox;

    internal void OnJidKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            _passwordTextBox?.Focus();
            e.Handled = true;
        }
    }

    internal void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (DataContext is LoginViewModel vm && !vm.IsConnecting)
            {
                if (_passwordTextBox?.Text is not null && vm.Password != _passwordTextBox.Text)
                {
                    vm.Password = _passwordTextBox.Text;
                }
                if (_jidTextBox?.Text is not null && vm.Jid != _jidTextBox.Text)
                {
                    vm.Jid = _jidTextBox.Text;
                }

                if (vm.ConnectCommand.CanExecute(null))
                {
                    vm.ConnectCommand.Execute(null);
                }
            }
            e.Handled = true;
        }
    }

    internal void OnRegisterDomainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            _registerUsernameTextBox?.Focus();
            e.Handled = true;
        }
    }

    internal void OnRegisterUsernameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            _registerPasswordTextBox?.Focus();
            e.Handled = true;
        }
    }

    internal void OnRegisterPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            _registerConfirmPasswordTextBox?.Focus();
            e.Handled = true;
        }
    }

    internal void OnRegisterSubmitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (DataContext is LoginViewModel vm && !vm.IsConnecting)
            {
                if (_registerDomainTextBox?.Text is not null && vm.RegisterDomain != _registerDomainTextBox.Text)
                {
                    vm.RegisterDomain = _registerDomainTextBox.Text;
                }
                if (_registerUsernameTextBox?.Text is not null && vm.RegisterUsername != _registerUsernameTextBox.Text)
                {
                    vm.RegisterUsername = _registerUsernameTextBox.Text;
                }
                if (_registerPasswordTextBox?.Text is not null && vm.RegisterPassword != _registerPasswordTextBox.Text)
                {
                    vm.RegisterPassword = _registerPasswordTextBox.Text;
                }
                if (_registerConfirmPasswordTextBox?.Text is not null && vm.RegisterConfirmPassword != _registerConfirmPasswordTextBox.Text)
                {
                    vm.RegisterConfirmPassword = _registerConfirmPasswordTextBox.Text;
                }
                if (_captchaAnswerTextBox?.Text is not null && vm.CaptchaAnswer != _captchaAnswerTextBox.Text)
                {
                    vm.CaptchaAnswer = _captchaAnswerTextBox.Text;
                }

                if (vm.RegisterCommand.CanExecute(null))
                {
                    vm.RegisterCommand.Execute(null);
                }
            }
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_jidTextBox is not null)
        {
            _jidTextBox.RemoveHandler(InputElement.KeyDownEvent, OnJidKeyDown);
        }

        if (_passwordTextBox is not null)
        {
            _passwordTextBox.RemoveHandler(InputElement.KeyDownEvent, OnPasswordKeyDown);
        }

        if (_registerDomainTextBox is not null)
        {
            _registerDomainTextBox.RemoveHandler(InputElement.KeyDownEvent, OnRegisterDomainKeyDown);
        }

        if (_registerUsernameTextBox is not null)
        {
            _registerUsernameTextBox.RemoveHandler(InputElement.KeyDownEvent, OnRegisterUsernameKeyDown);
        }

        if (_registerPasswordTextBox is not null)
        {
            _registerPasswordTextBox.RemoveHandler(InputElement.KeyDownEvent, OnRegisterPasswordKeyDown);
        }

        if (_registerConfirmPasswordTextBox is not null)
        {
            _registerConfirmPasswordTextBox.RemoveHandler(InputElement.KeyDownEvent, OnRegisterSubmitKeyDown);
        }

        if (_captchaAnswerTextBox is not null)
        {
            _captchaAnswerTextBox.RemoveHandler(InputElement.KeyDownEvent, OnRegisterSubmitKeyDown);
        }
    }
}
