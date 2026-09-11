using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Almatter.App.Models;

/// <summary>Splits message text into plain-text and clickable-link segments, in reading order.</summary>
public static partial class MessageTextParser
{
    /// <summary>
    /// A Markdown [label](url) link (groups 1-2), a bare URL (group 3), or
    /// an @mention (group 4, username without the @) — one combined pass so
    /// a bare-URL match never re-fires on the URL already consumed inside a
    /// Markdown link's parentheses. The label allows a backslash-escaped
    /// "\[" / "\]" as literal characters (unescaped below) so a label
    /// that's itself a title containing brackets — e.g. "[Comparatif] ..."
    /// — doesn't end the link early. The mention's negative lookbehind
    /// keeps an email address's "@domain" part from being mistaken for one.
    /// </summary>
    [GeneratedRegex(@"\[((?:\\.|[^\[\]])+)\]\((https?://[^\s()]+)\)|(https?://[^\s<>""]+)|(?<![\w.])@([A-Za-z][A-Za-z0-9_.-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    private static string UnescapeLabel(string label) => Regex.Replace(label, @"\\(.)", "$1");

    /// <summary>Splits on the message's actual line breaks first, then link/mention-parses each line independently — see MessageTextLine for why a WrapPanel needs this rather than one flat segment list.</summary>
    public static IReadOnlyList<MessageTextLine> SplitLines(string text)
    {
        var rawLines = text.Split('\n');
        var lines = new List<MessageTextLine>(rawLines.Length);
        foreach (var rawLine in rawLines)
        {
            // A "\r\n" source (pasted from Windows-authored text, say) leaves
            // a trailing "\r" behind after splitting on "\n" alone.
            lines.Add(new MessageTextLine { Segments = Split(rawLine.TrimEnd('\r')) });
        }
        return lines;
    }

    public static IReadOnlyList<MessageTextSegment> Split(string text)
    {
        var segments = new List<MessageTextSegment>();
        var lastIndex = 0;

        foreach (Match match in LinkPattern().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                segments.Add(new MessageTextSegment { Text = text[lastIndex..match.Index], IsLink = false });
            }

            if (match.Groups[1].Success)
            {
                segments.Add(new MessageTextSegment { Text = UnescapeLabel(match.Groups[1].Value), IsLink = true, LinkUrl = match.Groups[2].Value });
                lastIndex = match.Index + match.Length;
            }
            else if (match.Groups[4].Success)
            {
                segments.Add(new MessageTextSegment { Text = "@" + match.Groups[4].Value, IsMention = true });
                lastIndex = match.Index + match.Length;
            }
            else
            {
                // Trailing punctuation is usually sentence punctuation, not part of the URL itself.
                var url = match.Groups[3].Value.TrimEnd('.', ',', ')', ']', '>', '!', '?', ';', ':');
                segments.Add(new MessageTextSegment { Text = url, IsLink = true, LinkUrl = url });
                lastIndex = match.Index + url.Length;
            }
        }

        if (lastIndex < text.Length)
        {
            segments.Add(new MessageTextSegment { Text = text[lastIndex..], IsLink = false });
        }

        if (segments.Count == 0)
        {
            segments.Add(new MessageTextSegment { Text = text, IsLink = false });
        }

        return segments;
    }
}
