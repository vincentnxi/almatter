using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Almatter.App.Models;

public enum FormatAction
{
    Bold,
    Italic,
    Strikethrough,
    Heading,
    Quote,
    BulletList,
    NumberedList,
    Code,
    CodeBlock,
    Link,
    Rule,
    Table,
}

/// <summary>The composer's text and selection after a formatting action.</summary>
public readonly record struct FormatResult(string Text, int SelectionStart, int SelectionEnd);

/// <summary>
/// What the formatting toolbar does to the composer: writes the Markdown
/// that MessageTextParser reads back, around the selection or at the caret.
///
/// Every action toggles where that makes sense — bolding text that is already
/// bold removes the asterisks — so a misclick is undone by clicking again.
/// Pure text in, text out, so it can be checked without a window.
/// </summary>
public static partial class MarkdownFormatter
{
    [GeneratedRegex(@"^#{1,6}[ \t]+")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^>[ \t]?")]
    private static partial Regex QuotePrefix();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+")]
    private static partial Regex BulletPrefix();

    [GeneratedRegex(@"^[ \t]*\d{1,9}[.)][ \t]+")]
    private static partial Regex NumberPrefix();

    /// <summary>Either kind of list marker — switching a bulleted list to a numbered one replaces the bullets.</summary>
    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d{1,9}[.)])[ \t]+")]
    private static partial Regex AnyListPrefix();

    private const string LinkPlaceholder = "texte du lien";
    private const string UrlPlaceholder = "https://";

    public static FormatResult Apply(string text, int selectionStart, int selectionEnd, FormatAction action)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);

        return action switch
        {
            FormatAction.Bold => Wrap(text, start, end, "**"),
            FormatAction.Italic => Wrap(text, start, end, "*"),
            FormatAction.Strikethrough => Wrap(text, start, end, "~~"),
            FormatAction.Code => Wrap(text, start, end, "`"),
            FormatAction.Heading => PrefixLines(text, start, end, HeadingPrefix(), HeadingPrefix(), _ => "### "),
            FormatAction.Quote => PrefixLines(text, start, end, QuotePrefix(), QuotePrefix(), _ => "> "),
            FormatAction.BulletList => PrefixLines(text, start, end, BulletPrefix(), AnyListPrefix(), _ => "- "),
            FormatAction.NumberedList => PrefixLines(text, start, end, NumberPrefix(), AnyListPrefix(), n => $"{n}. "),
            FormatAction.CodeBlock => Fence(text, start, end),
            FormatAction.Link => Link(text, start, end),
            FormatAction.Rule => InsertBlock(text, end, "---", selectFrom: 3, selectTo: 3, caretAfterBlock: true),
            FormatAction.Table => InsertBlock(text, end, "| Colonne 1 | Colonne 2 |\n| --- | --- |\n|  |  |", selectFrom: 2, selectTo: 11, caretAfterBlock: false),
            _ => new FormatResult(text, start, end),
        };
    }

    /// <summary>
    /// Surrounds the selection with a marker, or takes it away when the
    /// selection already has it — just outside, or selected along with it.
    /// With nothing selected, drops an empty pair and puts the caret inside.
    /// </summary>
    private static FormatResult Wrap(string text, int start, int end, string marker)
    {
        if (start == end)
        {
            // Clicking again straight away, before typing anything, removes the empty pair.
            if (HasMarker(text, start, marker, before: true) && HasMarker(text, start, marker, before: false))
            {
                var without = text.Remove(start, marker.Length).Remove(start - marker.Length, marker.Length);
                return new FormatResult(without, start - marker.Length, start - marker.Length);
            }
            var withPair = text.Insert(start, marker + marker);
            return new FormatResult(withPair, start + marker.Length, start + marker.Length);
        }

        // A double-clicked word often comes with its trailing space, and
        // "**word **" wouldn't render as bold.
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }
        if (start == end)
        {
            return new FormatResult(text, start, end);
        }

        // Emphasis stops at a line break, so each selected line gets its own pair.
        if (text.AsSpan(start, end - start).Contains('\n'))
        {
            return WrapLines(text, start, end, marker);
        }

        if (HasMarker(text, start, marker, before: true) && HasMarker(text, end, marker, before: false))
        {
            var unwrapped = text.Remove(end, marker.Length).Remove(start - marker.Length, marker.Length);
            return new FormatResult(unwrapped, start - marker.Length, end - marker.Length);
        }

        var selected = text[start..end];
        if (TryUnwrap(selected, marker, out var inner))
        {
            return new FormatResult(text[..start] + inner + text[end..], start, start + inner.Length);
        }

        var wrapped = text[..start] + marker + selected + marker + text[end..];
        return new FormatResult(wrapped, start + marker.Length, end + marker.Length);
    }

    private static FormatResult WrapLines(string text, int start, int end, string marker)
    {
        var lines = text[start..end].Split('\n');

        var allWrapped = true;
        foreach (var line in lines)
        {
            if (line.Trim().Length > 0 && !TryUnwrap(line.Trim(), marker, out _))
            {
                allWrapped = false;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var leading = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
            var trailing = lines[i][lines[i].TrimEnd().Length..];
            var content = allWrapped && TryUnwrap(trimmed, marker, out var inner) ? inner : marker + trimmed + marker;
            lines[i] = leading + content + trailing;
        }

        var block = string.Join('\n', lines);
        return new FormatResult(text[..start] + block + text[end..], start, start + block.Length);
    }

    /// <summary>
    /// Whether the marker sits right before (or after) a position. A single
    /// "*" is only italic when the run of asterisks there is odd: "**" is
    /// bold, "***" is both.
    /// </summary>
    private static bool HasMarker(string text, int position, string marker, bool before)
    {
        var c = marker[0];
        var run = 0;
        if (before)
        {
            while (position - run - 1 >= 0 && text[position - run - 1] == c)
            {
                run++;
            }
        }
        else
        {
            while (position + run < text.Length && text[position + run] == c)
            {
                run++;
            }
        }
        return marker == "*" ? run % 2 == 1 : run >= marker.Length;
    }

    private static bool TryUnwrap(string selected, string marker, out string inner)
    {
        inner = selected;
        if (selected.Length <= marker.Length * 2
            || !HasMarker(selected, 0, marker, before: false)
            || !HasMarker(selected, selected.Length, marker, before: true))
        {
            return false;
        }
        inner = selected[marker.Length..^marker.Length];
        return true;
    }

    /// <summary>
    /// Adds a line prefix ("### ", "> ", "- ", "1. ") to every line the
    /// selection touches, or removes it when they all have it already.
    /// <paramref name="replaces"/> is what gets swapped out when adding, so a
    /// bulleted list turns into a numbered one rather than getting both.
    /// </summary>
    private static FormatResult PrefixLines(string text, int start, int end, Regex existing, Regex replaces, Func<int, string> prefixFor)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        // A selection ending just after a line break doesn't include the next line.
        var lastPosition = end > start && text[end - 1] == '\n' ? end - 1 : end;
        var lineEnd = text.IndexOf('\n', lastPosition);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var lines = text[lineStart..lineEnd].Split('\n');
        var singleLine = lines.Length == 1;

        var allHave = true;
        var anyContent = false;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }
            anyContent = true;
            if (!existing.IsMatch(line))
            {
                allHave = false;
            }
        }
        var removing = anyContent && allHave;

        var number = 1;
        var caretShift = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            // An empty line in a multi-line selection is left alone; on its
            // own it gets the prefix, ready to type after.
            if (lines[i].Trim().Length == 0 && !singleLine)
            {
                continue;
            }

            var before = lines[i].Length;
            lines[i] = removing
                ? existing.Replace(lines[i], "", 1)
                : prefixFor(number++) + replaces.Replace(lines[i], "", 1);
            caretShift = lines[i].Length - before;
        }

        var block = string.Join('\n', lines);
        var result = text[..lineStart] + block + text[lineEnd..];

        if (singleLine && start == end)
        {
            // Keep the caret where it was in the text, never inside the prefix.
            var prefixLength = removing ? 0 : prefixFor(1).Length;
            var caret = Math.Clamp(start + caretShift, lineStart + prefixLength, lineStart + block.Length);
            return new FormatResult(result, caret, caret);
        }
        return new FormatResult(result, lineStart, lineStart + block.Length);
    }

    /// <summary>
    /// Puts the selection between ``` fences on their own lines, or removes
    /// the fences around it. With nothing selected, opens an empty block with
    /// the caret inside.
    /// </summary>
    private static FormatResult Fence(string text, int start, int end)
    {
        const string open = "```\n";
        const string close = "\n```";

        if (end > start)
        {
            var selected = text[start..end];
            if (selected.StartsWith(open) && selected.EndsWith(close) && selected.Length >= open.Length + close.Length)
            {
                var inner = selected[open.Length..^close.Length];
                return new FormatResult(text[..start] + inner + text[end..], start, start + inner.Length);
            }
            if (text[..start].EndsWith(open) && text[end..].StartsWith(close))
            {
                var unwrapped = text.Remove(end, close.Length).Remove(start - open.Length, open.Length);
                return new FormatResult(unwrapped, start - open.Length, end - open.Length);
            }
        }

        var lead = start > 0 && text[start - 1] != '\n' ? "\n" : "";
        var tail = end < text.Length && text[end] != '\n' ? "\n" : "";
        var code = text[start..end];
        var result = text[..start] + lead + open + code + close + tail + text[end..];
        var codeStart = start + lead.Length + open.Length;
        return new FormatResult(result, codeStart, codeStart + code.Length);
    }

    /// <summary>
    /// "[selection](https://)" with the address selected, ready to paste over.
    /// A selected address becomes the link target instead, with the caret in
    /// the label; with nothing selected, a placeholder label is selected.
    /// </summary>
    private static FormatResult Link(string text, int start, int end)
    {
        var selected = text[start..end].Trim();

        if (selected.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || selected.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var withUrl = text[..start] + "[](" + selected + ")" + text[end..];
            return new FormatResult(withUrl, start + 1, start + 1);
        }

        if (selected.Length == 0)
        {
            var placeholder = text[..start] + "[" + LinkPlaceholder + "](" + UrlPlaceholder + ")" + text[end..];
            return new FormatResult(placeholder, start + 1, start + 1 + LinkPlaceholder.Length);
        }

        var link = "[" + selected + "](" + UrlPlaceholder + ")";
        var urlStart = start + 1 + selected.Length + 2;
        return new FormatResult(text[..start] + link + text[end..], urlStart, urlStart + UrlPlaceholder.Length);
    }

    /// <summary>
    /// Inserts a block (a rule, a table) on lines of its own at the end of the
    /// selection, with a blank line above it: without one, the official
    /// client reads "---" under a line of text as a heading underline.
    /// </summary>
    private static FormatResult InsertBlock(string text, int position, string block, int selectFrom, int selectTo, bool caretAfterBlock)
    {
        var lead = new StringBuilder();
        if (position > 0)
        {
            var lineStart = text.LastIndexOf('\n', position - 1) + 1;
            if (position > lineStart)
            {
                // Mid-line or at the end of a line of text: break it, then leave a blank line.
                lead.Append("\n\n");
            }
            else if (lineStart > 0)
            {
                var previousLineStart = lineStart - 1 == 0 ? 0 : text.LastIndexOf('\n', lineStart - 2) + 1;
                if (text[previousLineStart..(lineStart - 1)].Trim().Length > 0)
                {
                    lead.Append('\n');
                }
            }
        }

        var atEnd = position == text.Length;
        var tail = atEnd ? (caretAfterBlock ? "\n" : "") : (text[position] == '\n' ? "" : "\n");
        var result = text[..position] + lead + block + tail + text[position..];
        var blockStart = position + lead.Length;

        if (caretAfterBlock)
        {
            var caret = blockStart + block.Length + tail.Length;
            return new FormatResult(result, caret, caret);
        }
        return new FormatResult(result, blockStart + selectFrom, blockStart + selectTo);
    }
}
