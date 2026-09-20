using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Transport;
using NetChatx.Gui.Converters;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.Services;
using NetChatx.Gui.Tests.Mocks;
using NetChatx.Gui.ViewModels;
using NetChatx.Storage;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class NotificationAndSettingsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly SettingsRepository _settingsRepo;

    public NotificationAndSettingsTests()
    {
        _dbPath = $"test_notifications_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _settingsRepo = new SettingsRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private MainChatViewModel CreateMainChatViewModel(string account = "user@test.org", INotificationService? notificationService = null)
    {
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        return new MainChatViewModel(client, _dbContext, () => Task.CompletedTask, notificationService ?? new MockNotificationService());
    }

    [Fact]
    public void TextBox_MaxLinesAndWrapping_BehavesAsExpected()
    {
        Assert.NotNull(Avalonia.Controls.TextBox.MaxLinesProperty);
        Assert.NotNull(Avalonia.Controls.TextBox.TextWrappingProperty);
    }

    [Fact]
    public async Task SettingsViewModel_DefaultsAndPersistence_WorkCorrectly()
    {
        string account = "user@test.org";
        bool bubbleMergeInvoked = false;
        bool popupsInvoked = false;
        bool flashingInvoked = false;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onBubbleMergeChanged: (enabled, threshold) => bubbleMergeInvoked = true,
            onPopupsChanged: enabled => popupsInvoked = true,
            onFlashingChanged: enabled => flashingInvoked = true);

        // Verify default values
        Assert.True(vm.NotificationPopupsEnabled);
        Assert.True(vm.IconFlashingEnabled);
        Assert.True(vm.EnableMessageMerging);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, vm.MessageMergeThresholdSeconds);
        Assert.False(vm.IsOpen);

        // Open and Close
        vm.Open();
        Assert.True(vm.IsOpen);
        vm.Close();
        Assert.False(vm.IsOpen);

        // Toggling NotificationPopupsEnabled persists to SQLite and fires callback
        vm.NotificationPopupsEnabled = false;
        Assert.True(popupsInvoked);
        Assert.False(await _settingsRepo.GetNotificationPopupsEnabledAsync(account));

        // Toggling IconFlashingEnabled persists to SQLite and fires callback
        vm.IconFlashingEnabled = false;
        Assert.True(flashingInvoked);
        Assert.False(await _settingsRepo.GetIconFlashingEnabledAsync(account));

        // Toggling MessageMerging persists to SQLite and fires callback
        vm.EnableMessageMerging = false;
        Assert.True(bubbleMergeInvoked);
        Assert.False(await _settingsRepo.GetMergeMessagesEnabledAsync(account));

        // Changing MessageMergeThresholdSeconds persists to SQLite
        vm.MessageMergeThresholdSeconds = 25;
        Assert.Equal(25, await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(account));

        // Default and changing ChatInputMaxLines
        Assert.Equal(SettingsRepository.DefaultChatInputMaxLines, vm.ChatInputMaxLines);
        vm.ChatInputMaxLines = 7;
        Assert.Equal(7, await _settingsRepo.GetChatInputMaxLinesAsync(account));

        // Loading from SQLite in a new SettingsViewModel retrieves the persisted values
        var vm2 = new SettingsViewModel(_settingsRepo, account);
        await vm2.LoadSettingsAsync();
        Assert.False(vm2.NotificationPopupsEnabled);
        Assert.False(vm2.IconFlashingEnabled);
        Assert.False(vm2.EnableMessageMerging);
        Assert.Equal(25, vm2.MessageMergeThresholdSeconds);
        Assert.Equal(7, vm2.ChatInputMaxLines);

        // Reset defaults
        await vm2.ResetDefaultsAsync();
        Assert.True(vm2.NotificationPopupsEnabled);
        Assert.True(vm2.IconFlashingEnabled);
        Assert.True(vm2.EnableMessageMerging);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, vm2.MessageMergeThresholdSeconds);
        Assert.Equal(SettingsRepository.DefaultChatInputMaxLines, vm2.ChatInputMaxLines);
        Assert.True(await _settingsRepo.GetNotificationPopupsEnabledAsync(account));
        Assert.True(await _settingsRepo.GetIconFlashingEnabledAsync(account));
        Assert.Equal(SettingsRepository.DefaultChatInputMaxLines, await _settingsRepo.GetChatInputMaxLinesAsync(account));
    }

    [Fact]
    public void NotificationService_FlashAndStopFlashing_UpdatesState()
    {
        var service = new NotificationService();
        Assert.True(service.IsWindowActive);
        Assert.False(service.IsFlashing);

        // Window active state
        service.IsWindowActive = false;
        Assert.False(service.IsWindowActive);

        // FlashWindow sets IsFlashing to true
        service.FlashWindow();
        Assert.True(service.IsFlashing);

        // StopFlashing sets IsFlashing to false
        service.StopFlashing();
        Assert.False(service.IsFlashing);

        // Attach/detach window without crashing
        service.AttachWindow(null);
        service.DetachWindow();
    }

    [Fact]
    public void NotificationService_SystemNotification_DispatchesAndTracks()
    {
        var service = new NotificationService(dispatchNative: false);
        Assert.Equal(0, service.SystemNotificationCount);
        Assert.Null(service.LastNotificationTitle);
        Assert.Null(service.LastNotificationMessage);

        service.ShowSystemNotification("Alice", "Hello system notification!");
        Assert.Equal(1, service.SystemNotificationCount);
        Assert.Equal("Alice", service.LastNotificationTitle);
        Assert.Equal("Hello system notification!", service.LastNotificationMessage);
    }

    [Fact]
    public void NotificationService_ShowSystemNotification_HandlesSpecialCharactersAndQuotesSafely()
    {
        var service = new NotificationService(dispatchNative: true);
        string payloadTitle = "Alice; rm -rf /; $(whoami) \"' `";
        string payloadMessage = "Hello \"quoted\" & 'single' $VAR `calc` ; echo test";

        // Enable native notifications temporarily for the test invocation
        bool prevEnable = NotificationService.EnableNativeNotifications;
        NotificationService.EnableNativeNotifications = true;
        try
        {
            service.ShowSystemNotification(payloadTitle, payloadMessage);
            Assert.Equal(payloadTitle, service.LastNotificationTitle);
            Assert.Equal(payloadMessage, service.LastNotificationMessage);
        }
        finally
        {
            NotificationService.EnableNativeNotifications = prevEnable;
        }
    }

    [Fact]
    public void MockNotificationService_MethodsAndProperties_WorkCorrectly()
    {
        var mock = new MockNotificationService();
        Assert.True(mock.IsWindowActive);
        Assert.False(mock.IsFlashing);
        Assert.Equal(0, mock.SystemNotificationCount);
        Assert.Null(mock.LastNotificationTitle);
        Assert.Null(mock.LastNotificationMessage);

        bool activeChangedFired = false;
        mock.WindowActiveChanged += active => activeChangedFired = true;
        mock.IsWindowActive = false;
        Assert.True(activeChangedFired);
        Assert.False(mock.IsWindowActive);

        mock.FlashWindow();
        Assert.True(mock.IsFlashing);
        mock.StopFlashing();
        Assert.False(mock.IsFlashing);

        mock.ShowSystemNotification("Test", "Mock message");
        Assert.Equal(1, mock.SystemNotificationCount);
        Assert.Equal("Test", mock.LastNotificationTitle);
        Assert.Equal("Mock message", mock.LastNotificationMessage);

        mock.AttachWindow(null);
        mock.DetachWindow();
    }

    [Fact]
    public async Task MainChatViewModel_Notifications_TriggerWhenInactiveOrDifferentChat()
    {
        var notifService = new MockNotificationService();
        var mainVm = CreateMainChatViewModel(notificationService: notifService);
        await mainVm.InitializeAsync();

        var aliceJid = Jid.Parse("alice@test.org");
        var bobJid = Jid.Parse("bob@test.org");

        var convAlice = mainVm.GetOrCreateConversation("alice@test.org", "Alice", aliceJid, isGroupChat: false);
        var convBob = mainVm.GetOrCreateConversation("bob@test.org", "Bob", bobJid, isGroupChat: false);

        // Case 1: Inbound message from Alice arrives while user is on Bob's chat -> System notification triggers and icon flashes
        mainVm.ActiveConversation = convBob;
        mainVm.IsWindowActive = true;
        mainVm.TriggerNotification("alice@test.org", "Alice", "Hey Alice here", isEncrypted: false);

        Assert.Equal(1, notifService.SystemNotificationCount);
        Assert.Equal("Alice", notifService.LastNotificationTitle);
        Assert.Equal("Hey Alice here", notifService.LastNotificationMessage);
        Assert.True(notifService.IsFlashing);

        // Selecting Alice's conversation stops flashing
        mainVm.ActiveConversation = convAlice;
        Assert.False(notifService.IsFlashing);

        // Case 2: Message from Bob arrives while user is on Alice's chat -> System notification triggers and flashing triggers!
        mainVm.TriggerNotification("bob@test.org", "Bob", "Hey this is Bob", isEncrypted: false);

        Assert.Equal(2, notifService.SystemNotificationCount);
        Assert.Equal("Bob", notifService.LastNotificationTitle);
        Assert.Equal("Hey this is Bob", notifService.LastNotificationMessage);
        Assert.True(notifService.IsFlashing);

        // Selecting Bob's conversation stops flashing
        mainVm.ActiveConversation = convBob;
        Assert.False(notifService.IsFlashing);

        // Case 3: Message from Bob arrives while user is on Bob's chat AND window is ACTIVE -> suppressed (actively viewing)
        mainVm.IsWindowActive = true;
        mainVm.TriggerNotification("bob@test.org", "Bob", "You are viewing this chat right now", isEncrypted: false);
        Assert.Equal(2, notifService.SystemNotificationCount);
        Assert.False(notifService.IsFlashing);

        // Case 4: Message from Bob arrives while user is on Bob's chat BUT window is INACTIVE (user in another app) -> triggers!
        mainVm.IsWindowActive = false;
        mainVm.TriggerNotification("bob@test.org", "Bob", "You are in another app right now", isEncrypted: false);
        Assert.Equal(3, notifService.SystemNotificationCount);
        Assert.Equal("You are in another app right now", notifService.LastNotificationMessage);
        Assert.True(notifService.IsFlashing);
        notifService.StopFlashing();

        // Case 5: NotificationPopupsEnabled is turned off in Settings -> No system notifications dispatched
        mainVm.Settings.NotificationPopupsEnabled = false;
        mainVm.TriggerNotification("alice@test.org", "Alice", "You won't see me in popups", isEncrypted: false);
        Assert.Equal(3, notifService.SystemNotificationCount);

        // Case 6: IconFlashingEnabled is turned off in Settings -> No flashing occurs
        mainVm.Settings.IconFlashingEnabled = false;
        mainVm.TriggerNotification("alice@test.org", "Alice", "No flashing either", isEncrypted: false);
        Assert.False(notifService.IsFlashing);
    }

    [Fact]
    public async Task MainChatViewModel_SettingsConfiguration_SynchronizesAndControlsBubbleMerging()
    {
        var mainVm = CreateMainChatViewModel();
        await mainVm.InitializeAsync();

        // Initial default sync
        Assert.True(mainVm.EnableMessageMerging);
        Assert.True(mainVm.Settings.EnableMessageMerging);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, mainVm.MessageMergeThresholdSeconds);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, mainVm.Settings.MessageMergeThresholdSeconds);

        var conv = mainVm.GetOrCreateConversation("peer@test.org", "Peer", Jid.Parse("peer@test.org"), isGroupChat: false);
        Assert.True(conv.EnableMessageMerging);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, conv.MessageMergeThresholdSeconds);

        // Open chat settings via command
        mainVm.OpenChatSettingsCommand.Execute(null);
        Assert.True(mainVm.Settings.IsOpen);

        // Change merge settings via SettingsViewModel
        mainVm.Settings.EnableMessageMerging = false;
        mainVm.Settings.MessageMergeThresholdSeconds = 45;

        // Verify propagation to MainChatViewModel and conversations
        Assert.False(mainVm.EnableMessageMerging);
        Assert.False(conv.EnableMessageMerging);
        Assert.Equal(45, mainVm.MessageMergeThresholdSeconds);
        Assert.Equal(45, conv.MessageMergeThresholdSeconds);

        // Close settings dialog
        mainVm.Settings.Close();
        Assert.False(mainVm.Settings.IsOpen);
    }

    [Fact]
    public async Task SettingsViewModel_FullConfigurabilityAndCallbacks_WorkCorrectly()
    {
        string account = "full_config@test.org";
        bool typographyInvoked = false;
        string lastFont = "";
        double lastSize = 0;
        bool sendOnEnterInvoked = false;
        bool lastSendOnEnter = true;
        bool use24HInvoked = false;
        bool last24H = true;
        bool mediaInvoked = false;
        bool lastShowPreviews = true;
        bool lastAutoDownload = true;
        bool themeInvoked = false;
        string lastTheme = "";
        string lastAccent = "";
        bool emojisInvoked = false;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onTypographyChanged: (font, size) => { typographyInvoked = true; lastFont = font; lastSize = size; },
            onSendOnEnterChanged: send => { sendOnEnterInvoked = true; lastSendOnEnter = send; },
            onUse24HourClockChanged: use24 => { use24HInvoked = true; last24H = use24; },
            onMediaSettingsChanged: (previews, download) => { mediaInvoked = true; lastShowPreviews = previews; lastAutoDownload = download; },
            onThemeChanged: (theme, accent) => { themeInvoked = true; lastTheme = theme; lastAccent = accent; },
            onQuickEmojisChanged: emojis => { emojisInvoked = true; });

        // 1. Check Initial Defaults
        Assert.Equal(SettingsRepository.DefaultFontFamily, vm.FontFamily);
        Assert.Equal(SettingsRepository.DefaultFontSize, vm.FontSize);
        Assert.True(vm.SendOnEnter);
        Assert.True(vm.Use24HourClock);
        Assert.True(vm.ShowInlinePreviews);
        Assert.True(vm.AutoDownloadMedia);
        Assert.Equal(SettingsRepository.DefaultThemeMode, vm.ThemeMode);
        Assert.Equal(SettingsRepository.DefaultAccentColor, vm.AccentColor);
        Assert.Equal(6, vm.QuickEmojis.Count);

        // 2. Tab Navigation
        vm.SelectTab(1);
        Assert.Equal(1, vm.SelectedTabIndex);

        // 3. Theme & Accent changes
        vm.SelectThemeMode("Light");
        Assert.True(themeInvoked);
        Assert.Equal("Light", lastTheme);
        Assert.Equal("Light", await _settingsRepo.GetThemeModeAsync(account));

        vm.SelectAccent("#A855F7");
        Assert.Equal("#A855F7", lastAccent);
        Assert.Equal("#A855F7", await _settingsRepo.GetAccentColorAsync(account));

        // 4. Typography changes
        vm.FontFamily = "Cascadia Code";
        Assert.True(typographyInvoked);
        Assert.Equal("Cascadia Code", lastFont);
        Assert.Equal("Cascadia Code", await _settingsRepo.GetFontFamilyAsync(account));

        vm.FontSize = 15;
        Assert.Equal(15, lastSize);
        Assert.Equal(15, await _settingsRepo.GetFontSizeAsync(account));

        // Custom font family
        vm.FontFamily = "Custom...";
        vm.CustomFontFamily = "Fira Code";
        Assert.True(vm.IsCustomFont);
        Assert.Equal("Fira Code", vm.EffectiveFontFamily);
        Assert.Equal("Fira Code", await _settingsRepo.GetFontFamilyAsync(account));

        // 5. Input keybinding
        vm.SendOnEnter = false;
        Assert.True(sendOnEnterInvoked);
        Assert.False(lastSendOnEnter);
        Assert.False(await _settingsRepo.GetSendOnEnterAsync(account));

        // 6. Clock format
        vm.Use24HourClock = false;
        Assert.True(use24HInvoked);
        Assert.False(last24H);
        Assert.False(await _settingsRepo.GetUse24HourClockAsync(account));

        // 7. Media settings
        vm.ShowInlinePreviews = false;
        Assert.True(mediaInvoked);
        Assert.False(lastShowPreviews);
        Assert.False(await _settingsRepo.GetShowInlinePreviewsAsync(account));

        vm.AutoDownloadMedia = false;
        Assert.False(lastAutoDownload);
        Assert.False(await _settingsRepo.GetAutoDownloadMediaAsync(account));

        // 8. Quick Emojis slot selection and replacement
        vm.SelectEmojiSlot(2);
        Assert.Equal(2, vm.SelectedEmojiSlot);
        Assert.Contains("Editing Slot 3", vm.SelectedSlotLabel);

        await vm.PickEmojiForSlotAsync("🚀");
        Assert.True(emojisInvoked);
        Assert.Equal("🚀", vm.QuickEmojis[2]);
        var savedEmojis = await _settingsRepo.GetQuickEmojisAsync(account);
        Assert.Equal("🚀", savedEmojis[2]);

        // Reset quick emojis
        await vm.ResetQuickEmojisAsync();
        Assert.Equal(SettingsRepository.DefaultQuickEmojis[2], vm.QuickEmojis[2]);

        // 9. Load in a separate ViewModel instance
        var vmLoaded = new SettingsViewModel(_settingsRepo, account);
        await vmLoaded.LoadSettingsAsync();
        Assert.Equal("Custom...", vmLoaded.FontFamily);
        Assert.Equal("Fira Code", vmLoaded.CustomFontFamily);
        Assert.Equal(15, vmLoaded.FontSize);
        Assert.False(vmLoaded.SendOnEnter);
        Assert.False(vmLoaded.Use24HourClock);
        Assert.False(vmLoaded.ShowInlinePreviews);
        Assert.False(vmLoaded.AutoDownloadMedia);
        Assert.Equal("Light", vmLoaded.ThemeMode);
        Assert.Equal("#A855F7", vmLoaded.AccentColor);

        // 10. Reset Defaults
        await vmLoaded.ResetDefaultsAsync();
        Assert.Equal(SettingsRepository.DefaultFontFamily, vmLoaded.FontFamily);
        Assert.Equal(SettingsRepository.DefaultFontSize, vmLoaded.FontSize);
        Assert.True(vmLoaded.SendOnEnter);
        Assert.True(vmLoaded.Use24HourClock);
        Assert.True(vmLoaded.ShowInlinePreviews);
        Assert.True(vmLoaded.AutoDownloadMedia);
        Assert.Equal(SettingsRepository.DefaultThemeMode, vmLoaded.ThemeMode);
        Assert.Equal(SettingsRepository.DefaultAccentColor, vmLoaded.AccentColor);
    }

    [Fact]
    public async Task MainChatViewModel_TypographyAndChatSettings_LiveSynchronization()
    {
        var mainVm = CreateMainChatViewModel();
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation("friend@test.org", "Friend", Jid.Parse("friend@test.org"), isGroupChat: false);
        var msg = new Storage.Models.ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountJid = "user@test.org",
            RemoteJid = "friend@test.org",
            SenderJid = "friend@test.org",
            Body = "Testing https://example.com/image.png",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = Storage.Models.MessageDirection.Inbound
        };
        conv.AddOrUpdateMessage(msg);
        var bubble = conv.Messages.First();

        // 1. Typography synchronization
        mainVm.Settings.FontFamily = "Consolas";
        mainVm.Settings.FontSize = 16;
        Assert.Equal("Consolas", mainVm.ChatFontFamily);
        Assert.Equal(16, mainVm.ChatFontSize);

        // 2. Send keybinding and watermark synchronization
        Assert.True(mainVm.SendOnEnter);
        Assert.Contains("Enter to send", mainVm.MessageInputWatermark);

        mainVm.Settings.SendOnEnter = false;
        Assert.False(mainVm.SendOnEnter);
        Assert.Contains("Ctrl+Enter to send", mainVm.MessageInputWatermark);

        // 3. 24-Hour vs 12-Hour clock
        mainVm.Settings.Use24HourClock = true;
        Assert.True(MessageBubbleViewModel.Use24HourClock);

        mainVm.Settings.Use24HourClock = false;
        Assert.False(MessageBubbleViewModel.Use24HourClock);

        // 4. Media preview toggle
        mainVm.Settings.ShowInlinePreviews = true;
        Assert.True(MessageBubbleViewModel.ShowInlinePreviews);
        Assert.True(bubble.IsPreviewVisible);

        mainVm.Settings.ShowInlinePreviews = false;
        Assert.False(MessageBubbleViewModel.ShowInlinePreviews);
        Assert.False(bubble.IsPreviewVisible);

        // 5. Quick emojis synchronization
        await mainVm.Settings.PickEmojiForSlotAsync("🔥");
        Assert.Equal("🔥", conv.QuickEmojis[0]);
        Assert.Equal("🔥", bubble.EmojiPicker?.QuickEmojis[0]);

        // 6. ChatInputMaxLines live synchronization
        Assert.Equal(SettingsRepository.DefaultChatInputMaxLines, mainVm.ChatInputMaxLines);
        mainVm.Settings.ChatInputMaxLines = 8;
        Assert.Equal(8, mainVm.ChatInputMaxLines);
        mainVm.Settings.ChatInputMaxLines = 2;
        Assert.Equal(2, mainVm.ChatInputMaxLines);
    }

    [Fact]
    public void ThemeManager_ApplyTheme_HandlesAllVariantsAndAccents()
    {
        // Dark theme with Cyan
        ThemeManager.ApplyTheme(ThemeManager.ThemeDark, ThemeManager.AccentCyan);
        Assert.Equal(ThemeManager.ThemeDark, ThemeManager.CurrentThemeMode);
        Assert.Equal(ThemeManager.AccentCyan, ThemeManager.CurrentAccentColor);

        // Light theme with Purple
        ThemeManager.ApplyTheme(ThemeManager.ThemeLight, ThemeManager.AccentPurple);
        Assert.Equal(ThemeManager.ThemeLight, ThemeManager.CurrentThemeMode);
        Assert.Equal(ThemeManager.AccentPurple, ThemeManager.CurrentAccentColor);

        // System theme with Emerald
        ThemeManager.ApplyTheme(ThemeManager.ThemeSystem, ThemeManager.AccentEmerald);
        Assert.Equal(ThemeManager.ThemeSystem, ThemeManager.CurrentThemeMode);
        Assert.Equal(ThemeManager.AccentEmerald, ThemeManager.CurrentAccentColor);

        // Amber accent
        ThemeManager.ApplyTheme(ThemeManager.ThemeDark, ThemeManager.AccentAmber);
        Assert.Equal(ThemeManager.AccentAmber, ThemeManager.CurrentAccentColor);
    }

    [Fact]
    public async Task SettingsViewModel_BubbleColors_DefaultsPersistenceAndPresets_WorkCorrectly()
    {
        string account = "bubble_user@test.org";
        string? callbackOutColor = null;
        string? callbackInColor = null;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onBubbleColorChanged: (outColor, inColor) =>
            {
                callbackOutColor = outColor;
                callbackInColor = inColor;
            });

        // 1. Check default values and flags
        Assert.Equal(SettingsRepository.DefaultOutboundBubbleColor, vm.OutboundBubbleColor);
        Assert.Equal(SettingsRepository.DefaultInboundBubbleColor, vm.InboundBubbleColor);
        Assert.True(vm.IsOutboundBlueSelected);
        Assert.False(vm.IsOutboundVioletSelected);
        Assert.True(vm.IsInboundSlateSelected);
        Assert.False(vm.IsInboundCharcoalSelected);

        // 2. Select presets via commands
        vm.SelectOutboundBubbleColor("#7C3AED");
        Assert.Equal("#7C3AED", vm.OutboundBubbleColor);
        Assert.False(vm.IsOutboundBlueSelected);
        Assert.True(vm.IsOutboundVioletSelected);
        Assert.Equal("#7C3AED", callbackOutColor);
        Assert.Equal(SettingsRepository.DefaultInboundBubbleColor, callbackInColor);
        Assert.Equal("#7C3AED", await _settingsRepo.GetOutboundBubbleColorAsync(account));

        vm.SelectInboundBubbleColor("#14332B");
        Assert.Equal("#14332B", vm.InboundBubbleColor);
        Assert.False(vm.IsInboundSlateSelected);
        Assert.True(vm.IsInboundForestSelected);
        Assert.Equal("#14332B", callbackInColor);
        Assert.Equal("#14332B", await _settingsRepo.GetInboundBubbleColorAsync(account));

        // 3. Match Accent command
        vm.AccentColor = "#00F0FF";
        vm.MatchAccentBubbleColor();
        Assert.Equal("#00F0FF", vm.OutboundBubbleColor);
        Assert.Equal("#00F0FF", callbackOutColor);
        Assert.Equal("#00F0FF", await _settingsRepo.GetOutboundBubbleColorAsync(account));

        // 4. Custom hex input and preview brushes
        vm.OutboundBubbleColor = "#FF0055";
        Assert.False(vm.IsOutboundBlueSelected);
        Assert.NotNull(vm.PreviewOutboundBrush);
        Assert.Equal(Color.Parse("#FF0055"), ((SolidColorBrush)vm.PreviewOutboundBrush).Color);

        vm.InboundBubbleColor = "#002233";
        Assert.False(vm.IsInboundSlateSelected);
        Assert.NotNull(vm.PreviewInboundBrush);
        Assert.Equal(Color.Parse("#002233"), ((SolidColorBrush)vm.PreviewInboundBrush).Color);

        // 5. Loading in a fresh ViewModel retrieves persisted values
        var vm2 = new SettingsViewModel(_settingsRepo, account);
        await vm2.LoadSettingsAsync();
        Assert.Equal("#FF0055", vm2.OutboundBubbleColor);
        Assert.Equal("#002233", vm2.InboundBubbleColor);

        // 6. Reset defaults restores default bubble colors
        await vm2.ResetDefaultsAsync();
        Assert.Equal(SettingsRepository.DefaultOutboundBubbleColor, vm2.OutboundBubbleColor);
        Assert.Equal(SettingsRepository.DefaultInboundBubbleColor, vm2.InboundBubbleColor);
        Assert.True(vm2.IsOutboundBlueSelected);
        Assert.True(vm2.IsInboundSlateSelected);
        Assert.Equal(SettingsRepository.DefaultOutboundBubbleColor, await _settingsRepo.GetOutboundBubbleColorAsync(account));
        Assert.Equal(SettingsRepository.DefaultInboundBubbleColor, await _settingsRepo.GetInboundBubbleColorAsync(account));
    }

    [Fact]
    public async Task MainChatViewModel_BubbleColors_LiveSynchronization()
    {
        var mainVm = CreateMainChatViewModel();
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation("partner@test.org", "Partner", Jid.Parse("partner@test.org"), isGroupChat: false);

        var inboundMsg = new Storage.Models.ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountJid = "user@test.org",
            RemoteJid = "partner@test.org",
            SenderJid = "partner@test.org",
            Body = "Inbound message",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = Storage.Models.MessageDirection.Inbound
        };
        var outboundMsg = new Storage.Models.ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountJid = "user@test.org",
            RemoteJid = "partner@test.org",
            SenderJid = "user@test.org",
            Body = "Outbound message",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = Storage.Models.MessageDirection.Outbound
        };

        conv.AddOrUpdateMessage(inboundMsg);
        conv.AddOrUpdateMessage(outboundMsg);

        var inBubble = conv.Messages.First(m => !m.IsOutbound);
        var outBubble = conv.Messages.First(m => m.IsOutbound);

        // Change outbound and inbound colors in Settings
        mainVm.Settings.OutboundBubbleColor = "#E11D48";
        mainVm.Settings.InboundBubbleColor = "#111827";

        // Verify DirectionToBackgroundConverter brushes updated
        Assert.Equal(Color.Parse("#E11D48"), ((SolidColorBrush)DirectionToBackgroundConverter.OutboundBrush).Color);
        Assert.Equal(Color.Parse("#111827"), ((SolidColorBrush)DirectionToBackgroundConverter.InboundBrush).Color);

        // Verify live bubble instances updated their BubbleBackground property
        Assert.Equal(Color.Parse("#E11D48"), ((SolidColorBrush)outBubble.BubbleBackground).Color);
        Assert.Equal(Color.Parse("#111827"), ((SolidColorBrush)inBubble.BubbleBackground).Color);
    }

    [Fact]
    public async Task SettingsViewModel_LaunchOnStartup_TogglesAndPersistsWithStartupService()
    {
        string account = "autostart_user@test.org";
        var mockStartupService = new MockStartupService();

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            startupService: mockStartupService);

        // 1. Initial state (default false)
        Assert.False(vm.LaunchOnStartup);
        Assert.False(mockStartupService.IsStartupEnabled());

        // 2. Toggle LaunchOnStartup to true
        vm.LaunchOnStartup = true;
        Assert.True(mockStartupService.IsStartupEnabled());
        Assert.True(await _settingsRepo.GetLaunchOnStartupAsync(account));

        // 3. Load in new ViewModel instance
        var mockStartupService2 = new MockStartupService();
        var vm2 = new SettingsViewModel(_settingsRepo, account, startupService: mockStartupService2);
        await vm2.LoadSettingsAsync();

        Assert.True(vm2.LaunchOnStartup);

        // 4. Toggle back to false
        vm2.LaunchOnStartup = false;
        Assert.False(mockStartupService2.IsStartupEnabled());
        Assert.False(await _settingsRepo.GetLaunchOnStartupAsync(account));
    }

    [Fact]
    public void StartupService_FileOperations_WorkCorrectlyWithCustomPath()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"netchatx_autostart_test_{Guid.NewGuid():N}.desktop");
        string tempExe = "/usr/bin/netchatx";

        try
        {
            var service = new StartupService(customExePath: tempExe, customAutostartPath: tempFile);

            // Initially not enabled
            Assert.False(service.IsStartupEnabled());

            // Enable startup creates file
            bool enabledResult = service.SetStartupEnabled(true);
            Assert.True(enabledResult);
            Assert.True(service.IsStartupEnabled());
            Assert.True(File.Exists(tempFile));

            string content = File.ReadAllText(tempFile);
            Assert.Contains("/usr/bin/netchatx", content);

            // Disable startup removes file
            bool disabledResult = service.SetStartupEnabled(false);
            Assert.True(disabledResult);
            Assert.False(service.IsStartupEnabled());
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task SettingsRepository_LastActiveChatAndPresenceMode_PersistenceAndDefaults()
    {
        string account = "session_user@test.org";

        // Defaults
        var defaultChat = await _settingsRepo.GetLastActiveChatAsync(account);
        var defaultMode = await _settingsRepo.GetLastPresenceModeAsync(account);
        var defaultStatus = await _settingsRepo.GetLastStatusMessageAsync(account);

        Assert.Null(defaultChat);
        Assert.Equal(SettingsRepository.DefaultPresenceMode, defaultMode);
        Assert.Equal(SettingsRepository.DefaultStatusMessage, defaultStatus);

        // Update values
        await _settingsRepo.SetLastActiveChatAsync(account, "friend@test.org");
        await _settingsRepo.SetLastPresenceModeAsync(account, "away");
        await _settingsRepo.SetLastStatusMessageAsync(account, "Out for lunch");

        var updatedChat = await _settingsRepo.GetLastActiveChatAsync(account);
        var updatedMode = await _settingsRepo.GetLastPresenceModeAsync(account);
        var updatedStatus = await _settingsRepo.GetLastStatusMessageAsync(account);

        Assert.Equal("friend@test.org", updatedChat);
        Assert.Equal("away", updatedMode);
        Assert.Equal("Out for lunch", updatedStatus);
    }

    [Fact]
    public async Task SettingsRepository_CloseAction_PersistenceAndDefaults()
    {
        string account = "close_user@test.org";

        var defaultAction = await _settingsRepo.GetCloseActionAsync(account);
        Assert.Equal(SettingsRepository.DefaultCloseAction, defaultAction);

        await _settingsRepo.SetCloseActionAsync(account, "Minimize");
        Assert.Equal("Minimize", await _settingsRepo.GetCloseActionAsync(account));

        await _settingsRepo.SetCloseActionAsync(account, "Exit");
        Assert.Equal("Exit", await _settingsRepo.GetCloseActionAsync(account));

        await _settingsRepo.SetCloseActionAsync(account, "Ask");
        Assert.Equal("Ask", await _settingsRepo.GetCloseActionAsync(account));
    }

    [Fact]
    public async Task SettingsViewModel_CloseAction_OptionsPersistenceAndCallbacks()
    {
        string account = "close_vm_user@test.org";
        string? callbackAction = null;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onCloseActionChanged: action => callbackAction = action);

        Assert.Equal("Ask", vm.CloseAction);
        Assert.Contains("Ask", vm.AvailableCloseActionOptions);
        Assert.Contains("Minimize", vm.AvailableCloseActionOptions);
        Assert.Contains("Exit", vm.AvailableCloseActionOptions);

        vm.CloseAction = "Minimize";
        Assert.Equal("Minimize", callbackAction);
        Assert.Equal("Minimize", await _settingsRepo.GetCloseActionAsync(account));

        var vm2 = new SettingsViewModel(_settingsRepo, account);
        await vm2.LoadSettingsAsync();
        Assert.Equal("Minimize", vm2.CloseAction);

        await vm2.ResetDefaultsAsync();
        Assert.Equal("Ask", vm2.CloseAction);
        Assert.Equal("Ask", await _settingsRepo.GetCloseActionAsync(account));
    }

    [Fact]
    public async Task MainChatViewModel_WindowClosingAndPromptChoice_BehavesCorrectly()
    {
        var mainVm = CreateMainChatViewModel();
        await mainVm.InitializeAsync();

        bool hideInvoked = false;
        bool exitInvoked = false;
        bool eventHideInvoked = false;
        bool eventExitInvoked = false;

        mainVm.RequestHideWindow += () => eventHideInvoked = true;
        mainVm.RequestExitApp += () => eventExitInvoked = true;

        // 1. Initial default close action is "Ask"
        Assert.Equal("Ask", mainVm.CloseAction);

        bool allowClose = mainVm.HandleWindowClosing(() => hideInvoked = true, () => exitInvoked = true);
        Assert.False(allowClose);
        Assert.False(hideInvoked);
        Assert.False(exitInvoked);
        Assert.True(mainVm.IsClosePromptOpen);
        Assert.False(mainVm.RememberCloseChoice);

        // Cancel prompt
        mainVm.CancelClosePrompt();
        Assert.False(mainVm.IsClosePromptOpen);

        // 2. Open prompt again and choose "Minimize to Tray" WITHOUT remember choice
        mainVm.HandleWindowClosing(() => { }, () => { });
        Assert.True(mainVm.IsClosePromptOpen);
        mainVm.RememberCloseChoice = false;
        await mainVm.ChooseMinimizeToTrayAsync();

        Assert.False(mainVm.IsClosePromptOpen);
        Assert.True(eventHideInvoked);
        Assert.Equal("Ask", mainVm.CloseAction); // Unchanged since remember was false
        eventHideInvoked = false;

        // 3. Open prompt and choose "Minimize to Tray" WITH remember choice
        mainVm.HandleWindowClosing(() => { }, () => { });
        Assert.True(mainVm.IsClosePromptOpen);
        mainVm.RememberCloseChoice = true;
        await mainVm.ChooseMinimizeToTrayAsync();

        Assert.False(mainVm.IsClosePromptOpen);
        Assert.True(eventHideInvoked);
        Assert.Equal("Minimize", mainVm.CloseAction);
        Assert.Equal("Minimize", mainVm.Settings.CloseAction);
        Assert.Equal("Minimize", await _settingsRepo.GetCloseActionAsync("user@test.org"));
        eventHideInvoked = false;

        // 4. Since CloseAction is now "Minimize", HandleWindowClosing immediately minimizes without prompt
        bool allowCloseMin = mainVm.HandleWindowClosing(() => hideInvoked = true, () => exitInvoked = true);
        Assert.False(allowCloseMin);
        Assert.True(hideInvoked);
        Assert.False(exitInvoked);
        Assert.False(mainVm.IsClosePromptOpen);
        hideInvoked = false;

        // 5. Change CloseAction to "Exit" in Settings
        mainVm.Settings.CloseAction = "Exit";
        Assert.Equal("Exit", mainVm.CloseAction);

        bool allowCloseExit = mainVm.HandleWindowClosing(() => hideInvoked = true, () => exitInvoked = true);
        Assert.True(allowCloseExit);
        Assert.False(hideInvoked);
        Assert.False(exitInvoked);
        Assert.False(mainVm.IsClosePromptOpen);

        // 6. Test ChooseExitAppAsync from dialog
        mainVm.Settings.CloseAction = "Ask";
        mainVm.HandleWindowClosing(() => { }, () => { });
        mainVm.RememberCloseChoice = true;
        await mainVm.ChooseExitAppAsync();

        Assert.False(mainVm.IsClosePromptOpen);
        Assert.True(eventExitInvoked);
        Assert.Equal("Exit", mainVm.CloseAction);
        Assert.Equal("Exit", await _settingsRepo.GetCloseActionAsync("user@test.org"));
    }
}

