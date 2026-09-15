using System;
using System.ComponentModel;
using Avalonia.Controls;
using NetChatx.Gui.ViewModels;

namespace NetChatx.Gui.Views;

public partial class MainWindow : Window
{
    private bool _isForceClosing;
    private MainChatViewModel? _attachedChatVm;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        _isForceClosing = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isForceClosing)
        {
            base.OnClosing(e);
            return;
        }

        if (DataContext is MainWindowViewModel mainVm && mainVm.CurrentView is MainChatViewModel chatVm)
        {
            bool allowClose = chatVm.HandleWindowClosing(
                hideWindow: () => Hide(),
                exitApp: () => ForceClose()
            );

            if (!allowClose)
            {
                e.Cancel = true;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachToMainChatViewModel();
    }

    private void AttachToMainChatViewModel()
    {
        if (_attachedChatVm is not null)
        {
            _attachedChatVm.RequestHideWindow -= OnRequestHideWindow;
            _attachedChatVm.RequestExitApp -= OnRequestExitApp;
            _attachedChatVm = null;
        }

        if (DataContext is MainWindowViewModel mainVm)
        {
            mainVm.PropertyChanged -= OnMainWindowViewModelPropertyChanged;
            mainVm.PropertyChanged += OnMainWindowViewModelPropertyChanged;

            if (mainVm.CurrentView is MainChatViewModel chatVm)
            {
                chatVm.NotificationService?.AttachWindow(this);
                _attachedChatVm = chatVm;
                _attachedChatVm.RequestHideWindow += OnRequestHideWindow;
                _attachedChatVm.RequestExitApp += OnRequestExitApp;
            }
        }
    }

    private void OnMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentView))
        {
            AttachToMainChatViewModel();
        }
    }

    private void OnRequestHideWindow()
    {
        Hide();
    }

    private void OnRequestExitApp()
    {
        ForceClose();
    }
}
