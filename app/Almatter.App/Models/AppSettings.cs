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

    /// <summary>
    /// Softens the palette for long reading sessions — see
    /// ThemeDefinition.Softened. Applies to both themes: pure black on white
    /// and pure white on black are both tiring after a few hours.
    /// </summary>
    [ObservableProperty]
    public partial bool ReducedContrast { get; set; }

    public IBrush ReducedContrastCheckBrush => ReducedContrast ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnReducedContrastChanged(bool value) => OnPropertyChanged(nameof(ReducedContrastCheckBrush));

    /// <summary>
    /// Which skin tone the emoji picker offers for the emoji that accept
    /// one. A preference rather than a per-pick choice, like the official
    /// clients: choosing a tone on every single reaction would be tedious.
    /// </summary>
    [ObservableProperty]
    public partial EmojiSkinTone SkinTone { get; set; } = EmojiSkinTone.Default;

    /// <summary>
    /// How many times each emoji has been picked, keyed by its toneless
    /// shortcode — what the picker's "most used" row is ordered by. Kept
    /// here rather than on the server because Mattermost has no API for it:
    /// the official clients keep their own local tally too.
    ///
    /// Mutated in place, so a change raises nothing on its own; the picker
    /// calls NotifyEmojiUsageChanged to get it written to disk.
    /// </summary>
    [ObservableProperty]
    public partial Dictionary<string, int> EmojiUseCounts { get; set; } = [];

    public void NotifyEmojiUsageChanged() => OnPropertyChanged(nameof(EmojiUseCounts));

    /// <summary>Whether a shared link gets an og:title/description/image preview card, or just renders as plain clickable text.</summary>
    [ObservableProperty]
    public partial bool ShowLinkPreviews { get; set; } = true;

    public IBrush LinkPreviewsCheckBrush => ShowLinkPreviews ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnShowLinkPreviewsChanged(bool value) => OnPropertyChanged(nameof(LinkPreviewsCheckBrush));

    /// <summary>
    /// Every notification makes a sound, not only the ones aimed at this
    /// user (see DesktopNotification.IsPersonal). Those keep their 🔔 either
    /// way, so they still stand out. Off by default: in a busy team a sound
    /// for every channel message soon stops meaning anything.
    /// </summary>
    [ObservableProperty]
    public partial bool SoundForAllMessages { get; set; }

    public IBrush SoundForAllMessagesCheckBrush => SoundForAllMessages ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnSoundForAllMessagesChanged(bool value) => OnPropertyChanged(nameof(SoundForAllMessagesCheckBrush));

    /// <summary>Sidebar width in px, set by dragging its right edge. Clamped on load to the bounds the grid declares.</summary>
    [ObservableProperty]
    public partial double SidebarWidth { get; set; } = 280;

    /// <summary>The window's size at last close, restored on the next launch.</summary>
    [ObservableProperty]
    public partial double WindowWidth { get; set; } = 1280;

    [ObservableProperty]
    public partial double WindowHeight { get; set; } = 800;

    /// <summary>
    /// Where the window was, in screen coordinates. NaN means "never saved"
    /// — the first launch, which centres itself instead. The restore checks
    /// this against the screens actually attached: a position saved on a
    /// monitor that is no longer there would put the window somewhere the
    /// user cannot see or reach.
    /// </summary>
    [ObservableProperty]
    public partial double WindowX { get; set; } = double.NaN;

    [ObservableProperty]
    public partial double WindowY { get; set; } = double.NaN;

    /// <summary>Closed while maximised — reopen that way, at the size it would return to when un-maximised.</summary>
    [ObservableProperty]
    public partial bool WindowMaximised { get; set; }

    /// <summary>
    /// The channel or conversation being read at last close, reopened on the
    /// next launch. Ignored when it no longer exists (left, archived, or a
    /// different account), falling back to the first channel as before.
    /// </summary>
    [ObservableProperty]
    public partial string LastChannelId { get; set; } = "";

    /// <summary>Collapsed/expanded state of the three sidebar sections.</summary>
    [ObservableProperty]
    public partial bool FavoritesExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool ChannelsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool DirectMessagesExpanded { get; set; } = true;

    /// <summary>
    /// Whether the formatting bar shows above each composer. Separate for the
    /// thread, whose panel is narrower: room there may matter more.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowFormattingToolbar { get; set; }

    [ObservableProperty]
    public partial bool ShowThreadFormattingToolbar { get; set; }

    /// <summary>
    /// Draw the window on the graphics card instead of the processor.
    ///
    /// Off by default, which is the opposite of what most toolkits do, and
    /// deliberate. Avalonia's GPU path on Windows goes through ANGLE, which
    /// translates to Direct3D, which loads the graphics driver into this
    /// process: on the machine this was measured on that is two NVIDIA
    /// libraries worth about 190 MB of mapped code and 55 extra threads, for
    /// a window that draws text and small pictures. Measured side by side,
    /// same conversation open, a minute after launch: 194 MB of private
    /// memory and 86 threads on the GPU path against 112 MB and 31 threads
    /// on this one, the window appearing in 0.9 s instead of 2.0 s, and —
    /// the part that decides it — the same processor use while repainting
    /// continuously. Forcing repaints by resizing the window for fifteen
    /// seconds pushed the GPU path to 525 MB of private memory; the
    /// processor path stayed at 92 MB.
    ///
    /// It is a setting rather than a hard choice because none of that is
    /// guaranteed to hold elsewhere: a laptop with Intel graphics has a far
    /// lighter driver, and a very high-resolution screen gives the graphics
    /// card more of an advantage. Turn it on there if it helps. Read once at
    /// startup, in Program.Main, before any window exists — so changing it
    /// takes effect at the next launch.
    /// </summary>
    [ObservableProperty]
    public partial bool UseGpuRendering { get; set; }
}
