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
    /// Fluent's scroll bars sit thin and grow, animated, under the pointer.
    /// In performance mode they stay at full size instead, so nothing moves.
    /// The ":is(ScrollViewer)" style in App.axaml reads this resource:
    /// false pins the bars; UnsetValue makes the style set nothing, leaving
    /// Fluent's own behaviour exactly as if the style were absent. Changing
    /// a resource updates the bound value only; adding or removing a style
    /// restyled every control and shifted the window's buttons.
    /// </summary>
    private static void ApplyScrollBarMotion(bool performanceMode)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }
        resources["ScrollBarAutoHide"] = performanceMode ? false : AvaloniaProperty.UnsetValue;
    }

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
                : settings.NamedTheme == AppNamedTheme.Graphite ? ThemeVariant.Light
                : settings.ThemeMode switch
                {
                    AppThemeMode.Light => ThemeVariant.Light,
                    AppThemeMode.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
        }

        ApplyScrollBarMotion(settings.PerformanceMode);

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
        resources["SidebarGroundBrush"] = gradient ? ColorTokens.Solid(ColorTokens.GradientSidebarVeilHex)
            : ColorTokens.Solid(theme.Sidebar?.Bg ?? theme.BgPanel);
        ApplySidebarColors(window, theme);
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
        // Performance mode drops every shadow: empty BoxShadows draw nothing.
        var shadows = !settings.PerformanceMode;
        resources["PopoverShadow"] = shadows ? BoxShadows.Parse(theme.PopoverShadow) : default(BoxShadows);
        resources["CardShadow"] = shadows ? BoxShadows.Parse(theme.CardShadow) : default(BoxShadows);
        // A touch of depth: the composers sit on the conversation like a
        // sheet of paper (a tight contact shadow plus a soft one below), and
        // small labels such as the date card lift barely off their rule.
        // Small or still areas, so cheap to draw on the CPU.
        resources["ComposerShadow"] = !shadows ? default
            : BoxShadows.Parse(theme.IsDark ? "0 1 2 0 #59000000, 0 6 18 -4 #8C000000" : "0 1 2 0 #14101114, 0 6 16 -4 #2E101114");
        resources["RaisedShadow"] = !shadows ? default
            : BoxShadows.Parse(theme.IsDark ? "0 1 3 0 #66000000" : "0 1 3 0 #1F101114");
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
        // Without shadows (performance mode) the plain line comes back.
        var headerShadow = headerTitle && shadows;
        resources["HeaderShadow"] = !headerShadow ? default
            : BoxShadows.Parse(theme.IsDark ? "0 10 18 -8 #BF000000, 0 2 4 -1 #59000000" : "0 8 16 -8 #47101114, 0 1 3 0 #14101114");
        resources["HeaderBorderThickness"] = new Thickness(0, 0, 0, headerShadow ? 0 : 1);

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

    /// <summary>The resources a theme with its own sidebar colours overrides inside the sidebar.</summary>
    private static readonly string[] SidebarResourceKeys =
    [
        "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "CardBgBrush",
        "DividerBrush", "BgHoverBrush", "BgPanelBrush", "TextSelectionBrush",
    ];

    /// <summary>
    /// Inside the main window's sidebar (the "SidebarScope" element), a theme
    /// such as Graphite swaps the text, divider, hover and search-field
    /// colours for its sidebar's own, and the Fluent chrome (caret,
    /// placeholder, scroll bar) for the matching light or dark look. Every
    /// other theme leaves the scope empty, so the sidebar resolves the
    /// window's resources exactly as before. Windows without a sidebar skip this.
    /// </summary>
    private static void ApplySidebarColors(Window window, ThemeDefinition theme)
    {
        if (window.FindControl<ThemeVariantScope>("SidebarScope") is not { } scope)
        {
            return;
        }

        if (theme.Sidebar is not { } sidebar)
        {
            foreach (var key in SidebarResourceKeys)
            {
                scope.Resources.Remove(key);
            }
            scope.RequestedThemeVariant = ThemeVariant.Default;
            return;
        }

        scope.RequestedThemeVariant = sidebar.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        scope.Resources["TextPrimaryBrush"] = ColorTokens.Solid(sidebar.TextPrimary);
        scope.Resources["TextSecondaryBrush"] = ColorTokens.Solid(sidebar.TextSecondary);
        scope.Resources["TextTertiaryBrush"] = ColorTokens.Solid(sidebar.TextTertiary);
        scope.Resources["CardBgBrush"] = ColorTokens.Solid(sidebar.Rail);
        scope.Resources["DividerBrush"] = ColorTokens.Solid(sidebar.Divider);
        scope.Resources["BgHoverBrush"] = ColorTokens.Solid(sidebar.Hover);
        // The ring around a presence dot is cut from the sidebar's ground.
        scope.Resources["BgPanelBrush"] = ColorTokens.Solid(sidebar.Bg);
        scope.Resources["TextSelectionBrush"] = ColorTokens.Solid(ThemeDefinition.Mix(theme.AccentHex, sidebar.Bg, 0.55));
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
