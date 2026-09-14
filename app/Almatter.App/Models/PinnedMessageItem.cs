using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>
/// One row in the pinned-messages panel. Deliberately a flat, read-only
/// summary rather than a full MessageItem: the panel is a list to scan and
/// jump from, not a second conversation, and building real message rows
/// would drag in reactions, threads, attachments and their image fetches for
/// messages the user is only glancing at.
/// </summary>
public sealed class PinnedMessageItem
{
    public required string PostId { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorInitials { get; init; }
    public required string AvatarHex { get; init; }
    public required string TimeLabel { get; init; }
    public required string Text { get; init; }

    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
}
