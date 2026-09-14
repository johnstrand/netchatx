using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NetChatx.Gui.ViewModels;

namespace NetChatx.Gui.Views;

public partial class LoginView : UserControl
{
    private TextBox? _jidTextBox;
    private TextBox? _passwordTextBox;

    public LoginView()
    {
        InitializeComponent();

        _jidTextBox = this.FindControl<TextBox>("JidTextBox");
        _passwordTextBox = this.FindControl<TextBox>("PasswordTextBox");

        if (_jidTextBox is not null)
        {
            _jidTextBox.AddHandler(InputElement.KeyDownEvent, OnJidKeyDown, RoutingStrategies.Tunnel);
        }

        if (_passwordTextBox is not null)
        {
            _passwordTextBox.AddHandler(InputElement.KeyDownEvent, OnPasswordKeyDown, RoutingStrategies.Tunnel);
        }
    }

    internal TextBox? JidTextBoxControl => _jidTextBox;
    internal TextBox? PasswordTextBoxControl => _passwordTextBox;

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
    }
}
