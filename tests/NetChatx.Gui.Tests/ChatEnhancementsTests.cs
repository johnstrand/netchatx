using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Transport;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.ViewModels;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class ChatEnhancementsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly RosterRepository _rosterRepo;

    public ChatEnhancementsTests()
    {
        _dbPath = $"test_enhancements_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void ClipboardImageHelper_IsSupportedImageFile_RecognizesValidExtensions()
    {
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("photo.png"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("IMAGE.JPG"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("anim.GIF"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("banner.jpeg"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("sticker.webp"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("drawing.bmp"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile("icon.ico"));
        Assert.True(ClipboardImageHelper.IsSupportedImageFile(@"C:\Users\test\Pictures\sample.png"));

        Assert.False(ClipboardImageHelper.IsSupportedImageFile(null));
        Assert.False(ClipboardImageHelper.IsSupportedImageFile(string.Empty));
        Assert.False(ClipboardImageHelper.IsSupportedImageFile("   "));
        Assert.False(ClipboardImageHelper.IsSupportedImageFile("document.pdf"));
        Assert.False(ClipboardImageHelper.IsSupportedImageFile("script.py"));
        Assert.False(ClipboardImageHelper.IsSupportedImageFile("app.exe"));
    }

    [Fact]
    public async Task ChatConversationViewModel_ReplyWorkflow_TracksReplyingToMessageAndClearsOnSend()
    {
        string account = "me@example.com";
        var remote = Jid.Parse("peer@example.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        var bubble = new MessageBubbleViewModel
        {
            Id = "msg-reply-1",
            Body = "Hey there, how is the project going?",
            SenderName = "Peer",
            Direction = MessageDirection.Inbound,
            RemoteJid = remote.ToString()
        };

        // Initial state
        Assert.Null(conv.ReplyingToMessage);
        Assert.False(conv.HasReplyingMessage);

        // Initiate reply
        conv.ReplyToMessage(bubble);

        Assert.NotNull(conv.ReplyingToMessage);
        Assert.True(conv.HasReplyingMessage);
        Assert.Equal("Hey there, how is the project going?", conv.ReplyingToMessage.Body);
        Assert.StartsWith("> Peer: Hey there, how is the project going?", conv.InputText);

        // Cancel reply
        conv.CancelReplyingMessage();
        Assert.Null(conv.ReplyingToMessage);
        Assert.False(conv.HasReplyingMessage);

        // Re-initiate reply and send
        conv.ReplyToMessage(bubble);
        Assert.NotNull(conv.ReplyingToMessage);

        conv.InputText = "> Peer: Hey there\nAll good!";
        await conv.SendMessageAsync();

        // After sending, ReplyingToMessage must be cleared
        Assert.Null(conv.ReplyingToMessage);
        Assert.False(conv.HasReplyingMessage);
    }

    [Fact]
    public async Task MainChatViewModel_Search_EmptyQueryResetsState()
    {
        string account = "me@example.com";
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        mainVm.SearchQuery = "   ";
        await mainVm.ExecuteSearchAsync();

        Assert.False(mainVm.IsSearching);
        Assert.Empty(mainVm.SearchResults);
        Assert.Equal("Search Results", mainVm.SearchResultsHeader);
    }

    [Fact]
    public async Task MainChatViewModel_Search_ReturnsHeaderAndCount()
    {
        string account = "user@example.com";
        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "alex@example.com",
            SenderJid = "alex@example.com",
            Body = "Meet me at the cyber cafe",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        });

        await _messageRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = account,
            RemoteJid = "sam@example.com",
            SenderJid = "sam@example.com",
            Body = "Cyber security update ready",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        });

        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        mainVm.SearchQuery = "Cyber";
        await mainVm.ExecuteSearchAsync();

        Assert.True(mainVm.IsSearching);
        Assert.Equal(2, mainVm.SearchResults.Count);
        Assert.Equal("2 results for \"Cyber\"", mainVm.SearchResultsHeader);

        // Search with single match
        mainVm.SearchQuery = "security";
        await mainVm.ExecuteSearchAsync();

        Assert.True(mainVm.IsSearching);
        Assert.Single(mainVm.SearchResults);
        Assert.Equal("1 result for \"security\"", mainVm.SearchResultsHeader);

        // Search with zero matches
        mainVm.SearchQuery = "NonExistentTerm";
        await mainVm.ExecuteSearchAsync();

        Assert.True(mainVm.IsSearching);
        Assert.Empty(mainVm.SearchResults);
        Assert.Equal("No results for \"NonExistentTerm\"", mainVm.SearchResultsHeader);

        // Close search
        mainVm.CloseSearch();
        Assert.False(mainVm.IsSearching);
        Assert.Empty(mainVm.SearchResults);
        Assert.Equal("Search Results", mainVm.SearchResultsHeader);
    }

    [Fact]
    public async Task MainChatViewModel_SelectSearchResultAsync_SwitchesActiveConversation()
    {
        string account = "user@example.com";
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        var resultBubble = new MessageBubbleViewModel
        {
            Id = "res-1",
            RemoteJid = "target@example.com",
            SenderName = "Target User",
            Body = "Important message",
            Direction = MessageDirection.Inbound
        };

        await mainVm.SelectSearchResultAsync(resultBubble);

        Assert.NotNull(mainVm.ActiveConversation);
        Assert.Equal("target@example.com", mainVm.ActiveConversation.RemoteJid.ToString());
        Assert.False(mainVm.IsSearching);
    }

    [Fact]
    public async Task MainChatViewModel_NewChatDialog_ValidationAndSuccessFlow()
    {
        string account = "admin@example.com";
        var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());
        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        // Open dialog
        mainVm.OpenNewChatDialog();
        Assert.True(mainVm.IsNewChatDialogOpen);
        Assert.Equal(string.Empty, mainVm.NewChatJid);
        Assert.Equal(string.Empty, mainVm.NewChatDisplayName);
        Assert.Equal(string.Empty, mainVm.NewChatErrorMessage);

        // Attempt confirm with empty JID
        await mainVm.ConfirmNewChatAsync();
        Assert.True(mainVm.IsNewChatDialogOpen);
        Assert.Equal("Please enter a contact JID.", mainVm.NewChatErrorMessage);

        // Attempt confirm with invalid format
        mainVm.NewChatJid = "not-a-valid-jid@@@";
        await mainVm.ConfirmNewChatAsync();
        Assert.True(mainVm.IsNewChatDialogOpen);
        Assert.Equal("Invalid JID format (e.g. user@example.com).", mainVm.NewChatErrorMessage);

        // Cancel dialog
        mainVm.CancelNewChatDialog();
        Assert.False(mainVm.IsNewChatDialogOpen);
        Assert.Equal(string.Empty, mainVm.NewChatErrorMessage);

        // Valid confirm flow
        mainVm.OpenNewChatDialog();
        mainVm.NewChatJid = "newcontact@example.com";
        mainVm.NewChatDisplayName = "New Contact";
        await mainVm.ConfirmNewChatAsync();

        Assert.False(mainVm.IsNewChatDialogOpen);
        Assert.NotNull(mainVm.ActiveConversation);
        Assert.Equal("newcontact@example.com", mainVm.ActiveConversation.RemoteJid.ToString());
        Assert.Equal("New Contact", mainVm.ActiveConversation.Title);

        // Verify roster contact was saved to DB
        var contacts = await _rosterRepo.GetContactsAsync(account);
        var savedContact = contacts.FirstOrDefault(c => c.ContactJid == "newcontact@example.com");
        Assert.NotNull(savedContact);
        Assert.Equal("New Contact", savedContact.Name);
    }

    [Fact]
    public void ChatConversationViewModel_ButtonIcons_ReflectSyncAndLoadState()
    {
        string account = "me@example.com";
        var remote = Jid.Parse("peer@example.com");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        // Initial default state
        Assert.Equal("🔄", conv.SyncButtonIcon);
        Assert.Equal("Sync 🔄", conv.SyncButtonText);
        Assert.Equal("▲", conv.LoadOlderButtonIcon);
        Assert.Equal("▲ Load Older Messages", conv.LoadOlderButtonText);

        // While syncing / loading
        conv.IsSyncing = true;
        Assert.Equal("⏳", conv.SyncButtonIcon);
        Assert.Equal("Syncing... ⏳", conv.SyncButtonText);

        conv.IsLoadingOlderHistory = true;
        Assert.Equal("⏳", conv.LoadOlderButtonIcon);
        Assert.Equal("Loading older messages...", conv.LoadOlderButtonText);

        // Reset
        conv.IsSyncing = false;
        conv.IsLoadingOlderHistory = false;
        Assert.Equal("🔄", conv.SyncButtonIcon);
        Assert.Equal("▲", conv.LoadOlderButtonIcon);
    }

    [Fact]
    public void MessageBubbleViewModel_ResolveSenderDisplayName_ResolvesFriendlyNames()
    {
        // Outbound
        Assert.Equal("Me", MessageBubbleViewModel.ResolveSenderDisplayName("user@domain.com", MessageDirection.Outbound));
        Assert.Equal("Me", MessageBubbleViewModel.ResolveSenderDisplayName("Me", MessageDirection.Inbound));

        // Empty / Null
        Assert.Equal("Unknown", MessageBubbleViewModel.ResolveSenderDisplayName(null));
        Assert.Equal("Unknown", MessageBubbleViewModel.ResolveSenderDisplayName("   "));

        // 1-on-1 JIDs
        Assert.Equal("Bob", MessageBubbleViewModel.ResolveSenderDisplayName("bob@example.com"));
        Assert.Equal("John Doe", MessageBubbleViewModel.ResolveSenderDisplayName("john.doe@example.com"));
        Assert.Equal("Jane Smith", MessageBubbleViewModel.ResolveSenderDisplayName("jane_smith@example.com"));
        Assert.Equal("Alice", MessageBubbleViewModel.ResolveSenderDisplayName("alice@example.com/mobile"));

        // Groupchat (MUC / Conference) with resource
        Assert.Equal("Alice", MessageBubbleViewModel.ResolveSenderDisplayName("room@conference.example.com/Alice"));
        Assert.Equal("Richard", MessageBubbleViewModel.ResolveSenderDisplayName("team@muc.company.org/Richard"));

        // Bare domain
        Assert.Equal("domain.com", MessageBubbleViewModel.ResolveSenderDisplayName("domain.com"));
    }

    [Fact]
    public void MessageBubbleViewModel_SenderDisplayName_PropertyBehavior()
    {
        var msg = new ChatMessage
        {
            AccountJid = "me@example.com",
            RemoteJid = "john.doe@example.com",
            Direction = MessageDirection.Inbound,
            SenderJid = "john.doe@example.com/laptop",
            Body = "Hello world"
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg);
        Assert.Equal("john.doe@example.com/laptop", bubble.SenderName);
        Assert.Equal("John Doe", bubble.SenderDisplayName);

        // Custom display name override
        bubble.SenderDisplayName = "Johnny";
        Assert.Equal("Johnny", bubble.SenderDisplayName);
        Assert.Equal("john.doe@example.com/laptop", bubble.SenderName);

        // Reset custom display name back to null/empty
        bubble.SenderDisplayName = null!;
        Assert.Equal("John Doe", bubble.SenderDisplayName);
    }

    [Fact]
    public void ChatConversationViewModel_GetSenderDisplayName_HandlesOneOnOneAndGroupChat()
    {
        string account = "me@example.com";
        var peerJid = Jid.Parse("alice.cooper@example.com");

        // 1-on-1 conversation with friendly contact title
        var conv1 = new ChatConversationViewModel(
            account,
            peerJid.ToString(),
            "Alice Cooper",
            peerJid,
            isGroupChat: false,
            _messageRepo);

        var inboundMsg = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = peerJid.ToString(),
            Direction = MessageDirection.Inbound,
            SenderJid = "alice.cooper@example.com/phone",
            Body = "Hi there"
        };
        var outboundMsg = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = peerJid.ToString(),
            Direction = MessageDirection.Outbound,
            SenderJid = "me@example.com",
            Body = "Hello Alice"
        };

        Assert.Equal("Alice Cooper", conv1.GetSenderDisplayName(inboundMsg));
        Assert.Equal("Me", conv1.GetSenderDisplayName(outboundMsg));

        // Group chat conversation
        var roomJid = Jid.Parse("general@conference.example.com");
        var convGroup = new ChatConversationViewModel(
            account,
            roomJid.ToString(),
            "General Discussion",
            roomJid,
            isGroupChat: true,
            _messageRepo);

        var participantMsg = new ChatMessage
        {
            AccountJid = account,
            RemoteJid = roomJid.ToString(),
            Direction = MessageDirection.Inbound,
            SenderJid = "general@conference.example.com/Dave",
            Body = "Dave joined"
        };

        Assert.Equal("Dave", convGroup.GetSenderDisplayName(participantMsg));
    }

    [Fact]
    public void ChatConversationViewModel_OnTitleChanged_UpdatesInboundBubbleSenderDisplayNames()
    {
        string account = "me@example.com";
        var peerJid = Jid.Parse("bob@example.com");

        var conv = new ChatConversationViewModel(
            account,
            peerJid.ToString(),
            "bob@example.com",
            peerJid,
            isGroupChat: false,
            _messageRepo);

        conv.AddOrUpdateMessage(new ChatMessage
        {
            Id = "msg-1",
            AccountJid = account,
            RemoteJid = peerJid.ToString(),
            Direction = MessageDirection.Inbound,
            SenderJid = "bob@example.com/desktop",
            Body = "Hey!",
            Timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal("bob@example.com/desktop", conv.Messages[0].SenderName);
        Assert.Equal("Bob", conv.Messages[0].SenderDisplayName);

        // Update Title to a friendly contact name
        conv.Title = "Robert The Bruce";
        Assert.Equal("Robert The Bruce", conv.Messages[0].SenderDisplayName);
    }
}

