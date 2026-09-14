using System;
using System.ComponentModel;
using Avalonia.Controls;
using NetChatx.Gui.ViewModels;

namespace NetChatx.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachToMainChatViewModel();
    }

    private void AttachToMainChatViewModel()
    {
        if (DataContext is MainWindowViewModel mainVm)
        {
            mainVm.PropertyChanged -= OnMainWindowViewModelPropertyChanged;
            mainVm.PropertyChanged += OnMainWindowViewModelPropertyChanged;

            if (mainVm.CurrentView is MainChatViewModel chatVm)
            {
                chatVm.NotificationService?.AttachWindow(this);
            }
        }
    }

    private void OnMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentView))
        {
            if (sender is MainWindowViewModel mainVm && mainVm.CurrentView is MainChatViewModel chatVm)
            {
                chatVm.NotificationService?.AttachWindow(this);
            }
        }
    }
}
