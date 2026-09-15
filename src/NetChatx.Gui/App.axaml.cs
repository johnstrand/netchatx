using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NetChatx.Gui.ViewModels;
using NetChatx.Gui.Views;

namespace NetChatx.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
        ShowMainWindow();
    }

    private void OnTrayOpenClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
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
