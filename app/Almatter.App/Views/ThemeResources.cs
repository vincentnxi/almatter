using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// Pushes the active palette into a window's resource dictionary, where
/// DynamicResource references pick it up live. Shared by every window so
/// the login screen, the settings and the main window can never drift apart.
///
/// Only colours vary from one theme to the next: every theme keeps
/// Almatter's own layout, shapes and fonts, which are written into the
/// windows' XAML.
/// </summary>
internal static class ThemeResources
{
    /// <summary>The channel header's height: the drag band, with the window's buttons centred on it, when there is no title bar.</summary>
    public const double HeaderTitleBarHeight = 52;

    /// <summary>
    /// Also sets Application.RequestedThemeVariant, which is what makes
    /// Avalonia's own Fluent chrome — scrollbars, flyout and menu
    /// backgrounds, tooltips, text selection, the caret, TextBox borders —
    /// follow the app instead of staying stubbornly light. Default hands
    /// the decision back to the OS, which is what AppThemeMode.System
    /// wants; ColorTokens has already resolved the same thing for our own
    /// palette, so the two stay in step.
    /// </summary>
    /// <param name="isMainWindow">True for the main window, the only one drawn without a title bar (on Windows).</param>
    public static void Apply(Window window, AppSettings settings, bool isMainWindow = false)
    {
        if (Application.Current is { } app)
        {
            // A named theme is light or dark by nature, whatever the mode
            // setting says. That also gives context menus the right one of
            // their two looks: they stay system-styled on purpose (App.axaml).
            app.RequestedThemeVariant = NamedThemes.Find(settings.NamedTheme) is { } named
                ? (named.IsDark ? ThemeVariant.Dark : ThemeVariant.Light)
                : settings.ThemeMode switch
                {
                    AppThemeMode.Light => ThemeVariant.Light,
                    AppThemeMode.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
        }

        var theme = ColorTokens.Theme;
        var resources = window.Resources;

        resources["BgAppBrush"] = ColorTokens.Solid(theme.BgApp);
        resources["BgPanelBrush"] = ColorTokens.Solid(theme.BgPanel);
        resources["BgRailBrush"] = ColorTokens.Solid(theme.BgRail);
        var gradient = theme.HasGradientGround;
        resources["BgHoverBrush"] = ColorTokens.Solid(gradient ? ColorTokens.GradientHoverHex : theme.BgHover);
        resources["DividerBrush"] = ColorTokens.Divider;

        // The three big areas. On a gradient theme GroundBackdrop paints the
        // gradient once behind everything; the sidebar lays a dark veil over
        // it and the thread panel lets it through, so it runs across the
        // whole window.
        resources["GroundTheme"] = theme;
        resources["SidebarGroundBrush"] = gradient ? ColorTokens.Solid(ColorTokens.GradientSidebarVeilHex) : ColorTokens.Solid(theme.BgPanel);
        resources["ThreadGroundBrush"] = gradient ? Brushes.Transparent : ColorTokens.Solid(theme.BgApp);
        resources["DividerStrongBrush"] = ColorTokens.Solid(theme.DividerStrong);
        resources["TextPrimaryBrush"] = ColorTokens.TextPrimary;
        resources["TextSecondaryBrush"] = ColorTokens.TextSecondary;
        resources["TextTertiaryBrush"] = ColorTokens.Solid(theme.TextTertiary);
        resources["CardBgBrush"] = ColorTokens.CardBg;
        resources["AccentBrush"] = ColorTokens.Accent;
        resources["AccentSoftBrush"] = ColorTokens.AccentSoft;
        resources["AccentInkBrush"] = ColorTokens.AccentInk;
        resources["AccentTextBrush"] = ColorTokens.AccentText;
        resources["TextSelectionBrush"] = ColorTokens.TextSelection;
        resources["OnAccentBrush"] = ColorTokens.Solid(theme.OnAccentHex);
        resources["DangerBrush"] = ColorTokens.Solid(theme.DangerHex);
        resources["DangerSoftBrush"] = ColorTokens.Solid(theme.DangerSoftHex);
        resources["DangerBorderBrush"] = ColorTokens.Solid(theme.DangerBorderHex);
        resources["ScrimBrush"] = ColorTokens.Solid(theme.ScrimHex);
        resources["PresenceOnlineBrush"] = ColorTokens.Solid(theme.PresenceOnlineHex);
        resources["PresenceAwayBrush"] = ColorTokens.Solid(theme.PresenceAwayHex);
        resources["PresenceOfflineBrush"] = ColorTokens.Solid(theme.PresenceOfflineHex);
        resources["PopoverShadow"] = BoxShadows.Parse(theme.PopoverShadow);
        resources["CardShadow"] = BoxShadows.Parse(theme.CardShadow);
        resources["RowRadiusValue"] = new CornerRadius(theme.RowRadius);
        resources["SurfaceRadiusValue"] = new CornerRadius(theme.SurfaceRadius);

        // On Windows the main window has no title bar at all: the sidebar
        // runs up to the top edge and the system's three buttons sit at the
        // right of the channel header, centred on it. The header (and the
        // sidebar's team name) drag the window; its own buttons are marked
        // as ordinary input in MainWindow.axaml. Elsewhere the system's own
        // title bar stays (see docs/PLATFORM_PORTABILITY.md).
        var headerTitle = isMainWindow && System.OperatingSystem.IsWindows();
        if (isMainWindow)
        {
            window.ExtendClientAreaTitleBarHeightHint = headerTitle ? HeaderTitleBarHeight : -1;
            window.ExtendClientAreaToDecorationsHint = headerTitle;
        }
        // Room kept free at the right of whichever header sits under the
        // three buttons (46 px each, MainWindow.axaml): the channel's, or the
        // thread's while one is open.
        resources["CaptionReserveWidth"] = headerTitle ? 3 * 46.0 + 8 : 0.0;
        resources["HeaderTitleOn"] = headerTitle;

        // The header's shadow over the messages, in place of its bottom line.
        // A thin strip, so cheap to redraw as the messages scroll under it.
        resources["HeaderShadow"] = !headerTitle ? default
            : BoxShadows.Parse(theme.IsDark ? "0 10 18 -8 #BF000000, 0 2 4 -1 #59000000" : "0 8 16 -8 #47101114, 0 1 3 0 #14101114");
        resources["HeaderBorderThickness"] = new Thickness(0, 0, 0, headerTitle ? 0 : 1);

        // The red "Nouveau" rule (its label white, or near black on a red too
        // light for white text), and a message naming the reader tinted with
        // the palette's warm "away" colour.
        resources["NewMarkerBrush"] = ColorTokens.Solid(theme.DangerHex);
        resources["OnNewMarkerBrush"] = ColorTokens.Solid(ContrastWithWhite(theme.DangerHex) >= 4.5 ? "#FFFFFF" : "#14161A");
        resources["MentionRowBrush"] = ColorTokens.Solid((theme.IsDark ? "#1A" : "#24") + theme.PresenceAwayHex[1..]);
        resources["MentionBarBrush"] = ColorTokens.Solid(theme.PresenceAwayHex);

        // Not a palette token, but it travels the same path: one resource
        // the whole window inherits from, swapped live when the choice changes.
        resources["AppFontFamily"] = AppFonts.Resolve(settings.FontChoice, settings.EmojiStyle);

        // The window's own chrome-level colors aren't reachable through
        // DynamicResource on the Window element itself in XAML (they'd be
        // resolved against the dictionary being replaced), so set them here.
        // A gradient theme's gradient is drawn on top of this by the window's
        // GroundBackdrop (much cheaper than a gradient brush on the CPU);
        // the solid colour is what shows for every other theme.
        window.Background = ColorTokens.Solid(theme.BgApp);
        window.Foreground = ColorTokens.TextPrimary;
    }

    /// <summary>WCAG contrast of white text on a "#RRGGBB" colour.</summary>
    private static double ContrastWithWhite(string hex)
    {
        static double Channel(string h, int i)
        {
            var v = System.Convert.ToInt32(h.Substring(1 + i * 2, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(hex, 0) + 0.7152 * Channel(hex, 1) + 0.0722 * Channel(hex, 2);
        return 1.05 / (luminance + 0.05);
    }
}
