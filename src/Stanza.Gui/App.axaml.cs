using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Stanza.Gui.Services;
using Stanza.Gui.ViewModels;
using Stanza.Gui.Views;

namespace Stanza.Gui;

public partial class App : Application
{
    private readonly IStartupService _startupService = new StartupService();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationManager.Instance.ApplyToApplication(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
            _ = mainVm.InitializeAsync();
        }

        UpdateTrayAutostartState();
        base.OnFrameworkInitializationCompleted();
    }

    public void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow win)
        {
            win.Show();
            if (win.WindowState == WindowState.Minimized)
            {
                win.WindowState = WindowState.Normal;
            }
            win.Activate();
        }
    }

    public void UpdateTrayAutostartState()
    {
        try
        {
            var icons = TrayIcon.GetIcons(this);
            if (icons is { Count: > 0 } && icons[0].Menu is NativeMenu menu)
            {
                var isEnabled = _startupService.IsStartupEnabled();
                foreach (var item in menu.Items)
                {
                    if (item is NativeMenuItem menuItem &&
                        (menuItem.Header == "Start with Windows" || menuItem.Header == "Start on System Boot"))
                    {
                        if (!OperatingSystem.IsWindows() && menuItem.Header == "Start with Windows")
                        {
                            menuItem.Header = "Start on System Boot";
                        }
                        menuItem.IsChecked = isEnabled;
                        break;
                    }
                }
            }
        }
        catch
        {
            // Ignore in environments where tray is unavailable
        }
    }

    private MainChatViewModel? GetMainChatViewModel()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is MainWindowViewModel mainVm &&
            mainVm.CurrentView is MainChatViewModel chatVm)
        {
            return chatVm;
        }
        return null;
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        UpdateTrayAutostartState();
        ShowMainWindow();
    }

    private void OnTrayOpenClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnTraySettingsClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
        var chatVm = GetMainChatViewModel();
        chatVm?.Settings.Open();
    }

    private void OnTrayAutostartClick(object? sender, EventArgs e)
    {
        var newState = !_startupService.IsStartupEnabled();
        _startupService.SetStartupEnabled(newState);

        if (sender is NativeMenuItem item)
        {
            item.IsChecked = newState;
        }

        var chatVm = GetMainChatViewModel();
        if (chatVm?.Settings is not null)
        {
            chatVm.Settings.LaunchOnStartup = newState;
        }
    }

    private async void OnTrayStatusOnlineClick(object? sender, EventArgs e)
    {
        var chatVm = GetMainChatViewModel();
        if (chatVm is not null)
        {
            await chatVm.SetPresenceAsync("available");
        }
    }

    private async void OnTrayStatusAwayClick(object? sender, EventArgs e)
    {
        var chatVm = GetMainChatViewModel();
        if (chatVm is not null)
        {
            await chatVm.SetPresenceAsync("away");
        }
    }

    private async void OnTrayStatusBusyClick(object? sender, EventArgs e)
    {
        var chatVm = GetMainChatViewModel();
        if (chatVm is not null)
        {
            await chatVm.SetPresenceAsync("dnd");
        }
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is MainWindow win)
            {
                win.ForceClose();
            }
            else
            {
                desktop.Shutdown();
            }
        }
    }
}
