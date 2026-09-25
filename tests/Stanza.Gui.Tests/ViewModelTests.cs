using System;
using System.IO;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class ViewModelTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly AccountRepository _accountRepo;
    private readonly OmemoRepository _omemoRepo;

    public ViewModelTests()
    {
        _dbPath = $"testgui_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);
    }

    [Fact]
    public async Task ChatConversationViewModel_ToggleReaction_UpdatesUIAndDatabase()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var msg = new ChatMessage
        {
            Id = "msg_react_vm_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Reaction test bubble",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            StanzaId = "s_react_1"
        };
        await _messageRepo.SaveMessageAsync(msg);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        await conv.LoadHistoryAsync();
        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.Empty(bubble.Reactions);

        // Toggle 👍 reaction by me
        await conv.ToggleReactionAsync(bubble, "👍");

        Assert.Single(bubble.Reactions);
        Assert.Equal("👍", bubble.Reactions[0].Emoji);
        Assert.Equal(1, bubble.Reactions[0].Count);
        Assert.True(bubble.Reactions[0].IsReactedByMe);

        // Toggle 👍 again to remove
        await conv.ToggleReactionAsync(bubble, "👍");
        Assert.Empty(bubble.Reactions);
    }

    [Fact]
    public async Task ChatConversationViewModel_RemoteAndCarbonReactions_SyncsCorrectly()
    {
        var account = "me@test.org";
        var remote = Jid.Parse("peer@test.org");

        var msg = new ChatMessage
        {
            Id = "msg_sync_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Sync test message",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound,
            StanzaId = "s_sync_1"
        };
        await _messageRepo.SaveMessageAsync(msg);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        await conv.LoadHistoryAsync();
        var bubble = conv.Messages[0];

        // 1. Inbound remote reaction from peer@test.org/mobile
        await conv.HandleIncomingReactionAsync(new ReactionEventArgs
        {
            TargetMessageId = "s_sync_1",
            SenderJid = Jid.Parse("peer@test.org/mobile"),
            RemoteJid = remote,
            Emojis = ["🎉"],
            IsCarbonSent = false
        });

        Assert.Single(bubble.Reactions);
        Assert.Equal("🎉", bubble.Reactions[0].Emoji);
        Assert.Equal(1, bubble.Reactions[0].Count);
        Assert.False(bubble.Reactions[0].IsReactedByMe);

        // 2. Sent carbon copy from our other device me@test.org/phone
        await conv.HandleIncomingReactionAsync(new ReactionEventArgs
        {
            TargetMessageId = "s_sync_1",
            SenderJid = Jid.Parse("me@test.org/phone"),
            RemoteJid = remote,
            Emojis = ["❤️"],
            IsCarbonSent = true
        });

        Assert.Equal(2, bubble.Reactions.Count);
        var peerReact = bubble.Reactions.First(r => r.Emoji == "🎉");
        var myReact = bubble.Reactions.First(r => r.Emoji == "❤️");

        Assert.False(peerReact.IsReactedByMe);
        Assert.True(myReact.IsReactedByMe);
    }

    [Fact]
    public async Task EmojiPickerViewModel_QuickEmojisAndCustomization_Works()
    {
        var account = "user@test.org";
        var settingsRepo = new SettingsRepository(_dbContext);
        string? reactedEmoji = null;

        var picker = new EmojiPickerViewModel(
            account,
            ["👍", "❤️"],
            emoji =>
            {
                reactedEmoji = emoji;
                return Task.CompletedTask;
            },
            settingsRepo);

        // 1. Initial quick emojis
        Assert.Equal(2, picker.QuickEmojis.Count);
        Assert.Contains("👍", picker.QuickEmojis);
        Assert.Contains("❤️", picker.QuickEmojis);

        // 2. Select emoji triggers callback
        await picker.SelectEmojiAsync("👍");
        Assert.Equal("👍", reactedEmoji);

        // 3. Search emojis
        picker.SearchQuery = "fire";
        Assert.Contains("🔥", picker.DisplayedEmojis);

        // 4. Pin new emoji to quick emojis
        await picker.PinToQuickEmojisAsync("🔥");
        Assert.Equal(3, picker.QuickEmojis.Count);
        Assert.Contains("🔥", picker.QuickEmojis);

        // Verify persistence in SQLite
        var saved = await settingsRepo.GetQuickEmojisAsync(account);
        Assert.Equal(3, saved.Count);
        Assert.Contains("🔥", saved);

        // 5. Remove emoji from quick emojis
        await picker.RemoveFromQuickEmojisAsync("👍");
        Assert.Equal(2, picker.QuickEmojis.Count);
        Assert.DoesNotContain("👍", picker.QuickEmojis);
    }

    [Fact]
    public async Task LoginViewModel_Validation_FailsOnEmptyOrInvalidInputs()
    {
        AccountProfile? capturedProfile = null;
        var vm = new LoginViewModel(profile =>
        {
            capturedProfile = profile;
            return Task.FromResult(true);
        });

        // 1. Empty JID
        await vm.ConnectAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("JID", vm.ErrorMessage);
        Assert.Null(capturedProfile);

        // 2. Invalid JID format
        vm.Jid = "invalid@@jid@@format";
        await vm.ConnectAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Invalid JID", vm.ErrorMessage);
        Assert.Null(capturedProfile);

        // 3. Missing password
        vm.Jid = "alice@example.com";
        vm.Password = "";
        await vm.ConnectAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("password", vm.ErrorMessage);
        Assert.Null(capturedProfile);

        // 4. Valid inputs
        vm.Password = "secret123";
        vm.Host = "xmpp.example.com";
        vm.Port = "5222";
        vm.UseDirectTls = true;
        vm.AllowUntrustedCertificates = true;
        await vm.ConnectAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(capturedProfile);
        Assert.Equal("alice@example.com", capturedProfile.Jid);
        Assert.Equal("secret123", capturedProfile.Password);
        Assert.Equal("xmpp.example.com", capturedProfile.Host);
        Assert.Equal(5222, capturedProfile.Port);
        Assert.True(capturedProfile.UseDirectTls);
        Assert.True(capturedProfile.AllowUntrustedCertificates);
    }

    [Fact]
    public void MessageBubbleViewModel_FormattingAndReceipts_WorkCorrectly()
    {
        var inboundMsg = new ChatMessage
        {
            AccountJid = "me@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "bob@example.com",
            Body = "Hello there!",
            Direction = MessageDirection.Inbound,
            Timestamp = new DateTimeOffset(2026, 9, 9, 14, 30, 0, TimeSpan.Zero),
            IsEncrypted = true,
            EncryptionType = "OMEMO"
        };

        var bubbleIn = MessageBubbleViewModel.FromChatMessage(inboundMsg);
        Assert.Equal("bob@example.com", bubbleIn.SenderName);
        Assert.Equal("Hello there!", bubbleIn.Body);
        Assert.True(bubbleIn.IsEncrypted);
        Assert.Equal("OMEMO", bubbleIn.EncryptionType);
        Assert.Empty(bubbleIn.ReceiptIcon); // Inbound doesn't have receipt icons

        var outboundMsg = new ChatMessage
        {
            AccountJid = "me@example.com",
            RemoteJid = "bob@example.com",
            SenderJid = "me@example.com",
            Body = "General Kenobi!",
            Direction = MessageDirection.Outbound,
            Timestamp = new DateTimeOffset(2026, 9, 9, 14, 31, 0, TimeSpan.Zero),
            IsRead = true
        };

        var bubbleOut = MessageBubbleViewModel.FromChatMessage(outboundMsg);
        Assert.Equal("Me", bubbleOut.SenderName);
        Assert.Equal("◈", bubbleOut.ReceiptIcon); // Read receipt
    }

    [Fact]
    public void ContactItemViewModel_DisplayName_PrefersNameOverJid()
    {
        var contactWithName = new ContactItemViewModel
        {
            AccountJid = "me@example.com",
            ContactJid = "alice@example.com",
            Name = "Alice Wonderland"
        };
        Assert.Equal("Alice Wonderland", contactWithName.DisplayName);

        var contactWithoutName = new ContactItemViewModel
        {
            AccountJid = "me@example.com",
            ContactJid = "bob@example.com",
            Name = null
        };
        Assert.Equal("bob@example.com", contactWithoutName.DisplayName);
    }

    [Fact]
    public async Task ChatConversationViewModel_LoadHistoryAndPaging_Succeeds()
    {
        var account = "me@example.com";
        var remoteJid = Jid.Parse("charlie@example.com");

        var baseTime = DateTimeOffset.UtcNow.AddHours(-2);
        for (int i = 0; i < 15; i++)
        {
            await _messageRepo.SaveMessageAsync(new ChatMessage
            {
                AccountJid = account,
                RemoteJid = remoteJid.ToString(),
                SenderJid = (i % 2 == 0) ? account : remoteJid.ToString(),
                Body = $"Message #{i}",
                Timestamp = baseTime.AddMinutes(i),
                Direction = (i % 2 == 0) ? MessageDirection.Outbound : MessageDirection.Inbound
            });
        }

        var conv = new ChatConversationViewModel(
            account,
            remoteJid.ToString(),
            "Charlie",
            remoteJid,
            isGroupChat: false,
            _messageRepo);

        Assert.Empty(conv.Messages);
        Assert.False(conv.HasLoadedHistory);

        // Load history
        await conv.LoadHistoryAsync();
        Assert.True(conv.HasLoadedHistory);
        Assert.Equal(15, conv.Messages.Count);
        Assert.Equal("Message #0", conv.Messages[0].Body);
        Assert.Equal("Message #14", conv.Messages[14].Body);
    }

    [Fact]
    public async Task OmemoDeviceItemViewModel_ToggleTrust_UpdatesDatabase()
    {
        var account = "me@example.com";
        var remote = "deviceuser@example.com";
        uint devId = 12345;

        await _omemoRepo.SaveSessionAsync(new OmemoSessionRecord
        {
            AccountJid = account,
            RemoteJid = remote,
            DeviceId = (int)devId,
            SessionData = [1, 2, 3],
            LastActive = DateTimeOffset.UtcNow,
            TrustState = OmemoTrustState.Undecided
        });

        var devVm = new OmemoDeviceItemViewModel(
            _omemoRepo,
            account,
            remote,
            devId,
            "AA BB CC DD",
            OmemoTrustState.Undecided);

        Assert.False(devVm.IsTrusted);

        // Toggle to Trusted
        await devVm.ToggleTrustAsync();
        Assert.True(devVm.IsTrusted);
        Assert.Equal(OmemoTrustState.Trusted, devVm.TrustState);

        var saved = await _omemoRepo.GetSessionAsync(account, remote, (int)devId);
        Assert.NotNull(saved);
        Assert.Equal(OmemoTrustState.Trusted, saved.TrustState);

        // Toggle to Untrusted
        await devVm.ToggleTrustAsync();
        Assert.False(devVm.IsTrusted);
        Assert.Equal(OmemoTrustState.Untrusted, devVm.TrustState);

        var saved2 = await _omemoRepo.GetSessionAsync(account, remote, (int)devId);
        Assert.NotNull(saved2);
        Assert.Equal(OmemoTrustState.Untrusted, saved2.TrustState);
    }

    [Fact]
    public async Task MainChatViewModel_Search_ReturnsMatchedMessages()
    {
        var account = "me@example.com";
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "work@example.com",
            SenderJid = "work@example.com",
            Body = "Project roadmap for Q4 is ready.",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        });

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "friend@example.com",
            SenderJid = "friend@example.com",
            Body = "Want to grab pizza tonight?",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        });

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pw"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        mainVm.SearchQuery = "roadmap";
        await mainVm.ExecuteSearchAsync();

        Assert.True(mainVm.IsSearching);
        Assert.Single(mainVm.SearchResults);
        Assert.Contains("Project roadmap", mainVm.SearchResults[0].Body);

        mainVm.CloseSearch();
        Assert.False(mainVm.IsSearching);
        Assert.Empty(mainVm.SearchResults);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessageAsync_SavesToDatabaseAndAddsBubble()
    {
        var account = "user@chat.net";
        var remote = Jid.Parse("dest@chat.net");

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Dest User",
            remote,
            isGroupChat: false,
            _messageRepo,
            client);

        conv.InputText = "Hello from Avalonia GUI!";
        await conv.SendMessageAsync();

        Assert.Empty(conv.InputText);
        Assert.Single(conv.Messages);
        Assert.Equal("Hello from Avalonia GUI!", conv.Messages[0].Body);
        Assert.Equal(MessageDirection.Outbound, conv.Messages[0].Direction);

        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.Equal("Hello from Avalonia GUI!", dbMessages[0].Body);
    }

    [Fact]
    public async Task MainWindowViewModel_InitializeAsync_WithNoAccounts_SwitchesToLogin()
    {
        var mainWinVm = new MainWindowViewModel(_dbContext);
        await mainWinVm.InitializeAsync();

        Assert.NotNull(mainWinVm.CurrentView);
        Assert.IsType<LoginViewModel>(mainWinVm.CurrentView);
        Assert.Equal("Disconnected", mainWinVm.StatusText);
    }

    [Fact]
    public async Task MainChatViewModel_SetPresence_UpdatesUserPresence()
    {
        var account = "presence@test.com";
        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pw"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        Assert.Equal("available", mainVm.UserPresence);

        await mainVm.SetPresenceAsync("dnd");
        Assert.Equal("dnd", mainVm.UserPresence);

        await mainVm.SetPresenceAsync("away");
        Assert.Equal("away", mainVm.UserPresence);
    }

    [Theory]
    [InlineData("available")]
    [InlineData("away")]
    [InlineData("dnd")]
    [InlineData("xa")]
    [InlineData("custom_show")]
    [InlineData("")]
    public async Task MainChatViewModel_SetPresenceAsync_HandlesVariousShowValuesAndStatusMessages(string showValue)
    {
        var account = "presence_shows@test.com";
        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pw"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask)
        {
            StatusMessage = "Testing status message"
        };

        await mainVm.SetPresenceAsync(showValue);

        Assert.Equal(showValue, mainVm.UserPresence);
        Assert.Equal("Testing status message", mainVm.StatusMessage);
    }

    [Fact]
    public async Task MainChatViewModel_SetPresenceAsync_HandlesTransportExceptionGracefully()
    {
        var account = "presence_error@test.com";
        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pw"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);

        // Fault the output pipe so SendStanzaAsync throws an InvalidOperationException/IOException
        await transport.Output.CompleteAsync(new InvalidOperationException("Transport pipe error during presence write"));

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        // Act & Assert: SetPresenceAsync must catch the exception and complete gracefully without throwing
        var exception = await Record.ExceptionAsync(() => mainVm.SetPresenceAsync("dnd"));

        Assert.Null(exception);
        Assert.Equal("dnd", mainVm.UserPresence);
    }

    [Fact]
    public void MessageBubbleViewModel_FormattedTime_ShowsDateWhenOlderThanToday()
    {
        var now = DateTimeOffset.Now;

        // Today's message -> HH:mm
        var todayMsg = new MessageBubbleViewModel
        {
            Timestamp = now
        };
        Assert.Equal(now.ToString("HH:mm"), todayMsg.FormattedTime);

        // Yesterday's message -> Yesterday HH:mm
        var yesterday = now.AddDays(-1);
        var yesterdayMsg = new MessageBubbleViewModel
        {
            Timestamp = yesterday
        };
        Assert.StartsWith("Yesterday ", yesterdayMsg.FormattedTime);

        // Message from 10 days ago (same year) -> MMM d, HH:mm
        var tenDaysAgo = now.AddDays(-10);
        var olderThisYearMsg = new MessageBubbleViewModel
        {
            Timestamp = tenDaysAgo
        };
        Assert.Contains(tenDaysAgo.ToString("MMM d"), olderThisYearMsg.FormattedTime);

        // Message from previous year -> yyyy-MM-dd HH:mm
        var lastYear = now.AddYears(-1);
        var lastYearMsg = new MessageBubbleViewModel
        {
            Timestamp = lastYear
        };
        Assert.Contains(lastYear.ToString("yyyy-MM-dd"), lastYearMsg.FormattedTime);

        // Date Header test
        Assert.Equal("Today", MessageBubbleViewModel.FormatDateHeader(now));
        Assert.Equal("Yesterday", MessageBubbleViewModel.FormatDateHeader(yesterday));
    }

    [Fact]
    public async Task ChatConversationViewModel_DeduplicationAndDateHeaders_WorkCorrectly()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var now = DateTimeOffset.UtcNow;

        // Seed 2 messages across 2 different days
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Day 1 message",
            Timestamp = now.AddDays(-2),
            Direction = MessageDirection.Inbound,
            StanzaId = "stanza_1"
        });

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Day 2 message",
            Timestamp = now,
            Direction = MessageDirection.Inbound,
            StanzaId = "stanza_2"
        });

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        await conv.LoadHistoryAsync();
        Assert.Equal(2, conv.Messages.Count);

        // Verify date headers are shown on day boundaries
        Assert.True(conv.Messages[0].ShowDateHeader);
        Assert.True(conv.Messages[1].ShowDateHeader);

        // Try to load history again or load duplicate - count should NOT increase
        await conv.LoadHistoryAsync();
        Assert.Equal(2, conv.Messages.Count);

        // Receive already existing message - should be deduplicated
        conv.ReceiveMessage(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Day 2 message",
            Timestamp = now,
            Direction = MessageDirection.Inbound,
            StanzaId = "stanza_2"
        });
        Assert.Equal(2, conv.Messages.Count);
    }

    [Fact]
    public async Task ChatConversationViewModel_LoadsBothOlderAndNewer_WithoutDuplicates()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        // Simulate August 12th cached message in SQLite
        var aug12 = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg_aug12",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "August 12 message",
            Timestamp = aug12,
            Direction = MessageDirection.Inbound,
            StanzaId = "s_aug12"
        });

        // Simulate older July 15th message in SQLite
        var jul15 = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg_jul15",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "July 15 message",
            Timestamp = jul15,
            Direction = MessageDirection.Inbound,
            StanzaId = "s_jul15"
        });

        // Simulate newer September 9th message in SQLite
        var sep9 = new DateTimeOffset(2026, 9, 9, 15, 0, 0, TimeSpan.Zero);
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            Id = "msg_sep9",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "September 9 message",
            Timestamp = sep9,
            Direction = MessageDirection.Inbound,
            StanzaId = "s_sep9"
        });

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        // Load initial history
        await conv.LoadHistoryAsync();

        // Verify all 3 messages are loaded in strict chronological order
        Assert.Equal(3, conv.Messages.Count);
        Assert.Equal("July 15 message", conv.Messages[0].Body);
        Assert.Equal("August 12 message", conv.Messages[1].Body);
        Assert.Equal("September 9 message", conv.Messages[2].Body);

        // Verify date headers are set for distinct dates
        Assert.True(conv.Messages[0].ShowDateHeader);
        Assert.True(conv.Messages[1].ShowDateHeader);
        Assert.True(conv.Messages[2].ShowDateHeader);

        // Simulate receiving a MAM sync item matching the August 12 message with 2-second clock drift and new archive id
        var mamAug12Duplicate = new ChatMessage
        {
            Id = $"mam_{account}_{remote}_arch123",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "August 12 message",
            Timestamp = aug12.AddSeconds(2), // 2s drift
            Direction = MessageDirection.Inbound,
            StanzaId = "arch123"
        };
        conv.ReceiveMessage(mamAug12Duplicate);

        // Must still be 3 messages - no duplicate created!
        Assert.Equal(3, conv.Messages.Count);

        // Paging backwards should not re-add existing messages
        await conv.LoadOlderHistoryAsync();
        Assert.Equal(3, conv.Messages.Count);
    }

    [Fact]
    public async Task ChatConversationViewModel_ScrollToBottomRequested_TriggeredOnHistoryLoadSendAndReceive()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        var scrollRequests = 0;
        conv.ScrollToBottomRequested += () => scrollRequests++;

        // 1. Initial history load triggers scroll
        await conv.LoadHistoryAsync();
        Assert.Equal(1, scrollRequests);

        // 2. Inbound message received triggers scroll
        conv.ReceiveMessage(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Inbound message",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        });
        Assert.Equal(2, scrollRequests);

        // 3. Paging backwards (older history) must NOT trigger scroll to bottom
        await conv.LoadOlderHistoryAsync();
        Assert.Equal(2, scrollRequests); // still 2!
    }

    [Fact]
    public async Task ChatConversationViewModel_LoadOlderHistory_FiresOlderHistoryEvents_AndPreservesSnippet()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        // Seed messages in database: 2 older messages and 1 newer message
        var now = DateTimeOffset.UtcNow;
        var olderMsg1 = new ChatMessage
        {
            Id = "old_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Oldest message 1",
            Timestamp = now.AddMinutes(-30),
            Direction = MessageDirection.Inbound
        };
        var olderMsg2 = new ChatMessage
        {
            Id = "old_2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Older message 2 (outbound)",
            Timestamp = now.AddMinutes(-20),
            Direction = MessageDirection.Outbound
        };
        var latestMsg = new ChatMessage
        {
            Id = "latest_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "Latest message",
            Timestamp = now.AddMinutes(-5),
            Direction = MessageDirection.Inbound
        };

        await _messageRepo.SaveMessageAsync(olderMsg1);
        await _messageRepo.SaveMessageAsync(olderMsg2);
        await _messageRepo.SaveMessageAsync(latestMsg);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: null);

        // Initially initialize Messages with latest message
        conv.AddOrUpdateMessage(latestMsg);
        Assert.Equal("Latest message", conv.LastMessageSnippet);
        Assert.Single(conv.Messages);

        var eventsList = new System.Collections.Generic.List<string>();
        conv.OlderHistoryLoading += () => eventsList.Add("Loading");
        conv.OlderHistoryLoaded += () => eventsList.Add("Loaded");

        var scrollTriggered = false;
        conv.ScrollToBottomRequested += () => scrollTriggered = true;

        await conv.LoadOlderHistoryAsync();

        // Older history events must fire in sequence: Loading -> Loaded
        Assert.Equal(new[] { "Loading", "Loaded" }, eventsList);

        // Prepending older history (even with outbound messages) must NOT trigger ScrollToBottomRequested
        Assert.False(scrollTriggered);

        // All 3 messages should now be present in chronological order
        Assert.Equal(3, conv.Messages.Count);
        Assert.Equal("Oldest message 1", conv.Messages[0].Body);
        Assert.Equal("Older message 2 (outbound)", conv.Messages[1].Body);
        Assert.Equal("Latest message", conv.Messages[2].Body);

        // LastMessageSnippet must NOT be overwritten by prepended older messages!
        Assert.Equal("Latest message", conv.LastMessageSnippet);
    }

    [Theory]
    [InlineData("https://example.com/avatar.png", true, true, "https://example.com/avatar.png")]
    [InlineData("http://xmpp.org/files/photo.jpeg?token=123", true, true, "http://xmpp.org/files/photo.jpeg?token=123")]
    [InlineData("file:///C:/AppData/Stanza/media/snapshot.webp", false, false, null)]
    [InlineData("Here is the screenshot: https://test.org/image.gif please check it", true, false, "https://test.org/image.gif")]
    [InlineData("Just regular text message with no links", false, false, null)]
    [InlineData("https://example.com/document.pdf", false, false, null)]
    [InlineData("", false, false, null)]
    [InlineData(null, false, false, null)]
    public void MessageBubbleViewModel_ExtractImageUrl_DetectsHttpAndFileLinks(string? body, bool expectedHasImage, bool expectedIsOnlyImage, string? expectedUrl)
    {
        var vm = new MessageBubbleViewModel();
        vm.ExtractImageUrl(body!);

        Assert.Equal(expectedHasImage, vm.HasImage);
        Assert.Equal(expectedIsOnlyImage, vm.IsOnlyImage);
        Assert.Equal(expectedUrl, vm.ImageUrl);
    }

    [Theory]
    [InlineData("C:\\photos\\img.png", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("avatar.jpg", true)]
    [InlineData("/tmp/pic.jpeg", true)]
    [InlineData("anim.gif", true)]
    [InlineData("image.webp", true)]
    [InlineData("icon.bmp", true)]
    [InlineData("logo.ico", true)]
    [InlineData("doc.pdf", false)]
    [InlineData("archive.zip", false)]
    [InlineData("text.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClipboardImageHelper_IsImageFile_ValidatesExtensions(string? path, bool expected)
    {
        var result = ClipboardImageHelper.IsImageFile(path!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendImageAsync_CreatesMessageWithImageUrl()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        var dummyBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header bytes
        await conv.SendImageAsync(dummyBytes, "test_pic.png");

        Assert.Single(conv.Messages);
        var sentMsg = conv.Messages[0];
        Assert.False(sentMsg.HasImage); // file:// URIs are no longer auto-loaded as image URLs for security
        Assert.StartsWith("file:///", sentMsg.Body);

        // Verify stored in repository
        var history = await _messageRepo.GetMessagesAsync(account, remote.ToString(), 10);
        Assert.Single(history);
        Assert.Equal(sentMsg.Body, history[0].Body);
    }

    [Fact]
    public void ChatConversationViewModel_StageImageAttachment_SetsPendingPropertiesAndClears()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");
        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo);

        var dummyBytes = new byte[2048]; // 2 KB
        conv.StageImageAttachment(dummyBytes, "vacation.png");

        Assert.True(conv.HasPendingImage);
        Assert.Equal("vacation.png", conv.PendingImageFileName);
        Assert.Equal(dummyBytes, conv.PendingImageBytes);
        Assert.Equal("2 KB", conv.PendingImageSizeText);

        // Discard pending image
        conv.ClearPendingImage();

        Assert.False(conv.HasPendingImage);
        Assert.Null(conv.PendingImageFileName);
        Assert.Null(conv.PendingImageBytes);
        Assert.Null(conv.PendingImageSizeText);
        Assert.Null(conv.PendingImagePreview);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessageAsync_WithPendingImageAndCaption_SendsTwoMessagesAndGroupsWithImageAtBottom()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        var dummyBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        conv.StageImageAttachment(dummyBytes, "snapshot.png");
        conv.InputText = "Look at this snapshot!";

        await conv.SendMessageAsync();

        // Pending image and input text should be reset
        Assert.False(conv.HasPendingImage);
        Assert.Empty(conv.InputText);

        // Two distinct messages should be created and saved in repository
        var savedMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString(), 10);
        Assert.Equal(2, savedMessages.Count);
        Assert.Equal("Look at this snapshot!", savedMessages[0].Body);
        Assert.Contains("snapshot.png", savedMessages[1].Body);

        // In the chatbox with merging enabled, they merge into 1 bubble with the image kept at the bottom
        Assert.Single(conv.Messages);
        var sentBubble = conv.Messages[0];
        Assert.StartsWith("Look at this snapshot!", sentBubble.Body);
        Assert.Contains("snapshot.png", sentBubble.Body);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessageAsync_ProducesCompleteXmppMessageXml()
    {
        var account = "john@squishythoughts.com";
        var remote = Jid.Parse("richard@squishythoughts.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/Conversations.7Rzy"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Richard",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        var dummyBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        conv.StageImageAttachment(dummyBytes, "Y_NYoSXcSzOeKD6AyCUnEg.png");
        conv.InputText = "Test";

        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var sentMsg = conv.Messages[0];
        Assert.NotNull(sentMsg.RawXml);

        var xml = sentMsg.RawXml;

        // Verify root element attributes
        Assert.Contains("from=\"john@squishythoughts.com/Conversations.7Rzy\"", xml);
        Assert.Contains("to=\"richard@squishythoughts.com\"", xml);
        Assert.Contains("xml:lang=\"en\"", xml);
        Assert.Contains("type=\"chat\"", xml);
        Assert.Contains("xmlns=\"jabber:client\"", xml);

        // Verify child elements
        Assert.Contains("<request xmlns=\"urn:xmpp:receipts\"", xml);
        Assert.Contains("<markable xmlns=\"urn:xmpp:chat-markers:0\"", xml);
        Assert.Contains("<x xmlns=\"jabber:x:oob\">", xml);
        Assert.Contains("<url>file:///", xml);
        Assert.Contains("Y_NYoSXcSzOeKD6AyCUnEg.png</url>", xml);
        Assert.Contains("<body>file:///", xml);
        Assert.Contains("Test</body>", xml);
        Assert.Contains("<active xmlns=\"http://jabber.org/protocol/chatstates\"", xml);
        Assert.Contains("<stanza-id by=\"john@squishythoughts.com\"", xml);
        Assert.Contains("xmlns=\"urn:xmpp:sid:0\"", xml);
        Assert.Contains("<origin-id", xml);
    }

    [Fact]
    public void MessageBubbleViewModel_FromChatMessage_ExtractsOobImageUrl()
    {
        var rawXml =
            "<message from=\"john@squishythoughts.com/Conversations.7Rzy\" id=\"63f358a1-25dc-4b33-9e28-3e80c8252712\" to=\"richard@squishythoughts.com\" xml:lang=\"en\" type=\"chat\" xmlns=\"jabber:client\">\n" +
            "  <request xmlns=\"urn:xmpp:receipts\" />\n" +
            "  <markable xmlns=\"urn:xmpp:chat-markers:0\" />\n" +
            "  <x xmlns=\"jabber:x:oob\">\n" +
            "    <url>https://chat.squishythoughts.com/upload/11decc24-8d94-4bc2-830b-0890251cba5f/Y_NYoSXcSzOeKD6AyCUnEg.png</url>\n" +
            "  </x>\n" +
            "  <body>Check this photo!</body>\n" +
            "  <active xmlns=\"http://jabber.org/protocol/chatstates\" />\n" +
            "</message>";

        var msg = new ChatMessage
        {
            Id = "oob_test_1",
            AccountJid = "richard@squishythoughts.com",
            RemoteJid = "john@squishythoughts.com",
            SenderJid = "john@squishythoughts.com/Conversations.7Rzy",
            Body = "Check this photo!",
            RawXml = rawXml,
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg);

        Assert.True(bubble.HasImage);
        Assert.Equal("https://chat.squishythoughts.com/upload/11decc24-8d94-4bc2-830b-0890251cba5f/Y_NYoSXcSzOeKD6AyCUnEg.png", bubble.ImageUrl);
        Assert.False(bubble.IsOnlyImage);
        Assert.Equal("Check this photo!", bubble.Body);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessageAsync_EmptyWithoutImage_DoesNotSend()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        conv.InputText = "   ";
        await conv.SendMessageAsync();

        Assert.Empty(conv.Messages);
    }

    [Theory]
    [InlineData("image.png", true)]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("animation.gif", true)]
    [InlineData("graphic.webp", true)]
    [InlineData("bitmap.bmp", true)]
    [InlineData("icon.ico", true)]
    [InlineData("document.pdf", false)]
    [InlineData("binary.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClipboardImageHelper_IsImageFile_DetectsSupportedExtensions(string? path, bool expected)
    {
        Assert.Equal(expected, ClipboardImageHelper.IsImageFile(path!));
    }

    [Fact]
    public async Task AsyncImageLoader_Security_RejectsFileAndUncPaths()
    {
        // file:// URIs
        Assert.Null(await AsyncImageLoader.LoadImageAsync("file:///C:/Windows/System32/cmd.exe"));
        Assert.Null(await AsyncImageLoader.LoadImageAsync("file://attacker.com/share/test.png"));

        // UNC paths
        Assert.Null(await AsyncImageLoader.LoadImageAsync(@"\\attacker.com\share\test.png"));

        // Local file paths
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.Null(await AsyncImageLoader.LoadImageAsync(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("file://attacker.com/share/test.png")]
    [InlineData(@"\\attacker.com\share\image.png")]
    [InlineData(@"C:\Users\Public\secret.png")]
    public void MessageBubbleViewModel_Security_RejectsLocalAndUncPaths(string payload)
    {
        var vm = new MessageBubbleViewModel();
        vm.ExtractImageUrl(payload);

        Assert.False(vm.HasImage);
        Assert.False(vm.IsOnlyImage);
        Assert.Null(vm.ImageUrl);
    }

    [Fact]
    public void Win32ClipboardHelper_ConvertDibToPngBytes_HandlesInvalidOrCorruptDataSafely()
    {
        Assert.Null(Win32ClipboardHelper.ConvertDibToPngBytes(Array.Empty<byte>()));
        Assert.Null(Win32ClipboardHelper.ConvertDibToPngBytes(new byte[10]));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("file:///etc/passwd")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("customscheme://run/cmd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("relative/path/file.png")]
    [InlineData("")]
    [InlineData(null)]
    public void MessageBubbleViewModel_OpenImage_RejectsDangerousOrNonHttpUrls(string? url)
    {
        var vm = new MessageBubbleViewModel
        {
            ImageUrl = url
        };

        // OpenImage must return safely without throwing or launching external process for non-http(s) schemes
        var ex = Record.Exception(() => vm.OpenImage());
        Assert.Null(ex);
    }

    [Fact]
    public async Task MainChatViewModel_AutomaticReception_InboundAndOutboundCarbons_UpdatesConversation()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("peer@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // 1. Inbound direct message automatic reception
        var inboundStanza = new MessageStanza(to: Jid.Parse(account), type: MessageStanza.TypeChat)
        {
            From = remote,
            Body = "Automatic live message!"
        };

        await server.InjectStanzaAsync(inboundStanza);
        await Task.Delay(100);

        // Verify conversation was automatically created and updated with message
        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Peer", remote, isGroupChat: false);
        Assert.Single(conv.Messages);
        Assert.Equal("Automatic live message!", conv.Messages[0].Body);
        Assert.Equal(MessageDirection.Inbound, conv.Messages[0].Direction);

        // 2. Inbound Carbon copy message automatic reception (sent to us, received on another device)
        var carbonInboundElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", account)
            .Attr("to", $"{account}/desktop")
            .Child(new Stanza.Core.Xml.XmppElement("received", "urn:xmpp:carbons:2")
                .Child(new Stanza.Core.Xml.XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new Stanza.Core.Xml.XmppElement("message")
                        .Attr("from", "peer@mock.example.com/mobile")
                        .Attr("to", account)
                        .Child(new Stanza.Core.Xml.XmppElement("body") { Value = "Inbound Carbon message" }))));

        await server.InjectElementAsync(carbonInboundElem);
        await Task.Delay(100);

        Assert.Equal(2, conv.Messages.Count);
        Assert.Equal("Inbound Carbon message", conv.Messages[1].Body);
        Assert.Equal(MessageDirection.Inbound, conv.Messages[1].Direction);

        // 3. Outbound Carbon copy message automatic reception (sent by us from mobile device)
        var carbonOutboundElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", account)
            .Attr("to", $"{account}/desktop")
            .Child(new Stanza.Core.Xml.XmppElement("sent", "urn:xmpp:carbons:2")
                .Child(new Stanza.Core.Xml.XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new Stanza.Core.Xml.XmppElement("message")
                        .Attr("from", $"{account}/mobile")
                        .Attr("to", "peer@mock.example.com")
                        .Child(new Stanza.Core.Xml.XmppElement("body") { Value = "Outbound Carbon message from phone" }))));

        await server.InjectElementAsync(carbonOutboundElem);
        await Task.Delay(100);

        Assert.Equal(3, conv.Messages.Count);
        Assert.Equal("Outbound Carbon message from phone", conv.Messages[2].Body);
        Assert.Equal(MessageDirection.Outbound, conv.Messages[2].Direction);

        // Verify stored in DB
        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Equal(3, dbMessages.Count);

        await client.DisconnectAsync();
    }

    [Fact]
    public void ChatConversationViewModel_ReplyToMessage_FormatsSingleAndMultiLineQuoteInInputText()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        // 1. Single line message reply
        var singleLineBubble = new MessageBubbleViewModel
        {
            SenderName = "peer@test.org",
            Body = "Hello there!"
        };

        conv.ReplyToMessage(singleLineBubble);
        Assert.StartsWith("> peer@test.org: Hello there!\n\n", conv.InputText.Replace("\r\n", "\n"));

        // 2. Reply again when InputText already has user text
        conv.InputText = "My reply text";
        conv.ReplyToMessage(singleLineBubble);
        Assert.Equal("> peer@test.org: Hello there!\n\nMy reply text", conv.InputText.Replace("\r\n", "\n"));

        // 3. Multi-line message reply
        conv.InputText = string.Empty;
        var multiLineBubble = new MessageBubbleViewModel
        {
            SenderName = "Alice",
            Body = "Line 1\nLine 2\nLine 3"
        };

        conv.ReplyToMessage(multiLineBubble);
        var expectedMultiLine = "> Alice: Line 1\n> Line 2\n> Line 3\n\n";
        Assert.Equal(expectedMultiLine, conv.InputText.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task MessageBubbleViewModel_ReplyAndCopyText_TriggersCallbacksAndExecutesSafely()
    {
        var replyTriggered = false;
        MessageBubbleViewModel? target = null;

        var bubble = new MessageBubbleViewModel
        {
            SenderName = "Bob",
            Body = "Test copy & reply body"
        };

        bubble.ReplyRequested = vm =>
        {
            replyTriggered = true;
            target = vm;
        };

        // Test Reply command
        bubble.Reply();
        Assert.True(replyTriggered);
        Assert.Same(bubble, target);

        // Test CopyText command (runs safely even without UI desktop thread)
        var ex = await Record.ExceptionAsync(async () => await bubble.CopyTextAsync());
        Assert.Null(ex);
    }

    [Fact]
    public void MessageBubbleViewModel_RawXml_TogglesAndGeneratesFallback()
    {
        // 1. Explicit RawXml provided
        var msgWithXml = new ChatMessage
        {
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "Explicit XML Test",
            RawXml = "<message type='chat'><body>Explicit XML Test</body></message>"
        };

        var bubbleWithXml = MessageBubbleViewModel.FromChatMessage(msgWithXml);
        Assert.Equal("<message type='chat'><body>Explicit XML Test</body></message>", bubbleWithXml.RawXml);
        Assert.False(bubbleWithXml.IsRawXmlVisible);

        bubbleWithXml.ToggleRawXml();
        Assert.True(bubbleWithXml.IsRawXmlVisible);

        bubbleWithXml.ToggleRawXml();
        Assert.False(bubbleWithXml.IsRawXmlVisible);

        // 2. Fallback RawXml generation when RawXml is null
        var msgWithoutXml = new ChatMessage
        {
            AccountJid = "alice@test.org",
            RemoteJid = "bob@test.org",
            SenderJid = "alice@test.org",
            Body = "Fallback XML Test",
            Direction = MessageDirection.Outbound,
            RawXml = null
        };

        var bubbleWithoutXml = MessageBubbleViewModel.FromChatMessage(msgWithoutXml);
        Assert.NotNull(bubbleWithoutXml.RawXml);
        Assert.Contains("alice@test.org", bubbleWithoutXml.RawXml);
        Assert.Contains("bob@test.org", bubbleWithoutXml.RawXml);
        Assert.Contains("Fallback XML Test", bubbleWithoutXml.RawXml);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessage_CapturesRawXml()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("dest@test.org");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Dest",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        conv.InputText = "Testing RawXml on send";
        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.NotNull(bubble.RawXml);
        Assert.Contains("dest@test.org", bubble.RawXml);
        Assert.Contains("Testing RawXml on send", bubble.RawXml);

        var history = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(history);
        Assert.NotNull(history[0].RawXml);
        Assert.Contains("Testing RawXml on send", history[0].RawXml);
    }

    [Fact]
    public async Task MessageRepository_MarkAsRead_UpdatesIsReadColumn()
    {
        var account = "user@test.org";
        var remote = "peer@test.org";

        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = account,
            Body = "Outbound message",
            Direction = MessageDirection.Outbound,
            StanzaId = "s1",
            IsRead = false
        };

        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote,
            SenderJid = remote,
            Body = "Inbound unread message",
            Direction = MessageDirection.Inbound,
            StanzaId = "s2",
            IsRead = false
        };

        await _messageRepo.SaveMessagesAsync([msg1, msg2]);

        // Mark single message as read
        var updated = await _messageRepo.MarkMessageAsReadAsync(account, "s1");
        Assert.True(updated);

        var history = await _messageRepo.GetMessagesAsync(account, remote);
        Assert.True(history.First(m => m.Id == "m1").IsRead);
        Assert.False(history.First(m => m.Id == "m2").IsRead);

        // Mark all unread inbound messages as read
        var marked = await _messageRepo.MarkUnreadMessagesAsReadAsync(account, remote);
        Assert.Single(marked);
        Assert.Equal("s2", marked[0]);

        var history2 = await _messageRepo.GetMessagesAsync(account, remote);
        Assert.True(history2.First(m => m.Id == "m2").IsRead);
    }

    [Fact]
    public async Task MainChatViewModel_ReceiptOrMarkerReceived_UpdatesMessageReadStatusAndIcon()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Bob", remote, isGroupChat: false);

        // Send outbound message
        conv.InputText = "Hello Bob";
        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon); // Single icon before receipt

        var stanzaId = bubble.StanzaId ?? bubble.Id;

        // Simulate incoming XEP-0333 <displayed/> chat marker from Bob
        var markerElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Child(new Stanza.Core.Xml.XmppElement("displayed", "urn:xmpp:chat-markers").Attr("id", stanzaId));

        await server.InjectElementAsync(markerElem);
        await Task.Delay(100);

        Assert.True(bubble.IsRead);
        Assert.Equal("◈", bubble.ReceiptIcon); // Read icon after read marker received

        // Verify database was updated
        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.True(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_DeliveryReceipt_DoesNotMarkMessageAsRead()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Bob", remote, isGroupChat: false);

        conv.InputText = "Hello Bob";
        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var stanzaId = bubble.StanzaId ?? bubble.Id;

        // Simulate incoming XEP-0184 delivery receipt from Bob
        var receiptElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Child(new Stanza.Core.Xml.XmppElement("received", "urn:xmpp:receipts").Attr("id", stanzaId));

        await server.InjectElementAsync(receiptElem);
        await Task.Delay(100);

        // Delivery receipt indicates delivery to client, NOT read: icon must remain delivered diamond
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.False(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_ChatMarkerReceived_DoesNotMarkMessageAsRead()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Bob", remote, isGroupChat: false);

        conv.InputText = "Hello Bob";
        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var stanzaId = bubble.StanzaId ?? bubble.Id;

        // Simulate incoming XEP-0333 <received/> chat marker from Bob
        var markerElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Child(new Stanza.Core.Xml.XmppElement("received", "urn:xmpp:chat-markers").Attr("id", stanzaId));

        await server.InjectElementAsync(markerElem);
        await Task.Delay(100);

        // Received marker is delivery only: icon must remain delivered diamond
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.False(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_ChatMarkerAcknowledged_MarksMessageAsReadAndDoubleCheckmark()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Bob", remote, isGroupChat: false);

        conv.InputText = "Hello Bob";
        await conv.SendMessageAsync();

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var stanzaId = bubble.StanzaId ?? bubble.Id;

        // Simulate incoming XEP-0333 <acknowledged/> chat marker from Bob
        var markerElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Child(new Stanza.Core.Xml.XmppElement("acknowledged", "urn:xmpp:chat-markers").Attr("id", stanzaId));

        await server.InjectElementAsync(markerElem);
        await Task.Delay(100);

        // Acknowledged marker indicates read/acknowledged: icon must become read diamond
        Assert.True(bubble.IsRead);
        Assert.Equal("◈", bubble.ReceiptIcon);

        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.True(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_CarbonSentMessage_IsNotPrematurelyMarkedAsRead()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var conv = mainVm.GetOrCreateConversation(remote.ToString(), "Bob", remote, isGroupChat: false);

        // Simulate receiving a carbon copy of an outbound message sent by Alice on mobile
        var carbonOutboundElem = new Stanza.Core.Xml.XmppElement("message")
            .Attr("from", account)
            .Attr("to", $"{account}/desktop")
            .Child(new Stanza.Core.Xml.XmppElement("sent", "urn:xmpp:carbons:2")
                .Child(new Stanza.Core.Xml.XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new Stanza.Core.Xml.XmppElement("message")
                        .Attr("from", $"{account}/mobile")
                        .Attr("to", "bob@mock.example.com")
                        .Attr("id", "carbon_outbound_123")
                        .Child(new Stanza.Core.Xml.XmppElement("body") { Value = "Outbound Carbon message from phone" }))));

        await server.InjectElementAsync(carbonOutboundElem);
        await Task.Delay(100);

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.Equal(MessageDirection.Outbound, bubble.Direction);
        Assert.False(bubble.IsRead);
        Assert.Equal("◇", bubble.ReceiptIcon);

        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.Equal(MessageDirection.Outbound, dbMessages[0].Direction);
        Assert.False(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public void ChatConversationViewModel_HandleRemoteChatState_UpdatesIsRemoteComposing()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo);

        Assert.False(conv.IsRemoteComposing);

        // Receive Composing -> IsRemoteComposing = true
        conv.HandleRemoteChatState(ChatState.Composing);
        Assert.True(conv.IsRemoteComposing);

        // Receive Paused -> IsRemoteComposing = false
        conv.HandleRemoteChatState(ChatState.Paused);
        Assert.False(conv.IsRemoteComposing);

        // Receive Active -> IsRemoteComposing = false
        conv.HandleRemoteChatState(ChatState.Composing);
        Assert.True(conv.IsRemoteComposing);
        conv.HandleRemoteChatState(ChatState.Active);
        Assert.False(conv.IsRemoteComposing);
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchiveAsync_ThreadSafeAndUpdatesUI()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        var mam = new Xep0313MessageArchiveManagement();
        await mam.AttachAsync(client);
        await client.ConnectAsync();

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client,
            mamManager: mam);

        // Prepare MAM result to inject when sync query arrives
        server.OnIqReceived += async iq =>
        {
            var queryElem = iq.RawElement.Element("query", "urn:xmpp:mam:2");
            if (queryElem is not null)
            {
                var qid = queryElem.GetAttr("queryid");
                var resultElem = new XmppElement("result", "urn:xmpp:mam:2")
                    .Attr("id", "arch_msg_001");
                if (!string.IsNullOrEmpty(qid))
                {
                    resultElem.Attr("queryid", qid);
                }

                var mamMsg = new XmppElement("message")
                    .Child(resultElem
                        .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                            .Child(new XmppElement("delay", "urn:xmpp:delay")
                                .Attr("stamp", "2026-09-10T14:00:00Z"))
                            .Child(new XmppElement("message")
                                .Attr("from", "bob@mock.example.com")
                                .Attr("to", account)
                                .Child(new XmppElement("body") { Value = "New archived message from Bob" }))));

                await server.InjectElementAsync(mamMsg);
            }
        };

        // Call SyncArchiveAsync from background task (simulating non-UI thread call)
        await Task.Run(async () =>
        {
            await conv.SyncArchiveAsync();
        });

        // Verify message was processed, added to Messages, and saved in SQLite
        Assert.Single(conv.Messages);
        Assert.Equal("New archived message from Bob", conv.Messages[0].Body);
        Assert.Equal("bob@mock.example.com", conv.Messages[0].SenderName);

        var dbMsgs = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMsgs);
        Assert.Equal("New archived message from Bob", dbMsgs[0].Body);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_InitializeAsync_LoadsContactSummariesAndSendsPresence()
    {
        var account = "alice@mock.example.com";
        var contact1Jid = "peer1@mock.example.com";
        var contact2Jid = "peer2@mock.example.com";

        // Seed contacts in roster
        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact
            {
                AccountJid = account,
                ContactJid = contact1Jid,
                Name = "Peer One",
                Subscription = "both"
            },
            new RosterContact
            {
                AccountJid = account,
                ContactJid = contact2Jid,
                Name = "Peer Two",
                Subscription = "both"
            }
        ]);

        // Seed unread message from peer2 in DB (peer1 will be selected first by default)
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = contact2Jid,
            SenderJid = contact2Jid,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound,
            Body = "Hey, are you free?",
            IsRead = false
        });

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // 1. Verify presence was initialized to available
        Assert.Equal("available", mainVm.UserPresence);

        // 2. Verify contact has last message preview and unread count loaded from SQLite
        var contact2 = mainVm.Contacts.FirstOrDefault(c => c.ContactJid.Equals(contact2Jid, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(contact2);
        Assert.Equal("Hey, are you free?", contact2.LastMessagePreview);
        Assert.Equal(1, contact2.UnreadCount);

        // 3. Verify Conversations contains chats populated from contacts
        Assert.True(mainVm.Conversations.Count >= 2);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_ActiveConversationChanged_LoadsHistoryAndClearsUnread()
    {
        var account = "alice@mock.example.com";
        var contact1Jid = "peer1@mock.example.com";
        var contact2Jid = "peer2@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contact1Jid, Name = "Peer One", Subscription = "both" },
            new RosterContact { AccountJid = account, ContactJid = contact2Jid, Name = "Peer Two", Subscription = "both" }
        ]);

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = contact2Jid,
            SenderJid = contact2Jid,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            Direction = MessageDirection.Inbound,
            Body = "Message for Peer 2",
            IsRead = false
        });

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // Initially peer1 is selected; peer2 conversation has not loaded messages yet
        var peer2Conv = mainVm.Conversations.First(c => c.RemoteJid.ToString().Equals(contact2Jid, StringComparison.OrdinalIgnoreCase));
        var contact2 = mainVm.Contacts.First(c => c.ContactJid.Equals(contact2Jid, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, contact2.UnreadCount);

        // Act: User selects Peer 2's chat in the "Chats" tab (binding ActiveConversation)
        mainVm.ActiveConversation = peer2Conv;

        // Allow async history load to complete
        await Task.Delay(100);

        // Assert: Unread count reset, history loaded into Messages collection
        Assert.Equal(0, contact2.UnreadCount);
        Assert.Single(peer2Conv.Messages);
        Assert.Equal("Message for Peer 2", peer2Conv.Messages[0].Body);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_IncomingOfflineMessage_PreservesDelayTimestamp()
    {
        var account = "alice@mock.example.com";
        var sender = "bob@mock.example.com";

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // Simulate server delivering offline queued message with delay stamp from 2 hours ago
        var historicalTime = DateTimeOffset.UtcNow.AddHours(-2);
        var stampStr = historicalTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var offlineMsg = new XmppElement("message")
            .Attr("from", sender)
            .Attr("to", account)
            .Attr("type", "chat")
            .Child(new XmppElement("body") { Value = "Offline queued text" })
            .Child(new XmppElement("delay", "urn:xmpp:delay")
                .Attr("stamp", stampStr)
                .Attr("from", "mock.example.com"));

        await server.InjectElementAsync(offlineMsg);
        await Task.Delay(100);

        var savedMsgs = await _messageRepo.GetMessagesAsync(account, sender);
        Assert.Single(savedMsgs);
        Assert.Equal("Offline queued text", savedMsgs[0].Body);
        // Timestamp must be within 2 seconds of historical delay stamp, not UtcNow
        Assert.True(Math.Abs((savedMsgs[0].Timestamp - DateTimeOffset.Parse(stampStr)).TotalSeconds) < 2);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task ChatConversationViewModel_SyncArchiveAsync_WithXmlLangMessages_SuccessfullyParsesAndUpdatesUI()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        var mam = new Xep0313MessageArchiveManagement();
        await mam.AttachAsync(client);
        await client.ConnectAsync();

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client,
            mamManager: mam);

        ChatMessage? processedMsg = null;
        conv.MessageProcessed += m => processedMsg = m;

        Assert.Equal("Sync 🔄", conv.SyncButtonText);

        // Prepare MAM result with xml:lang on inner message to test proper attribute handling
        server.OnIqReceived += async iq =>
        {
            var queryElem = iq.RawElement.Element("query", "urn:xmpp:mam:2");
            if (queryElem is not null)
            {
                var qid = queryElem.GetAttr("queryid");
                var resultElem = new XmppElement("result", "urn:xmpp:mam:2")
                    .Attr("id", "arch_msg_lang_01");
                if (!string.IsNullOrEmpty(qid))
                {
                    resultElem.Attr("queryid", qid);
                }

                var innerMsg = new XmppElement("message")
                    .Attr("type", "chat")
                    .Attr("to", account)
                    .Attr("from", "bob@mock.example.com/mobile")
                    .Attr("xml:lang", "en-US")
                    .Child(new XmppElement("body") { Value = "Hello with xml:lang!" })
                    .Child(new XmppElement("origin-id", "urn:xmpp:sid:0").Attr("id", "orig_lang_1"));

                var mamMsg = new XmppElement("message")
                    .Child(resultElem
                        .Child(new XmppElement("forwarded", "urn:xmpp:forward:0")
                            .Child(new XmppElement("delay", "urn:xmpp:delay")
                                .Attr("stamp", "2026-09-11T05:00:00Z"))
                            .Child(innerMsg)));

                await server.InjectElementAsync(mamMsg);
            }
        };

        await conv.SyncArchiveAsync();

        Assert.Equal("Sync 🔄", conv.SyncButtonText);
        Assert.Single(conv.Messages);
        Assert.Equal("Hello with xml:lang!", conv.Messages[0].Body);
        Assert.Equal("bob@mock.example.com/mobile", conv.Messages[0].SenderName);
        Assert.Contains("xml:lang=\"en-US\"", conv.Messages[0].RawXml);

        Assert.NotNull(processedMsg);
        Assert.Equal("Hello with xml:lang!", processedMsg.Body);

        var dbMsgs = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMsgs);
        Assert.Equal("Hello with xml:lang!", dbMsgs[0].Body);

        await client.DisconnectAsync();
    }

    [Fact]
    public void MainChatViewModel_SidebarCollapseAndRestore_ManagesStateAndIcons()
    {
        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "pass"
        }, transport);

        var vm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        // Initial state
        Assert.True(vm.IsSidebarOpen);
        Assert.Equal("◀", vm.SidebarToggleIcon);
        Assert.Equal("Collapse sidebar (Ctrl+B)", vm.SidebarToggleTooltip);
        Assert.False(vm.ShowEmptyStateHeader); // ActiveConversation is null, but sidebar is open

        // Collapse sidebar
        vm.CollapseSidebar();
        Assert.False(vm.IsSidebarOpen);
        Assert.Equal("▶", vm.SidebarToggleIcon);
        Assert.Equal("Restore sidebar (Ctrl+B)", vm.SidebarToggleTooltip);
        Assert.True(vm.ShowEmptyStateHeader); // Sidebar is collapsed and ActiveConversation is null

        // Restore sidebar
        vm.RestoreSidebar();
        Assert.True(vm.IsSidebarOpen);
        Assert.Equal("◀", vm.SidebarToggleIcon);
        Assert.False(vm.ShowEmptyStateHeader);

        // Toggle sidebar
        vm.ToggleSidebar();
        Assert.False(vm.IsSidebarOpen);
        Assert.True(vm.ShowEmptyStateHeader);

        vm.ToggleSidebar();
        Assert.True(vm.IsSidebarOpen);
        Assert.False(vm.ShowEmptyStateHeader);

        // When conversation becomes active, ShowEmptyStateHeader is false even if collapsed
        var conv = vm.GetOrCreateConversation("bob@mock.example.com", "Bob", Jid.Parse("bob@mock.example.com"), false);
        vm.ActiveConversation = conv;
        vm.CollapseSidebar();
        Assert.False(vm.IsSidebarOpen);
        Assert.False(vm.ShowEmptyStateHeader); // ActiveConversation is not null
    }

    [Fact]
    public void MessageBubbleViewModel_EditAndDelete_PropertiesAndCallbacks_WorkCorrectly()
    {
        var msgOut = new ChatMessage
        {
            Id = "msg_test_edit_1",
            AccountJid = "user@test.org",
            RemoteJid = "contact@test.org",
            SenderJid = "user@test.org",
            Body = "Initial text",
            Direction = MessageDirection.Outbound,
            ReplaceId = "replaced_stanza_1"
        };
        var bubbleOut = MessageBubbleViewModel.FromChatMessage(msgOut);

        Assert.True(bubbleOut.IsOutbound);
        Assert.True(bubbleOut.IsEdited);
        Assert.Equal("replaced_stanza_1", bubbleOut.ReplaceId);

        var msgIn = new ChatMessage
        {
            Id = "msg_test_edit_2",
            AccountJid = "user@test.org",
            RemoteJid = "contact@test.org",
            SenderJid = "contact@test.org",
            Body = "Peer text",
            Direction = MessageDirection.Inbound
        };
        var bubbleIn = MessageBubbleViewModel.FromChatMessage(msgIn);

        Assert.False(bubbleIn.IsOutbound);
        Assert.False(bubbleIn.IsEdited);

        var editCalled = false;
        var deleteCalled = false;
        bubbleOut.EditRequested = b => editCalled = true;
        bubbleOut.DeleteRequested = b => deleteCalled = true;

        bubbleOut.Edit();
        bubbleOut.Delete();

        Assert.True(editCalled);
        Assert.True(deleteCalled);
    }

    [Fact]
    public void ChatConversationViewModel_StartAndCancelEditing_ManagesStateAndDraft()
    {
        var remote = Jid.Parse("contact@test.org");
        var conv = new ChatConversationViewModel(
            "user@test.org",
            remote.ToString(),
            "Contact",
            remote,
            isGroupChat: false,
            _messageRepo);

        var bubble1 = new MessageBubbleViewModel
        {
            Id = "m1",
            Body = "First outbound message",
            Direction = MessageDirection.Outbound
        };
        var bubble2 = new MessageBubbleViewModel
        {
            Id = "m2",
            Body = "Inbound message from contact",
            Direction = MessageDirection.Inbound
        };
        var bubble3 = new MessageBubbleViewModel
        {
            Id = "m3",
            Body = "Latest outbound message",
            Direction = MessageDirection.Outbound
        };
        conv.Messages.Add(bubble1);
        conv.Messages.Add(bubble2);
        conv.Messages.Add(bubble3);

        // Type some draft text
        conv.InputText = "My unsent draft message";

        // Up arrow triggers StartEditingLastSentMessage
        conv.StartEditingLastSentMessage();

        Assert.True(conv.IsEditingMessage);
        Assert.Equal("m3", conv.EditingMessageId);
        Assert.Equal("Latest outbound message", conv.EditingMessagePreviewText);
        Assert.Equal("Latest outbound message", conv.InputText);
        Assert.Equal("✓", conv.SendButtonIcon);

        // Cancel editing restores draft
        conv.CancelEditingMessage();

        Assert.False(conv.IsEditingMessage);
        Assert.Null(conv.EditingMessageId);
        Assert.Null(conv.EditingMessagePreviewText);
        Assert.Equal("My unsent draft message", conv.InputText);
        Assert.Equal("➤", conv.SendButtonIcon);

        // Start editing bubble1 directly
        conv.StartEditingMessage(bubble1);
        Assert.True(conv.IsEditingMessage);
        Assert.Equal("m1", conv.EditingMessageId);
        Assert.Equal("First outbound message", conv.InputText);

        // Inbound message cannot be edited
        conv.CancelEditingMessage();
        conv.StartEditingMessage(bubble2);
        Assert.False(conv.IsEditingMessage);
    }

    [Fact]
    public async Task ChatConversationViewModel_EditSentMessage_SendsXep0308StanzaAndUpdatesUiAndDatabase()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        MessageStanza? serverReceivedStanza = null;
        server.OnMessageReceived += msg => serverReceivedStanza = msg;

        // Save original message in DB and conversation
        var origMsg = new ChatMessage
        {
            Id = "stanza_orig_123",
            StanzaId = "stanza_orig_123",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Original message text",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Outbound
        };
        await _messageRepo.SaveMessageAsync(origMsg);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        await conv.LoadHistoryAsync();
        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.False(bubble.IsEdited);

        // Start editing
        conv.StartEditingMessage(bubble);
        Assert.True(conv.IsEditingMessage);
        Assert.Equal("stanza_orig_123", conv.EditingMessageId);

        // Change text and send
        conv.InputText = "Corrected message text";
        await conv.SendMessageAsync();

        for (int i = 0; i < 20 && serverReceivedStanza is null; i++) await Task.Delay(25);

        // 1. Edit mode exited
        Assert.False(conv.IsEditingMessage);
        Assert.Equal(string.Empty, conv.InputText);

        // 2. UI bubble updated in-place
        Assert.Equal("Corrected message text", bubble.Body);
        Assert.True(bubble.IsEdited);
        Assert.False(string.IsNullOrEmpty(bubble.ReplaceId));

        // 3. Stanza sent contains XEP-0308 <replace id="stanza_orig_123" xmlns="urn:xmpp:message-correct:0"/>
        Assert.NotNull(serverReceivedStanza);
        Assert.Equal("Corrected message text", serverReceivedStanza.Body);
        var replaceElem = serverReceivedStanza.RawElement.Element("replace", "urn:xmpp:message-correct:0");
        Assert.NotNull(replaceElem);
        Assert.Equal("stanza_orig_123", replaceElem.GetAttr("id"));

        // 4. SQLite database updated
        var dbMsgs = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMsgs);
        Assert.Equal("Corrected message text", dbMsgs[0].Body);
        Assert.Equal(serverReceivedStanza.Id, dbMsgs[0].ReplaceId);
    }

    [Fact]
    public async Task ChatConversationViewModel_DeleteOutboundMessage_SendsXep0424StanzaAndRemovesFromUiAndDatabase()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        MessageStanza? serverReceivedStanza = null;
        server.OnMessageReceived += msg => serverReceivedStanza = msg;

        var msg = new ChatMessage
        {
            Id = "stanza_del_456",
            StanzaId = "stanza_del_456",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Message to be deleted",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Outbound
        };
        await _messageRepo.SaveMessageAsync(msg);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        await conv.LoadHistoryAsync();
        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];

        // Delete message
        await conv.DeleteMessageAsync(bubble);

        for (int i = 0; i < 20 && serverReceivedStanza is null; i++) await Task.Delay(25);

        // 1. Removed from UI Messages collection
        Assert.Empty(conv.Messages);

        // 2. Sent retraction stanza per XEP-0424
        Assert.NotNull(serverReceivedStanza);
        Assert.Equal("jabber:client", serverReceivedStanza.RawElement.GetAttr("xmlns"));
        var originIdElem = serverReceivedStanza.RawElement.Element("origin-id", "urn:xmpp:sid:0");
        Assert.NotNull(originIdElem);
        Assert.Equal(serverReceivedStanza.Id, originIdElem.GetAttr("id"));

        var retractElem = serverReceivedStanza.RawElement.Element("retract", "urn:xmpp:message-retract:1");
        Assert.NotNull(retractElem);
        Assert.Equal("stanza_del_456", retractElem.GetAttr("id"));

        var fallbackElem = serverReceivedStanza.RawElement.Element("fallback", "urn:xmpp:fallback:0");
        Assert.NotNull(fallbackElem);
        Assert.Equal("urn:xmpp:message-retract:1", fallbackElem.GetAttr("for"));

        Assert.Equal("/me retracted a previous message, but it's unsupported by your client.", serverReceivedStanza.Body);

        // 3. Removed from database
        var dbMsgs = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Empty(dbMsgs);
    }

    [Fact]
    public async Task ChatConversationViewModel_HandleIncomingCorrectionAndRetraction_UpdatesUi()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo);

        var bubble1 = new MessageBubbleViewModel
        {
            Id = "msg_peer_1",
            StanzaId = "stanza_peer_1",
            Body = "Before edit",
            Direction = MessageDirection.Inbound
        };
        var bubble2 = new MessageBubbleViewModel
        {
            Id = "msg_peer_2",
            StanzaId = "stanza_peer_2",
            Body = "Message to retract",
            Direction = MessageDirection.Inbound
        };
        conv.Messages.Add(bubble1);
        conv.Messages.Add(bubble2);

        // Handle incoming correction for bubble1
        conv.HandleIncomingCorrection("stanza_peer_1", "After edit by peer");
        Assert.Equal("After edit by peer", bubble1.Body);
        Assert.True(bubble1.IsEdited);

        // Handle incoming retraction for bubble2
        conv.HandleIncomingRetraction("stanza_peer_2");
        Assert.Single(conv.Messages);
        Assert.Equal("msg_peer_1", conv.Messages[0].Id);
    }

    [Fact]
    public async Task MainChatViewModel_IncomingCorrectionAndRetractionStanzas_HandledProperly()
    {
        var account = "alice@mock.example.com";
        var remote = Jid.Parse("bob@mock.example.com");

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        var vm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await vm.InitializeAsync();
        await client.ConnectAsync();

        // 1. Initial message from Bob
        var origStanza = new MessageStanza(
            to: Jid.Parse(account),
            body: "Bob original message",
            type: MessageStanza.TypeChat,
            from: remote);
        origStanza.Id = "bob_msg_101";

        await server.InjectStanzaAsync(origStanza);

        var conv = vm.GetOrCreateConversation(remote.ToString(), "Bob", remote, false);
        for (int i = 0; i < 20 && conv.Messages.Count == 0; i++) await Task.Delay(25);

        Assert.Single(conv.Messages);
        Assert.Equal("Bob original message", conv.Messages[0].Body);
        Assert.False(conv.Messages[0].IsEdited);

        // 2. Incoming XEP-0308 Correction from Bob
        var correctionStanza = new MessageStanza(
            to: Jid.Parse(account),
            body: "Bob corrected message",
            type: MessageStanza.TypeChat,
            from: remote);
        correctionStanza.Id = "bob_msg_102";
        correctionStanza.RawElement.Child(new XmppElement("replace", "urn:xmpp:message-correct:0").Attr("id", "bob_msg_101"));

        await server.InjectStanzaAsync(correctionStanza);
        for (int i = 0; i < 20 && !conv.Messages[0].IsEdited; i++) await Task.Delay(25);

        // Still 1 message, but updated text and edited indicator
        Assert.Single(conv.Messages);
        Assert.Equal("Bob corrected message", conv.Messages[0].Body);
        Assert.True(conv.Messages[0].IsEdited);

        // Verify DB updated
        var dbMsgs = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMsgs);
        Assert.Equal("Bob corrected message", dbMsgs[0].Body);

        // 3. Incoming XEP-0424 Retraction from Bob
        var retractionStanza = new MessageStanza(
            to: Jid.Parse(account),
            type: MessageStanza.TypeChat,
            from: remote);
        retractionStanza.Id = "bob_msg_103";
        retractionStanza.RawElement.Child(new XmppElement("retract", "urn:xmpp:message-retract:0").Attr("id", "bob_msg_101"));

        await server.InjectStanzaAsync(retractionStanza);
        for (int i = 0; i < 20 && conv.Messages.Count > 0; i++) await Task.Delay(25);

        // Message removed from conversation and database
        Assert.Empty(conv.Messages);
        var dbMsgsAfterRetract = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Empty(dbMsgsAfterRetract);
    }

    [Fact]
    public async Task MainChatViewModel_IncomingPresence_UpdatesContactOnlineIndicator()
    {
        var account = "alice@mock.example.com";
        var contactJid = "bob@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contactJid, Name = "Bob", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var bob = mainVm.Contacts.FirstOrDefault(c => c.ContactJid.Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(bob);
        Assert.Equal("offline", bob.PresenceShow);

        // 1. Bob comes online
        var presOnline = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"));
        await server.InjectStanzaAsync(presOnline);
        for (int i = 0; i < 20 && bob.PresenceShow != "available"; i++) await Task.Delay(25);
        Assert.Equal("available", bob.PresenceShow);

        // 2. Bob changes presence to away with status
        var presAway = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"), show: "away", status: "Stepped out");
        await server.InjectStanzaAsync(presAway);
        for (int i = 0; i < 20 && bob.PresenceShow != "away"; i++) await Task.Delay(25);
        Assert.Equal("away", bob.PresenceShow);
        Assert.Equal("Stepped out", bob.StatusMessage);

        // 3. Bob changes to dnd
        var presDnd = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"), show: "dnd", status: "In a meeting");
        await server.InjectStanzaAsync(presDnd);
        for (int i = 0; i < 20 && bob.PresenceShow != "dnd"; i++) await Task.Delay(25);
        Assert.Equal("dnd", bob.PresenceShow);
        Assert.Equal("In a meeting", bob.StatusMessage);

        // 4. Bob goes offline
        var presOffline = new PresenceStanza(type: PresenceStanza.TypeUnavailable, from: Jid.Parse($"{contactJid}/desktop"));
        await server.InjectStanzaAsync(presOffline);
        for (int i = 0; i < 20 && bob.PresenceShow != "offline"; i++) await Task.Delay(25);
        Assert.Equal("offline", bob.PresenceShow);
        Assert.Null(bob.StatusMessage);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_MultiResourcePresence_PrioritizesCorrectly()
    {
        var account = "alice@mock.example.com";
        var contactJid = "bob@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contactJid, Name = "Bob", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var bob = mainVm.Contacts.First(c => c.ContactJid.Equals(contactJid, StringComparison.OrdinalIgnoreCase));

        // Mobile comes online with priority 5, away
        var presMobile = new PresenceStanza(from: Jid.Parse($"{contactJid}/mobile"), show: "away", priority: 5);
        await server.InjectStanzaAsync(presMobile);
        for (int i = 0; i < 20 && bob.PresenceShow != "away"; i++) await Task.Delay(25);
        Assert.Equal("away", bob.PresenceShow);

        // Desktop comes online with priority 10, available (higher priority)
        var presDesktop = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"), priority: 10);
        await server.InjectStanzaAsync(presDesktop);
        for (int i = 0; i < 20 && bob.PresenceShow != "available"; i++) await Task.Delay(25);
        Assert.Equal("available", bob.PresenceShow);

        // Desktop disconnects -> reverts to mobile (away)
        var presDesktopOff = new PresenceStanza(type: PresenceStanza.TypeUnavailable, from: Jid.Parse($"{contactJid}/desktop"));
        await server.InjectStanzaAsync(presDesktopOff);
        for (int i = 0; i < 20 && bob.PresenceShow != "away"; i++) await Task.Delay(25);
        Assert.Equal("away", bob.PresenceShow);

        // Mobile disconnects -> offline
        var presMobileOff = new PresenceStanza(type: PresenceStanza.TypeUnavailable, from: Jid.Parse($"{contactJid}/mobile"));
        await server.InjectStanzaAsync(presMobileOff);
        for (int i = 0; i < 20 && bob.PresenceShow != "offline"; i++) await Task.Delay(25);
        Assert.Equal("offline", bob.PresenceShow);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_Disconnect_ResetsContactsToOffline()
    {
        var account = "alice@mock.example.com";
        var contactJid = "bob@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contactJid, Name = "Bob", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var bob = mainVm.Contacts.First(c => c.ContactJid.Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        var presOnline = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"), status: "Working");
        await server.InjectStanzaAsync(presOnline);
        for (int i = 0; i < 20 && bob.PresenceShow != "available"; i++) await Task.Delay(25);
        Assert.Equal("available", bob.PresenceShow);

        await mainVm.DisconnectAsync();
        Assert.Equal("offline", bob.PresenceShow);
        Assert.Null(bob.StatusMessage);
    }

    [Fact]
    public void ContactItemViewModel_FromRosterContact_InitializesPresenceFields()
    {
        var roster = new RosterContact
        {
            AccountJid = "alice@example.com",
            ContactJid = "carol@example.com",
            Name = "Carol",
            Subscription = "both",
            PresenceShow = "dnd",
            PresenceStatus = "In a meeting"
        };

        var vm = ContactItemViewModel.FromRosterContact(roster);
        Assert.Equal("alice@example.com", vm.AccountJid);
        Assert.Equal("carol@example.com", vm.ContactJid);
        Assert.Equal("Carol", vm.DisplayName);
        Assert.Equal("dnd", vm.PresenceShow);
        Assert.Equal("In a meeting", vm.StatusMessage);
    }

    [Fact]
    public async Task MainChatViewModel_InitializeAsync_RestoresSavedPresenceAndActiveChat()
    {
        var account = "alice_restore@mock.example.com";
        var contact1Jid = "peer1@mock.example.com";
        var contact2Jid = "peer2@mock.example.com";

        var settingsRepo = new SettingsRepository(_dbContext);
        await settingsRepo.SetLastPresenceModeAsync(account, "dnd");
        await settingsRepo.SetLastStatusMessageAsync(account, "In deep focus");
        await settingsRepo.SetLastActiveChatAsync(account, contact2Jid);

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contact1Jid, Name = "Peer One", Subscription = "both" },
            new RosterContact { AccountJid = account, ContactJid = contact2Jid, Name = "Peer Two", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // Presence restored
        Assert.Equal("dnd", mainVm.UserPresence);
        Assert.Equal("In deep focus", mainVm.StatusMessage);

        // Active chat restored to contact2
        Assert.NotNull(mainVm.ActiveConversation);
        Assert.Equal(contact2Jid, mainVm.ActiveConversation.RemoteJid.ToString());

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_ActiveConversationChanged_PersistsLastActiveChat()
    {
        var account = "alice_persist@mock.example.com";
        var contact1Jid = "peer1@mock.example.com";
        var contact2Jid = "peer2@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contact1Jid, Name = "Peer One", Subscription = "both" },
            new RosterContact { AccountJid = account, ContactJid = contact2Jid, Name = "Peer Two", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var settingsRepo = new SettingsRepository(_dbContext);

        // Switch to peer 2
        var peer2Conv = mainVm.Conversations.First(c => c.RemoteJid.ToString().Equals(contact2Jid, StringComparison.OrdinalIgnoreCase));
        mainVm.ActiveConversation = peer2Conv;

        // Verify persisted to SQLite
        string? savedChat = null;
        for (int i = 0; i < 40 && savedChat != contact2Jid; i++)
        {
            await Task.Delay(25);
            savedChat = await settingsRepo.GetLastActiveChatAsync(account);
        }
        Assert.Equal(contact2Jid, savedChat);

        // Switch to peer 1
        var peer1Conv = mainVm.Conversations.First(c => c.RemoteJid.ToString().Equals(contact1Jid, StringComparison.OrdinalIgnoreCase));
        mainVm.ActiveConversation = peer1Conv;

        string? savedChat2 = null;
        for (int i = 0; i < 40 && savedChat2 != contact1Jid; i++)
        {
            await Task.Delay(25);
            savedChat2 = await settingsRepo.GetLastActiveChatAsync(account);
        }
        Assert.Equal(contact1Jid, savedChat2);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_SetPresenceAsync_PersistsPresenceAndStatus()
    {
        var account = "alice_pres_persist@mock.example.com";
        var settingsRepo = new SettingsRepository(_dbContext);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        mainVm.StatusMessage = "Stepped away for a moment";
        await mainVm.SetPresenceAsync("away");

        var savedMode = await settingsRepo.GetLastPresenceModeAsync(account);
        var savedStatus = await settingsRepo.GetLastStatusMessageAsync(account);

        Assert.Equal("away", savedMode);
        Assert.Equal("Stepped away for a moment", savedStatus);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_IncomingPresence_ForeignResourceDoesNotOverwriteOwnPresence()
    {
        var account = "alice@mock.example.com";
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Resource = "desktop",
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        Assert.Equal("available", mainVm.UserPresence);

        // Another device (e.g. mobile client) on same bare account broadcasts presence away
        var foreignMobilePresence = new PresenceStanza(from: Jid.Parse($"{account}/mobile"), show: "away", status: "On phone");
        await server.InjectStanzaAsync(foreignMobilePresence);
        await Task.Delay(50);

        // Our UserPresence on desktop must remain available!
        Assert.Equal("available", mainVm.UserPresence);

        // If a presence from our OWN bound resource is received (server reflection), it updates
        var boundPresence = new PresenceStanza(from: client.BoundJid, show: "away", status: "Updated from desktop");
        await server.InjectStanzaAsync(boundPresence);
        for (int i = 0; i < 20 && mainVm.UserPresence != "away"; i++) await Task.Delay(25);

        Assert.Equal("away", mainVm.UserPresence);
        Assert.Equal("Updated from desktop", mainVm.StatusMessage);

        await client.DisconnectAsync();
    }

    [Fact]
    public void ChatConversationViewModel_UpdateSnippet_FormatsOutboundWithYouPrefix()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Peer", remote, false, _messageRepo);

        conv.UpdateSnippet("Hello world", MessageDirection.Outbound, account, DateTimeOffset.UtcNow);

        Assert.Equal("You: Hello world", conv.LastMessageSnippet);
        Assert.False(string.IsNullOrEmpty(conv.LastMessageTime));
    }

    [Fact]
    public void ChatConversationViewModel_UpdateSnippet_FormatsInboundGroupChatWithSenderPrefix()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("room@conference.test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Room", remote, isGroupChat: true, _messageRepo);

        conv.UpdateSnippet("Hello team", MessageDirection.Inbound, "room@conference.test.org/Alice", DateTimeOffset.UtcNow);

        Assert.Equal("Alice: Hello team", conv.LastMessageSnippet);
    }

    [Fact]
    public void ChatConversationViewModel_UpdateSnippet_FormatsInbound1to1WithoutSenderPrefix()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Peer", remote, false, _messageRepo);

        conv.UpdateSnippet("Hey how are you?", MessageDirection.Inbound, "peer@test.org", DateTimeOffset.UtcNow);

        Assert.Equal("Hey how are you?", conv.LastMessageSnippet);
    }

    [Fact]
    public void ChatConversationViewModel_UpdateSnippet_FormatsImageAttachments()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Peer", remote, false, _messageRepo);

        conv.UpdateSnippet("https://example.com/photo.png", MessageDirection.Outbound, account, DateTimeOffset.UtcNow);
        Assert.Equal("You: 📷 Image", conv.LastMessageSnippet);

        conv.UpdateSnippet("https://example.com/photo.jpg", MessageDirection.Inbound, "peer@test.org", DateTimeOffset.UtcNow);
        Assert.Equal("📷 Image", conv.LastMessageSnippet);
    }

    [Fact]
    public void ChatConversationViewModel_UpdateSnippet_FormatsActionMessages()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Peer", remote, false, _messageRepo);

        conv.UpdateSnippet("/me waves", MessageDirection.Outbound, account, DateTimeOffset.UtcNow);
        Assert.Equal("* You waves", conv.LastMessageSnippet);

        conv.UpdateSnippet("/me dances", MessageDirection.Inbound, "peer@test.org", DateTimeOffset.UtcNow);
        Assert.Equal("* Peer dances", conv.LastMessageSnippet);
    }

    [Fact]
    public void ChatConversationViewModel_CorrectionAndRetraction_UpdatesSnippet()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(account, remote.ToString(), "Peer", remote, false, _messageRepo);

        var msg1 = new ChatMessage
        {
            Id = "msg_1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = remote.ToString(),
            Body = "First message",
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            StanzaId = "s1"
        };
        var msg2 = new ChatMessage
        {
            Id = "msg_2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second message",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow,
            StanzaId = "s2"
        };

        conv.ReceiveMessage(msg1);
        Assert.Equal("First message", conv.LastMessageSnippet);

        conv.ReceiveMessage(msg2);
        Assert.Equal("You: Second message", conv.LastMessageSnippet);

        // Edit second message
        conv.HandleIncomingCorrection("s2", "Second message corrected", "<xml/>");
        Assert.Equal("You: Second message corrected", conv.LastMessageSnippet);

        // Retract second message -> snippet reverts to first message
        conv.HandleIncomingRetraction("s2");
        Assert.Equal("First message", conv.LastMessageSnippet);

        // Retract first message -> snippet becomes empty
        conv.HandleIncomingRetraction("s1");
        Assert.Equal(string.Empty, conv.LastMessageSnippet);
    }

    [Fact]
    public async Task MainChatViewModel_StartupSummaries_PopulatesSnippetAndTimeOnConversationsAndContacts()
    {
        var account = "user@test.org";
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Resource = "desktop",
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        // Seed messages in repository
        var remoteJid = "peer@test.org";
        var outboundMsg = new ChatMessage
        {
            Id = "m_seed_1",
            AccountJid = account,
            RemoteJid = remoteJid,
            SenderJid = account,
            Body = "Hello from yesterday",
            Direction = MessageDirection.Outbound,
            Timestamp = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await _messageRepo.SaveMessageAsync(outboundMsg);

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // Check contact preview
        var contact = Assert.Single(mainVm.Contacts, c => c.ContactJid == remoteJid);
        Assert.Equal("You: Hello from yesterday", contact.LastMessagePreview);

        // Check conversation snippet and time
        var conv = Assert.Single(mainVm.Conversations, c => c.RemoteJid.ToString() == remoteJid);
        Assert.Equal("You: Hello from yesterday", conv.LastMessageSnippet);
        Assert.Equal("Yesterday", conv.LastMessageTime);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_Conversation_PresenceShow_UpdatesWhenAvatarCached()
    {
        var account = "alice@mock.example.com";
        var contactJid = "bob@mock.example.com";

        // 1. Seed contact in roster
        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contactJid, Name = "Bob", Subscription = "both" }
        ]);

        // 2. Seed cached avatar for contact in SQLite
        var avatarRepo = new AvatarRepository(_dbContext);
        await avatarRepo.SaveAvatarAsync(contactJid, "hash123", "image/png", SamplePngBytes);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        // 3. Bob comes online
        var presOnline = new PresenceStanza(from: Jid.Parse($"{contactJid}/desktop"));
        await server.InjectStanzaAsync(presOnline);

        var bob = mainVm.Contacts.First(c => c.ContactJid.Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < 20 && bob.PresenceShow != "available"; i++) await Task.Delay(25);
        Assert.Equal("available", bob.PresenceShow);

        // 4. Verify conversation exists and has PresenceShow == "available" despite cached avatar
        var conv = mainVm.Conversations.FirstOrDefault(c => c.RemoteJid.ToString().Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(conv);
        Assert.Equal("available", conv.PresenceShow);
        Assert.True(conv.IsOnline);
        Assert.Equal("Online", conv.PresenceStatusText);

        // 5. Select contact and verify ActiveConversation has PresenceShow == "available"
        await mainVm.SelectContactAsync(bob);
        Assert.NotNull(mainVm.ActiveConversation);
        Assert.Equal("available", mainVm.ActiveConversation.PresenceShow);
        Assert.True(mainVm.ActiveConversation.IsOnline);
        Assert.Equal("Online", mainVm.ActiveConversation.PresenceStatusText);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MainChatViewModel_IncomingPresence_WithTypeAvailable_UpdatesPresence()
    {
        var account = "alice@mock.example.com";
        var contactJid = "carol@mock.example.com";

        var rosterRepo = new RosterRepository(_dbContext);
        await rosterRepo.UpsertContactsAsync([
            new RosterContact { AccountJid = account, ContactJid = contactJid, Name = "Carol", Subscription = "both" }
        ]);

        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "password123"
        }, transport);

        await client.ConnectAsync();

        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);
        await mainVm.InitializeAsync();

        var carol = mainVm.Contacts.First(c => c.ContactJid.Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("offline", carol.PresenceShow);

        // Presence with explicit type="available" (some servers/gateways send this)
        var presOnline = new PresenceStanza(type: "available", from: Jid.Parse($"{contactJid}/resource"));
        await server.InjectStanzaAsync(presOnline);

        for (int i = 0; i < 20 && carol.PresenceShow != "available"; i++) await Task.Delay(25);
        Assert.Equal("available", carol.PresenceShow);

        var conv = mainVm.Conversations.FirstOrDefault(c => c.RemoteJid.ToString().Equals(contactJid, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(conv);
        Assert.Equal("available", conv.PresenceShow);

        await client.DisconnectAsync();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
