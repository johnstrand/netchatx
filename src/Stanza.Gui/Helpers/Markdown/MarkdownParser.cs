using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Stanza.Gui.Helpers.Markdown;

public static class MarkdownParser
{
    private static readonly Regex InlinePattern = new(
        @"(?<code>`[^`\r\n]+`)|" +
        @"(?<mdlink>\[(?<linktext>[^\]\r\n]+)\]\((?<linkurl>[^\)\s]+)\))|" +
        @"(?<autolink>(?:https?://|mailto:|xmpp:|www\.)[^\s<>""]+)|" +
        @"(?<bolditalic>\*\*\*(?<bitext>[^\*\r\n\s](?:[^\*\r\n]*?[^\*\r\n\s])?)\*\*\*|___(?<bitext>[^_\r\n\s](?:[^_\r\n]*?[^_\r\n\s])?)___)|" +
        @"(?<bold>\*\*(?<btext>[^\*\r\n\s](?:[^\*\r\n]*?[^\*\r\n\s])?)\*\*|__(?<btext>[^_\r\n\s](?:[^_\r\n]*?[^_\r\n\s])?)__|(?<=(?:^|[\s\p{P}]))\*(?<btext>[^\*\r\n\s](?:[^\*\r\n]*?[^\*\r\n\s])?)\*(?=(?:$|[\s\p{P}])))|" +
        @"(?<italic>(?<=(?:^|[\s\p{P}]))_(?<itext>[^_\r\n\s](?:[^_\r\n]*?[^_\r\n\s])?)_(?=(?:$|[\s\p{P}])))|" +
        @"(?<strike>~~(?<stext>[^~\r\n\s](?:[^~\r\n]*?[^~\r\n\s])?)~~|(?<=(?:^|[\s\p{P}]))~(?<stext>[^~\r\n\s](?:[^~\r\n]*?[^~\r\n\s])?)~(?=(?:$|[\s\p{P}])))",
        RegexOptions.Compiled);

    public static bool HasMarkdownOrLinks(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '*' or '_' or '~' or '`' or '#' or '>') return true;
            if (c == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    return true;
            }
            if ((c is 'h' or 'H') && i + 7 <= text.Length)
            {
                if (text.Substring(i, 7).Equals("http://", StringComparison.OrdinalIgnoreCase) ||
                    (i + 8 <= text.Length && text.Substring(i, 8).Equals("https://", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            if ((c is 'w' or 'W') && i + 4 <= text.Length && text.Substring(i, 4).Equals("www.", StringComparison.OrdinalIgnoreCase))
                return true;
            if ((c is 'm' or 'M') && i + 7 <= text.Length && text.Substring(i, 7).Equals("mailto:", StringComparison.OrdinalIgnoreCase))
                return true;
            if ((c is 'x' or 'X') && i + 5 <= text.Length && text.Substring(i, 5).Equals("xmpp:", StringComparison.OrdinalIgnoreCase))
                return true;
            if ((c == '-' || c == '*') && (i == 0 || text[i - 1] == '\n') && i + 1 < text.Length && text[i + 1] == ' ')
                return true;
            if (char.IsDigit(c) && (i == 0 || text[i - 1] == '\n') && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == ' ')
                return true;
        }

        return false;
    }

    public static MarkdownDocument Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new MarkdownDocument([]);
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var blocks = new List<IMarkdownBlock>();

        var lineIndex = 0;
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];

            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                continue;
            }

            // 1. Preformatted Code Block (```)
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var trimmed = line.TrimStart();
                var language = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : null;
                if (string.IsNullOrWhiteSpace(language)) language = null;

                var codeLines = new List<string>();
                lineIndex++;
                while (lineIndex < lines.Length)
                {
                    if (lines[lineIndex].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        lineIndex++;
                        break;
                    }
                    codeLines.Add(lines[lineIndex]);
                    lineIndex++;
                }

                blocks.Add(new MarkdownCodeBlock(string.Join("\n", codeLines), language));
                continue;
            }

            // 2. Blockquote (>)
            if (line.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (lineIndex < lines.Length && lines[lineIndex].StartsWith('>'))
                {
                    var qLine = lines[lineIndex].Substring(1);
                    if (qLine.StartsWith(' ')) qLine = qLine.Substring(1);
                    quoteLines.Add(qLine);
                    lineIndex++;
                }

                var childText = string.Join("\n", quoteLines);
                var childDoc = Parse(childText);
                blocks.Add(new MarkdownBlockquote(childDoc.Blocks));
                continue;
            }

            // 3. Headers (# Header)
            if (line.StartsWith('#'))
            {
                var level = 0;
                while (level < line.Length && line[level] == '#') level++;
                if (level <= 6 && level < line.Length && line[level] == ' ')
                {
                    var headerContent = line.Substring(level + 1).Trim();
                    blocks.Add(new MarkdownHeader(level, ParseInlines(headerContent)));
                    lineIndex++;
                    continue;
                }
            }

            // 4. Lists (- item, * item, 1. item)
            if (IsListItem(line, out bool isOrdered, out int listNum, out string itemText))
            {
                var listItems = new List<MarkdownListItem>();
                var currentNum = isOrdered ? listNum : 1;
                listItems.Add(new MarkdownListItem(currentNum, ParseInlines(itemText)));
                lineIndex++;

                while (lineIndex < lines.Length && IsListItem(lines[lineIndex], out bool nextOrdered, out int nextNum, out string nextText))
                {
                    if (nextOrdered != isOrdered) break;
                    currentNum = nextOrdered ? nextNum : currentNum + 1;
                    listItems.Add(new MarkdownListItem(currentNum, ParseInlines(nextText)));
                    lineIndex++;
                }

                blocks.Add(new MarkdownList(isOrdered, listItems));
                continue;
            }

            // 5. Normal paragraph lines
            var paraLines = new List<string>();
            while (lineIndex < lines.Length)
            {
                var cur = lines[lineIndex];
                if (cur.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                    cur.StartsWith('>') ||
                    (cur.StartsWith('#') && cur.Length > 1 && cur.IndexOf(' ') > 0 && cur.IndexOf(' ') <= 6) ||
                    IsListItem(cur, out _, out _, out _))
                {
                    break;
                }

                paraLines.Add(cur);
                lineIndex++;
            }

            if (paraLines.Count > 0)
            {
                var inlines = new List<IMarkdownInline>();
                for (int p = 0; p < paraLines.Count; p++)
                {
                    if (p > 0)
                    {
                        inlines.Add(new MarkdownLineBreak());
                    }
                    inlines.AddRange(ParseInlines(paraLines[p]));
                }
                blocks.Add(new MarkdownParagraph(inlines));
            }
        }

        return new MarkdownDocument(blocks);
    }

    private static bool IsListItem(string line, out bool isOrdered, out int number, out string content)
    {
        isOrdered = false;
        number = 1;
        content = string.Empty;

        if (string.IsNullOrWhiteSpace(line)) return false;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            isOrdered = false;
            content = trimmed.Substring(2);
            return true;
        }

        var dotIdx = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dotIdx > 0 && dotIdx <= 6)
        {
            var numPart = trimmed.Substring(0, dotIdx);
            if (int.TryParse(numPart, out int num))
            {
                isOrdered = true;
                number = num;
                content = trimmed.Substring(dotIdx + 2);
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<IMarkdownInline> ParseInlines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var inlines = new List<IMarkdownInline>();
        var matches = InlinePattern.Matches(text);

        if (matches.Count == 0)
        {
            inlines.Add(new MarkdownText(text));
            return inlines;
        }

        var currentIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index < currentIndex)
            {
                continue;
            }

            // Text preceding this match
            if (match.Index > currentIndex)
            {
                inlines.Add(new MarkdownText(text.Substring(currentIndex, match.Index - currentIndex)));
            }

            if (match.Groups["code"].Success)
            {
                var raw = match.Groups["code"].Value;
                var codeVal = raw.Substring(1, raw.Length - 2);
                inlines.Add(new MarkdownInlineCode(codeVal));
                currentIndex = match.Index + match.Length;
            }
            else if (match.Groups["mdlink"].Success)
            {
                var linkText = match.Groups["linktext"].Value;
                var linkUrl = match.Groups["linkurl"].Value;
                if (LinkParser.IsValidUrl(linkUrl, out var targetUri))
                {
                    inlines.Add(new MarkdownLink(linkText, targetUri));
                }
                else
                {
                    inlines.Add(new MarkdownText(match.Value));
                }
                currentIndex = match.Index + match.Length;
            }
            else if (match.Groups["autolink"].Success)
            {
                var rawUrl = match.Groups["autolink"].Value;
                var (cleanUrl, trailing) = LinkParser.TrimTrailingPunctuation(rawUrl);
                if (LinkParser.IsValidUrl(cleanUrl, out var targetUri))
                {
                    inlines.Add(new MarkdownLink(cleanUrl, targetUri));
                    currentIndex = match.Index + cleanUrl.Length;
                    if (!string.IsNullOrEmpty(trailing))
                    {
                        inlines.Add(new MarkdownText(trailing));
                        currentIndex += trailing.Length;
                    }
                }
                else
                {
                    inlines.Add(new MarkdownText(rawUrl));
                    currentIndex = match.Index + rawUrl.Length;
                }
            }
            else if (match.Groups["bolditalic"].Success)
            {
                var content = match.Groups["bitext"].Value;
                inlines.Add(new MarkdownBoldItalic(ParseInlines(content)));
                currentIndex = match.Index + match.Length;
            }
            else if (match.Groups["bold"].Success)
            {
                var content = match.Groups["btext"].Value;
                inlines.Add(new MarkdownBold(ParseInlines(content)));
                currentIndex = match.Index + match.Length;
            }
            else if (match.Groups["italic"].Success)
            {
                var content = match.Groups["itext"].Value;
                inlines.Add(new MarkdownItalic(ParseInlines(content)));
                currentIndex = match.Index + match.Length;
            }
            else if (match.Groups["strike"].Success)
            {
                var content = match.Groups["stext"].Value;
                inlines.Add(new MarkdownStrikethrough(ParseInlines(content)));
                currentIndex = match.Index + match.Length;
            }
            else
            {
                inlines.Add(new MarkdownText(match.Value));
                currentIndex = match.Index + match.Length;
            }
        }

        if (currentIndex < text.Length)
        {
            inlines.Add(new MarkdownText(text.Substring(currentIndex)));
        }

        return inlines;
    }
}
