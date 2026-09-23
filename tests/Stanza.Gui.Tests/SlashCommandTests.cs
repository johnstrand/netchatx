using System;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage.Models;
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
    public void MessageBubbleViewModel_FromChatMessage_MeCommand_FormatsItalics()
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
        Assert.Equal("_Friend waves hello_", bubble.Body);
    }
}
