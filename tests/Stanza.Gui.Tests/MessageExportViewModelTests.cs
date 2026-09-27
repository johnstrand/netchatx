using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Export;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class MessageExportViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly SettingsRepository _settingsRepo;

    public MessageExportViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stanza_vm_export_test_{Guid.NewGuid():N}.db");
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
    public async Task ChatConversationViewModel_RequestExportChat_TriggersCallback()
    {
        var vm = new ChatConversationViewModel(
            "alice@example.com",
            "bob@example.com",
            "Bob",
            Jid.Parse("bob@example.com"),
            false,
            _messageRepo);

        var callbackTriggered = false;
        vm.RequestExportChatCallback = () =>
        {
            callbackTriggered = true;
            return Task.CompletedTask;
        };

        await vm.RequestExportChatCommand.ExecuteAsync(null);

        Assert.True(callbackTriggered);
    }

    [Fact]
    public async Task ChatConversationViewModel_ExportChatAsync_ExportsMessages()
    {
        var accountJid = "alice@example.com";
        var remoteJid = "bob@example.com";

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg-1",
            AccountJid = accountJid,
            RemoteJid = remoteJid,
            SenderJid = accountJid,
            Body = "Hello from Bob test",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Outbound
        });

        var vm = new ChatConversationViewModel(
            accountJid,
            remoteJid,
            "Bob",
            Jid.Parse(remoteJid),
            false,
            _messageRepo);

        using var ms = new MemoryStream();
        await vm.ExportChatAsync(ms, MessageExportFormat.PlainText);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Hello from Bob test", text);
        Assert.Contains(remoteJid, text);
    }

    [Fact]
    public async Task SettingsViewModel_RequestExportAll_TriggersCallback()
    {
        var vm = new SettingsViewModel(_settingsRepo, "alice@example.com", messageRepo: _messageRepo);
        var callbackTriggered = false;
        vm.RequestExportAllCallback = () =>
        {
            callbackTriggered = true;
            return Task.CompletedTask;
        };

        await vm.RequestExportAllCommand.ExecuteAsync(null);

        Assert.True(callbackTriggered);
    }

    [Fact]
    public async Task SettingsViewModel_RequestImportBackup_TriggersCallback()
    {
        var vm = new SettingsViewModel(_settingsRepo, "alice@example.com", messageRepo: _messageRepo);
        var callbackTriggered = false;
        vm.RequestImportBackupCallback = () =>
        {
            callbackTriggered = true;
            return Task.CompletedTask;
        };

        await vm.RequestImportBackupCommand.ExecuteAsync(null);

        Assert.True(callbackTriggered);
    }

    [Fact]
    public async Task SettingsViewModel_ExportAllMessagesAsync_SucceedsAndSetsStatus()
    {
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg-setting-1",
            AccountJid = "user@example.com",
            RemoteJid = "friend@example.com",
            SenderJid = "user@example.com",
            Body = "Global backup message",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Outbound
        });

        var vm = new SettingsViewModel(_settingsRepo, "user@example.com", messageRepo: _messageRepo);
        using var ms = new MemoryStream();

        await vm.ExportAllMessagesAsync(ms, MessageExportFormat.Json);

        Assert.True(ms.Length > 0);
        Assert.NotNull(vm.BackupStatusMessage);
        Assert.NotEmpty(vm.BackupStatusMessage);
    }

    [Fact]
    public async Task SettingsViewModel_ImportBackupAsync_RestoresMessagesAndSetsStatus()
    {
        var msg = new ChatMessage
        {
            Id = "msg-setting-2",
            AccountJid = "user@example.com",
            RemoteJid = "friend@example.com",
            SenderJid = "friend@example.com",
            Body = "Export then import me",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        };
        await _messageRepo.SaveMessageAsync(msg);

        var vm = new SettingsViewModel(_settingsRepo, "user@example.com", messageRepo: _messageRepo);
        using var exportStream = new MemoryStream();
        await vm.ExportAllMessagesAsync(exportStream, MessageExportFormat.Json);

        exportStream.Position = 0;

        // Clear or create a fresh DB to test restoration
        var newDbPath = Path.Combine(Path.GetTempPath(), $"stanza_restore_test_{Guid.NewGuid():N}.db");
        using var newDbContext = new DatabaseContext(newDbPath);
        var newMessageRepo = new MessageRepository(newDbContext);
        var newSettingsRepo = new SettingsRepository(newDbContext);
        var newVm = new SettingsViewModel(newSettingsRepo, "user@example.com", messageRepo: newMessageRepo);

        try
        {
            var result = await newVm.ImportBackupAsync(exportStream);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ImportedCount);
            Assert.NotNull(newVm.BackupStatusMessage);

            var messages = await newMessageRepo.GetAllMessagesForConversationAsync("user@example.com", "friend@example.com");
            var restored = messages.FirstOrDefault(m => m.Id == "msg-setting-2");
            Assert.NotNull(restored);
            Assert.Equal("Export then import me", restored.Body);
        }
        finally
        {
            if (File.Exists(newDbPath))
            {
                try { File.Delete(newDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task SettingsViewModel_ImportBackupAsync_InvalidStream_ReportsFailure()
    {
        var vm = new SettingsViewModel(_settingsRepo, "user@example.com", messageRepo: _messageRepo);
        using var invalidStream = new MemoryStream(Encoding.UTF8.GetBytes("{ not valid json !!! }"));

        var result = await vm.ImportBackupAsync(invalidStream);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(vm.BackupStatusMessage);
    }

    [Fact]
    public async Task SettingsViewModel_ExportAndImport_WithoutMessageRepo_SetsErrorMessage()
    {
        var vm = new SettingsViewModel(_settingsRepo, "user@example.com", messageRepo: null);

        using var ms = new MemoryStream();
        await vm.ExportAllMessagesAsync(ms, MessageExportFormat.Json);
        Assert.Equal("Message repository not available.", vm.BackupStatusMessage);

        ms.Position = 0;
        var result = await vm.ImportBackupAsync(ms);
        Assert.False(result.IsSuccess);
        Assert.Equal("Message repository not available.", vm.BackupStatusMessage);
    }
}
