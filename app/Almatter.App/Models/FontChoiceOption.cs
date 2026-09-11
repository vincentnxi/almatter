using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One choice in the settings panel's typeface picker.</summary>
public sealed partial class FontChoiceOption : ObservableObject
{
    public required AppFontChoice Choice { get; init; }
    public required string Name { get; init; }

    /// <summary>Rendered in the face it selects, so the pill previews the actual lettering.</summary>
    public required FontFamily Preview { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush TextBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;

    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
    }

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
