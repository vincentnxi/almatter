using Avalonia.Media;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>
/// One row of the settings panel's theme menu: three dots showing the
/// theme's ground, text and accent, its name, and whether it is dark or light.
/// The first row, "Almatter", stands for the app's own palette and shows it
/// as the user's own settings currently make it look.
/// </summary>
public sealed partial class NamedThemeOption : ObservableObject
{
    public required AppNamedTheme Theme { get; init; }

    /// <summary>Null for the "Almatter" row.</summary>
    public NamedPalette? Palette { get; init; }

    public string Name => Palette?.Name ?? "Almatter";

    public string Tag => Palette is null
        ? Loc.S.ThemeAlmatterTag
        : Palette.IsDark ? Loc.S.ThemeDark : Loc.S.ThemeLight;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial IBrush GroundDot { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush TextDot { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush AccentDot { get; set; } = Brushes.Transparent;

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush TextBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;
    public IBrush TagBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextTertiary;

    /// <summary>
    /// Called after the palette changes. <paramref name="almatterPalette"/> is
    /// what the app's own palette looks like with the user's mode, accent and
    /// contrast settings, for the "Almatter" row's dots.
    /// </summary>
    public void RefreshColors(ThemeDefinition almatterPalette)
    {
        if (Palette is { } p)
        {
            // A gradient theme's dot shows its gradient, top to bottom.
            GroundDot = ColorTokens.Ground(ThemeDefinition.FromPalette(p));
            TextDot = ColorTokens.Solid(p.Text);
            AccentDot = ColorTokens.Solid(p.Accent);
        }
        else
        {
            GroundDot = ColorTokens.Solid(almatterPalette.BgApp);
            TextDot = ColorTokens.Solid(almatterPalette.TextPrimary);
            AccentDot = ColorTokens.Solid(almatterPalette.AccentHex);
        }
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(TagBrush));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Tag));

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(TagBrush));
    }
}
