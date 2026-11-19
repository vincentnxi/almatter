using Avalonia.Media;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One pastel swatch in the accent picker, shown instead of the usual ones while Ark is the theme.</summary>
public sealed partial class ArkAccentOption : ObservableObject
{
    public AppArkAccent Accent { get; }

    public string Name => Accent switch
    {
        AppArkAccent.Sauge => Loc.S.AccentSage,
        AppArkAccent.Ciel => Loc.S.AccentSky,
        AppArkAccent.Lavande => Loc.S.AccentLavender,
        AppArkAccent.Peche => Loc.S.AccentPeach,
        AppArkAccent.Rose => Loc.S.AccentPink,
        _ => Loc.S.AccentCitron,
    };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The pastel is the same on the light and the dark palette.</summary>
    public IBrush SwatchBrush { get; } = Brushes.Transparent;

    /// <summary>
    /// A ring around the one in use: the pastel itself on the dark palette,
    /// its deep shade on the light one, where a pastel ring would barely show.
    /// </summary>
    public IBrush RingBrush => !IsSelected
        ? Brushes.Transparent
        : ColorTokens.Theme.IsDark ? SwatchBrush : ColorTokens.Solid(ArkAccentPalette.DeepHex(Accent));

    /// <summary>Called after the palette changes: the ring depends on whether it is light or dark.</summary>
    public void RefreshColors() => OnPropertyChanged(nameof(RingBrush));

    public ArkAccentOption(AppArkAccent accent)
    {
        Accent = accent;
        SwatchBrush = ColorTokens.Solid(ArkAccentPalette.PastelHex(accent));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(RingBrush));
}
