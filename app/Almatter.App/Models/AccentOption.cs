using Avalonia.Media;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One swatch in the settings panel's accent-color picker.</summary>
public sealed partial class AccentOption : ObservableObject
{
    public required AppAccent Accent { get; init; }

    public string Name => Accent switch
    {
        AppAccent.Teal => Loc.S.AccentTeal,
        AppAccent.Green => Loc.S.AccentGreen,
        AppAccent.Amber => Loc.S.AccentAmber,
        AppAccent.Rose => Loc.S.AccentRose,
        AppAccent.Violet => Loc.S.AccentViolet,
        _ => Loc.S.AccentBlue,
    };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The color as it will actually look: each accent has a different shade on the light and the dark palette.</summary>
    public IBrush SwatchBrush => ColorTokens.Solid(AccentPalette.Hex(Accent, ColorTokens.Theme.IsDark));

    /// <summary>A ring in the swatch's own color around the one in use.</summary>
    public IBrush RingBrush => IsSelected ? SwatchBrush : Brushes.Transparent;

    /// <summary>Called after the palette changes so the swatches show the shades of the new one.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(SwatchBrush));
        OnPropertyChanged(nameof(RingBrush));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(RingBrush));
}
