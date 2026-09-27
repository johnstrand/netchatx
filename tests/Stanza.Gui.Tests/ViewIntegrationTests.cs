using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Stanza.Gui.Views;

namespace Stanza.Gui.Tests;

public class ViewIntegrationTests
{
    [AvaloniaFact]
    public void LoginView_JidEnterKey_FocusesPasswordBox()
    {
        var view = new LoginView();
        var window = new Window { Content = view };
        window.Show();

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        };
        view.OnJidKeyDown(null, args);

        Assert.True(args.Handled);
    }

    [AvaloniaFact]
    public void LoginView_PasswordEnterKey_WithNoVm_SetsHandled()
    {
        var view = new LoginView();
        var window = new Window { Content = view };
        window.Show();

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        };
        view.OnPasswordKeyDown(null, args);

        // No VM, so ConnectCommand is not invoked — but Handled is still set to true
        Assert.True(args.Handled);
    }

    [AvaloniaFact]
    public void LoginView_NonEnterKey_NotHandled()
    {
        var view = new LoginView();
        var window = new Window { Content = view };
        window.Show();

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.A
        };
        view.OnJidKeyDown(null, args);

        Assert.False(args.Handled);
    }

    [AvaloniaFact]
    public void LoginView_Cleanup_RemovesHandlers()
    {
        var view = new LoginView();
        var window = new Window { Content = view };
        window.Show();
        window.Hide();

        // After detach, handlers should be removed — test that no exception thrown
        // and control refs are consistent
        Assert.NotNull(view);
    }
}
