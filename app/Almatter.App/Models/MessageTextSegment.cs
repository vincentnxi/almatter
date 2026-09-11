namespace Almatter.App.Models;

/// <summary>One chunk of a message's text — plain text, a clickable link, or an @mention — in reading order.</summary>
public sealed class MessageTextSegment
{
    /// <summary>What's actually shown — the bare URL for an autolinked link, just the label for a Markdown [label](url) link, or "@username" for a mention.</summary>
    public required string Text { get; init; }
    public bool IsLink { get; init; }

    /// <summary>Where a link segment actually goes when clicked — the same as Text for a bare autolinked URL, but differs for a Markdown [label](url) link.</summary>
    public string LinkUrl { get; init; } = "";

    /// <summary>True for an "@username" (or @channel/@here/@all) token — rendered in accent color, official-client style.</summary>
    public bool IsMention { get; init; }

    public bool IsPlainText => !IsLink && !IsMention;
}
