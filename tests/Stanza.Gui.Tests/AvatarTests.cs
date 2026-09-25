using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Helpers;
using Stanza.Gui.Tests.Mocks;
using Stanza.Gui.ViewModels;
using Stanza.Protocol.Xeps.Avatars;
using Stanza.Storage;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class AvatarTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly AvatarRepository _avatarRepo;

    public AvatarTests()
    {
        _dbPath = $"test_avatar_gui_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _avatarRepo = new AvatarRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private MainChatViewModel CreateMainChatViewModel(string account = "user@test.org")
    {
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        return new MainChatViewModel(client, _dbContext, () => Task.CompletedTask, new MockNotificationService());
    }

    [Theory]
    [InlineData("Alice Wonderland", null, "AW")]
    [InlineData("Alice", null, "Al")]
    [InlineData("", "alice@example.com", "Al")]
    [InlineData(null, "alice.bob@example.com", "AB")]
    [InlineData("John H. Watson", null, "JH")]
    [InlineData(null, "single@example.com", "Si")]
    [InlineData("", "", "?")]
    [InlineData(null, null, "?")]
    [InlineData("   ", "   ", "?")]
    public void AvatarHelper_GetInitials_ComputesExpectedInitials(string? displayName, string? jid, string expected)
    {
        var initials = AvatarHelper.GetInitials(displayName, jid);
        Assert.Equal(expected, initials);
    }

    [Fact]
    public void AvatarHelper_GetAvatarColorBrush_ReturnsConsistentBrush()
    {
        var brush1 = AvatarHelper.GetAvatarColorBrush("alice@example.com");
        var brush2 = AvatarHelper.GetAvatarColorBrush("alice@example.com");
        var brushOther = AvatarHelper.GetAvatarColorBrush("bob@example.com");

        Assert.NotNull(brush1);
        Assert.NotNull(brush2);
        Assert.IsType<SolidColorBrush>(brush1);
        Assert.Equal(((SolidColorBrush)brush1).Color, ((SolidColorBrush)brush2).Color);
    }

    [Fact]
    public void AvatarHelper_ComputeSha1_ComputesValidSha1Hex()
    {
        var hash = AvatarHelper.ComputeSha1(SamplePngBytes);
        using var sha1 = SHA1.Create();
        var expected = Convert.ToHexString(sha1.ComputeHash(SamplePngBytes)).ToLowerInvariant();

        Assert.Equal(expected, hash);
    }

    [Fact]
    public void AvatarHelper_ComputeSha1_EmptyOrNullReturnsEmpty()
    {
        Assert.Equal(string.Empty, AvatarHelper.ComputeSha1(null!));
        Assert.Equal(string.Empty, AvatarHelper.ComputeSha1([]));
    }

    [AvaloniaFact]
    public void AvatarHelper_CreateBitmapFromBytes_CreatesValidBitmap()
    {
        var bitmap = AvatarHelper.CreateBitmapFromBytes(SamplePngBytes);
        Assert.NotNull(bitmap);

        Assert.Null(AvatarHelper.CreateBitmapFromBytes(null));
        Assert.Null(AvatarHelper.CreateBitmapFromBytes([]));
    }

    [AvaloniaFact]
    public void ContactItemViewModel_AvatarProperties_WorkCorrectly()
    {
        var contact = new ContactItemViewModel
        {
            ContactJid = "alice@example.com",
            Name = "Alice Wonderland",
            PresenceShow = "available"
        };

        Assert.Equal("AW", contact.Initials);
        Assert.False(contact.HasAvatar);
        Assert.Null(contact.Avatar);
        Assert.Null(contact.AvatarHash);
        Assert.NotNull(contact.AvatarBackgroundBrush);

        var bitmap = AvatarHelper.CreateBitmapFromBytes(SamplePngBytes);
        contact.Avatar = bitmap;
        contact.AvatarHash = "hash123";

        Assert.True(contact.HasAvatar);
        Assert.Equal(bitmap, contact.Avatar);
        Assert.Equal("hash123", contact.AvatarHash);
    }

    [AvaloniaFact]
    public void ChatConversationViewModel_AvatarProperties_WorkCorrectly()
    {
        var remote = Jid.Parse("bob@example.com");
        var conv = new ChatConversationViewModel(
            "user@example.com",
            remote.ToString(),
            "Bob Builder",
            remote,
            isGroupChat: false,
            messageRepo: new MessageRepository(_dbContext)
        );

        Assert.Equal("BB", conv.Initials);
        Assert.False(conv.HasAvatar);
        Assert.Null(conv.Avatar);
        Assert.Null(conv.AvatarHash);
        Assert.NotNull(conv.AvatarBackgroundBrush);

        var bitmap = AvatarHelper.CreateBitmapFromBytes(SamplePngBytes);
        conv.Avatar = bitmap;
        conv.AvatarHash = "hash456";

        Assert.True(conv.HasAvatar);
        Assert.Equal(bitmap, conv.Avatar);
        Assert.Equal("hash456", conv.AvatarHash);
    }

    [AvaloniaFact]
    public async Task SettingsViewModel_ProfileAvatarMethods_WorkCorrectly()
    {
        byte[]? changedBytes = null;
        string? changedMime = null;
        var removeInvoked = false;

        var vm = new SettingsViewModel(
            new SettingsRepository(_dbContext),
            "alice@example.com",
            onAvatarChanged: (bytes, mime) => { changedBytes = bytes; changedMime = mime; return Task.CompletedTask; },
            onAvatarRemoved: () => { removeInvoked = true; return Task.CompletedTask; }
        );

        Assert.Equal("alice@example.com", vm.AccountJid);
        Assert.Equal("Al", vm.UserInitials);
        Assert.False(vm.HasUserAvatar);
        Assert.Null(vm.UserAvatar);

        // Select Tab 4 (Profile)
        vm.SelectTab(4);
        Assert.Equal(4, vm.SelectedTabIndex);

        // Set avatar bytes
        await vm.SetAvatarBytesAsync(SamplePngBytes, "image/png");

        Assert.True(vm.HasUserAvatar);
        Assert.NotNull(vm.UserAvatar);
        Assert.NotNull(vm.UserAvatarHash);
        Assert.NotNull(changedBytes);
        Assert.Equal("image/png", changedMime);
        Assert.Equal("Avatar updated successfully!", vm.AvatarStatusMessage);

        // Remove avatar
        await vm.RemoveAvatarAsync();

        Assert.False(vm.HasUserAvatar);
        Assert.Null(vm.UserAvatar);
        Assert.Null(vm.UserAvatarHash);
        Assert.True(removeInvoked);
        Assert.Equal("Avatar removed.", vm.AvatarStatusMessage);
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_SetUserAvatarAndRemove_PersistsAndUpdatesUI()
    {
        var vm = CreateMainChatViewModel("user@example.com");

        Assert.False(vm.HasUserAvatar);
        Assert.Null(vm.UserAvatar);

        // Set user avatar
        await vm.SetUserAvatarFromBytesAsync(SamplePngBytes, "image/png");

        Assert.True(vm.HasUserAvatar);
        Assert.NotNull(vm.UserAvatar);
        Assert.NotNull(vm.UserAvatarHash);

        // Verify persisted in repository
        var saved = await _avatarRepo.GetAvatarAsync("user@example.com");
        Assert.NotNull(saved);
        Assert.Equal("image/png", saved.MimeType);
        Assert.Equal(SamplePngBytes.Length, saved.Data.Length);

        // Remove user avatar
        await vm.RemoveUserAvatarAsync();

        Assert.False(vm.HasUserAvatar);
        Assert.Null(vm.UserAvatar);
        Assert.Null(vm.UserAvatarHash);

        var afterDelete = await _avatarRepo.GetAvatarAsync("user@example.com");
        Assert.Null(afterDelete);
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_PreExistingAvatar_LoadedOnInit()
    {
        // Pre-save avatar in repo
        var hash = AvatarHelper.ComputeSha1(SamplePngBytes);
        await _avatarRepo.SaveAvatarAsync("existing@example.com", hash, "image/png", SamplePngBytes);

        var vm = CreateMainChatViewModel("existing@example.com");
        await vm.InitializeAsync();

        Assert.True(vm.HasUserAvatar);
        Assert.NotNull(vm.UserAvatar);
        Assert.NotNull(vm.UserAvatarHash);
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_IncomingAvatarUpdate_AppliesToContactAndConversation()
    {
        var vm = CreateMainChatViewModel("user@example.com");
        await vm.InitializeAsync();

        var contact = new ContactItemViewModel
        {
            ContactJid = "peer@example.com",
            Name = "Peer Name",
            PresenceShow = "available"
        };
        vm.Contacts.Add(contact);

        var conv = vm.GetOrCreateConversation("peer@example.com", "Peer Name", Jid.Parse("peer@example.com"), false);

        Assert.False(contact.HasAvatar);
        Assert.False(conv.HasAvatar);

        // Simulate incoming avatar update
        var hash = AvatarHelper.ComputeSha1(SamplePngBytes);
        await vm.HandleAvatarUpdatedAsync(new AvatarChangedEventArgs
        {
            Jid = Jid.Parse("peer@example.com"),
            Hash = hash,
            Data = SamplePngBytes,
            MimeType = "image/png"
        });

        Assert.True(contact.HasAvatar);
        Assert.Equal(hash, contact.AvatarHash);
        Assert.NotNull(contact.Avatar);

        Assert.True(conv.HasAvatar);
        Assert.Equal(hash, conv.AvatarHash);
        Assert.NotNull(conv.Avatar);
    }

    [AvaloniaFact]
    public async Task SettingsViewModel_SyncAvatarCommand_InvokesCallbackAndUpdatesStatus()
    {
        var syncInvoked = false;
        var vm = new SettingsViewModel(
            new SettingsRepository(_dbContext),
            "alice@example.com",
            onAvatarSyncRequested: () => { syncInvoked = true; return Task.CompletedTask; }
        );

        await vm.SyncAvatarAsync();

        Assert.True(syncInvoked);
        Assert.Equal("No avatar found on server.", vm.AvatarStatusMessage);

        // When HasUserAvatar is true after sync
        await vm.SetAvatarBytesAsync(SamplePngBytes, "image/png");
        await vm.SyncAvatarAsync();
        Assert.Equal("Avatar synced from server!", vm.AvatarStatusMessage);
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_HandleAvatarUpdatedAsync_OwnJid_UpdatesUserAvatar()
    {
        var vm = CreateMainChatViewModel("user@example.com");
        await vm.InitializeAsync();

        Assert.False(vm.HasUserAvatar);

        var hash = AvatarHelper.ComputeSha1(SamplePngBytes);
        await vm.HandleAvatarUpdatedAsync(new AvatarChangedEventArgs
        {
            Jid = Jid.Parse("user@example.com"),
            Hash = hash,
            Data = SamplePngBytes,
            MimeType = "image/png"
        });

        Assert.True(vm.HasUserAvatar);
        Assert.Equal(hash, vm.UserAvatarHash);
        Assert.NotNull(vm.UserAvatar);
        Assert.True(vm.Settings.HasUserAvatar);
        Assert.Equal(hash, vm.Settings.UserAvatarHash);
    }

    [AvaloniaFact]
    public void SettingsView_Initializes_WithStandardDimensionsAndProfileButtonLayout()
    {
        var view = new Stanza.Gui.Views.SettingsView();
        Assert.NotNull(view);
        var border = Assert.IsType<Avalonia.Controls.Border>(view.Content);
        Assert.Equal(560.0, border.Width);
        Assert.Equal(580.0, border.Height);

        var chooseBtn = view.FindControl<Avalonia.Controls.Button>("ChooseAvatarButton");
        Assert.NotNull(chooseBtn);
        Assert.IsType<Avalonia.Controls.WrapPanel>(chooseBtn.Parent);
    }
}
