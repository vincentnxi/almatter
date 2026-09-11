using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// Pushes the active palette into a window's resource dictionary, where
/// DynamicResource references pick it up live. Shared by both windows so
/// the login screen and the main window can never drift apart.
/// </summary>
internal static class ThemeResources
{
    /// <summary>
    /// Also sets Application.RequestedThemeVariant, which is what makes
    /// Avalonia's own Fluent chrome — scrollbars, flyout and menu
    /// backgrounds, tooltips, text selection, the caret, TextBox borders —
    /// follow the app instead of staying stubbornly light. Default hands
    /// the decision back to the OS, which is what AppThemeMode.System
    /// wants; ColorTokens has already resolved the same thing for our own
    /// palette, so the two stay in step.
    /// </summary>
    public static void Apply(Window window, AppSettings settings)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = settings.ThemeMode switch
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
        resources["BgHoverBrush"] = ColorTokens.Solid(theme.BgHover);
        resources["DividerBrush"] = ColorTokens.Solid(theme.Divider);
        resources["DividerStrongBrush"] = ColorTokens.Solid(theme.DividerStrong);
        resources["TextPrimaryBrush"] = ColorTokens.TextPrimary;
        resources["TextSecondaryBrush"] = ColorTokens.TextSecondary;
        resources["TextTertiaryBrush"] = ColorTokens.Solid(theme.TextTertiary);
        resources["CardBgBrush"] = ColorTokens.CardBg;
        resources["AccentBrush"] = ColorTokens.Accent;
        resources["AccentSoftBrush"] = ColorTokens.AccentSoft;
        resources["AccentInkBrush"] = ColorTokens.AccentInk;
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

        // Not a palette token, but it travels the same path: one resource
        // the whole window inherits from, swapped live when the choice changes.
        resources["AppFontFamily"] = AppFonts.Resolve(settings.FontChoice);

        // The window's own chrome-level colors aren't reachable through
        // DynamicResource on the Window element itself in XAML (they'd be
        // resolved against the dictionary being replaced), so set them here.
        window.Background = ColorTokens.Solid(theme.BgApp);
        window.Foreground = ColorTokens.TextPrimary;
    }
}
