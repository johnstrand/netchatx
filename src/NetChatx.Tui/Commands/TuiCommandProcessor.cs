using System.Text.RegularExpressions;

namespace NetChatx.Tui.Commands;

public sealed class ParsedCommand
{
    public required string Command { get; init; }
    public required string[] Arguments { get; init; }
    public required string RawInput { get; init; }
}

public static class TuiCommandProcessor
{
    private static readonly string[] KnownCommands =
    [
        "/connect",
        "/disconnect",
        "/join",
        "/leave",
        "/msg",
        "/query",
        "/close",
        "/clear",
        "/history",
        "/search",
        "/roster",
        "/status",
        "/omemo",
        "/theme",
        "/help",
        "/quit"
    ];

    public static bool IsCommand(string input) => input.StartsWith('/');

    public static ParsedCommand Parse(string input)
    {
        string trimmed = input.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return new ParsedCommand
            {
                Command = "",
                Arguments = [trimmed],
                RawInput = input
            };
        }

        // Split by space, keeping quoted arguments intact
        var matches = Regex.Matches(trimmed, @"[\""].+?[\""]|[^ ]+");
        if (matches.Count == 0)
        {
            return new ParsedCommand { Command = "", Arguments = [], RawInput = input };
        }

        string cmd = matches[0].Value.ToLowerInvariant();
        var args = new string[matches.Count - 1];
        for (int i = 1; i < matches.Count; i++)
        {
            args[i - 1] = matches[i].Value.Trim('"');
        }

        return new ParsedCommand
        {
            Command = cmd,
            Arguments = args,
            RawInput = input
        };
    }

    public static string[] GetCompletions(string currentInput, IEnumerable<string> contextualItems)
    {
        if (string.IsNullOrEmpty(currentInput))
            return [];

        if (currentInput.StartsWith('/'))
        {
            return KnownCommands
                .Where(c => c.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        // Contextual nickname/JID autocomplete
        return contextualItems
            .Where(item => item.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
