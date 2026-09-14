using System.Collections.Generic;
using System.Text;
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

    /// <summary>
    /// A ":shortcode:". Run only over the plain-text runs left by the pass
    /// above, never over the whole line, so it can't fire on the colons
    /// inside a URL that has already been recognised as a link.
    /// </summary>
    [GeneratedRegex(@":([A-Za-z0-9_+\-]{1,64}):")]
    private static partial Regex EmojiPattern();

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
                AddText(segments, text[lastIndex..match.Index]);
            }

            if (match.Groups[1].Success)
            {
                segments.Add(new LinkSegment { Text = UnescapeLabel(match.Groups[1].Value), LinkUrl = match.Groups[2].Value });
                lastIndex = match.Index + match.Length;
            }
            else if (match.Groups[4].Success)
            {
                segments.Add(new MentionSegment { Text = "@" + match.Groups[4].Value });
                lastIndex = match.Index + match.Length;
            }
            else
            {
                // Trailing punctuation is usually sentence punctuation, not part of the URL itself.
                var url = match.Groups[3].Value.TrimEnd('.', ',', ')', ']', '>', '!', '?', ';', ':');
                segments.Add(new LinkSegment { Text = url, LinkUrl = url });
                lastIndex = match.Index + url.Length;
            }
        }

        if (lastIndex < text.Length)
        {
            AddText(segments, text[lastIndex..]);
        }

        if (segments.Count == 0)
        {
            segments.Add(new PlainTextSegment { Text = text });
        }

        return segments;
    }

    /// <summary>
    /// Adds a run of ordinary text, expanding any ":shortcode:" in it.
    ///
    /// A standard emoji is substituted straight into the text, so it stays
    /// part of the sentence and wraps with it rather than becoming its own
    /// control. Anything else becomes a segment of its own, because a custom
    /// emoji has to be drawn as a picture once the server's copy arrives.
    /// </summary>
    private static void AddText(List<MessageTextSegment> segments, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        StringBuilder? rebuilt = null;
        var lastIndex = 0;

        foreach (Match match in EmojiPattern().Matches(text))
        {
            var name = match.Groups[1].Value;
            var known = EmojiShortcodes.IsKnown(name);

            // "10:30:45" is a time, not an emoji called "30". A name made
            // only of digits is never a real shortcode, and treating one as
            // a custom emoji would mean silently replacing a timestamp with
            // a picture if the server happened to have that name.
            if (!known && !HasLetter(name))
            {
                continue;
            }

            rebuilt ??= new StringBuilder();
            rebuilt.Append(text, lastIndex, match.Index - lastIndex);
            lastIndex = match.Index + match.Length;

            if (known)
            {
                rebuilt.Append(EmojiShortcodes.ToGlyph(name));
                continue;
            }

            if (rebuilt.Length > 0)
            {
                segments.Add(new PlainTextSegment { Text = rebuilt.ToString() });
                rebuilt.Clear();
            }
            segments.Add(new CustomEmojiSegment { Text = match.Value, EmojiName = name });
        }

        // Nothing in this run was a shortcode — hand back the one segment the
        // parser produced before any of this existed.
        if (rebuilt is null)
        {
            segments.Add(new PlainTextSegment { Text = text });
            return;
        }

        rebuilt.Append(text, lastIndex, text.Length - lastIndex);
        if (rebuilt.Length > 0)
        {
            segments.Add(new PlainTextSegment { Text = rebuilt.ToString() });
        }
    }

    private static bool HasLetter(string name)
    {
        foreach (var c in name)
        {
            if (char.IsAsciiLetter(c))
            {
                return true;
            }
        }
        return false;
    }
}
