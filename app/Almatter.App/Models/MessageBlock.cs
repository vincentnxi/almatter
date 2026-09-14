using System.Collections.Generic;

namespace Almatter.App.Models;

/// <summary>
/// One block of a message's Markdown, in reading order: a line of text, a
/// heading, a quote line, a list item, a code block, a table or a rule. The
/// view picks a template per type, the same way it does for segments.
///
/// Ordinary lines stay one block per typed line rather than being joined
/// into paragraphs: Mattermost keeps a single line break as a line break,
/// and a blank line keeps its height, so the message reads exactly as typed.
/// </summary>
public abstract class MessageBlock;

/// <summary>A block whose content is a run of formatted text.</summary>
public abstract class InlineBlock : MessageBlock
{
    public required IReadOnlyList<MessageTextSegment> Segments { get; init; }
}

public sealed class ParagraphBlock : InlineBlock;

/// <summary>A "#" to "######" heading.</summary>
public sealed class HeadingBlock : InlineBlock
{
    public required int Level { get; init; }

    /// <summary>How much bigger than message text the heading is drawn.</summary>
    public double FontScale => Level switch
    {
        1 => 1.45,
        2 => 1.3,
        3 => 1.15,
        _ => 1.0,
    };
}

/// <summary>A "&gt;" line. Consecutive ones sit flush, so their bars join into one.</summary>
public sealed class QuoteBlock : InlineBlock;

/// <summary>A "-", "*", "+" or "1." item.</summary>
public sealed class ListItemBlock : InlineBlock
{
    /// <summary>What's drawn in front: a bullet, or the number as typed ("3.").</summary>
    public required string Marker { get; init; }

    /// <summary>Nesting depth, from the item's leading spaces.</summary>
    public required int Depth { get; init; }

    public Avalonia.Thickness IndentMargin => new(Depth * 20, 0, 0, 0);
}

/// <summary>A fenced ``` block, shown as typed: no formatting, links or emoji inside.</summary>
public sealed class CodeBlock : MessageBlock
{
    public required string Code { get; init; }
}

public sealed class RuleBlock : MessageBlock;

/// <summary>A pipe table. The first row is the header.</summary>
public sealed class TableBlock : MessageBlock
{
    public required IReadOnlyList<IReadOnlyList<IReadOnlyList<MessageTextSegment>>> Rows { get; init; }
}
