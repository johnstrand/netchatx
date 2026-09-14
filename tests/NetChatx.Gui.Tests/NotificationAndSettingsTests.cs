using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Transport;
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

        // Loading from SQLite in a new SettingsViewModel retrieves the persisted values
        var vm2 = new SettingsViewModel(_settingsRepo, account);
        await vm2.LoadSettingsAsync();
        Assert.False(vm2.NotificationPopupsEnabled);
        Assert.False(vm2.IconFlashingEnabled);
        Assert.False(vm2.EnableMessageMerging);
        Assert.Equal(25, vm2.MessageMergeThresholdSeconds);

        // Reset defaults
        await vm2.ResetDefaultsAsync();
        Assert.True(vm2.NotificationPopupsEnabled);
        Assert.True(vm2.IconFlashingEnabled);
        Assert.True(vm2.EnableMessageMerging);
        Assert.Equal(SettingsRepository.DefaultMergeMessagesThresholdSeconds, vm2.MessageMergeThresholdSeconds);
        Assert.True(await _settingsRepo.GetNotificationPopupsEnabledAsync(account));
        Assert.True(await _settingsRepo.GetIconFlashingEnabledAsync(account));
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
}
