using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Gui.Helpers;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class EmojiPickerViewModel : ViewModelBase
{
    private readonly string _accountJid;
    private readonly SettingsRepository? _settingsRepo;
    private readonly Func<string, Task> _onEmojiSelected;
    private readonly Action? _onRequestClose;

    public ObservableCollection<string> QuickEmojis { get; } = [];

    public IReadOnlyList<EmojiCategory> Categories => EmojiData.Categories;

    [ObservableProperty]
    private bool _isFullPickerOpen;

    [ObservableProperty]
    private bool _isCustomizing;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "Smileys";

    public ObservableCollection<string> DisplayedEmojis { get; } = [];

    public EmojiPickerViewModel(
        string accountJid,
        IEnumerable<string> initialQuickEmojis,
        Func<string, Task> onEmojiSelected,
        SettingsRepository? settingsRepo = null,
        Action? onRequestClose = null)
    {
        _accountJid = accountJid;
        _settingsRepo = settingsRepo;
        _onEmojiSelected = onEmojiSelected;
        _onRequestClose = onRequestClose;

        foreach (var emoji in initialQuickEmojis)
        {
            QuickEmojis.Add(emoji);
        }

        UpdateDisplayedEmojis();
    }

    partial void OnSearchQueryChanged(string value)
    {
        UpdateDisplayedEmojis();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        UpdateDisplayedEmojis();
    }

    private void UpdateDisplayedEmojis()
    {
        DisplayedEmojis.Clear();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var searchResults = EmojiData.SearchEmojis(SearchQuery);
            foreach (var emoji in searchResults)
            {
                DisplayedEmojis.Add(emoji);
            }
        }
        else
        {
            var cat = Categories.FirstOrDefault(c => c.Name.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase))
                      ?? Categories[0];
            foreach (var emoji in cat.Emojis)
            {
                DisplayedEmojis.Add(emoji);
            }
        }
    }

    [RelayCommand]
    public void ToggleFullPicker()
    {
        IsFullPickerOpen = !IsFullPickerOpen;
    }

    [RelayCommand]
    public void ToggleCustomize()
    {
        IsCustomizing = !IsCustomizing;
    }

    [RelayCommand]
    public void SelectCategory(string categoryName)
    {
        SearchQuery = string.Empty;
        SelectedCategory = categoryName;
    }

    [RelayCommand]
    public async Task SelectEmojiAsync(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;

        if (IsCustomizing)
        {
            await PinToQuickEmojisAsync(emoji);
            return;
        }

        _onRequestClose?.Invoke();
        await _onEmojiSelected(emoji);
    }

    [RelayCommand]
    public async Task PinToQuickEmojisAsync(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;

        if (!QuickEmojis.Contains(emoji))
        {
            QuickEmojis.Add(emoji);
            await SaveQuickEmojisAsync();
        }
    }

    [RelayCommand]
    public async Task RemoveFromQuickEmojisAsync(string emoji)
    {
        if (QuickEmojis.Remove(emoji))
        {
            await SaveQuickEmojisAsync();
        }
    }

    [RelayCommand]
    public async Task ResetDefaultQuickEmojisAsync()
    {
        QuickEmojis.Clear();
        foreach (var emoji in EmojiData.DefaultQuickEmojis)
        {
            QuickEmojis.Add(emoji);
        }
        await SaveQuickEmojisAsync();
    }

    private async Task SaveQuickEmojisAsync()
    {
        if (_settingsRepo is not null && !string.IsNullOrEmpty(_accountJid))
        {
            try
            {
                await _settingsRepo.SetQuickEmojisAsync(_accountJid, QuickEmojis);
            }
            catch
            {
                // Soft failure saving settings
            }
        }
    }
}
