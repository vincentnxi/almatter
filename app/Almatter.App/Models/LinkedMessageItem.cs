namespace Almatter.App.Models;

/// <summary>
/// The card under a message that links to another one (Mattermost's "Copy
/// link"): who wrote the original, where, and the start of what they said.
/// Built from the preview the server attaches to the linking message, so it
/// never costs a request of its own. Clicking it jumps to the original.
/// </summary>
public sealed class LinkedMessageItem
{
    public required string PostId { get; init; }
    public required string ChannelId { get; init; }
    public required string AuthorName { get; init; }
    public required string ChannelLabel { get; init; }
    public required string TimeLabel { get; init; }
    public required string Text { get; init; }
}
