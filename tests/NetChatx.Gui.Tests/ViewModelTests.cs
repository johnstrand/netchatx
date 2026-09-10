using System;
using System.IO;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.ViewModels;
using NetChatx.MockServer;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class ViewModelTests : IDisposable
{
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
        await vm.ConnectAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(capturedProfile);
        Assert.Equal("alice@example.com", capturedProfile.Jid);
        Assert.Equal("secret123", capturedProfile.Password);
        Assert.Equal("xmpp.example.com", capturedProfile.Host);
        Assert.Equal(5222, capturedProfile.Port);
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
        Assert.Empty(bubbleIn.ReceiptIcon); // Inbound doesn't have receipt checkmarks

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
        Assert.Equal("✓✓", bubbleOut.ReceiptIcon); // Read receipt
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
        string account = "me@example.com";
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
        string account = "me@example.com";
        string remote = "deviceuser@example.com";
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
        string account = "me@example.com";
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
        string account = "user@chat.net";
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
        string account = "presence@test.com";
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
        string account = "presence_shows@test.com";
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
        string account = "presence_error@test.com";
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
        string account = "user@test.org";
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
        string account = "user@test.org";
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
        string account = "user@test.org";
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

        int scrollRequests = 0;
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

    [Theory]
    [InlineData("https://example.com/avatar.png", true, true, "https://example.com/avatar.png")]
    [InlineData("http://xmpp.org/files/photo.jpeg?token=123", true, true, "http://xmpp.org/files/photo.jpeg?token=123")]
    [InlineData("file:///C:/AppData/NetChatx/media/snapshot.webp", false, false, null)]
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
        string account = "alice@example.com";
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
        string account = "alice@mock.example.com";
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
        var carbonInboundElem = new NetChatx.Core.Xml.XmppElement("message")
            .Attr("from", account)
            .Attr("to", $"{account}/desktop")
            .Child(new NetChatx.Core.Xml.XmppElement("received", "urn:xmpp:carbons:2")
                .Child(new NetChatx.Core.Xml.XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new NetChatx.Core.Xml.XmppElement("message")
                        .Attr("from", "peer@mock.example.com/mobile")
                        .Attr("to", account)
                        .Child(new NetChatx.Core.Xml.XmppElement("body") { Value = "Inbound Carbon message" }))));

        await server.InjectElementAsync(carbonInboundElem);
        await Task.Delay(100);

        Assert.Equal(2, conv.Messages.Count);
        Assert.Equal("Inbound Carbon message", conv.Messages[1].Body);
        Assert.Equal(MessageDirection.Inbound, conv.Messages[1].Direction);

        // 3. Outbound Carbon copy message automatic reception (sent by us from mobile device)
        var carbonOutboundElem = new NetChatx.Core.Xml.XmppElement("message")
            .Attr("from", account)
            .Attr("to", $"{account}/desktop")
            .Child(new NetChatx.Core.Xml.XmppElement("sent", "urn:xmpp:carbons:2")
                .Child(new NetChatx.Core.Xml.XmppElement("forwarded", "urn:xmpp:forward:0")
                    .Child(new NetChatx.Core.Xml.XmppElement("message")
                        .Attr("from", $"{account}/mobile")
                        .Attr("to", "peer@mock.example.com")
                        .Child(new NetChatx.Core.Xml.XmppElement("body") { Value = "Outbound Carbon message from phone" }))));

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
        string account = "user@test.org";
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
        string expectedMultiLine = "> Alice: Line 1\n> Line 2\n> Line 3\n\n";
        Assert.Equal(expectedMultiLine, conv.InputText.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task MessageBubbleViewModel_ReplyAndCopyText_TriggersCallbacksAndExecutesSafely()
    {
        bool replyTriggered = false;
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
        string account = "user@test.org";
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
        string account = "user@test.org";
        string remote = "peer@test.org";

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
        bool updated = await _messageRepo.MarkMessageAsReadAsync(account, "s1");
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
        string account = "alice@mock.example.com";
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
        Assert.Equal("✓", bubble.ReceiptIcon); // Single checkmark before receipt

        string stanzaId = bubble.StanzaId ?? bubble.Id;

        // Simulate incoming XEP-0333 <displayed/> chat marker from Bob
        var markerElem = new NetChatx.Core.Xml.XmppElement("message")
            .Attr("from", "bob@mock.example.com/res")
            .Child(new NetChatx.Core.Xml.XmppElement("displayed", "urn:xmpp:chat-markers").Attr("id", stanzaId));

        await server.InjectElementAsync(markerElem);
        await Task.Delay(100);

        Assert.True(bubble.IsRead);
        Assert.Equal("✓✓", bubble.ReceiptIcon); // Double checkmark after read marker received

        // Verify database was updated
        var dbMessages = await _messageRepo.GetMessagesAsync(account, remote.ToString());
        Assert.Single(dbMessages);
        Assert.True(dbMessages[0].IsRead);

        await client.DisconnectAsync();
    }

    [Fact]
    public void ChatConversationViewModel_HandleRemoteChatState_UpdatesIsRemoteComposing()
    {
        string account = "alice@example.com";
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

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
