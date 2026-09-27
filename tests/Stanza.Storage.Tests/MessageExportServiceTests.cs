using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stanza.Storage.Export;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Storage.Tests;

public sealed class MessageExportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly MessageExportService _exportService;

    public MessageExportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stanza_export_test_{Guid.NewGuid():N}.db");
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
        _exportService = new MessageExportService(_messageRepo);
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
    public async Task ExportConversation_Json_ContainsAllFieldsAndMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = now.AddMinutes(-5),
            Direction = MessageDirection.Inbound,
            Body = "Hello Alice! Check this image https://example.com/photo.png",
            IsEncrypted = true,
            EncryptionType = "OMEMO",
            StanzaId = "s-1",
            OriginId = "o-1"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "alice@example.com",
            Timestamp = now,
            Direction = MessageDirection.Outbound,
            Body = "Hi Bob, looking good!",
            IsEncrypted = true,
            EncryptionType = "OMEMO"
        };

        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        var options = new MessageExportOptions
        {
            Format = MessageExportFormat.Json,
            IncludeMedia = true,
            IncludeMetadata = true
        };

        var json = await _exportService.ExportConversationToStringAsync("alice@example.com", "bob@example.com", options);

        Assert.NotNull(json);
        Assert.Contains("\"client\": \"Stanza\"", json);
        Assert.Contains("\"totalMessages\": 2", json);
        Assert.Contains("Hello Alice!", json);
        Assert.Contains("Hi Bob, looking good!", json);
        Assert.Contains("\"encryptionType\": \"OMEMO\"", json);
        Assert.Contains("https://example.com/photo.png", json);
    }

    [Fact]
    public async Task ExportConversation_PlainText_FormattedCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var msg = new ChatMessage
        {
            Id = "m1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = now,
            Direction = MessageDirection.Inbound,
            Body = "Meeting at 3pm.",
            IsEncrypted = true,
            EncryptionType = "OMEMO"
        };
        await _messageRepo.SaveMessagesAsync([msg]);

        var options = new MessageExportOptions
        {
            Format = MessageExportFormat.PlainText,
            IncludeMetadata = true
        };

        var text = await _exportService.ExportConversationToStringAsync("alice@example.com", "bob@example.com", options);

        Assert.Contains("Stanza Message History Export", text);
        Assert.Contains("Account: alice@example.com", text);
        Assert.Contains("Conversation: bob@example.com", text);
        Assert.Contains("<bob@example.com> [🔒 OMEMO]:", text);
        Assert.Contains("Meeting at 3pm.", text);
    }

    [Fact]
    public async Task ExportConversation_Html_ContainsStyledChatAndEscapedContent()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = now.AddMinutes(-2),
            Direction = MessageDirection.Inbound,
            Body = "Look at this <script>alert('xss')</script> & cool photo: https://example.com/image.jpg",
            IsEncrypted = true,
            EncryptionType = "OMEMO"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "alice@example.com",
            Timestamp = now,
            Direction = MessageDirection.Outbound,
            Body = "Got it!",
            IsEncrypted = false
        };
        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        var options = new MessageExportOptions
        {
            Format = MessageExportFormat.Html,
            IncludeMedia = true,
            IncludeMetadata = true
        };

        var html = await _exportService.ExportConversationToStringAsync("alice@example.com", "bob@example.com", options);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Chat with bob@example.com", html);
        // Ensure XSS characters are escaped properly
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp; cool photo", html);
        // Media preview image tag
        Assert.Contains("<img src=\"https://example.com/image.jpg\" class=\"media-preview\"", html);
        // Badges and styles
        Assert.Contains("🔒 OMEMO", html);
        Assert.Contains("outbound", html);
        Assert.Contains("inbound", html);
    }

    [Fact]
    public async Task ExportConversation_DateFilter_FiltersCorrectly()
    {
        var baseTime = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = baseTime,
            Direction = MessageDirection.Inbound,
            Body = "Day 1"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = baseTime.AddDays(2),
            Direction = MessageDirection.Inbound,
            Body = "Day 3"
        };
        var msg3 = new ChatMessage
        {
            Id = "m3",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = baseTime.AddDays(4),
            Direction = MessageDirection.Inbound,
            Body = "Day 5"
        };

        await _messageRepo.SaveMessagesAsync([msg1, msg2, msg3]);

        var options = new MessageExportOptions
        {
            Format = MessageExportFormat.PlainText,
            FromDate = baseTime.AddDays(1),
            ToDate = baseTime.AddDays(3)
        };

        var text = await _exportService.ExportConversationToStringAsync("alice@example.com", "bob@example.com", options);

        Assert.DoesNotContain("Day 1", text);
        Assert.Contains("Day 3", text);
        Assert.DoesNotContain("Day 5", text);
        Assert.Contains("Total Messages: 1", text);
    }

    [Fact]
    public async Task ExportAllMessages_ExportsMultipleConversations()
    {
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            Body = "From Bob"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "alice@example.com",
            RemoteJid = "carol@example.com",
            SenderJid = "carol@example.com",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            Body = "From Carol"
        };

        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        var options = new MessageExportOptions { Format = MessageExportFormat.Json };
        var json = await _exportService.ExportAllMessagesToStringAsync(options);

        Assert.Contains("From Bob", json);
        Assert.Contains("From Carol", json);
        Assert.Contains("\"totalMessages\": 2", json);
    }

    [Fact]
    public async Task ImportBackup_RoundTrip_RestoresMessagesSuccessfully()
    {
        var msg1 = new ChatMessage
        {
            Id = "orig-1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            Direction = MessageDirection.Inbound,
            Body = "Message to be backed up and restored",
            IsEncrypted = true,
            EncryptionType = "OMEMO",
            StanzaId = "st-99"
        };

        await _messageRepo.SaveMessagesAsync([msg1]);

        // 1. Export
        var exportJson = await _exportService.ExportConversationToStringAsync(
            "alice@example.com",
            "bob@example.com",
            new MessageExportOptions { Format = MessageExportFormat.Json });

        // 2. Clear database / create new fresh database
        var targetDbPath = Path.Combine(Path.GetTempPath(), $"stanza_restore_target_{Guid.NewGuid():N}.db");
        using var targetDbContext = new DatabaseContext(targetDbPath);
        var targetRepo = new MessageRepository(targetDbContext);
        var targetService = new MessageExportService(targetRepo);

        try
        {
            // Verify empty
            var existing = await targetRepo.GetAllMessagesAsync();
            Assert.Empty(existing);

            // 3. Import
            var importResult = await targetService.ImportBackupFromJsonAsync(exportJson);

            Assert.True(importResult.IsSuccess);
            Assert.Equal(1, importResult.TotalInBackup);
            Assert.Equal(1, importResult.ImportedCount);
            Assert.Equal(0, importResult.SkippedCount);

            // Verify in database
            var restored = await targetRepo.GetAllMessagesAsync();
            Assert.Single(restored);
            Assert.Equal("orig-1", restored[0].Id);
            Assert.Equal("Message to be backed up and restored", restored[0].Body);
            Assert.True(restored[0].IsEncrypted);
            Assert.Equal("OMEMO", restored[0].EncryptionType);
            Assert.Equal("st-99", restored[0].StanzaId);
        }
        finally
        {
            targetDbContext.Dispose();
            if (File.Exists(targetDbPath))
            {
                try { File.Delete(targetDbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ImportBackup_Deduplication_DoesNotDuplicateExisting()
    {
        var msg = new ChatMessage
        {
            Id = "dedup-1",
            AccountJid = "alice@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            Body = "Duplicate check message"
        };

        await _messageRepo.SaveMessagesAsync([msg]);

        var exportJson = await _exportService.ExportConversationToStringAsync(
            "alice@example.com",
            "bob@example.com",
            new MessageExportOptions { Format = MessageExportFormat.Json });

        // Re-importing into the same repo
        var result = await _exportService.ImportBackupFromJsonAsync(exportJson);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ImportedCount);

        var allMessages = await _messageRepo.GetAllMessagesAsync();
        Assert.Single(allMessages); // Exactly 1 message, no duplicates!
    }

    [Fact]
    public async Task ImportBackup_InvalidJson_ReturnsError()
    {
        var result = await _exportService.ImportBackupFromJsonAsync("this is not valid json {{{");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }
}
