using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>
/// Small helper so items can carry a ready-to-bind color without XAML
/// converters. Every token here is mutable and app-wide — ApplyTheme
/// updates them on a theme change (including Windows flipping light/dark
/// underneath a "System" preference), and already-built items are told to
/// repaint (see each model's RefreshColors).
/// </summary>
public static class ColorTokens
{
    public static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>The palette currently in force — the single source of truth every other token here is derived from.</summary>
    public static ThemeDefinition Theme { get; private set; } = ThemeDefinition.Light;

    public static IBrush Accent { get; private set; } = Solid(ThemeDefinition.Light.AccentHex);
    public static IBrush AccentSoft { get; private set; } = Solid("#D7E3F0");
    public static IBrush AccentInk { get; private set; } = Solid("#074581");

    /// <summary>
    /// The accent where it is text or an icon straight on the ground: links,
    /// mentions, "3 replies", the pin label. The accent itself, except on a
    /// palette whose accent is too pale for that (ThemeDefinition.AccentDeepHex).
    /// </summary>
    public static IBrush AccentText { get; private set; } = Solid(ThemeDefinition.Light.AccentHex);

    /// <summary>Behind selected text. AccentSoft itself on a light palette; a brighter blue on a dark one.</summary>
    public static IBrush TextSelection { get; private set; } = Solid("#D7E3F0");

    public static IBrush TextPrimary { get; private set; } = Solid(ThemeDefinition.Light.TextPrimary);
    public static IBrush TextSecondary { get; private set; } = Solid(ThemeDefinition.Light.TextSecondary);
    public static IBrush TextTertiary { get; private set; } = Solid(ThemeDefinition.Light.TextTertiary);
    public static IBrush CardBg { get; private set; } = Solid(ThemeDefinition.Light.CardBg);
    public static IBrush Divider { get; private set; } = Solid(ThemeDefinition.Light.Divider);

    // The sidebar rows' colours: the ones above, except on a theme whose
    // sidebar has its own (ThemeDefinition.Sidebar, Graphite).
    public static IBrush SidebarAccentSoft { get; private set; } = AccentSoft;
    public static IBrush SidebarAccentInk { get; private set; } = AccentInk;
    public static IBrush SidebarTextPrimary { get; private set; } = TextPrimary;
    public static IBrush SidebarTextSecondary { get; private set; } = TextSecondary;

    /// <summary>
    /// Full palette swap. AccentSoft/AccentInk are lerped toward the
    /// palette's OWN panel/text colors rather than universal white/black:
    /// on the light palette that lands almost exactly where a white/black
    /// lerp would anyway, but on the dark one it's the difference between a
    /// readable, subtly-tinted selection (soft toward the dark panel, ink
    /// toward the light text) and a washed-out pale highlight with
    /// too-dark, illegible selected text.
    /// </summary>
    public static void ApplyTheme(ThemeDefinition theme)
    {
        Theme = theme;

        var accentColor = Color.Parse(theme.AccentHex);
        Accent = new SolidColorBrush(accentColor);
        AccentSoft = new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.BgPanel), 0.88));

        // An accent too faint to read as text on its own ground comes with
        // a deeper shade for that (Salon); every other
        // palette's accent reads as text as it is.
        AccentInk = theme.AccentDeepHex is { } deep
            ? Solid(deep)
            : new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.TextPrimary), 0.35));
        AccentText = theme.AccentDeepHex is { } deepText ? Solid(deepText) : Accent;

        // AccentSoft is nearly the dark background itself, fine for a whole
        // row but too faint to show which words are selected. Stopping the
        // lerp about halfway keeps the blue visible and light text readable.
        TextSelection = theme.IsDark
            ? new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.BgPanel), 0.55))
            : AccentSoft;

        TextPrimary = Solid(theme.TextPrimary);
        TextSecondary = Solid(theme.TextSecondary);
        TextTertiary = Solid(theme.TextTertiary);
        CardBg = Solid(theme.CardBg);
        Divider = Solid(theme.Divider);

        // Over a gradient, a solid selection or divider would be right at one
        // height of the window and a visible patch at another. See-through
        // ones pick up whatever is under them.
        if (theme.HasGradientGround)
        {
            AccentSoft = new SolidColorBrush(Color.FromArgb(GradientAccentSoftAlpha, accentColor.R, accentColor.G, accentColor.B));
            Divider = Solid(GradientDividerHex);
        }

        if (theme.Sidebar is { } sidebar)
        {
            SidebarAccentSoft = Solid(sidebar.AccentSoft);
            SidebarAccentInk = Solid(sidebar.AccentInk);
            SidebarTextPrimary = Solid(sidebar.TextPrimary);
            SidebarTextSecondary = Solid(sidebar.TextSecondary);
        }
        else
        {
            SidebarAccentSoft = AccentSoft;
            SidebarAccentInk = AccentInk;
            SidebarTextPrimary = TextPrimary;
            SidebarTextSecondary = TextSecondary;
        }
    }

    /// <summary>About 20 % of the accent: the selected-row tint on a gradient ground.</summary>
    public const byte GradientAccentSoftAlpha = 0x33;

    /// <summary>Lines and hover on a gradient ground: a thin white veil.</summary>
    public const string GradientDividerHex = "#17FFFFFF";
    public const string GradientHoverHex = "#14FFFFFF";

    /// <summary>The sidebar's darker veil over the gradient.</summary>
    public const string GradientSidebarVeilHex = "#2E000000";

    /// <summary>
    /// A theme's ground as a brush, for small swatches such as the theme
    /// menu's dots: the gradient when the theme has one (with a stop at 40 %,
    /// as GroundBackdrop draws it), otherwise plain BgApp. Not for the window
    /// itself: on the CPU a gradient brush that size is slow, see GroundBackdrop.
    /// </summary>
    public static IBrush Ground(ThemeDefinition theme)
    {
        if (theme.GroundTopHex is not { } top || theme.GroundBottomHex is not { } bottom)
        {
            return Solid(theme.BgApp);
        }
        return new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(top), 0),
                new GradientStop(Color.Parse(ThemeDefinition.Mix(top, bottom, 0.6)), 0.4),
                new GradientStop(Color.Parse(bottom), 1),
            },
        };
    }

    private static Color Lerp(Color from, Color to, double amount)
    {
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * amount);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    public static IBrush Presence(PresenceStatus status) => status switch
    {
        PresenceStatus.Online => Solid(Theme.PresenceOnlineHex),
        PresenceStatus.Away => Solid(Theme.PresenceAwayHex),
        PresenceStatus.DoNotDisturb => Solid(Theme.PresenceDndHex),
        _ => Solid(Theme.PresenceOfflineHex),
    };
}
