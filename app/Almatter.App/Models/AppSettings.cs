using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Almatter.App.Localization;
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

    /// <summary>
    /// The interface language. A settings.json from before languages existed
    /// has no such key and lands on Windows' own language — French for
    /// everyone who was using the French-only versions on a French Windows.
    /// </summary>
    [ObservableProperty]
    public partial AppLanguage Language { get; set; } = Loc.SystemDefault;

    /// <summary>Defaults to the official client's own typeface, so someone switching over isn't met with unfamiliar lettering on day one.</summary>
    [ObservableProperty]
    public partial AppFontChoice FontChoice { get; set; } = AppFontChoice.Mattermost;

    /// <summary>The system's emoji unless changed: they are already in memory, the bundled Twemoji font adds about 2 MB.</summary>
    [ObservableProperty]
    public partial AppEmojiStyle EmojiStyle { get; set; } = AppEmojiStyle.System;

    /// <summary>
    /// "Normale". The official client's 14px reads smaller in this app, whose
    /// renderer draws text paler than a browser does, hence 15,5.
    /// </summary>
    public const double DefaultMessageFontSize = 15.5;

    /// <summary>
    /// Bounds of the size picker. Below 12 messages stop being comfortably
    /// readable; above 22 a line of text holds only a few words and the
    /// fixed-size parts of a message (avatar, name row) look out of scale.
    /// </summary>
    public const double MinMessageFontSize = 12;
    public const double MaxMessageFontSize = 22;
    public const double MessageFontSizeStep = 0.5;

    /// <summary>
    /// Snaps any value — a hand-edited settings.json included — onto the
    /// half-point grid between the bounds; garbage falls back to Normale.
    /// </summary>
    public static double ClampMessageFontSize(double size)
    {
        if (double.IsNaN(size) || double.IsInfinity(size))
        {
            return DefaultMessageFontSize;
        }
        var snapped = Math.Round(size / MessageFontSizeStep, MidpointRounding.AwayFromZero) * MessageFontSizeStep;
        return Math.Clamp(snapped, MinMessageFontSize, MaxMessageFontSize);
    }

    /// <summary>
    /// The message text size, picked in half-point steps between
    /// <see cref="MinMessageFontSize"/> and <see cref="MaxMessageFontSize"/>.
    /// It used to be one of three pills (Petite 14 / Normale 15,5 / Grande
    /// 17,5, earlier 13 / 14,5 / 16,5); any of those saved values is still a
    /// valid size and is kept as is.
    /// </summary>
    [ObservableProperty]
    public partial double MessageFontSize { get; set; } = DefaultMessageFontSize;

    /// <summary>
    /// Channel/DM ids in the order the user dragged them within Favoris —
    /// a purely local, per-device preference (Mattermost's favorite
    /// preference is just a set, with no server-side order of its own).
    /// A favorite not yet in this list sorts after everything that is.
    /// </summary>
    [ObservableProperty]
    public partial List<string> FavoriteOrder { get; set; } = [];

    /// <summary>
    /// Messages whose pictures the reader folded away, oldest first. Capped
    /// (see MainViewModel.MaxRememberedCollapsedImages) so the file never
    /// grows without end: the oldest fold is forgotten first, and that
    /// message simply shows its pictures again.
    /// </summary>
    [ObservableProperty]
    public partial List<string> CollapsedImagePostIds { get; set; } = [];

    /// <summary>Messages whose link preview card the reader folded away, kept and capped like CollapsedImagePostIds.</summary>
    [ObservableProperty]
    public partial List<string> CollapsedLinkPreviewPostIds { get; set; } = [];

    /// <summary>
    /// Softens the palette for long reading sessions — see
    /// ThemeDefinition.Softened. Applies to both themes: pure black on white
    /// and pure white on black are both tiring after a few hours.
    /// </summary>
    [ObservableProperty]
    public partial bool ReducedContrast { get; set; }

    /// <summary>The accent color, blue unless changed — what the app had before there was a choice.</summary>
    [ObservableProperty]
    public partial AppAccent Accent { get; set; } = AppAccent.Blue;

    /// <summary>The logo's colour (windows, taskbar, notification area, shortcuts), blue unless changed. Independent of the theme.</summary>
    [ObservableProperty]
    public partial AppIconTint AppIconTint { get; set; } = AppIconTint.Blue;

    /// <summary>
    /// A named theme (Nord, Crème…), or None for Almatter's own palette.
    /// While one is chosen it decides what ThemeMode, Accent and
    /// ReducedContrast normally do. Those three keep their saved values all
    /// the same, so going back to None brings the user's own look back as
    /// they left it.
    /// </summary>
    [ObservableProperty]
    public partial AppNamedTheme NamedTheme { get; set; } = AppNamedTheme.None;

    /// <summary>Called after the palette changes so the ticked boxes of the settings panel take the new accent.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(ReducedContrastCheckBrush));
        OnPropertyChanged(nameof(LinkPreviewsCheckBrush));
        OnPropertyChanged(nameof(SoundForAllMessagesCheckBrush));
        OnPropertyChanged(nameof(CheckForUpdatesCheckBrush));
    }

    // The three "…CheckBrush" properties are drawing state for the settings
    // panel, computed from the switches next to them. Without [JsonIgnore]
    // each was written into settings.json as a serialized brush.
    [JsonIgnore]
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

    [JsonIgnore]
    public IBrush LinkPreviewsCheckBrush => ShowLinkPreviews ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnShowLinkPreviewsChanged(bool value) => OnPropertyChanged(nameof(LinkPreviewsCheckBrush));

    /// <summary>
    /// For modest machines: no drop shadows (each is a blur the processor
    /// renderer computes, and some sit over the scrolling messages) and no
    /// animations (the thread panel opens and closes at
    /// once instead of sliding, which re-lays the conversation every frame;
    /// scroll bars stay at full size instead of growing under the pointer).
    /// Off by default. Takes effect immediately.
    /// </summary>
    [ObservableProperty]
    public partial bool PerformanceMode { get; set; }

    [JsonIgnore]
    public IBrush PerformanceModeCheckBrush => PerformanceMode ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnPerformanceModeChanged(bool value) => OnPropertyChanged(nameof(PerformanceModeCheckBrush));

    /// <summary>
    /// Every notification makes a sound, not only the ones aimed at this
    /// user (see DesktopNotification.IsPersonal). Those keep their 🔔 either
    /// way, so they still stand out. Off by default: in a busy team a sound
    /// for every channel message soon stops meaning anything.
    /// </summary>
    [ObservableProperty]
    public partial bool SoundForAllMessages { get; set; }

    [JsonIgnore]
    public IBrush SoundForAllMessagesCheckBrush => SoundForAllMessages ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnSoundForAllMessagesChanged(bool value) => OnPropertyChanged(nameof(SoundForAllMessagesCheckBrush));

    /// <summary>
    /// Ask GitHub, shortly after startup and then once a day, whether a newer
    /// Almatter has been published, and say so in a banner. On by default:
    /// the installer is not updated any other way. The request carries
    /// nothing about the user; GitHub sees the address it comes from.
    /// </summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; } = true;

    [JsonIgnore]
    public IBrush CheckForUpdatesCheckBrush => CheckForUpdates ? ColorTokens.Accent : Brushes.Transparent;

    partial void OnCheckForUpdatesChanged(bool value) => OnPropertyChanged(nameof(CheckForUpdatesCheckBrush));

    /// <summary>The version the banner's "Ignorer cette version" was clicked for: no banner for it again, only for a later one.</summary>
    [ObservableProperty]
    public partial string SkippedUpdateVersion { get; set; } = "";

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
