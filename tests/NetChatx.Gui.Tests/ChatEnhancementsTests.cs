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
}
