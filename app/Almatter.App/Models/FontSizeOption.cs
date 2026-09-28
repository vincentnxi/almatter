using System;
using Almatter.App.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One choice in the settings panel's message font-size picker.</summary>
public sealed partial class FontSizeOption : ObservableObject
{
    /// <summary>Picks this option's name out of the current language's texts.</summary>
    public required Func<Strings, string> Wording { get; init; }

    public string Label => Wording(Loc.S);

    public required double Size { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush TextBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;

    /// <summary>Called after the accent color changes so the selected pill repaints in place.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
    }
}
