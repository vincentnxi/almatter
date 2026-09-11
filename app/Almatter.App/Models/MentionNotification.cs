namespace Almatter.App.Models;

/// <summary>A resolved, ready-to-show desktop notification for a mention — carries enough to also jump to the message on click.</summary>
public sealed class MentionNotification
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required string ChannelId { get; init; }
    public required string PostId { get; init; }
}
