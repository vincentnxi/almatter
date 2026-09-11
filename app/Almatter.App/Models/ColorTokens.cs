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

    public static IBrush TextPrimary { get; private set; } = Solid(ThemeDefinition.Light.TextPrimary);
    public static IBrush TextSecondary { get; private set; } = Solid(ThemeDefinition.Light.TextSecondary);
    public static IBrush CardBg { get; private set; } = Solid(ThemeDefinition.Light.CardBg);
    public static IBrush Divider { get; private set; } = Solid(ThemeDefinition.Light.Divider);

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
        AccentInk = new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.TextPrimary), 0.35));

        TextPrimary = Solid(theme.TextPrimary);
        TextSecondary = Solid(theme.TextSecondary);
        CardBg = Solid(theme.CardBg);
        Divider = Solid(theme.Divider);
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
