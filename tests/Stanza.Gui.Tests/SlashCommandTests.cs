using System;
using System.IO;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Helpers;
using Stanza.Gui.Tests.Mocks;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public sealed class SlashCommandTests
{
    [Fact]
    public void Process_NormalMessage_ReturnsSendMessage()
    {
        var res = SlashCommandProcessor.Process("Hello world!");
        Assert.Equal(SlashCommandResultType.SendMessage, res.Type);
        Assert.Equal("Hello world!", res.MessageText);
    }

    [Fact]
    public void Process_EscapedSlash_StripsLeadingSlash()
    {
        var res = SlashCommandProcessor.Process("//me is testing");
        Assert.Equal(SlashCommandResultType.SendMessage, res.Type);
        Assert.Equal("/me is testing", res.MessageText);
    }

    [Fact]
    public void Process_MeCommand_SendsLiteralCommand()
    {
        var res = SlashCommandProcessor.Process("/me smiles warmly");
        Assert.Equal(SlashCommandResultType.SendMessage, res.Type);
        Assert.Equal("/me smiles warmly", res.MessageText);
    }

    [Fact]
    public void Process_MeCommand_NoArgs_ReturnsUsageSystemMessage()
    {
        var res = SlashCommandProcessor.Process("/me");
        Assert.Equal(SlashCommandResultType.SystemMessage, res.Type);
        Assert.Contains("Usage: /me", res.SystemOutput);
    }

    [Fact]
    public void Process_ShrugCommand_AppendsEmoticon()
    {
        var res1 = SlashCommandProcessor.Process("/shrug");
        Assert.Equal(SlashCommandResultType.SendMessage, res1.Type);
        Assert.Equal("¯\\_(ツ)_/¯", res1.MessageText);

        var res2 = SlashCommandProcessor.Process("/shrug I don't know");
        Assert.Equal(SlashCommandResultType.SendMessage, res2.Type);
        Assert.Equal("I don't know ¯\\_(ツ)_/¯", res2.MessageText);
    }

    [Fact]
    public void Process_TableflipAndUnflip_AppendsEmoticons()
    {
        var res1 = SlashCommandProcessor.Process("/tableflip error");
        Assert.Equal(SlashCommandResultType.SendMessage, res1.Type);
        Assert.Equal("error (╯°□°)╯︵ ┻━┻", res1.MessageText);

        var res2 = SlashCommandProcessor.Process("/unflip fixed");
        Assert.Equal(SlashCommandResultType.SendMessage, res2.Type);
        Assert.Equal("fixed ┬─┬ノ( º _ ºノ)", res2.MessageText);
    }

    [Fact]
    public void Process_ClearCommand_ReturnsClearChat()
    {
        var res = SlashCommandProcessor.Process("/clear");
        Assert.Equal(SlashCommandResultType.ClearChat, res.Type);
    }

    [Fact]
    public void Process_StatusCommand_ValidAndInvalid()
    {
        var valid = SlashCommandProcessor.Process("/status away In a meeting");
        Assert.Equal(SlashCommandResultType.ChangeStatus, valid.Type);
        Assert.Equal("away", valid.StatusShow);
        Assert.Equal("In a meeting", valid.StatusMessage);

        var invalid = SlashCommandProcessor.Process("/status invalid_mode");
        Assert.Equal(SlashCommandResultType.SystemMessage, invalid.Type);
        Assert.Contains("Invalid status mode", invalid.SystemOutput);
    }

    [Fact]
    public void Process_TopicCommand_OnlyInGroupChat()
    {
        var direct = SlashCommandProcessor.Process("/topic New Subject", isGroupChat: false);
        Assert.Equal(SlashCommandResultType.SystemMessage, direct.Type);
        Assert.Contains("only be used in group chats", direct.SystemOutput);

        var group = SlashCommandProcessor.Process("/topic New Subject", isGroupChat: true);
        Assert.Equal(SlashCommandResultType.SetTopic, group.Type);
        Assert.Equal("New Subject", group.MessageText);
    }

    [Fact]
    public void Process_JoinCommand_ReturnsJoinRoom()
    {
        var res = SlashCommandProcessor.Process("/join room@conference.example.com");
        Assert.Equal(SlashCommandResultType.JoinRoom, res.Type);
        Assert.Equal("room@conference.example.com", res.TargetJid);
    }

    [Fact]
    public void Process_MsgCommand_ReturnsOpenChat()
    {
        var res = SlashCommandProcessor.Process("/msg user@example.com Hello there");
        Assert.Equal(SlashCommandResultType.OpenChat, res.Type);
        Assert.Equal("user@example.com", res.TargetJid);
        Assert.Equal("Hello there", res.MessageText);
    }

    [Fact]
    public void Process_HelpCommand_ReturnsList()
    {
        var res = SlashCommandProcessor.Process("/help");
        Assert.Equal(SlashCommandResultType.SystemMessage, res.Type);
        Assert.Contains("Available Slash Commands:", res.SystemOutput);
        Assert.Contains("/me", res.SystemOutput);
        Assert.Contains("/shrug", res.SystemOutput);
    }

    [Fact]
    public void Process_UnknownCommand_ReturnsWarningSystemMessage()
    {
        var res = SlashCommandProcessor.Process("/foobar test");
        Assert.Equal(SlashCommandResultType.SystemMessage, res.Type);
        Assert.Contains("Unknown command: /foobar", res.SystemOutput);
    }

    [Fact]
    public void MessageBubbleViewModel_FromChatMessage_MeCommand_FormatsItalicsAndBoldSenderName()
    {
        var msg = new ChatMessage
        {
            AccountJid = "user@example.com",
            RemoteJid = "friend@example.com",
            SenderJid = "friend@example.com",
            Body = "/me waves hello",
            Direction = MessageDirection.Inbound
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg, "user@example.com", senderDisplayName: "Friend");
        Assert.True(bubble.IsActionMessage);
        Assert.Equal("/me waves hello", bubble.RawBody);
        Assert.Equal("waves hello", bubble.ActionContent);
        Assert.Equal("_**Friend** waves hello_", bubble.Body);
        Assert.Equal(Avalonia.Media.Brushes.Transparent, bubble.BubbleBackground);
    }

    [Fact]
    public void Process_InvalidAndValidCommands_SetIsInvalidFlagCorrectly()
    {
        var unknown = SlashCommandProcessor.Process("/invalidcommand");
        Assert.True(unknown.IsInvalid);
        Assert.True(unknown.IsError);

        var meNoArgs = SlashCommandProcessor.Process("/me");
        Assert.True(meNoArgs.IsInvalid);

        var statusBad = SlashCommandProcessor.Process("/status invalid_mode");
        Assert.True(statusBad.IsInvalid);

        var topicDirect = SlashCommandProcessor.Process("/topic subject", isGroupChat: false);
        Assert.True(topicDirect.IsInvalid);

        var joinEmpty = SlashCommandProcessor.Process("/join");
        Assert.True(joinEmpty.IsInvalid);

        var msgEmpty = SlashCommandProcessor.Process("/msg");
        Assert.True(msgEmpty.IsInvalid);

        var nickDirect = SlashCommandProcessor.Process("/nick test", isGroupChat: false);
        Assert.True(nickDirect.IsInvalid);

        var help = SlashCommandProcessor.Process("/help");
        Assert.False(help.IsInvalid);

        var validSend = SlashCommandProcessor.Process("/shrug");
        Assert.False(validSend.IsInvalid);
    }

    [Fact]
    public async Task ChatConversationViewModel_InvalidSlashCommand_ShowsBannerAndDoesNotAddSystemMessage()
    {
        var dbPath = $"test_slash_banner_{Guid.NewGuid():N}.db";
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var messageRepo = new MessageRepository(dbContext);
            var account = "user@example.com";
            var remote = Jid.Parse("peer@example.com");
            var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());

            var conv = new ChatConversationViewModel(
                account,
                remote.ToString(),
                "Peer",
                remote,
                isGroupChat: false,
                messageRepo,
                client: client);

            Assert.False(conv.ShowSlashCommandWarning);
            Assert.Null(conv.SlashCommandWarningText);

            conv.InputText = "/foobar something";
            await conv.SendMessageAsync();

            Assert.True(conv.ShowSlashCommandWarning);
            Assert.Contains("Unknown command: /foobar", conv.SlashCommandWarningText);
            Assert.Empty(conv.Messages);

            // Manual dismiss
            conv.DismissSlashCommandWarningCommand.Execute(null);
            Assert.False(conv.ShowSlashCommandWarning);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ChatConversationViewModel_InvalidSlashCommand_AutoDismissesAfterDelay()
    {
        var dbPath = $"test_slash_autodismiss_{Guid.NewGuid():N}.db";
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var messageRepo = new MessageRepository(dbContext);
            var account = "user@example.com";
            var remote = Jid.Parse("peer@example.com");
            var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());

            var conv = new ChatConversationViewModel(
                account,
                remote.ToString(),
                "Peer",
                remote,
                isGroupChat: false,
                messageRepo,
                client: client)
            {
                SlashCommandWarningDuration = TimeSpan.FromMilliseconds(50)
            };

            conv.InputText = "/unknowncmd";
            await conv.SendMessageAsync();

            Assert.True(conv.ShowSlashCommandWarning);
            Assert.Contains("Unknown command: /unknowncmd", conv.SlashCommandWarningText);

            // Wait for auto-dismiss timer to fire
            await Task.Delay(120);

            Assert.False(conv.ShowSlashCommandWarning);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ChatConversationViewModel_HelpCommand_ShowsSystemMessageAndNoWarningBanner()
    {
        var dbPath = $"test_slash_help_{Guid.NewGuid():N}.db";
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var messageRepo = new MessageRepository(dbContext);
            var account = "user@example.com";
            var remote = Jid.Parse("peer@example.com");
            var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());

            var conv = new ChatConversationViewModel(
                account,
                remote.ToString(),
                "Peer",
                remote,
                isGroupChat: false,
                messageRepo,
                client: client);

            conv.InputText = "/help";
            await conv.SendMessageAsync();

            Assert.False(conv.ShowSlashCommandWarning);
            Assert.Single(conv.Messages);
            Assert.Contains("Available Slash Commands:", conv.Messages[0].Body);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ChatConversationViewModel_SendingValidMessage_DismissesActiveWarningBanner()
    {
        var dbPath = $"test_slash_dismiss_on_send_{Guid.NewGuid():N}.db";
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var messageRepo = new MessageRepository(dbContext);
            var account = "user@example.com";
            var remote = Jid.Parse("peer@example.com");
            var client = new XmppClient(new XmppClientOptions { Jid = Jid.Parse(account), Password = "pw" }, new LoopbackTransport());

            var conv = new ChatConversationViewModel(
                account,
                remote.ToString(),
                "Peer",
                remote,
                isGroupChat: false,
                messageRepo,
                client: client);

            conv.InputText = "/invalidcmd";
            await conv.SendMessageAsync();

            Assert.True(conv.ShowSlashCommandWarning);

            conv.InputText = "A normal message";
            await conv.SendMessageAsync();

            Assert.False(conv.ShowSlashCommandWarning);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
