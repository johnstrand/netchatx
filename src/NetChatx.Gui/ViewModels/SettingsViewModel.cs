using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using NetChatx.Gui.Converters;
using NetChatx.Gui.Helpers;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settingsRepo;
    private readonly string _accountJid;
    private readonly Action<bool, int>? _onBubbleMergeChanged;
    private readonly Action<bool>? _onPopupsChanged;
    private readonly Action<bool>? _onFlashingChanged;
    private readonly Action<string, double>? _onTypographyChanged;
    private readonly Action<bool>? _onSendOnEnterChanged;
    private readonly Action<bool>? _onUse24HourClockChanged;
    private readonly Action<bool, bool>? _onMediaSettingsChanged;
    private readonly Action<string, string>? _onThemeChanged;
    private readonly Action<IReadOnlyList<string>>? _onQuickEmojisChanged;
    private readonly Action<string, string>? _onBubbleColorChanged;
    private readonly Action<int>? _onChatInputMaxLinesChanged;
    private readonly Action<string>? _onCloseActionChanged;
    private bool _isInitializing;

    public static readonly IReadOnlyList<string> CuratedFontFamilies =
    [
        "Inter",
        "Segoe UI",
        "Roboto",
        "Cascadia Code",
        "Consolas",
        "System Default",
        "Custom..."
    ];

    public static readonly IReadOnlyList<string> ThemeModes =
    [
        "Dark",
        "Light",
        "System"
    ];

    public static readonly IReadOnlyList<string> CloseActionOptions =
    [
        "Ask",
        "Minimize",
        "Exit"
    ];

    public static readonly IReadOnlyList<string> CuratedAccents =
    [
        "#00F0FF", // Cyan
        "#A855F7", // Purple
        "#10B981", // Emerald
        "#F59E0B"  // Amber
    ];

    public static readonly IReadOnlyList<string> CuratedOutboundBubbleColors =
    [
        "#2563EB", // Primary Blue
        "#7C3AED", // Deep Violet
        "#0D9488", // Teal
        "#059669", // Emerald
        "#E11D48", // Rose
        "#EA580C"  // Sunset Orange
    ];

    public static readonly IReadOnlyList<string> CuratedInboundBubbleColors =
    [
        "#1E293B", // Obsidian Slate
        "#111827", // Charcoal Black
        "#1E1B4B", // Midnight Indigo
        "#14332B", // Dark Forest
        "#2A1B28", // Dark Plum
        "#292524"  // Dark Stone
    ];

    public static readonly IReadOnlyList<string> PaletteEmojis =
    [
        "👍", "👎", "❤️", "🔥", "😂", "🎉", "😮", "😢", "🚀", "👏",
        "✨", "💯", "🙏", "😍", "🥳", "🤔", "👀", "🙌", "💀", "💙"
    ];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _selectedTabIndex;

    // --- Appearance & Theme ---
    [ObservableProperty]
    private string _themeMode = SettingsRepository.DefaultThemeMode;

    [ObservableProperty]
    private string _accentColor = SettingsRepository.DefaultAccentColor;

    [ObservableProperty]
    private string _fontFamily = SettingsRepository.DefaultFontFamily;

    [ObservableProperty]
    private string _customFontFamily = string.Empty;

    [ObservableProperty]
    private bool _isCustomFont;

    [ObservableProperty]
    private double _fontSize = SettingsRepository.DefaultFontSize;

    // --- System & Window Behavior ---
    [ObservableProperty]
    private string _closeAction = SettingsRepository.DefaultCloseAction;

    // --- Keyboard & Input ---
    [ObservableProperty]
    private bool _sendOnEnter = SettingsRepository.DefaultSendOnEnter;

    [ObservableProperty]
    private int _chatInputMaxLines = SettingsRepository.DefaultChatInputMaxLines;

    // --- Chat Display ---
    [ObservableProperty]
    private bool _use24HourClock = SettingsRepository.DefaultUse24HourClock;

    [ObservableProperty]
    private bool _enableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;

    [ObservableProperty]
    private int _messageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;

    [ObservableProperty]
    private ObservableCollection<string> _quickEmojis = new(SettingsRepository.DefaultQuickEmojis);

    [ObservableProperty]
    private int _selectedEmojiSlot = -1;

    // --- Media & Previews ---
    [ObservableProperty]
    private bool _showInlinePreviews = SettingsRepository.DefaultShowInlinePreviews;

    [ObservableProperty]
    private bool _autoDownloadMedia = SettingsRepository.DefaultAutoDownloadMedia;

    // --- Chat Bubbles Color ---
    [ObservableProperty]
    private string _outboundBubbleColor = SettingsRepository.DefaultOutboundBubbleColor;

    [ObservableProperty]
    private string _inboundBubbleColor = SettingsRepository.DefaultInboundBubbleColor;

    // --- Notifications ---
    [ObservableProperty]
    private bool _notificationPopupsEnabled = SettingsRepository.DefaultNotificationPopupsEnabled;

    [ObservableProperty]
    private bool _iconFlashingEnabled = SettingsRepository.DefaultIconFlashingEnabled;

    public IReadOnlyList<string> AvailableFontFamilies => CuratedFontFamilies;
    public IReadOnlyList<string> AvailableThemeModes => ThemeModes;
    public IReadOnlyList<string> AvailableCloseActionOptions => CloseActionOptions;
    public IReadOnlyList<string> AvailableAccents => CuratedAccents;
    public IReadOnlyList<string> AvailablePaletteEmojis => PaletteEmojis;
    public IReadOnlyList<string> AvailableOutboundBubbleColors => CuratedOutboundBubbleColors;
    public IReadOnlyList<string> AvailableInboundBubbleColors => CuratedInboundBubbleColors;

    public string EffectiveFontFamily
    {
        get
        {
            if (IsCustomFont)
            {
                return !string.IsNullOrWhiteSpace(CustomFontFamily) ? CustomFontFamily : "Inter";
            }
            if (FontFamily == "System Default")
            {
                return string.Empty;
            }
            return !string.IsNullOrWhiteSpace(FontFamily) ? FontFamily : "Inter";
        }
    }

    public SettingsViewModel(
        SettingsRepository settingsRepo,
        string accountJid,
        Action<bool, int>? onBubbleMergeChanged = null,
        Action<bool>? onPopupsChanged = null,
        Action<bool>? onFlashingChanged = null,
        Action<string, double>? onTypographyChanged = null,
        Action<bool>? onSendOnEnterChanged = null,
        Action<bool>? onUse24HourClockChanged = null,
        Action<bool, bool>? onMediaSettingsChanged = null,
        Action<string, string>? onThemeChanged = null,
        Action<IReadOnlyList<string>>? onQuickEmojisChanged = null,
        Action<string, string>? onBubbleColorChanged = null,
        Action<int>? onChatInputMaxLinesChanged = null,
        Action<string>? onCloseActionChanged = null)
    {
        _settingsRepo = settingsRepo;
        _accountJid = accountJid;
        _onBubbleMergeChanged = onBubbleMergeChanged;
        _onPopupsChanged = onPopupsChanged;
        _onFlashingChanged = onFlashingChanged;
        _onTypographyChanged = onTypographyChanged;
        _onSendOnEnterChanged = onSendOnEnterChanged;
        _onUse24HourClockChanged = onUse24HourClockChanged;
        _onMediaSettingsChanged = onMediaSettingsChanged;
        _onThemeChanged = onThemeChanged;
        _onQuickEmojisChanged = onQuickEmojisChanged;
        _onBubbleColorChanged = onBubbleColorChanged;
        _onChatInputMaxLinesChanged = onChatInputMaxLinesChanged;
        _onCloseActionChanged = onCloseActionChanged;
    }

    public async Task LoadSettingsAsync()
    {
        _isInitializing = true;
        try
        {
            NotificationPopupsEnabled = await _settingsRepo.GetNotificationPopupsEnabledAsync(_accountJid);
            IconFlashingEnabled = await _settingsRepo.GetIconFlashingEnabledAsync(_accountJid);
            EnableMessageMerging = await _settingsRepo.GetMergeMessagesEnabledAsync(_accountJid);
            MessageMergeThresholdSeconds = await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(_accountJid);

            var savedFont = await _settingsRepo.GetFontFamilyAsync(_accountJid);
            if (CuratedFontFamilies.Contains(savedFont))
            {
                FontFamily = savedFont;
                CustomFontFamily = string.Empty;
                IsCustomFont = false;
            }
            else
            {
                FontFamily = "Custom...";
                CustomFontFamily = savedFont;
                IsCustomFont = true;
            }

            FontSize = await _settingsRepo.GetFontSizeAsync(_accountJid);
            SendOnEnter = await _settingsRepo.GetSendOnEnterAsync(_accountJid);
            Use24HourClock = await _settingsRepo.GetUse24HourClockAsync(_accountJid);
            ShowInlinePreviews = await _settingsRepo.GetShowInlinePreviewsAsync(_accountJid);
            AutoDownloadMedia = await _settingsRepo.GetAutoDownloadMediaAsync(_accountJid);
            ThemeMode = await _settingsRepo.GetThemeModeAsync(_accountJid);
            AccentColor = await _settingsRepo.GetAccentColorAsync(_accountJid);
            OutboundBubbleColor = await _settingsRepo.GetOutboundBubbleColorAsync(_accountJid);
            InboundBubbleColor = await _settingsRepo.GetInboundBubbleColorAsync(_accountJid);
            ChatInputMaxLines = await _settingsRepo.GetChatInputMaxLinesAsync(_accountJid);
            CloseAction = await _settingsRepo.GetCloseActionAsync(_accountJid);

            var emojis = await _settingsRepo.GetQuickEmojisAsync(_accountJid);
            QuickEmojis.Clear();
            foreach (var emoji in emojis.Take(6))
            {
                QuickEmojis.Add(emoji);
            }
            while (QuickEmojis.Count < 6)
            {
                QuickEmojis.Add(SettingsRepository.DefaultQuickEmojis[QuickEmojis.Count]);
            }
        }
        finally
        {
            _isInitializing = false;
        }

        // Apply theme and accent
        ThemeManager.ApplyTheme(ThemeMode, AccentColor);
        DirectionToBackgroundConverter.SetColors(OutboundBubbleColor, InboundBubbleColor);
    }

    partial void OnNotificationPopupsEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetNotificationPopupsEnabledAsync(_accountJid, value);
            _onPopupsChanged?.Invoke(value);
        }
    }

    partial void OnIconFlashingEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetIconFlashingEnabledAsync(_accountJid, value);
            _onFlashingChanged?.Invoke(value);
        }
    }

    partial void OnEnableMessageMergingChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetMergeMessagesEnabledAsync(_accountJid, value);
            _onBubbleMergeChanged?.Invoke(value, MessageMergeThresholdSeconds);
        }
    }

    partial void OnMessageMergeThresholdSecondsChanged(int value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetMergeMessagesThresholdSecondsAsync(_accountJid, value);
            _onBubbleMergeChanged?.Invoke(EnableMessageMerging, value);
        }
    }

    partial void OnFontFamilyChanged(string value)
    {
        IsCustomFont = value == "Custom...";
        OnPropertyChanged(nameof(EffectiveFontFamily));

        if (!_isInitializing)
        {
            string toSave = IsCustomFont ? CustomFontFamily : value;
            _ = _settingsRepo.SetFontFamilyAsync(_accountJid, toSave);
            _onTypographyChanged?.Invoke(EffectiveFontFamily, FontSize);
        }
    }

    partial void OnCustomFontFamilyChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveFontFamily));

        if (!_isInitializing && IsCustomFont)
        {
            _ = _settingsRepo.SetFontFamilyAsync(_accountJid, value);
            _onTypographyChanged?.Invoke(EffectiveFontFamily, FontSize);
        }
    }

    partial void OnFontSizeChanged(double value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetFontSizeAsync(_accountJid, value);
            _onTypographyChanged?.Invoke(EffectiveFontFamily, value);
        }
    }

    partial void OnSendOnEnterChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetSendOnEnterAsync(_accountJid, value);
            _onSendOnEnterChanged?.Invoke(value);
        }
    }

    partial void OnChatInputMaxLinesChanged(int value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetChatInputMaxLinesAsync(_accountJid, value);
            _onChatInputMaxLinesChanged?.Invoke(value);
        }
    }

    partial void OnCloseActionChanged(string value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetCloseActionAsync(_accountJid, value);
            _onCloseActionChanged?.Invoke(value);
        }
    }

    partial void OnUse24HourClockChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetUse24HourClockAsync(_accountJid, value);
            _onUse24HourClockChanged?.Invoke(value);
        }
    }

    partial void OnShowInlinePreviewsChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetShowInlinePreviewsAsync(_accountJid, value);
            _onMediaSettingsChanged?.Invoke(value, AutoDownloadMedia);
        }
    }

    partial void OnAutoDownloadMediaChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetAutoDownloadMediaAsync(_accountJid, value);
            _onMediaSettingsChanged?.Invoke(ShowInlinePreviews, value);
        }
    }

    partial void OnThemeModeChanged(string value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetThemeModeAsync(_accountJid, value);
            ThemeManager.ApplyTheme(value, AccentColor);
            _onThemeChanged?.Invoke(value, AccentColor);
        }
    }

    public bool IsCyanSelected => AccentColor.Contains("00F0FF", StringComparison.OrdinalIgnoreCase);
    public bool IsPurpleSelected => AccentColor.Contains("A855F7", StringComparison.OrdinalIgnoreCase);
    public bool IsEmeraldSelected => AccentColor.Contains("10B981", StringComparison.OrdinalIgnoreCase);
    public bool IsAmberSelected => AccentColor.Contains("F59E0B", StringComparison.OrdinalIgnoreCase);

    public string SelectedAccentName => IsPurpleSelected
        ? "Neon Purple (#A855F7)"
        : IsEmeraldSelected
            ? "Emerald Green (#10B981)"
            : IsAmberSelected
                ? "Vibrant Amber (#F59E0B)"
                : "Electric Cyan (#00F0FF)";

    partial void OnAccentColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsCyanSelected));
        OnPropertyChanged(nameof(IsPurpleSelected));
        OnPropertyChanged(nameof(IsEmeraldSelected));
        OnPropertyChanged(nameof(IsAmberSelected));
        OnPropertyChanged(nameof(SelectedAccentName));

        if (!_isInitializing)
        {
            _ = _settingsRepo.SetAccentColorAsync(_accountJid, value);
            ThemeManager.ApplyTheme(ThemeMode, value);
            _onThemeChanged?.Invoke(ThemeMode, value);
        }
    }

    // Outbound presets selection flags
    public bool IsOutboundBlueSelected => OutboundBubbleColor.Contains("2563EB", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundVioletSelected => OutboundBubbleColor.Contains("7C3AED", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundTealSelected => OutboundBubbleColor.Contains("0D9488", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundEmeraldSelected => OutboundBubbleColor.Contains("059669", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundRoseSelected => OutboundBubbleColor.Contains("E11D48", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundOrangeSelected => OutboundBubbleColor.Contains("EA580C", StringComparison.OrdinalIgnoreCase);

    // Inbound presets selection flags
    public bool IsInboundSlateSelected => InboundBubbleColor.Contains("1E293B", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundCharcoalSelected => InboundBubbleColor.Contains("111827", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundIndigoSelected => InboundBubbleColor.Contains("1E1B4B", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundForestSelected => InboundBubbleColor.Contains("14332B", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundPlumSelected => InboundBubbleColor.Contains("2A1B28", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundStoneSelected => InboundBubbleColor.Contains("292524", StringComparison.OrdinalIgnoreCase);

    public IBrush PreviewOutboundBrush => Color.TryParse(OutboundBubbleColor, out var c)
        ? new SolidColorBrush(c)
        : DirectionToBackgroundConverter.OutboundBrush;

    public IBrush PreviewInboundBrush => Color.TryParse(InboundBubbleColor, out var c)
        ? new SolidColorBrush(c)
        : DirectionToBackgroundConverter.InboundBrush;

    partial void OnOutboundBubbleColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsOutboundBlueSelected));
        OnPropertyChanged(nameof(IsOutboundVioletSelected));
        OnPropertyChanged(nameof(IsOutboundTealSelected));
        OnPropertyChanged(nameof(IsOutboundEmeraldSelected));
        OnPropertyChanged(nameof(IsOutboundRoseSelected));
        OnPropertyChanged(nameof(IsOutboundOrangeSelected));
        OnPropertyChanged(nameof(PreviewOutboundBrush));

        DirectionToBackgroundConverter.SetColors(value, InboundBubbleColor);

        if (!_isInitializing)
        {
            _ = _settingsRepo.SetOutboundBubbleColorAsync(_accountJid, value);
            _onBubbleColorChanged?.Invoke(value, InboundBubbleColor);
        }
    }

    partial void OnInboundBubbleColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsInboundSlateSelected));
        OnPropertyChanged(nameof(IsInboundCharcoalSelected));
        OnPropertyChanged(nameof(IsInboundIndigoSelected));
        OnPropertyChanged(nameof(IsInboundForestSelected));
        OnPropertyChanged(nameof(IsInboundPlumSelected));
        OnPropertyChanged(nameof(IsInboundStoneSelected));
        OnPropertyChanged(nameof(PreviewInboundBrush));

        DirectionToBackgroundConverter.SetColors(OutboundBubbleColor, value);

        if (!_isInitializing)
        {
            _ = _settingsRepo.SetInboundBubbleColorAsync(_accountJid, value);
            _onBubbleColorChanged?.Invoke(OutboundBubbleColor, value);
        }
    }

    public string SelectedSlotLabel => SelectedEmojiSlot >= 0 && SelectedEmojiSlot < QuickEmojis.Count
        ? $"Editing Slot {SelectedEmojiSlot + 1}: Click an emoji below to swap"
        : "Click a slot (1-6) above, then pick an emoji below to replace it:";

    partial void OnSelectedEmojiSlotChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedSlotLabel));
    }

    [RelayCommand]
    public void SelectTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }

    [RelayCommand]
    public void SelectThemeMode(string mode)
    {
        ThemeMode = mode;
    }

    [RelayCommand]
    public void SelectAccent(object? param)
    {
        string? hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            AccentColor = hex;
        }
    }

    [RelayCommand]
    public void SelectOutboundBubbleColor(object? param)
    {
        string? hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OutboundBubbleColor = hex;
        }
    }

    [RelayCommand]
    public void SelectInboundBubbleColor(object? param)
    {
        string? hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            InboundBubbleColor = hex;
        }
    }

    [RelayCommand]
    public void MatchAccentBubbleColor()
    {
        if (!string.IsNullOrWhiteSpace(AccentColor))
        {
            OutboundBubbleColor = AccentColor;
        }
    }

    [RelayCommand]
    public void SelectEmojiSlot(object? param)
    {
        int index = -1;
        if (param is int i) index = i;
        else if (param is string s && int.TryParse(s, out int parsed)) index = parsed;

        SelectedEmojiSlot = (SelectedEmojiSlot == index) ? -1 : index;
        OnPropertyChanged(nameof(SelectedSlotLabel));
    }

    [RelayCommand]
    public async Task PickEmojiForSlotAsync(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        int target = (SelectedEmojiSlot >= 0 && SelectedEmojiSlot < QuickEmojis.Count) ? SelectedEmojiSlot : 0;
        QuickEmojis[target] = emoji;
        OnPropertyChanged(nameof(SelectedSlotLabel));
        await SaveQuickEmojisAsync();
    }

    [RelayCommand]
    public async Task ResetQuickEmojisAsync()
    {
        QuickEmojis.Clear();
        foreach (var emoji in SettingsRepository.DefaultQuickEmojis)
        {
            QuickEmojis.Add(emoji);
        }
        SelectedEmojiSlot = -1;
        await SaveQuickEmojisAsync();
    }

    private async Task SaveQuickEmojisAsync()
    {
        if (!_isInitializing)
        {
            await _settingsRepo.SetQuickEmojisAsync(_accountJid, QuickEmojis);
            _onQuickEmojisChanged?.Invoke(QuickEmojis.ToList());
        }
    }

    [RelayCommand]
    public void Open()
    {
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        SelectedEmojiSlot = -1;
    }

    [RelayCommand]
    public async Task ResetDefaultsAsync()
    {
        NotificationPopupsEnabled = SettingsRepository.DefaultNotificationPopupsEnabled;
        IconFlashingEnabled = SettingsRepository.DefaultIconFlashingEnabled;
        EnableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;
        MessageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;
        FontFamily = SettingsRepository.DefaultFontFamily;
        CustomFontFamily = string.Empty;
        IsCustomFont = false;
        FontSize = SettingsRepository.DefaultFontSize;
        SendOnEnter = SettingsRepository.DefaultSendOnEnter;
        Use24HourClock = SettingsRepository.DefaultUse24HourClock;
        ShowInlinePreviews = SettingsRepository.DefaultShowInlinePreviews;
        AutoDownloadMedia = SettingsRepository.DefaultAutoDownloadMedia;
        ThemeMode = SettingsRepository.DefaultThemeMode;
        AccentColor = SettingsRepository.DefaultAccentColor;
        OutboundBubbleColor = SettingsRepository.DefaultOutboundBubbleColor;
        InboundBubbleColor = SettingsRepository.DefaultInboundBubbleColor;
        ChatInputMaxLines = SettingsRepository.DefaultChatInputMaxLines;
        CloseAction = SettingsRepository.DefaultCloseAction;

        QuickEmojis.Clear();
        foreach (var emoji in SettingsRepository.DefaultQuickEmojis)
        {
            QuickEmojis.Add(emoji);
        }
        SelectedEmojiSlot = -1;

        await _settingsRepo.SetNotificationPopupsEnabledAsync(_accountJid, NotificationPopupsEnabled);
        await _settingsRepo.SetIconFlashingEnabledAsync(_accountJid, IconFlashingEnabled);
        await _settingsRepo.SetMergeMessagesEnabledAsync(_accountJid, EnableMessageMerging);
        await _settingsRepo.SetMergeMessagesThresholdSecondsAsync(_accountJid, MessageMergeThresholdSeconds);
        await _settingsRepo.SetFontFamilyAsync(_accountJid, FontFamily);
        await _settingsRepo.SetFontSizeAsync(_accountJid, FontSize);
        await _settingsRepo.SetSendOnEnterAsync(_accountJid, SendOnEnter);
        await _settingsRepo.SetChatInputMaxLinesAsync(_accountJid, ChatInputMaxLines);
        await _settingsRepo.SetUse24HourClockAsync(_accountJid, Use24HourClock);
        await _settingsRepo.SetShowInlinePreviewsAsync(_accountJid, ShowInlinePreviews);
        await _settingsRepo.SetAutoDownloadMediaAsync(_accountJid, AutoDownloadMedia);
        await _settingsRepo.SetThemeModeAsync(_accountJid, ThemeMode);
        await _settingsRepo.SetAccentColorAsync(_accountJid, AccentColor);
        await _settingsRepo.SetOutboundBubbleColorAsync(_accountJid, OutboundBubbleColor);
        await _settingsRepo.SetInboundBubbleColorAsync(_accountJid, InboundBubbleColor);
        await _settingsRepo.SetCloseActionAsync(_accountJid, CloseAction);
        await _settingsRepo.SetQuickEmojisAsync(_accountJid, QuickEmojis);

        ThemeManager.ApplyTheme(ThemeMode, AccentColor);
        DirectionToBackgroundConverter.SetColors(OutboundBubbleColor, InboundBubbleColor);

        _onBubbleMergeChanged?.Invoke(EnableMessageMerging, MessageMergeThresholdSeconds);
        _onPopupsChanged?.Invoke(NotificationPopupsEnabled);
        _onFlashingChanged?.Invoke(IconFlashingEnabled);
        _onTypographyChanged?.Invoke(EffectiveFontFamily, FontSize);
        _onSendOnEnterChanged?.Invoke(SendOnEnter);
        _onChatInputMaxLinesChanged?.Invoke(ChatInputMaxLines);
        _onUse24HourClockChanged?.Invoke(Use24HourClock);
        _onMediaSettingsChanged?.Invoke(ShowInlinePreviews, AutoDownloadMedia);
        _onThemeChanged?.Invoke(ThemeMode, AccentColor);
        _onQuickEmojisChanged?.Invoke(QuickEmojis.ToList());
        _onBubbleColorChanged?.Invoke(OutboundBubbleColor, InboundBubbleColor);
        _onCloseActionChanged?.Invoke(CloseAction);
    }
}

