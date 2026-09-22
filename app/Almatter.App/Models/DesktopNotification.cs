namespace Almatter.App.Models;

/// <summary>A resolved, ready-to-show desktop notification — a message, or a reaction to one of this user's own — carrying enough to also jump to the message on click.</summary>
public sealed class DesktopNotification
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required string ChannelId { get; init; }
    public required string PostId { get; init; }
}
