using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>One person in the "start a conversation" panel — anyone on the team, not just members of the current channel.</summary>
public sealed class DirectoryUserItem
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Shown as "@username" under the display name, so two people with the same real name can be told apart.</summary>
    public required string Username { get; init; }

    public required string Initials { get; init; }
    public required string AvatarHex { get; init; }

    public string UsernameLabel => $"@{Username}";
    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
}
