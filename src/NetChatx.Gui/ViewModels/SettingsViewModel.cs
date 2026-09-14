using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settingsRepo;
    private readonly string _accountJid;
    private readonly Action<bool, int>? _onBubbleMergeChanged;
    private readonly Action<bool>? _onPopupsChanged;
    private readonly Action<bool>? _onFlashingChanged;
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _notificationPopupsEnabled = SettingsRepository.DefaultNotificationPopupsEnabled;

    [ObservableProperty]
    private bool _iconFlashingEnabled = SettingsRepository.DefaultIconFlashingEnabled;

    [ObservableProperty]
    private bool _enableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;

    [ObservableProperty]
    private int _messageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;

    public SettingsViewModel(
        SettingsRepository settingsRepo,
        string accountJid,
        Action<bool, int>? onBubbleMergeChanged = null,
        Action<bool>? onPopupsChanged = null,
        Action<bool>? onFlashingChanged = null)
    {
        _settingsRepo = settingsRepo;
        _accountJid = accountJid;
        _onBubbleMergeChanged = onBubbleMergeChanged;
        _onPopupsChanged = onPopupsChanged;
        _onFlashingChanged = onFlashingChanged;
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
        }
        finally
        {
            _isInitializing = false;
        }
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

    [RelayCommand]
    public async Task ResetDefaultsAsync()
    {
        NotificationPopupsEnabled = SettingsRepository.DefaultNotificationPopupsEnabled;
        IconFlashingEnabled = SettingsRepository.DefaultIconFlashingEnabled;
        EnableMessageMerging = SettingsRepository.DefaultMergeMessagesEnabled;
        MessageMergeThresholdSeconds = SettingsRepository.DefaultMergeMessagesThresholdSeconds;

        await _settingsRepo.SetNotificationPopupsEnabledAsync(_accountJid, NotificationPopupsEnabled);
        await _settingsRepo.SetIconFlashingEnabledAsync(_accountJid, IconFlashingEnabled);
        await _settingsRepo.SetMergeMessagesEnabledAsync(_accountJid, EnableMessageMerging);
        await _settingsRepo.SetMergeMessagesThresholdSecondsAsync(_accountJid, MessageMergeThresholdSeconds);

        _onBubbleMergeChanged?.Invoke(EnableMessageMerging, MessageMergeThresholdSeconds);
        _onPopupsChanged?.Invoke(NotificationPopupsEnabled);
        _onFlashingChanged?.Invoke(IconFlashingEnabled);
    }
}
