using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stanza.Gui.ViewModels;

public sealed partial class HelpViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [RelayCommand]
    public void Open()
    {
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
    }
}
