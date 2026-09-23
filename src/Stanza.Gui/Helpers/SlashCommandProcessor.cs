using System;
using System.Text;

namespace Stanza.Gui.Helpers;

public enum SlashCommandResultType
{
    SendMessage,
    SystemMessage,
    ClearChat,
    ChangeStatus,
    SetTopic,
    JoinRoom,
    LeaveRoom,
    OpenChat,
    ChangeNick,
    Handled
}

public sealed class SlashCommandResult
{
    public SlashCommandResultType Type { get; init; }
    public string? MessageText { get; init; }
    public string? TargetJid { get; init; }
    public string? StatusShow { get; init; }
    public string? StatusMessage { get; init; }
    public string? SystemOutput { get; init; }
    public bool IsInvalid { get; init; }
    public bool IsError => IsInvalid;
}

public static class SlashCommandProcessor
{
    public static SlashCommandResult Process(string input, bool isGroupChat = false)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SlashCommandResult { Type = SlashCommandResultType.Handled };
        }

        var trimmed = input.Trim();

        // Escaped slash: `//command` -> send `/command`
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SendMessage,
                MessageText = input.Substring(1) // Strips first slash
            };
        }

        // Not a slash command if it doesn't start with `/`
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SendMessage,
                MessageText = input
            };
        }

        // Extract command and arguments
        var spaceIdx = trimmed.IndexOf(' ');
        var commandName = (spaceIdx < 0 ? trimmed.Substring(1) : trimmed.Substring(1, spaceIdx - 1)).ToLowerInvariant();
        var args = spaceIdx < 0 ? string.Empty : trimmed.Substring(spaceIdx + 1).Trim();

        return commandName switch
        {
            "me" => ProcessMeCommand(args),
            "shrug" => ProcessEmoticonCommand("¯\\_(ツ)_/¯", args),
            "tableflip" => ProcessEmoticonCommand("(╯°□°)╯︵ ┻━┻", args),
            "unflip" => ProcessEmoticonCommand("┬─┬ノ( º _ ºノ)", args),
            "clear" => new SlashCommandResult { Type = SlashCommandResultType.ClearChat },
            "status" => ProcessStatusCommand(args),
            "topic" or "subject" => ProcessTopicCommand(args, isGroupChat),
            "join" => ProcessJoinCommand(args),
            "leave" => new SlashCommandResult { Type = SlashCommandResultType.LeaveRoom },
            "msg" => ProcessMsgCommand(args),
            "nick" => ProcessNickCommand(args, isGroupChat),
            "help" => ProcessHelpCommand(),
            _ => new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = $"Unknown command: /{commandName}. Type /help for available commands.",
                IsInvalid = true
            }
        };
    }

    private static SlashCommandResult ProcessMeCommand(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /me <action>",
                IsInvalid = true
            };
        }

        // Send `/me <action>` literally over XMPP per protocol conventions
        return new SlashCommandResult
        {
            Type = SlashCommandResultType.SendMessage,
            MessageText = $"/me {args}"
        };
    }

    private static SlashCommandResult ProcessEmoticonCommand(string emoticon, string args)
    {
        var text = string.IsNullOrWhiteSpace(args)
            ? emoticon
            : $"{args} {emoticon}";

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.SendMessage,
            MessageText = text
        };
    }

    private static SlashCommandResult ProcessStatusCommand(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /status <available|away|dnd|xa> [status message]",
                IsInvalid = true
            };
        }

        var spaceIdx = args.IndexOf(' ');
        var show = (spaceIdx < 0 ? args : args.Substring(0, spaceIdx)).ToLowerInvariant();
        var statusMsg = spaceIdx < 0 ? null : args.Substring(spaceIdx + 1).Trim();

        var normalizedShow = show switch
        {
            "available" or "online" => "available",
            "away" => "away",
            "dnd" or "busy" => "dnd",
            "xa" => "xa",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(normalizedShow))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Invalid status mode. Valid options: available, away, dnd, xa.",
                IsInvalid = true
            };
        }

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.ChangeStatus,
            StatusShow = normalizedShow,
            StatusMessage = statusMsg
        };
    }

    private static SlashCommandResult ProcessTopicCommand(string args, bool isGroupChat)
    {
        if (!isGroupChat)
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "The /topic command can only be used in group chats.",
                IsInvalid = true
            };
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /topic <new subject>",
                IsInvalid = true
            };
        }

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.SetTopic,
            MessageText = args
        };
    }

    private static SlashCommandResult ProcessJoinCommand(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /join <room_jid>",
                IsInvalid = true
            };
        }

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.JoinRoom,
            TargetJid = args
        };
    }

    private static SlashCommandResult ProcessMsgCommand(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /msg <jid> [message]",
                IsInvalid = true
            };
        }

        var spaceIdx = args.IndexOf(' ');
        var jid = spaceIdx < 0 ? args : args.Substring(0, spaceIdx).Trim();
        var msg = spaceIdx < 0 ? null : args.Substring(spaceIdx + 1).Trim();

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.OpenChat,
            TargetJid = jid,
            MessageText = msg
        };
    }

    private static SlashCommandResult ProcessNickCommand(string args, bool isGroupChat)
    {
        if (!isGroupChat)
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "The /nick command can only be used in group chats.",
                IsInvalid = true
            };
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            return new SlashCommandResult
            {
                Type = SlashCommandResultType.SystemMessage,
                SystemOutput = "Usage: /nick <new_nickname>",
                IsInvalid = true
            };
        }

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.ChangeNick,
            MessageText = args
        };
    }

    private static SlashCommandResult ProcessHelpCommand()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available Slash Commands:");
        sb.AppendLine("• `/me <action>` — Send an action message (e.g. /me smiles)");
        sb.AppendLine("• `/shrug [text]` — Append ¯\\_(ツ)_/¯ to text");
        sb.AppendLine("• `/tableflip [text]` — Append (╯°□°)╯︵ ┻━┻ to text");
        sb.AppendLine("• `/unflip [text]` — Append ┬─┬ノ( º _ ºノ) to text");
        sb.AppendLine("• `/clear` — Clear current chat messages");
        sb.AppendLine("• `/status <available|away|dnd|xa> [status]` — Change presence status");
        sb.AppendLine("• `/topic <subject>` — Change group chat room subject");
        sb.AppendLine("• `/join <room_jid>` — Join or open a group chat room");
        sb.AppendLine("• `/leave` — Leave current chat room");
        sb.AppendLine("• `/msg <jid> [message]` — Open direct chat with a user");
        sb.AppendLine("• `/nick <new_nickname>` — Change nickname in group chat");
        sb.AppendLine("• `/help` — Display this help message");
        sb.AppendLine("• `//<command>` — Escape leading slash to send literally");

        return new SlashCommandResult
        {
            Type = SlashCommandResultType.SystemMessage,
            SystemOutput = sb.ToString().TrimEnd()
        };
    }
}
