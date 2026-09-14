using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One swatch in the emoji picker's skin-tone strip.</summary>
public sealed partial class SkinToneOption : ObservableObject
{
    public required EmojiSkinTone Tone { get; init; }

    /// <summary>A raised hand in this tone — the swatch is the thing it does, rather than a label describing it.</summary>
    public required string Swatch { get; init; }

    public required string Label { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;

    public void RefreshColors() => OnPropertyChanged(nameof(RowBackground));

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
