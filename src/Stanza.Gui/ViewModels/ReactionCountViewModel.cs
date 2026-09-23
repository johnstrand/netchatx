using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stanza.Gui.ViewModels;

public sealed partial class ReactionCountViewModel : ViewModelBase
{
    private readonly Func<string, Task>? _onToggleReaction;

    [ObservableProperty]
    private string _emoji = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isReactedByMe;

    public ReactionCountViewModel(string emoji, int count, bool isReactedByMe, Func<string, Task>? onToggleReaction = null)
    {
        _emoji = emoji;
        _count = count;
        _isReactedByMe = isReactedByMe;
        _onToggleReaction = onToggleReaction;
    }

    [RelayCommand]
    public async Task ToggleAsync()
    {
        if (_onToggleReaction is not null)
        {
            await _onToggleReaction(Emoji);
        }
    }
}
