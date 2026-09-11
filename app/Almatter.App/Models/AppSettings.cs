using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>User-editable preferences, persisted to disk by SettingsStore.</summary>
public sealed partial class AppSettings : ObservableObject
{
    /// <summary>
    /// Deliberately a different JSON name from the old multi-theme "Theme"
    /// key: a settings.json still carrying one of the removed designer
    /// themes (Nocturne, Éditorial, Aurora) would otherwise land on an
    /// arbitrary new value. The old key is simply ignored and everyone
    /// starts on "follow Windows".
    /// </summary>
    [ObservableProperty]
    public partial AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    /// <summary>Defaults to the official client's own typeface, so someone switching over isn't met with unfamiliar lettering on day one.</summary>
    [ObservableProperty]
    public partial AppFontChoice FontChoice { get; set; } = AppFontChoice.Mattermost;

    /// <summary>14,5px on Segoe UI Variable reads at about the same size as the official client's 14px Open Sans, whose x-height is taller.</summary>
    [ObservableProperty]
    public partial double MessageFontSize { get; set; } = 14.5;

    /// <summary>
    /// Channel/DM ids in the order the user dragged them within Favoris —
    /// a purely local, per-device preference (Mattermost's favorite
    /// preference is just a set, with no server-side order of its own).
    /// A favorite not yet in this list sorts after everything that is.
    /// </summary>
    [ObservableProperty]
    public partial List<string> FavoriteOrder { get; set; } = [];

    /// <summary>Whether a shared link gets an og:title/description/image preview card, or just renders as plain clickable text.</summary>
    [ObservableProperty]
    public partial bool ShowLinkPreviews { get; set; } = true;

    public IBrush LinkPreviewsCheckBrush => ShowLinkPreviews ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnShowLinkPreviewsChanged(bool value) => OnPropertyChanged(nameof(LinkPreviewsCheckBrush));

    /// <summary>Sidebar width in px, set by dragging its right edge. Clamped on load to the bounds the grid declares.</summary>
    [ObservableProperty]
    public partial double SidebarWidth { get; set; } = 280;

    /// <summary>The window's size at last close, restored on the next launch.</summary>
    [ObservableProperty]
    public partial double WindowWidth { get; set; } = 1280;

    [ObservableProperty]
    public partial double WindowHeight { get; set; } = 800;
}
