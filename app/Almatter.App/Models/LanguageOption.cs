using Almatter.App.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One choice in the settings panel's language picker.</summary>
public sealed partial class LanguageOption : ObservableObject
{
    public required AppLanguage Language { get; init; }

    /// <summary>The language's name in that language — "Français", "English" — whatever the interface is currently in.</summary>
    public required string Name { get; init; }

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
