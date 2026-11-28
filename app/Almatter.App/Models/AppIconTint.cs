using System;
using Avalonia.Media;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>
/// The colour of the app's own logo — its windows, taskbar button,
/// notification-area icon and (in an installed copy on Windows) its
/// shortcuts. Picked in Apparence, separate from the theme. Stored by number:
/// never renumber, only add.
/// </summary>
public enum AppIconTint
{
    Blue = 0,
    Orange = 1,
    Green = 2,
    Violet = 3,
    Raspberry = 4,
    Graphite = 5,
}

public static class AppIconTints
{
    public static readonly AppIconTint[] All =
        [AppIconTint.Blue, AppIconTint.Orange, AppIconTint.Green, AppIconTint.Violet, AppIconTint.Raspberry, AppIconTint.Graphite];

    /// <summary>The logo's ground in each tint. Keep in step with tools/generate-app-icon.ps1, which draws the .ico files.</summary>
    public static string Hex(AppIconTint tint) => tint switch
    {
        AppIconTint.Orange => "#B84A12",
        AppIconTint.Green => "#2E6B45",
        AppIconTint.Violet => "#5A3F8F",
        AppIconTint.Raspberry => "#A32B4C",
        AppIconTint.Graphite => "#3B3F47",
        _ => "#28427B",
    };

    /// <summary>The tint's icon among the app's resources. A Debug build swaps in versions with a yellow corner at the same paths (see the csproj).</summary>
    public static Uri ResourceUri(AppIconTint tint) =>
        new($"avares://Almatter/Assets/AppIcons/{FileName(tint)}.ico");

    private static string FileName(AppIconTint tint) => tint switch
    {
        AppIconTint.Orange => "orange",
        AppIconTint.Green => "green",
        AppIconTint.Violet => "violet",
        AppIconTint.Raspberry => "raspberry",
        AppIconTint.Graphite => "graphite",
        _ => "blue",
    };

    /// <summary>A number from an older or newer settings file that names no tint here falls back to blue.</summary>
    public static AppIconTint Known(AppIconTint tint) => Array.IndexOf(All, tint) >= 0 ? tint : AppIconTint.Blue;
}

/// <summary>One logo in the settings' icon picker.</summary>
public sealed partial class AppIconTintOption : ObservableObject
{
    public required AppIconTint Tint { get; init; }

    public string Name => Tint switch
    {
        AppIconTint.Orange => Loc.S.IconTintOrange,
        AppIconTint.Green => Loc.S.IconTintGreen,
        AppIconTint.Violet => Loc.S.IconTintViolet,
        AppIconTint.Raspberry => Loc.S.IconTintRaspberry,
        AppIconTint.Graphite => Loc.S.IconTintGraphite,
        _ => Loc.S.IconTintBlue,
    };

    public IBrush GroundBrush => ColorTokens.Solid(AppIconTints.Hex(Tint));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The ring around the one in use, in the accent colour like the other pickers' selection.</summary>
    public IBrush RingBrush => IsSelected ? ColorTokens.Accent : Brushes.Transparent;

    public IBrush NameBrush => IsSelected ? ColorTokens.TextPrimary : ColorTokens.TextSecondary;

    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RingBrush));
        OnPropertyChanged(nameof(NameBrush));
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
}
