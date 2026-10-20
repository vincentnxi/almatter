using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>One person in the channel's member list — a flat, read-only row, like the pinned-messages panel's.</summary>
public sealed class ChannelMemberItem
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Username { get; init; }
    public required string Initials { get; init; }
    public required string AvatarHex { get; init; }
    public required PresenceStatus Presence { get; init; }

    /// <summary>False for this user's own row: there is no conversation to open with oneself.</summary>
    public required bool CanMessage { get; init; }

    public string UsernameLabel => $"@{Username}";
    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
    public IBrush PresenceBrush => ColorTokens.Presence(Presence);
}
