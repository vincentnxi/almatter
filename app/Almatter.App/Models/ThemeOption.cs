using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One choice in the settings panel's light/dark/system picker.</summary>
public sealed partial class ThemeOption : ObservableObject
{
    public required AppThemeMode Mode { get; init; }
    public required string Name { get; init; }

    /// <summary>Path geometry for the option's glyph — a sun, a moon, or a half-filled disc for "follow Windows".</summary>
    public required string IconData { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush TextBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;
    public IBrush IconBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextSecondary;

    /// <summary>Called after the palette changes so the selected pill repaints in place.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(IconBrush));
    }

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
