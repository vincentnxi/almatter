using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

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

/// <summary>
/// A ":shortcode:" that isn't one of the standard emoji — on this server,
/// almost always a custom emoji, which people type by name from the
/// keyboard. Drawn as its picture once that has been fetched, and as the
/// literal ":shortcode:" until then — or for good, when no such emoji
/// exists, which is exactly how it read before any of this.
/// </summary>
[INotifyPropertyChanged]
public sealed partial class CustomEmojiSegment : MessageTextSegment
{
    /// <summary>The name between the colons — what the server knows the emoji by.</summary>
    public required string EmojiName { get; init; }

    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    /// <summary>Until the picture lands, the segment falls back to showing the shortcode it was written as.</summary>
    public bool HasImage => Image is not null;

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
}

public sealed class LinkSegment : MessageTextSegment
{
    /// <summary>Where the link actually goes when clicked — the same as Text for a bare autolinked URL, but different for a Markdown [label](url) link.</summary>
    public required string LinkUrl { get; init; }
}
