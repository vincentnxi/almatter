namespace Almatter.App.Models;

/// <summary>
/// One chunk of a message's text, in reading order. Split into a type per
/// kind rather than one type with IsLink/IsMention flags so the view can
/// pick a template by type: the flag version had to declare all three
/// renderings in one template and hide two, which built seven controls —
/// a context menu among them — for every chunk of every visible message.
/// </summary>
public abstract class MessageTextSegment
{
    /// <summary>What's actually shown — the bare URL for an autolinked link, just the label for a Markdown [label](url) link, or "@username" for a mention.</summary>
    public required string Text { get; init; }
}

public sealed class PlainTextSegment : MessageTextSegment;

/// <summary>An "@username" (or @channel/@here/@all) token — rendered in accent color, official-client style.</summary>
public sealed class MentionSegment : MessageTextSegment;

public sealed class LinkSegment : MessageTextSegment
{
    /// <summary>Where the link actually goes when clicked — the same as Text for a bare autolinked URL, but different for a Markdown [label](url) link.</summary>
    public required string LinkUrl { get; init; }
}
