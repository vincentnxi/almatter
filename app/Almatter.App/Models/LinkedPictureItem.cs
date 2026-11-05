namespace Almatter.App.Models;

/// <summary>
/// A picture the message's text links to rather than attaches: a bare
/// address ending in a picture, or a Markdown picture — "![Enorme GIF](https://media.giphy.com/…)",
/// which is how the official client's GIF picker posts. Fetched straight
/// from that address, like a link preview's picture, and shown with
/// previews switched on only.
/// </summary>
public sealed class LinkedPictureItem : PictureItem
{
    public required string Url { get; init; }

    /// <summary>The Markdown alt text, or the address's last part ("giphy.gif") when there is none.</summary>
    public required string Name { get; init; }

    public override string Caption => Name;

    /// <summary>Where it comes from, without the address itself — a Giphy one runs to two hundred characters.</summary>
    protected override string TipText =>
        System.Uri.TryCreate(Url, System.UriKind.Absolute, out var uri) ? $"{Name} · {uri.Host}" : Name;
}
