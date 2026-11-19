using Avalonia.Media;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One choice in the settings panel's emoji picker.</summary>
public sealed partial class EmojiStyleOption : ObservableObject
{
    public required AppEmojiStyle Style { get; init; }

    /// <summary>"Twemoji" is a name, the same in every language.</summary>
    public string Name => Style == AppEmojiStyle.Twemoji ? "Twemoji" : Loc.S.FontSystem;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush TextBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;

    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(TextBrush));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
