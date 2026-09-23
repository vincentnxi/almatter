namespace Almatter.App.Models;

/// <summary>A resolved, ready-to-show desktop notification — a message, or a reaction to one of this user's own — carrying enough to also jump to the message on click.</summary>
public sealed class DesktopNotification
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required string ChannelId { get; init; }
    public required string PostId { get; init; }

    /// <summary>
    /// Aimed at this user — a mention, a private message, a reply in a
    /// thread they started, a link to one of their messages, or a reaction to
    /// one — rather than just any message in a channel they're in. Shown so
    /// it stands out from the rest.
    /// </summary>
    public bool IsPersonal { get; init; }

    /// <summary>Always for a personal one; for the rest, only when the user asked for a sound on every message.</summary>
    public bool PlaysSound { get; init; }
}
