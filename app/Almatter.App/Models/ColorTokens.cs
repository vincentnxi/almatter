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

    /// <summary>The selected sidebar row: fill, text, outline and shadow. See ThemeDefinition.SelectedRowHex.</summary>
    public static IBrush SelectedRow { get; private set; } = Solid("#D7E3F0");
    public static IBrush SelectedRowInk { get; private set; } = Solid("#074581");
    public static IBrush SelectedRowBorder { get; private set; } = Brushes.Transparent;
    public static BoxShadows SelectedRowShadow { get; private set; }

    /// <summary>Reaction pills: outlined only on most themes, tinted on Ark. See ReactionItem.</summary>
    public static IBrush ReactionMineBorder { get; private set; } = Solid(ThemeDefinition.Light.AccentHex);
    public static IBrush ReactionMineBackground { get; private set; } = Brushes.Transparent;
    public static IBrush ReactionBackground { get; private set; } = Brushes.Transparent;

    /// <summary>Behind selected text. AccentSoft itself on a light palette; a brighter blue on a dark one.</summary>
    public static IBrush TextSelection { get; private set; } = Solid("#D7E3F0");

    public static IBrush TextPrimary { get; private set; } = Solid(ThemeDefinition.Light.TextPrimary);
    public static IBrush TextSecondary { get; private set; } = Solid(ThemeDefinition.Light.TextSecondary);
    public static IBrush TextTertiary { get; private set; } = Solid(ThemeDefinition.Light.TextTertiary);
    public static IBrush CardBg { get; private set; } = Solid(ThemeDefinition.Light.CardBg);
    public static IBrush Divider { get; private set; } = Solid(ThemeDefinition.Light.Divider);

    /// <summary>
    /// A sidebar row with nothing waiting: secondary text, or (Salon) the
    /// quieter tertiary, so an unread row in the main text colour stands
    /// out clearly from it.
    /// </summary>
    public static IBrush ReadRowInk { get; private set; } = Solid(ThemeDefinition.Light.TextSecondary);

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
        AccentSoft = theme.AccentSoftHex is { } soft
            ? Solid(soft)
            : new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.BgPanel), 0.88));

        // A pastel accent on a light palette can't be read as text, so the
        // palette hands over a deep shade of it for that (Ark); every other
        // palette's accent reads as text as it is.
        AccentInk = theme.AccentDeepHex is { } deep
            ? Solid(deep)
            : new SolidColorBrush(Lerp(accentColor, Color.Parse(theme.TextPrimary), 0.35));
        AccentText = theme.AccentDeepHex is { } deepText ? Solid(deepText) : Accent;

        // The selected sidebar row: the accent tint with accent-coloured
        // text, or (Ark) a raised card in the panel's own ink.
        SelectedRow = theme.SelectedRowHex is { } card ? Solid(card) : AccentSoft;
        SelectedRowInk = theme.SelectedRowHex is null ? AccentInk : Solid(theme.TextPrimary);
        SelectedRowBorder = theme.SelectedRowHex is null ? Brushes.Transparent : Solid(theme.Divider);
        // Reactions. On a light Ark palette the pastel itself is too faint
        // for an outline, so yours is ringed in its deep shade, at 45 %.
        // Salon also has a deep shade, but only for text: its accent is
        // strong enough to outline a pill as it is.
        ReactionMineBorder = theme.ArkStyle && theme.AccentDeepHex is { } ring ? Solid("#73" + ring[1..]) : Accent;
        var filledReactions = theme.ArkStyle || theme.SalonStyle;
        ReactionMineBackground = filledReactions ? AccentSoft : Brushes.Transparent;
        ReactionBackground = filledReactions ? Solid(theme.BgPanel) : Brushes.Transparent;

        // Strong enough to lift the card off the sidebar, as in the mockup:
        // a fine edge plus a soft fall below. Off with the "Shadows" setting.
        SelectedRowShadow = theme.SelectedRowHex is null || !theme.CastsShadows
            ? default
            : BoxShadows.Parse(theme.IsDark
                ? "0 1 2 0 #59000000, 0 6 14 -4 #8C000000"
                : "0 1 2 0 #14101114, 0 6 14 -4 #33101114");

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
        ReadRowInk = theme.SalonStyle ? TextTertiary : TextSecondary;

        // Over a gradient, a solid selection or divider would be right at one
        // height of the window and a visible patch at another. See-through
        // ones pick up whatever is under them.
        if (theme.HasGradientGround)
        {
            AccentSoft = new SolidColorBrush(Color.FromArgb(GradientAccentSoftAlpha, accentColor.R, accentColor.G, accentColor.B));
            Divider = Solid(GradientDividerHex);
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
