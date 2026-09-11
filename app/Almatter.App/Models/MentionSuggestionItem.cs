using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One row of the composer's @mention autocomplete popup.</summary>
public sealed partial class MentionSuggestionItem : ObservableObject
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Initials { get; init; }
    public required string AvatarHex { get; init; }

    /// <summary>True for the keyboard-highlighted suggestion (Up/Down arrows move this) — a mouse click accepts whichever row it lands on regardless.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(RowBackground));
}
