using Almatter.App.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One swatch in the emoji picker's skin-tone strip.</summary>
public sealed partial class SkinToneOption : ObservableObject
{
    public required EmojiSkinTone Tone { get; init; }

    /// <summary>A raised hand in this tone — the swatch is the thing it does, rather than a label describing it.</summary>
    public required string Swatch { get; init; }

    public string Label => Tone switch
    {
        EmojiSkinTone.Light => Loc.S.SkinToneLight,
        EmojiSkinTone.MediumLight => Loc.S.SkinToneMediumLight,
        EmojiSkinTone.Medium => Loc.S.SkinToneMedium,
        EmojiSkinTone.MediumDark => Loc.S.SkinToneMediumDark,
        EmojiSkinTone.Dark => Loc.S.SkinToneDark,
        _ => Loc.S.SkinToneDefault,
    };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;

    public void RefreshColors() => OnPropertyChanged(nameof(RowBackground));

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
