using NetChatx.Tui.Commands;
using Xunit;

namespace NetChatx.Core.Tests;

public class TuiCommandProcessorTests
{
    [Fact]
    public void IsCommand_IdentifiesSlashCommands()
    {
        Assert.True(TuiCommandProcessor.IsCommand("/connect"));
        Assert.True(TuiCommandProcessor.IsCommand("/msg"));
        Assert.False(TuiCommandProcessor.IsCommand("Hello world"));
        Assert.False(TuiCommandProcessor.IsCommand(""));
    }

    [Fact]
    public void Parse_QuotedArguments_PreservesQuotedText()
    {
        string input = "/status away \"In a long meeting\"";
        var cmd = TuiCommandProcessor.Parse(input);

        Assert.Equal("/status", cmd.Command);
        Assert.Equal(2, cmd.Arguments.Length);
        Assert.Equal("away", cmd.Arguments[0]);
        Assert.Equal("In a long meeting", cmd.Arguments[1]);
    }

    [Fact]
    public void GetCompletions_CommandPrefix_ReturnsMatchingCommands()
    {
        var completions = TuiCommandProcessor.GetCompletions("/c", []);

        Assert.Contains("/connect", completions);
        Assert.Contains("/close", completions);
        Assert.Contains("/clear", completions);
        Assert.DoesNotContain("/join", completions);
    }

    [Fact]
    public void GetCompletions_ContextualContacts_MatchesPrefix()
    {
        var contacts = new[] { "alice@example.com", "alex@example.com", "bob@example.com" };
        var completions = TuiCommandProcessor.GetCompletions("al", contacts);

        Assert.Equal(2, completions.Length);
        Assert.Contains("alice@example.com", completions);
        Assert.Contains("alex@example.com", completions);
    }
}
