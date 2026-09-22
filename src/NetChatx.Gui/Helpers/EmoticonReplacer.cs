using System;
using System.Collections.Generic;
using System.Linq;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.Helpers;

public static class EmoticonReplacer
{
    public static (string ReplacedText, bool WasReplaced) ReplaceEmoticons(string input, IEnumerable<EmoticonMapping> mappings)
    {
        if (string.IsNullOrEmpty(input))
        {
            return (input, false);
        }

        // Sort mappings by shortcut length descending so longer shortcuts like ":-)" are checked before ":)"
        var sortedMappings = mappings
            .Where(m => !string.IsNullOrEmpty(m.Shortcut) && !string.IsNullOrEmpty(m.Emoji))
            .OrderByDescending(m => m.Shortcut.Length)
            .ToList();

        if (sortedMappings.Count == 0)
        {
            return (input, false);
        }

        var result = input;
        var wasReplaced = false;

        foreach (var mapping in sortedMappings)
        {
            if (result.Contains(mapping.Shortcut, StringComparison.Ordinal))
            {
                result = result.Replace(mapping.Shortcut, mapping.Emoji, StringComparison.Ordinal);
                wasReplaced = true;
            }
        }

        return (result, wasReplaced);
    }
}
