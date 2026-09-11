using System.Collections.Generic;

namespace Almatter.App.Models;

/// <summary>
/// One line of a message's text (split on the original '\n'), itself made
/// of plain-text/link/mention segments in reading order. Kept as its own
/// wrapped row rather than flattening every line into one big WrapPanel: a
/// WrapPanel doesn't know about embedded newlines, so a segment ending in
/// "\n" (a sentence followed by a link on the next line, say) left the
/// following segment floating wherever there happened to be horizontal
/// room instead of starting a fresh line beneath it — read as a large,
/// confusing empty gap. Grouping segments per source line and giving each
/// line its own WrapPanel makes wrapping (long lines) and line breaks
/// (multiple lines) both behave exactly as typed.
/// </summary>
public sealed class MessageTextLine
{
    public required IReadOnlyList<MessageTextSegment> Segments { get; init; }
}
