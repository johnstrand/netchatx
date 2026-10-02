using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Gui.ViewModels;
using Stanza.Protocol.Xeps.Privacy;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class BlockingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly SettingsRepository _settingsRepo;

    public BlockingTests()
    {
        _dbPath = $"testblocking_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
        _settingsRepo = new SettingsRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void ContactItemViewModel_IsBlocked_DefaultFalse_CanBeToggled()
    {
        var vm = new ContactItemViewModel
        {
            ContactJid = "contact@example.com",
            Name = "Contact"
        };

        Assert.False(vm.IsBlocked);

        bool changed = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ContactItemViewModel.IsBlocked))
            {
                changed = true;
            }
        };

        vm.IsBlocked = true;
        Assert.True(vm.IsBlocked);
        Assert.True(changed);
    }

    [Fact]
    public void ChatConversationViewModel_IsBlocked_DefaultFalse_CanBeToggled()
    {
        var remote = Jid.Parse("contact@example.com");
        var vm = new ChatConversationViewModel(
            "user@example.com",
            "test_id",
            "Test Title",
            remote,
            isGroupChat: false,
            _messageRepo);

        Assert.False(vm.IsBlocked);

        bool changed = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatConversationViewModel.IsBlocked))
            {
                changed = true;
            }
        };

        vm.IsBlocked = true;
        Assert.True(vm.IsBlocked);
        Assert.True(changed);
    }

    [Fact]
    public void SettingsViewModel_PrivacyTab_PropertiesAndCommands_Initialized()
    {
        var settingsVm = new SettingsViewModel(_settingsRepo, "user@example.com");
        Assert.Empty(settingsVm.BlockedContacts);
        Assert.NotNull(settingsVm.BlockNewContactCommand);
        Assert.NotNull(settingsVm.UnblockContactCommand);
        Assert.NotNull(settingsVm.UnblockAllContactsCommand);
        Assert.NotNull(settingsVm.RefreshBlockedContactsCommand);
    }
}
