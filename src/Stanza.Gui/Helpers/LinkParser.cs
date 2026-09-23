using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Stanza.Gui.Helpers;

public sealed record TextSegment(string Text, bool IsLink, string? NavigateUri = null);

public static class LinkParser
{
    private static readonly Regex CombinedLinkRegex = new(
        @"(?<mdlink>\[(?<label>[^\]\r\n]+)\]\((?<mdurl>[^\)\s]+)\))|(?<url>(?:https?://|mailto:|xmpp:|www\.)[^\s<>""]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<TextSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = new List<TextSegment>();
        var matches = CombinedLinkRegex.Matches(text);

        if (matches.Count == 0)
        {
            segments.Add(new TextSegment(text, IsLink: false));
            return segments;
        }

        var currentIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index < currentIndex)
            {
                continue;
            }

            if (match.Groups["mdlink"].Success)
            {
                var label = match.Groups["label"].Value;
                var mdurl = match.Groups["mdurl"].Value;

                if (IsValidUrl(mdurl, out var targetUri))
                {
                    if (match.Index > currentIndex)
                    {
                        segments.Add(new TextSegment(text.Substring(currentIndex, match.Index - currentIndex), IsLink: false));
                    }

                    segments.Add(new TextSegment(label, IsLink: true, NavigateUri: targetUri));
                    currentIndex = match.Index + match.Length;
                    continue;
                }
            }

            var rawCandidate = match.Groups["url"].Value;
            if (string.IsNullOrEmpty(rawCandidate))
            {
                continue;
            }

            var candidateStart = match.Index;
            var (cleanUrl, trailingPunctuation) = TrimTrailingPunctuation(rawCandidate);

            if (IsValidUrl(cleanUrl, out var targetUri2))
            {
                // Preceding plain text
                if (candidateStart > currentIndex)
                {
                    segments.Add(new TextSegment(text.Substring(currentIndex, candidateStart - currentIndex), IsLink: false));
                }

                // Link segment
                segments.Add(new TextSegment(cleanUrl, IsLink: true, NavigateUri: targetUri2));
                currentIndex = candidateStart + cleanUrl.Length;

                // Trailing punctuation from trimming
                if (!string.IsNullOrEmpty(trailingPunctuation))
                {
                    segments.Add(new TextSegment(trailingPunctuation, IsLink: false));
                    currentIndex += trailingPunctuation.Length;
                }
            }
        }

        // Remaining text
        if (currentIndex < text.Length)
        {
            segments.Add(new TextSegment(text.Substring(currentIndex), IsLink: false));
        }

        return MergeAdjacentPlainTextSegments(segments);
    }

    public static bool IsValidUrl(string url, out string targetUri)
    {
        targetUri = url;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var testUrl = url;
        if (testUrl.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            testUrl = "https://" + testUrl;
        }

        if (Uri.TryCreate(testUrl, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == Uri.UriSchemeMailto ||
                uri.Scheme.Equals("xmpp", StringComparison.OrdinalIgnoreCase))
            {
                if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.Host))
                {
                    return false;
                }

                targetUri = uri.AbsoluteUri;
                return true;
            }
        }

        return false;
    }

    public static (string CleanUrl, string Trailing) TrimTrailingPunctuation(string candidate)
    {
        var trailing = new StringBuilder();
        var url = candidate;

        while (url.Length > 0)
        {
            var last = url[^1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '>' or '<')
            {
                trailing.Insert(0, last);
                url = url[..^1];
            }
            else if (last == ')')
            {
                var openCount = 0;
                var closeCount = 0;
                foreach (var c in url)
                {
                    if (c == '(') openCount++;
                    else if (c == ')') closeCount++;
                }

                if (closeCount > openCount)
                {
                    trailing.Insert(0, last);
                    url = url[..^1];
                }
                else
                {
                    break;
                }
            }
            else if (last == ']')
            {
                var openCount = 0;
                var closeCount = 0;
                foreach (var c in url)
                {
                    if (c == '[') openCount++;
                    else if (c == ']') closeCount++;
                }

                if (closeCount > openCount)
                {
                    trailing.Insert(0, last);
                    url = url[..^1];
                }
                else
                {
                    break;
                }
            }
            else if (last == '}')
            {
                var openCount = 0;
                var closeCount = 0;
                foreach (var c in url)
                {
                    if (c == '{') openCount++;
                    else if (c == '}') closeCount++;
                }

                if (closeCount > openCount)
                {
                    trailing.Insert(0, last);
                    url = url[..^1];
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return (url, trailing.ToString());
    }

    private static IReadOnlyList<TextSegment> MergeAdjacentPlainTextSegments(List<TextSegment> segments)
    {
        if (segments.Count <= 1) return segments;

        var merged = new List<TextSegment>();
        var sb = new StringBuilder();

        foreach (var seg in segments)
        {
            if (!seg.IsLink)
            {
                sb.Append(seg.Text);
            }
            else
            {
                if (sb.Length > 0)
                {
                    merged.Add(new TextSegment(sb.ToString(), IsLink: false));
                    sb.Clear();
                }
                merged.Add(seg);
            }
        }

        if (sb.Length > 0)
        {
            merged.Add(new TextSegment(sb.ToString(), IsLink: false));
        }

        return merged;
    }
}
