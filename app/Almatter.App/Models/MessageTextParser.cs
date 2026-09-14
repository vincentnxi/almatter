using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Almatter.App.Models;

/// <summary>
/// Reads a message's Markdown the way Mattermost writes it: blocks first
/// (code fences, tables, headings, quotes, lists, rules), then, inside each,
/// emphasis, inline code, links, mentions and emoji shortcodes.
///
/// Deliberately a small hand-written reader for the subset people actually
/// type in chat rather than a full CommonMark library: it has to stay light,
/// and anything it doesn't recognise simply shows as the text that was typed,
/// which is exactly how every message read before any of this existed.
/// </summary>
public static partial class MessageTextParser
{
    /// <summary>
    /// Anchored at the position being read (\G): a Markdown [label](url) link
    /// (groups 1-2), a bare URL (group 3), or an @mention (group 4, username
    /// without the @). The label allows a backslash-escaped "\[" / "\]" as
    /// literal characters so a label that's itself a title containing
    /// brackets — e.g. "[Comparatif] ..." — doesn't end the link early. The
    /// mention's negative lookbehind keeps an email address's "@domain" part
    /// from being mistaken for one.
    /// </summary>
    [GeneratedRegex(@"\G(?:\[((?:\\.|[^\[\]])+)\]\((https?://[^\s()]+)\)|(https?://[^\s<>""]+)|(?<![\w.])@([A-Za-z][A-Za-z0-9_.-]*))", RegexOptions.IgnoreCase)]
    private static partial Regex LinkAtPattern();

    /// <summary>
    /// A ":shortcode:". Run only over the plain-text runs left once links and
    /// code are taken out, never over the whole line, so it can't fire on the
    /// colons inside a URL or a snippet of code.
    /// </summary>
    [GeneratedRegex(@":([A-Za-z0-9_+\-]{1,64}):")]
    private static partial Regex EmojiPattern();

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})(.*)$")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$")]
    private static partial Regex RulePattern();

    /// <summary>A trailing run of "#" is decoration only when a space sets it apart, so "# C#" keeps its "#".</summary>
    [GeneratedRegex(@"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^ {0,3}>[ \t]?(.*)$")]
    private static partial Regex QuotePattern();

    /// <summary>The marker has to be followed by a space: "**bold**" and "-1" at the start of a line are not list items.</summary>
    [GeneratedRegex(@"^([ \t]*)([-*+]|\d{1,9}[.)])[ \t]+(.*)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^[ \t]*\|?(?:[ \t]*:?-+:?[ \t]*\|)*[ \t]*:?-+:?[ \t]*\|?[ \t]*$")]
    private static partial Regex TableSeparatorPattern();

    private const string EscapableCharacters = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    private static readonly string[] Bullets = ["•", "◦", "▪"];

    public static IReadOnlyList<MessageBlock> ParseBlocks(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // A "\r\n" source (pasted from Windows-authored text, say) leaves
            // a trailing "\r" behind after splitting on "\n" alone.
            lines[i] = lines[i].TrimEnd('\r');
        }

        var blocks = new List<MessageBlock>();

        // The indentation of each list item still open above the current
        // one. Depth comes from how many of them sit to its left, so a
        // sub-item nests whether it was indented by two spaces or four.
        var listIndents = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var fence = FencePattern().Match(line);
            // "```code```" on one line is inline code, not the start of a block.
            if (fence.Success && !(fence.Groups[1].Value[0] == '`' && fence.Groups[2].Value.Contains('`')))
            {
                var marker = fence.Groups[1].Value;
                var code = new List<string>();
                var j = i + 1;
                while (j < lines.Length && !IsClosingFence(lines[j], marker))
                {
                    code.Add(lines[j]);
                    j++;
                }
                blocks.Add(new CodeBlock { Code = string.Join('\n', code) });
                // Lands on the closing fence, which the loop then steps past.
                // An unclosed fence runs to the end of the message.
                i = j;
                listIndents.Clear();
                continue;
            }

            if (TryReadTable(lines, i, out var table, out var tableEnd))
            {
                blocks.Add(table);
                i = tableEnd;
                listIndents.Clear();
                continue;
            }

            if (RulePattern().IsMatch(line))
            {
                blocks.Add(new RuleBlock());
                listIndents.Clear();
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                blocks.Add(new HeadingBlock { Level = heading.Groups[1].Length, Segments = ParseInline(heading.Groups[2].Value) });
                listIndents.Clear();
                continue;
            }

            var quote = QuotePattern().Match(line);
            if (quote.Success)
            {
                // A quote of a quote reads as one quote, rather than bars inside bars.
                var content = quote.Groups[1].Value;
                while (QuotePattern().Match(content) is { Success: true } nested)
                {
                    content = nested.Groups[1].Value;
                }
                blocks.Add(new QuoteBlock { Segments = ParseInline(content) });
                listIndents.Clear();
                continue;
            }

            var item = ListItemPattern().Match(line);
            if (item.Success)
            {
                var indent = IndentWidth(item.Groups[1].Value);
                while (listIndents.Count > 0 && listIndents[^1] >= indent)
                {
                    listIndents.RemoveAt(listIndents.Count - 1);
                }
                var depth = listIndents.Count;
                listIndents.Add(indent);

                var rawMarker = item.Groups[2].Value;
                blocks.Add(new ListItemBlock
                {
                    Marker = char.IsAsciiDigit(rawMarker[0]) ? rawMarker : Bullets[depth % Bullets.Length],
                    Depth = Math.Min(depth, 6),
                    Segments = ParseInline(item.Groups[3].Value),
                });
                continue;
            }

            // A blank line between two items of the same list doesn't end it.
            if (!string.IsNullOrWhiteSpace(line))
            {
                listIndents.Clear();
            }
            blocks.Add(new ParagraphBlock { Segments = ParseInline(line) });
        }

        return blocks;
    }

    /// <summary>
    /// The message as it reads with the Markdown taken out — for the places
    /// that show a one-line excerpt (search results, pinned messages, a
    /// reply's quote, notifications), where "**urgent**" should read "urgent".
    /// </summary>
    public static string ToPlainText(string text)
    {
        var builder = new StringBuilder();
        foreach (var block in ParseBlocks(text))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            switch (block)
            {
                case ListItemBlock listItem:
                    builder.Append(listItem.Marker).Append(' ');
                    AppendSegments(builder, listItem.Segments);
                    break;
                case InlineBlock inline:
                    AppendSegments(builder, inline.Segments);
                    break;
                case CodeBlock code:
                    builder.Append(code.Code);
                    break;
                case TableBlock table:
                    for (var r = 0; r < table.Rows.Count; r++)
                    {
                        if (r > 0)
                        {
                            builder.Append('\n');
                        }
                        for (var c = 0; c < table.Rows[r].Count; c++)
                        {
                            if (c > 0)
                            {
                                builder.Append(" | ");
                            }
                            AppendSegments(builder, table.Rows[r][c]);
                        }
                    }
                    break;
            }
        }
        return builder.ToString();
    }

    private static void AppendSegments(StringBuilder builder, IReadOnlyList<MessageTextSegment> segments)
    {
        foreach (var segment in segments)
        {
            builder.Append(segment.Text);
        }
    }

    /// <summary>One line's worth of formatted text: emphasis, inline code, links, mentions and emoji.</summary>
    public static IReadOnlyList<MessageTextSegment> ParseInline(string text)
    {
        var segments = new List<MessageTextSegment>();
        AppendInline(segments, text, TextStyle.None);
        if (segments.Count == 0)
        {
            segments.Add(new PlainTextSegment { Text = "" });
        }
        return segments;
    }

    /// <summary>
    /// Reads left to right; whichever construct starts first wins. Ordinary
    /// characters collect in a buffer that becomes one plain segment as soon
    /// as something else begins, so a sentence with nothing special in it is
    /// still a single segment.
    /// </summary>
    private static void AppendInline(List<MessageTextSegment> segments, string text, TextStyle style)
    {
        var plain = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\\' && i + 1 < text.Length && EscapableCharacters.Contains(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                var run = RunLength(text, i, '`');
                var close = FindCodeSpanEnd(text, i + run, run);
                if (close < 0)
                {
                    plain.Append('`', run);
                    i += run;
                    continue;
                }

                FlushPlain(segments, plain, style);
                var code = text[(i + run)..close];
                // "`` `tick` ``" — the padding spaces are there only to keep the backticks apart.
                if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0)
                {
                    code = code[1..^1];
                }
                segments.Add(new PlainTextSegment { Text = code, Style = style | TextStyle.Code });
                i = close + run;
                continue;
            }

            if (c is '*' or '_' or '~')
            {
                var run = RunLength(text, i, c);
                if (!TryEmphasis(segments, plain, text, i, run, style, out var next))
                {
                    plain.Append(c, run);
                }
                i = next;
                continue;
            }

            if (c is '[' or 'h' or 'H' or '@' && LinkAtPattern().Match(text, i) is { Success: true } match)
            {
                FlushPlain(segments, plain, style);
                if (match.Groups[1].Success)
                {
                    var label = PlainInline(match.Groups[1].Value);
                    segments.Add(new LinkSegment { Text = label, LinkUrl = match.Groups[2].Value, Style = style });
                    i += match.Length;
                }
                else if (match.Groups[4].Success)
                {
                    segments.Add(new MentionSegment { Text = "@" + match.Groups[4].Value, Style = style });
                    i += match.Length;
                }
                else
                {
                    // Trailing punctuation is usually sentence punctuation, not part of the URL itself.
                    var url = match.Groups[3].Value.TrimEnd('.', ',', ')', ']', '>', '!', '?', ';', ':', '*', '~');
                    segments.Add(new LinkSegment { Text = url, LinkUrl = url, Style = style });
                    i += url.Length;
                }
                continue;
            }

            plain.Append(c);
            i++;
        }

        FlushPlain(segments, plain, style);
    }

    /// <summary>
    /// An opening run of "*", "_" or "~~" at <paramref name="start"/>. Tries
    /// the longest emphasis the run could open first — "***" is bold and
    /// italic — then shorter ones, leaving any unused delimiters as text.
    /// </summary>
    private static bool TryEmphasis(List<MessageTextSegment> segments, StringBuilder plain, string text, int start, int run, TextStyle style, out int next)
    {
        var delimiter = text[start];
        var contentStart = start + run;
        next = contentStart;

        // An opener has to be touching the text it emphasises: "2 * 3 * 4" is arithmetic.
        if (contentStart >= text.Length || char.IsWhiteSpace(text[contentStart]))
        {
            return false;
        }
        // Underscores inside a word are part of it: snake_case_names stay as typed.
        if (delimiter == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1]))
        {
            return false;
        }
        // A single "~" is how Mattermost links a channel, not strikethrough.
        if (delimiter == '~' && run != 2)
        {
            return false;
        }

        for (var size = delimiter == '~' ? 2 : Math.Min(run, 3); size >= (delimiter == '~' ? 2 : 1); size--)
        {
            var close = FindEmphasisCloser(text, contentStart, delimiter, size);
            if (close < 0)
            {
                continue;
            }

            plain.Append(delimiter, run - size);
            FlushPlain(segments, plain, style);

            var emphasis = delimiter == '~'
                ? TextStyle.Strikethrough
                : size switch
                {
                    1 => TextStyle.Italic,
                    2 => TextStyle.Bold,
                    _ => TextStyle.Bold | TextStyle.Italic,
                };
            AppendInline(segments, text[contentStart..close], style | emphasis);
            next = close + size;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Where a run of <paramref name="size"/> delimiters closes the emphasis,
    /// or -1. A run exactly that long wins; failing that, the end of a longer
    /// run — which is how "**bold *italic***" closes both at once.
    /// </summary>
    private static int FindEmphasisCloser(string text, int from, char delimiter, int size)
    {
        var fallback = -1;
        var j = from;
        while (j < text.Length)
        {
            var c = text[j];
            if (c == '\\')
            {
                j += 2;
                continue;
            }
            if (c == '`')
            {
                // Delimiters inside inline code don't count.
                var ticks = RunLength(text, j, '`');
                var codeEnd = FindCodeSpanEnd(text, j + ticks, ticks);
                j = codeEnd < 0 ? j + ticks : codeEnd + ticks;
                continue;
            }
            if (c != delimiter)
            {
                j++;
                continue;
            }

            var run = RunLength(text, j, delimiter);
            var closes = j > from
                && !char.IsWhiteSpace(text[j - 1])
                && (delimiter != '_' || j + run >= text.Length || !char.IsLetterOrDigit(text[j + run]));
            if (closes)
            {
                if (run == size)
                {
                    return j;
                }
                if (run > size && fallback < 0 && delimiter != '~')
                {
                    fallback = j + run - size;
                }
            }
            j += run;
        }
        return fallback;
    }

    /// <summary>The start of a closing run of exactly <paramref name="ticks"/> backticks, or -1.</summary>
    private static int FindCodeSpanEnd(string text, int from, int ticks)
    {
        var j = from;
        while (j < text.Length)
        {
            if (text[j] != '`')
            {
                j++;
                continue;
            }
            var run = RunLength(text, j, '`');
            if (run == ticks)
            {
                return j;
            }
            j += run;
        }
        return -1;
    }

    private static int RunLength(string text, int start, char c)
    {
        var end = start;
        while (end < text.Length && text[end] == c)
        {
            end++;
        }
        return end - start;
    }

    /// <summary>A link label with its own emphasis markers taken out — a link is drawn as one piece.</summary>
    private static string PlainInline(string text)
    {
        var builder = new StringBuilder();
        AppendSegments(builder, ParseInline(text));
        return builder.ToString();
    }

    private static void FlushPlain(List<MessageTextSegment> segments, StringBuilder plain, TextStyle style)
    {
        if (plain.Length == 0)
        {
            return;
        }
        AddText(segments, plain.ToString(), style);
        plain.Clear();
    }

    private static bool IsClosingFence(string line, string marker)
    {
        var trimmed = line.TrimStart(' ');
        if (line.Length - trimmed.Length > 3)
        {
            return false;
        }
        var run = RunLength(trimmed, 0, marker[0]);
        return run >= marker.Length && string.IsNullOrWhiteSpace(trimmed[run..]);
    }

    private static int IndentWidth(string whitespace)
    {
        var width = 0;
        foreach (var c in whitespace)
        {
            width += c == '\t' ? 4 : 1;
        }
        return width;
    }

    /// <summary>
    /// A header row, a "---|---" separator with as many columns, then every
    /// following line that still has a pipe in it. Ragged rows are padded or
    /// cut to the header's width so the grid stays rectangular.
    /// </summary>
    private static bool TryReadTable(string[] lines, int start, out TableBlock table, out int lastLine)
    {
        table = null!;
        lastLine = start;

        if (start + 1 >= lines.Length
            || !lines[start].Contains('|')
            || !lines[start + 1].Contains('|')
            || !TableSeparatorPattern().IsMatch(lines[start + 1]))
        {
            return false;
        }

        var header = SplitRow(lines[start]);
        if (header.Count != SplitRow(lines[start + 1]).Count)
        {
            return false;
        }

        var rows = new List<IReadOnlyList<IReadOnlyList<MessageTextSegment>>> { ParseRow(header, header.Count) };
        var j = start + 2;
        while (j < lines.Length && lines[j].Contains('|') && !string.IsNullOrWhiteSpace(lines[j]))
        {
            rows.Add(ParseRow(SplitRow(lines[j]), header.Count));
            j++;
        }

        table = new TableBlock { Rows = rows };
        lastLine = j - 1;
        return true;
    }

    private static List<IReadOnlyList<MessageTextSegment>> ParseRow(List<string> cells, int columns)
    {
        var row = new List<IReadOnlyList<MessageTextSegment>>(columns);
        for (var c = 0; c < columns; c++)
        {
            row.Add(ParseInline(c < cells.Count ? cells[c] : ""));
        }
        return row;
    }

    /// <summary>Splits on unescaped pipes; an escaped "\|" stays in the cell as a pipe.</summary>
    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|"))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (trimmed[i] == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(trimmed[i]);
            }
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }

    /// <summary>
    /// Adds a run of ordinary text, expanding any ":shortcode:" in it.
    ///
    /// A standard emoji is substituted straight into the text, so it stays
    /// part of the sentence and wraps with it rather than becoming its own
    /// control. Anything else becomes a segment of its own, because a custom
    /// emoji has to be drawn as a picture once the server's copy arrives.
    /// </summary>
    private static void AddText(List<MessageTextSegment> segments, string text, TextStyle style)
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
                segments.Add(new PlainTextSegment { Text = rebuilt.ToString(), Style = style });
                rebuilt.Clear();
            }
            segments.Add(new CustomEmojiSegment { Text = match.Value, EmojiName = name, Style = style });
        }

        // Nothing in this run was a shortcode — one plain segment, as typed.
        if (rebuilt is null)
        {
            segments.Add(new PlainTextSegment { Text = text, Style = style });
            return;
        }

        rebuilt.Append(text, lastIndex, text.Length - lastIndex);
        if (rebuilt.Length > 0)
        {
            segments.Add(new PlainTextSegment { Text = rebuilt.ToString(), Style = style });
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
