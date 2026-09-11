using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>One row in the search results panel — a matched message plus enough context (which channel, who, when) to show and jump to it.</summary>
public sealed class SearchResultItem
{
    public required string PostId { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelLabel { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorInitials { get; init; }
    public required string AvatarHex { get; init; }
    public required string TimeLabel { get; init; }
    public required string Text { get; init; }

    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
}
