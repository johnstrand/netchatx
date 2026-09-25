using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Stanza.Gui.Converters;
using Stanza.Gui.Helpers;
using Stanza.Gui.Services;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settingsRepo;
    private readonly IStartupService _startupService;
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
    private readonly Action<bool, IReadOnlyList<EmoticonMapping>>? _onEmoticonSettingsChanged;
    private readonly Action<string>? _onLanguageChanged;
    private readonly Action<bool>? _onEvaluateExpressionsChanged;
    private readonly Func<byte[], string, Task>? _onAvatarChanged;
    private readonly Func<Task>? _onAvatarRemoved;
    private readonly Func<Task>? _onAvatarSyncRequested;
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

    public static readonly IReadOnlyList<string> CuratedAccents = ThemeManager.CuratedAccents;

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

    public static readonly IReadOnlyList<string> CuratedOutboundBubbleTextColors =
    [
        "#FFFFFF", // Pure White
        "#F8FAFC", // Off-White
        "#0F172A", // Dark Slate
        "#000000"  // Black
    ];

    public static readonly IReadOnlyList<string> CuratedInboundBubbleTextColors =
    [
        "#F1F5F9", // Crisp Light
        "#FFFFFF", // Pure White
        "#94A3B8", // Muted Slate
        "#0F172A"  // Dark Slate
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

    // --- Language ---
    public IReadOnlyList<LanguageItem> AvailableLanguages => LocalizationManager.SupportedLanguages;

    [ObservableProperty]
    private LanguageItem _selectedLanguageItem = LocalizationManager.SupportedLanguages[0];

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

    // --- Emoticon Auto-Replacement ---
    [ObservableProperty]
    private bool _autoReplaceEmoticons = SettingsRepository.DefaultAutoReplaceEmoticons;

    // --- Expression Evaluation ---
    [ObservableProperty]
    private bool _evaluateExpressions = SettingsRepository.DefaultEvaluateExpressions;

    [ObservableProperty]
    private ObservableCollection<EmoticonMapping> _emoticonMappings = new(SettingsRepository.DefaultEmoticonMappings);

    [ObservableProperty]
    private string _newShortcut = string.Empty;

    [ObservableProperty]
    private string _newEmoji = string.Empty;

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

    [ObservableProperty]
    private string _outboundBubbleTextColor = SettingsRepository.DefaultOutboundBubbleTextColor;

    [ObservableProperty]
    private string _inboundBubbleTextColor = SettingsRepository.DefaultInboundBubbleTextColor;

    // --- Notifications & Startup ---
    [ObservableProperty]
    private bool _notificationPopupsEnabled = SettingsRepository.DefaultNotificationPopupsEnabled;

    [ObservableProperty]
    private bool _iconFlashingEnabled = SettingsRepository.DefaultIconFlashingEnabled;

    [ObservableProperty]
    private bool _launchOnStartup = SettingsRepository.DefaultLaunchOnStartup;

    // --- Profile & Avatar ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserAvatar))]
    private Avalonia.Media.Imaging.Bitmap? _userAvatar;

    [ObservableProperty]
    private string? _userAvatarHash;

    [ObservableProperty]
    private string? _avatarStatusMessage;

    public bool HasUserAvatar => UserAvatar != null;

    public string AccountJid => _accountJid;

    public string UserInitials => Helpers.AvatarHelper.GetInitials(_accountJid);

    public Avalonia.Media.IBrush UserAvatarBackgroundBrush => Helpers.AvatarHelper.GetAvatarColorBrush(_accountJid);

    public IReadOnlyList<string> AvailableFontFamilies => CuratedFontFamilies;
    public IReadOnlyList<string> AvailableThemeModes => ThemeModes;
    public IReadOnlyList<string> AvailableCloseActionOptions => CloseActionOptions;
    public IReadOnlyList<string> AvailableAccents => CuratedAccents;
    public IReadOnlyList<string> AvailablePaletteEmojis => PaletteEmojis;
    public IReadOnlyList<string> AvailableOutboundBubbleColors => CuratedOutboundBubbleColors;
    public IReadOnlyList<string> AvailableInboundBubbleColors => CuratedInboundBubbleColors;
    public IReadOnlyList<string> AvailableOutboundTextColors => CuratedOutboundBubbleTextColors;
    public IReadOnlyList<string> AvailableInboundTextColors => CuratedInboundBubbleTextColors;

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
        Action<string>? onCloseActionChanged = null,
        Action<bool, IReadOnlyList<EmoticonMapping>>? onEmoticonSettingsChanged = null,
        Action<bool>? onEvaluateExpressionsChanged = null,
        IStartupService? startupService = null,
        Func<byte[], string, Task>? onAvatarChanged = null,
        Func<Task>? onAvatarRemoved = null,
        Func<Task>? onAvatarSyncRequested = null,
        Action<string>? onLanguageChanged = null)
    {
        _settingsRepo = settingsRepo;
        _startupService = startupService ?? new StartupService();
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
        _onEmoticonSettingsChanged = onEmoticonSettingsChanged;
        _onEvaluateExpressionsChanged = onEvaluateExpressionsChanged;
        _onAvatarChanged = onAvatarChanged;
        _onAvatarRemoved = onAvatarRemoved;
        _onAvatarSyncRequested = onAvatarSyncRequested;
        _onLanguageChanged = onLanguageChanged;
    }

    [RelayCommand]
    public async Task SyncAvatarAsync()
    {
        AvatarStatusMessage = "Syncing avatar from server...";
        if (_onAvatarSyncRequested is not null)
        {
            await _onAvatarSyncRequested();
            AvatarStatusMessage = HasUserAvatar ? "Avatar synced from server!" : "No avatar found on server.";
        }
    }

    public async Task SetAvatarBytesAsync(byte[] bytes, string mimeType)
    {
        var hash = Helpers.AvatarHelper.ComputeSha1(bytes);
        var bmp = Helpers.AvatarHelper.CreateBitmapFromBytes(bytes);
        UserAvatar = bmp;
        UserAvatarHash = hash;
        AvatarStatusMessage = "Avatar updated successfully!";

        if (_onAvatarChanged is not null)
        {
            await _onAvatarChanged(bytes, mimeType);
        }
    }

    [RelayCommand]
    public async Task RemoveAvatarAsync()
    {
        UserAvatar = null;
        UserAvatarHash = null;
        AvatarStatusMessage = "Avatar removed.";

        if (_onAvatarRemoved is not null)
        {
            await _onAvatarRemoved();
        }
    }

    public async Task LoadSettingsAsync()
    {
        _isInitializing = true;
        try
        {
            NotificationPopupsEnabled = await _settingsRepo.GetNotificationPopupsEnabledAsync(_accountJid);
            IconFlashingEnabled = await _settingsRepo.GetIconFlashingEnabledAsync(_accountJid);

            var osAutostart = _startupService.IsStartupEnabled();
            var dbAutostart = await _settingsRepo.GetLaunchOnStartupAsync(_accountJid);
            if (dbAutostart && !osAutostart)
            {
                _startupService.SetStartupEnabled(true);
                LaunchOnStartup = true;
            }
            else
            {
                LaunchOnStartup = osAutostart;
                if (osAutostart != dbAutostart)
                {
                    await _settingsRepo.SetLaunchOnStartupAsync(_accountJid, osAutostart);
                }
            }

            EnableMessageMerging = await _settingsRepo.GetMergeMessagesEnabledAsync(_accountJid);
            MessageMergeThresholdSeconds = await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(_accountJid);
            AutoReplaceEmoticons = await _settingsRepo.GetAutoReplaceEmoticonsAsync(_accountJid);
            EvaluateExpressions = await _settingsRepo.GetEvaluateExpressionsAsync(_accountJid);

            var mappings = await _settingsRepo.GetEmoticonMappingsAsync(_accountJid);
            EmoticonMappings.Clear();
            foreach (var m in mappings)
            {
                EmoticonMappings.Add(m);
            }

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
            OutboundBubbleTextColor = await _settingsRepo.GetOutboundBubbleTextColorAsync(_accountJid);
            InboundBubbleTextColor = await _settingsRepo.GetInboundBubbleTextColorAsync(_accountJid);
            ChatInputMaxLines = await _settingsRepo.GetChatInputMaxLinesAsync(_accountJid);
            CloseAction = await _settingsRepo.GetCloseActionAsync(_accountJid);

            var savedLanguage = await _settingsRepo.GetLanguageAsync(_accountJid);
            var matchingLang = AvailableLanguages.FirstOrDefault(l => l.Code.Equals(savedLanguage, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];
            SelectedLanguageItem = matchingLang;
            LocalizationManager.Instance.SetLanguage(matchingLang.Code, notify: false);

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
        DirectionToForegroundConverter.SetColors(OutboundBubbleTextColor, InboundBubbleTextColor);
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

    partial void OnLaunchOnStartupChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetLaunchOnStartupAsync(_accountJid, value);
            _startupService.SetStartupEnabled(value);
        }
    }

    partial void OnAutoReplaceEmoticonsChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetAutoReplaceEmoticonsAsync(_accountJid, value);
            _onEmoticonSettingsChanged?.Invoke(value, EmoticonMappings.ToList());
        }
    }

    partial void OnEvaluateExpressionsChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetEvaluateExpressionsAsync(_accountJid, value);
            _onEvaluateExpressionsChanged?.Invoke(value);
        }
    }

    [RelayCommand]
    public async Task AddEmoticonMappingAsync()
    {
        var shortcut = NewShortcut?.Trim();
        var emoji = NewEmoji?.Trim();

        if (string.IsNullOrEmpty(shortcut) || string.IsNullOrEmpty(emoji)) return;

        var existing = EmoticonMappings.FirstOrDefault(m => string.Equals(m.Shortcut, shortcut, StringComparison.Ordinal));
        if (existing is not null)
        {
            EmoticonMappings.Remove(existing);
        }

        var newMapping = new EmoticonMapping(shortcut, emoji);
        EmoticonMappings.Add(newMapping);
        NewShortcut = string.Empty;
        NewEmoji = string.Empty;

        if (!_isInitializing)
        {
            await _settingsRepo.SetEmoticonMappingsAsync(_accountJid, EmoticonMappings);
            _onEmoticonSettingsChanged?.Invoke(AutoReplaceEmoticons, EmoticonMappings.ToList());
        }
    }

    [RelayCommand]
    public async Task RemoveEmoticonMappingAsync(EmoticonMapping mapping)
    {
        if (mapping is null) return;
        EmoticonMappings.Remove(mapping);

        if (!_isInitializing)
        {
            await _settingsRepo.SetEmoticonMappingsAsync(_accountJid, EmoticonMappings);
            _onEmoticonSettingsChanged?.Invoke(AutoReplaceEmoticons, EmoticonMappings.ToList());
        }
    }

    [RelayCommand]
    public async Task ResetEmoticonMappingsAsync()
    {
        EmoticonMappings.Clear();
        foreach (var m in SettingsRepository.DefaultEmoticonMappings)
        {
            EmoticonMappings.Add(m);
        }

        if (!_isInitializing)
        {
            await _settingsRepo.SetEmoticonMappingsAsync(_accountJid, EmoticonMappings);
            _onEmoticonSettingsChanged?.Invoke(AutoReplaceEmoticons, EmoticonMappings.ToList());
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
            var toSave = IsCustomFont ? CustomFontFamily : value;
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

    partial void OnSelectedLanguageItemChanged(LanguageItem value)
    {
        if (value is null) return;
        LocalizationManager.Instance.SetLanguage(value.Code);
        if (!_isInitializing)
        {
            _ = _settingsRepo.SetLanguageAsync(_accountJid, value.Code);
            _onLanguageChanged?.Invoke(value.Code);
        }
    }

    public bool IsCyanSelected => AccentColor.Contains("00F0FF", StringComparison.OrdinalIgnoreCase);
    public bool IsPurpleSelected => AccentColor.Contains("A855F7", StringComparison.OrdinalIgnoreCase);
    public bool IsEmeraldSelected => AccentColor.Contains("10B981", StringComparison.OrdinalIgnoreCase);
    public bool IsAmberSelected => AccentColor.Contains("F59E0B", StringComparison.OrdinalIgnoreCase);
    public bool IsCrimsonSelected => AccentColor.Contains("EF4444", StringComparison.OrdinalIgnoreCase);
    public bool IsRoseSelected => AccentColor.Contains("EC4899", StringComparison.OrdinalIgnoreCase);
    public bool IsIndigoSelected => AccentColor.Contains("6366F1", StringComparison.OrdinalIgnoreCase);
    public bool IsOrangeSelected => AccentColor.Contains("F97316", StringComparison.OrdinalIgnoreCase);

    public bool IsCustomAccentSelected => !IsCyanSelected && !IsPurpleSelected && !IsEmeraldSelected && !IsAmberSelected &&
                                          !IsCrimsonSelected && !IsRoseSelected && !IsIndigoSelected && !IsOrangeSelected;

    public string SelectedAccentName
    {
        get
        {
            if (IsCyanSelected) return "Electric Cyan (#00F0FF)";
            if (IsPurpleSelected) return "Neon Purple (#A855F7)";
            if (IsEmeraldSelected) return "Emerald Green (#10B981)";
            if (IsAmberSelected) return "Vibrant Amber (#F59E0B)";
            if (IsCrimsonSelected) return "Crimson Red (#EF4444)";
            if (IsRoseSelected) return "Rose Pink (#EC4899)";
            if (IsIndigoSelected) return "Indigo (#6366F1)";
            if (IsOrangeSelected) return "Sunset Orange (#F97316)";
            return $"Custom Accent ({AccentColor})";
        }
    }

    public IBrush CustomAccentBrush => Color.TryParse(ThemeManager.NormalizeHex(AccentColor), out var c)
        ? new SolidColorBrush(c)
        : new SolidColorBrush(Color.Parse("#00F0FF"));

    partial void OnAccentColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsCyanSelected));
        OnPropertyChanged(nameof(IsPurpleSelected));
        OnPropertyChanged(nameof(IsEmeraldSelected));
        OnPropertyChanged(nameof(IsAmberSelected));
        OnPropertyChanged(nameof(IsCrimsonSelected));
        OnPropertyChanged(nameof(IsRoseSelected));
        OnPropertyChanged(nameof(IsIndigoSelected));
        OnPropertyChanged(nameof(IsOrangeSelected));
        OnPropertyChanged(nameof(IsCustomAccentSelected));
        OnPropertyChanged(nameof(SelectedAccentName));
        OnPropertyChanged(nameof(CustomAccentBrush));

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

    // Outbound text color flags
    public bool IsOutboundTextWhiteSelected => OutboundBubbleTextColor.Contains("FFFFFF", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundTextOffWhiteSelected => OutboundBubbleTextColor.Contains("F8FAFC", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundTextDarkSlateSelected => OutboundBubbleTextColor.Contains("0F172A", StringComparison.OrdinalIgnoreCase);
    public bool IsOutboundTextBlackSelected => OutboundBubbleTextColor.Contains("000000", StringComparison.OrdinalIgnoreCase);

    // Inbound text color flags
    public bool IsInboundTextCrispLightSelected => InboundBubbleTextColor.Contains("F1F5F9", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundTextWhiteSelected => InboundBubbleTextColor.Contains("FFFFFF", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundTextMutedSlateSelected => InboundBubbleTextColor.Contains("94A3B8", StringComparison.OrdinalIgnoreCase);
    public bool IsInboundTextDarkSlateSelected => InboundBubbleTextColor.Contains("0F172A", StringComparison.OrdinalIgnoreCase);

    public IBrush PreviewOutboundTextBrush => Color.TryParse(OutboundBubbleTextColor, out var c)
        ? new SolidColorBrush(c)
        : Brushes.White;

    public IBrush PreviewInboundTextBrush => Color.TryParse(InboundBubbleTextColor, out var c)
        ? new SolidColorBrush(c)
        : new SolidColorBrush(Color.Parse("#F1F5F9"));

    partial void OnOutboundBubbleTextColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsOutboundTextWhiteSelected));
        OnPropertyChanged(nameof(IsOutboundTextOffWhiteSelected));
        OnPropertyChanged(nameof(IsOutboundTextDarkSlateSelected));
        OnPropertyChanged(nameof(IsOutboundTextBlackSelected));
        OnPropertyChanged(nameof(PreviewOutboundTextBrush));

        DirectionToForegroundConverter.SetColors(value, InboundBubbleTextColor);

        if (!_isInitializing)
        {
            _ = _settingsRepo.SetOutboundBubbleTextColorAsync(_accountJid, value);
            _onBubbleColorChanged?.Invoke(OutboundBubbleColor, InboundBubbleColor);
        }
    }

    partial void OnInboundBubbleTextColorChanged(string value)
    {
        OnPropertyChanged(nameof(IsInboundTextCrispLightSelected));
        OnPropertyChanged(nameof(IsInboundTextWhiteSelected));
        OnPropertyChanged(nameof(IsInboundTextMutedSlateSelected));
        OnPropertyChanged(nameof(IsInboundTextDarkSlateSelected));
        OnPropertyChanged(nameof(PreviewInboundTextBrush));

        DirectionToForegroundConverter.SetColors(OutboundBubbleTextColor, value);

        if (!_isInitializing)
        {
            _ = _settingsRepo.SetInboundBubbleTextColorAsync(_accountJid, value);
            _onBubbleColorChanged?.Invoke(OutboundBubbleColor, InboundBubbleColor);
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
        var hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            AccentColor = hex;
        }
    }

    [RelayCommand]
    public void SelectOutboundBubbleColor(object? param)
    {
        var hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OutboundBubbleColor = hex;
        }
    }

    [RelayCommand]
    public void SelectInboundBubbleColor(object? param)
    {
        var hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            InboundBubbleColor = hex;
        }
    }

    [RelayCommand]
    public void SelectOutboundBubbleTextColor(object? param)
    {
        var hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OutboundBubbleTextColor = hex;
        }
    }

    [RelayCommand]
    public void SelectInboundBubbleTextColor(object? param)
    {
        var hex = param?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            InboundBubbleTextColor = hex;
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
        var index = -1;
        if (param is int i) index = i;
        else if (param is string s && int.TryParse(s, out int parsed)) index = parsed;

        SelectedEmojiSlot = (SelectedEmojiSlot == index) ? -1 : index;
        OnPropertyChanged(nameof(SelectedSlotLabel));
    }

    [RelayCommand]
    public async Task PickEmojiForSlotAsync(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        var target = (SelectedEmojiSlot >= 0 && SelectedEmojiSlot < QuickEmojis.Count) ? SelectedEmojiSlot : 0;
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
        var osAutostart = _startupService.IsStartupEnabled();
        if (LaunchOnStartup != osAutostart)
        {
            LaunchOnStartup = osAutostart;
        }
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
        LaunchOnStartup = SettingsRepository.DefaultLaunchOnStartup;
        EnableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;
        MessageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;
        AutoReplaceEmoticons = SettingsRepository.DefaultAutoReplaceEmoticons;
        EvaluateExpressions = SettingsRepository.DefaultEvaluateExpressions;

        EmoticonMappings.Clear();
        foreach (var m in SettingsRepository.DefaultEmoticonMappings)
        {
            EmoticonMappings.Add(m);
        }
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
        OutboundBubbleTextColor = SettingsRepository.DefaultOutboundBubbleTextColor;
        InboundBubbleTextColor = SettingsRepository.DefaultInboundBubbleTextColor;
        ChatInputMaxLines = SettingsRepository.DefaultChatInputMaxLines;
        CloseAction = SettingsRepository.DefaultCloseAction;
        SelectedLanguageItem = AvailableLanguages.FirstOrDefault(l => l.Code == SettingsRepository.DefaultLanguage) ?? AvailableLanguages[0];
        LocalizationManager.Instance.SetLanguage(SettingsRepository.DefaultLanguage);

        QuickEmojis.Clear();
        foreach (var emoji in SettingsRepository.DefaultQuickEmojis)
        {
            QuickEmojis.Add(emoji);
        }
        SelectedEmojiSlot = -1;

        await _settingsRepo.SetNotificationPopupsEnabledAsync(_accountJid, NotificationPopupsEnabled);
        await _settingsRepo.SetIconFlashingEnabledAsync(_accountJid, IconFlashingEnabled);
        await _settingsRepo.SetLaunchOnStartupAsync(_accountJid, LaunchOnStartup);
        _startupService.SetStartupEnabled(LaunchOnStartup);
        await _settingsRepo.SetMergeMessagesEnabledAsync(_accountJid, EnableMessageMerging);
        await _settingsRepo.SetMergeMessagesThresholdSecondsAsync(_accountJid, MessageMergeThresholdSeconds);
        await _settingsRepo.SetAutoReplaceEmoticonsAsync(_accountJid, AutoReplaceEmoticons);
        await _settingsRepo.SetEvaluateExpressionsAsync(_accountJid, EvaluateExpressions);
        await _settingsRepo.SetEmoticonMappingsAsync(_accountJid, EmoticonMappings);
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
        await _settingsRepo.SetOutboundBubbleTextColorAsync(_accountJid, OutboundBubbleTextColor);
        await _settingsRepo.SetInboundBubbleTextColorAsync(_accountJid, InboundBubbleTextColor);
        await _settingsRepo.SetCloseActionAsync(_accountJid, CloseAction);
        await _settingsRepo.SetQuickEmojisAsync(_accountJid, QuickEmojis);
        await _settingsRepo.SetLanguageAsync(_accountJid, SettingsRepository.DefaultLanguage);

        ThemeManager.ApplyTheme(ThemeMode, AccentColor);
        DirectionToBackgroundConverter.SetColors(OutboundBubbleColor, InboundBubbleColor);
        DirectionToForegroundConverter.SetColors(OutboundBubbleTextColor, InboundBubbleTextColor);

        _onBubbleMergeChanged?.Invoke(EnableMessageMerging, MessageMergeThresholdSeconds);
        _onEmoticonSettingsChanged?.Invoke(AutoReplaceEmoticons, EmoticonMappings.ToList());
        _onEvaluateExpressionsChanged?.Invoke(EvaluateExpressions);
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
        _onLanguageChanged?.Invoke(SettingsRepository.DefaultLanguage);
    }
}

