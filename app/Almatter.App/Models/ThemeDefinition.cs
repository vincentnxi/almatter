using System;

namespace Almatter.App.Models;

/// <summary>
/// One of the two palettes — light or dark. Everything visual that varies
/// between them lives here: backgrounds, text, accent, semantic (danger,
/// presence) colors, the scrim and shadow used by overlays, and the corner
/// radii. Typography does NOT vary: the whole app is on one font stack.
///
/// The accent is deliberately per-palette rather than shared. #005FB8 is
/// the Windows system blue and is right on a light background, but far too
/// dark to read on #16181C — the dark palette lifts it to #4C9DF5. Those two
/// are the default; Resolve swaps in the user's own choice of accent.
/// </summary>
public sealed record ThemeDefinition
{
    public required string Name { get; init; }

    /// <summary>Drives the Fluent theme variant (scrollbars, flyouts, tooltips, text selection) and the native title bar.</summary>
    public required bool IsDark { get; init; }

    /// <summary>The conversation surface — the largest area on screen, so the least tinted.</summary>
    public required string BgApp { get; init; }

    /// <summary>The sidebar and other secondary panels.</summary>
    public required string BgPanel { get; init; }

    /// <summary>Inset areas inside a panel (the composer well, the search field).</summary>
    public required string BgRail { get; init; }

    /// <summary>Pointer-over feedback on sidebar rows — sits between BgPanel and BgRail.</summary>
    public required string BgHover { get; init; }

    public required string Divider { get; init; }
    public required string DividerStrong { get; init; }
    public required string TextPrimary { get; init; }
    public required string TextSecondary { get; init; }
    public required string TextTertiary { get; init; }

    /// <summary>Raised surfaces: message cards, popovers, the settings panel.</summary>
    public required string CardBg { get; init; }

    public required string AccentHex { get; init; }

    /// <summary>Text and icons drawn ON the accent — white reads on the dark light-mode blue, but is glary on the light dark-mode blue.</summary>
    public required string OnAccentHex { get; init; }

    public required string DangerHex { get; init; }

    /// <summary>Error-banner fill and border.</summary>
    public required string DangerSoftHex { get; init; }
    public required string DangerBorderHex { get; init; }

    /// <summary>Full-window veil behind the loading state — the app background at ~70% opacity.</summary>
    public required string ScrimHex { get; init; }

    /// <summary>Drop shadow under popovers. Dark needs a heavier one: a soft black shadow is invisible against a dark ground.</summary>
    public required string PopoverShadow { get; init; }
    public required string CardShadow { get; init; }

    public required string PresenceOnlineHex { get; init; }
    public required string PresenceAwayHex { get; init; }
    public required string PresenceDndHex { get; init; }
    public required string PresenceOfflineHex { get; init; }

    public required double RowRadius { get; init; }
    public required double SurfaceRadius { get; init; }

    /// <summary>
    /// Top and bottom of a gradient behind the whole window, or both null
    /// for the usual solid BgApp. Only some named themes have one.
    /// </summary>
    public string? GroundTopHex { get; init; }
    public string? GroundBottomHex { get; init; }

    public bool HasGradientGround => GroundTopHex is not null && GroundBottomHex is not null;

    /// <summary>
    /// The accent as text and icons on the ground (links, mentions, "3
    /// replies", the pin label), and as the selected row's text. Null for
    /// the usual case where the accent itself reads as text; set when the
    /// accent is a pastel on a light palette, which it doesn't.
    /// </summary>
    public string? AccentDeepHex { get; init; }

    /// <summary>The selected row's tint, when the usual one (the accent nearly faded into the panel) would vanish. Null for the usual one.</summary>
    public string? AccentSoftHex { get; init; }

    /// <summary>
    /// Panels drawn as rounded cards with a gap around them, over a ground
    /// of their own (WindowGroundHex), rather than edge to edge with a line
    /// between them. Only Ark has them.
    /// </summary>
    public bool FloatingPanels { get; init; }

    /// <summary>What shows in the gaps between floating panels. Null: the window is BgApp, as usual.</summary>
    public string? WindowGroundHex { get; init; }

    /// <summary>
    /// Ark's own styling beyond colours and panels: small monospaced labels
    /// (Geist Mono), a light channel title, ink-coloured primary
    /// buttons, the selected row as a raised card, the composer in a well,
    /// and the title bar drawn into the window (on Windows).
    /// </summary>
    public bool ArkStyle { get; init; }

    /// <summary>The selected sidebar row's card. Null: the usual accent tint.</summary>
    public string? SelectedRowHex { get; init; }

    /// <summary>Ark's soft shadows are on (the user's "Shadows" setting): panels, composer, header buttons and the selected row.</summary>
    public bool CastsShadows { get; init; }

    /// <summary>
    /// Salon's styling beyond colours: slate greys with no line between the
    /// sidebar and the conversation, capitalised section headings, red
    /// mention badges, a small bar left of an unread channel, tinted
    /// mentions in messages, the composer on one line in a filled box, and
    /// a thin title bar drawn into the window (on Windows) in the sidebar's
    /// colour.
    /// </summary>
    public bool SalonStyle { get; init; }

    /// <summary>The composer's fill. Null: the card colour, as usual.</summary>
    public string? ComposerHex { get; init; }

    /// <summary>The sidebar's foot (you, your status, the settings). Null: no fill of its own, as usual.</summary>
    public string? FooterHex { get; init; }

    /// <summary>The text, buttons and tools of the composer sit on one line (Ark, Salon), not the text above a row of buttons.</summary>
    public bool OneLineComposer => ArkStyle || SalonStyle;

    /// <summary>The sidebar's section headings are set in capitals (Ark, Salon).</summary>
    public bool CapitalHeadings => ArkStyle || SalonStyle;

    public static readonly ThemeDefinition Light = new()
    {
        Name = "Clair",
        IsDark = false,
        BgApp = "#FCFCFD",
        BgPanel = "#F5F6F8",
        BgRail = "#EDEFF3",
        BgHover = "#ECEEF2",
        Divider = "#E6E8EC",
        DividerStrong = "#D3D7DE",
        TextPrimary = "#14161A",
        TextSecondary = "#5B626D",
        TextTertiary = "#8A929E",
        CardBg = "#FFFFFF",
        AccentHex = "#005FB8",
        OnAccentHex = "#FFFFFF",
        DangerHex = "#C42B1C",
        DangerSoftHex = "#FBEAE8",
        DangerBorderHex = "#F0C4BE",
        ScrimHex = "#B3FCFCFD",
        PopoverShadow = "0 6 20 0 #33000000",
        CardShadow = "0 2 6 0 #1A000000",
        PresenceOnlineHex = "#1F9E4A",
        PresenceAwayHex = "#D9A521",
        PresenceDndHex = "#C42B1C",
        PresenceOfflineHex = "#9AA1AB",
        RowRadius = 6,
        SurfaceRadius = 10,
    };

    public static readonly ThemeDefinition Dark = new()
    {
        Name = "Sombre",
        IsDark = true,
        BgApp = "#16181C",
        BgPanel = "#1B1E23",
        BgRail = "#202429",
        BgHover = "#23272E",
        Divider = "#282C33",
        DividerStrong = "#383D46",
        TextPrimary = "#E9EBEE",
        TextSecondary = "#9DA5B0",
        TextTertiary = "#6E7681",
        CardBg = "#1E2127",
        AccentHex = "#4C9DF5",
        OnAccentHex = "#0F1114",
        DangerHex = "#F0574A",
        DangerSoftHex = "#2E1B1A",
        DangerBorderHex = "#5A2B27",
        ScrimHex = "#B316181C",
        PopoverShadow = "0 6 20 0 #99000000",
        CardShadow = "0 2 8 0 #80000000",
        PresenceOnlineHex = "#3FB950",
        PresenceAwayHex = "#D9A521",
        PresenceDndHex = "#F0574A",
        PresenceOfflineHex = "#6E7681",
        RowRadius = 6,
        SurfaceRadius = 10,
    };

    /// <summary>
    /// Ark, light: neutral greys, the panels as near-white cards floating
    /// on a darker grey ground. The accent fields are placeholders: Resolve
    /// lays the chosen pastel over them.
    /// </summary>
    public static readonly ThemeDefinition ArkLight = new()
    {
        Name = "Ark",
        IsDark = false,
        WindowGroundHex = "#E3E4E7",
        FloatingPanels = true,
        ArkStyle = true,
        SelectedRowHex = "#FFFFFF",
        BgApp = "#FCFCFD",
        BgPanel = "#F4F5F6",
        BgRail = "#ECEDEF",
        BgHover = "#EEEFF1",
        Divider = "#E0E1E5",
        DividerStrong = "#C9CBD0",
        TextPrimary = "#16171A",
        TextSecondary = "#5C5F65",
        TextTertiary = "#73767C",
        CardBg = "#FFFFFF",
        AccentHex = ArkAccentPalette.PastelHex(AppArkAccent.Citron),
        OnAccentHex = ArkAccentPalette.OnPastelHex,
        DangerHex = "#C42B1C",
        DangerSoftHex = "#FBEAE8",
        DangerBorderHex = "#F0C4BE",
        ScrimHex = "#B3FCFCFD",
        PopoverShadow = "0 8 24 0 #2E101114",
        CardShadow = "0 2 6 0 #14101114",
        PresenceOnlineHex = "#1F9E4A",
        PresenceAwayHex = "#D9A521",
        PresenceDndHex = "#C42B1C",
        PresenceOfflineHex = "#8E9197",
        RowRadius = 7,
        SurfaceRadius = 10,
    };

    /// <summary>Ark, dark: anthracite cards on a near-black ground. The pastels read as text here as they are.</summary>
    public static readonly ThemeDefinition ArkDark = new()
    {
        Name = "Ark",
        IsDark = true,
        WindowGroundHex = "#08090A",
        FloatingPanels = true,
        ArkStyle = true,
        SelectedRowHex = "#2A2B30",
        BgApp = "#1A1B1F",
        BgPanel = "#151619",
        BgRail = "#212226",
        BgHover = "#1E1F23",
        Divider = "#2A2C31",
        DividerStrong = "#3A3D43",
        TextPrimary = "#EDEEF0",
        TextSecondary = "#A2A5AC",
        TextTertiary = "#80848C",
        CardBg = "#222328",
        AccentHex = ArkAccentPalette.PastelHex(AppArkAccent.Citron),
        OnAccentHex = ArkAccentPalette.OnPastelHex,
        DangerHex = "#F06A6E",
        DangerSoftHex = "#2E1B1C",
        DangerBorderHex = "#5A2B2D",
        ScrimHex = "#B31A1B1F",
        PopoverShadow = "0 10 28 0 #B3000000",
        CardShadow = "0 2 8 0 #80000000",
        PresenceOnlineHex = "#5FC77A",
        PresenceAwayHex = "#E0B04A",
        PresenceDndHex = "#F06A6E",
        PresenceOfflineHex = "#80848C",
        RowRadius = 7,
        SurfaceRadius = 10,
    };

    /// <summary>
    /// Salon, dark: slate greys, the sidebar a step darker than the
    /// conversation and the inset fields darker still, with a blue-violet
    /// accent. That accent is only 2,8:1 as text on the conversation, so
    /// text and icons in it (links, mentions, "3 replies") take a lighter
    /// shade, 5,0:1, through AccentDeepHex.
    /// Measured on the conversation: text 11,4:1, secondary 6,5:1,
    /// tertiary 4,5:1 (4,9:1 on the sidebar).
    /// </summary>
    public static readonly ThemeDefinition SalonDark = new()
    {
        Name = "Salon",
        IsDark = true,
        SalonStyle = true,
        // The window's own ground shows only in the title bar drawn into it,
        // which takes the sidebar's colour.
        WindowGroundHex = "#2B2D31",
        SelectedRowHex = "#404249",
        ComposerHex = "#383A40",
        FooterHex = "#232428",
        BgApp = "#313338",
        BgPanel = "#2B2D31",
        BgRail = "#1E1F22",
        BgHover = "#35373C",
        Divider = "#1F2023",
        DividerStrong = "#3F4147",
        TextPrimary = "#F2F3F5",
        TextSecondary = "#B5BAC1",
        TextTertiary = "#949BA4",
        CardBg = "#2B2D31",
        AccentHex = "#5865F2",
        OnAccentHex = "#FFFFFF",
        AccentDeepHex = "#949CF7",
        DangerHex = "#F23F43",
        DangerSoftHex = "#483439",
        DangerBorderHex = "#6F373C",
        ScrimHex = "#B3313338",
        PopoverShadow = "0 8 24 0 #B3000000",
        CardShadow = "0 2 8 0 #80000000",
        PresenceOnlineHex = "#23A55A",
        PresenceAwayHex = "#F0B232",
        PresenceDndHex = "#F23F43",
        PresenceOfflineHex = "#80848E",
        RowRadius = 4,
        SurfaceRadius = 8,
    };

    /// <summary>
    /// Salon, light: white conversation, pale grey sidebar. The accent
    /// itself is 4,6:1 on white but 4,1:1 on the sidebar, so text in it
    /// takes the deeper shade, 6,4:1 (5,8:1 on the sidebar). Secondary
    /// text is 7,2:1 on the sidebar, tertiary 5,8:1.
    /// </summary>
    public static readonly ThemeDefinition SalonLight = new()
    {
        Name = "Salon",
        IsDark = false,
        SalonStyle = true,
        WindowGroundHex = "#F2F3F5",
        SelectedRowHex = "#D7D9DD",
        ComposerHex = "#EBEDEF",
        FooterHex = "#EBEDEF",
        BgApp = "#FFFFFF",
        BgPanel = "#F2F3F5",
        BgRail = "#E3E5E8",
        BgHover = "#E8E9EC",
        Divider = "#E1E2E5",
        DividerStrong = "#C4C9CE",
        TextPrimary = "#060607",
        TextSecondary = "#4E5058",
        TextTertiary = "#5C5E66",
        CardBg = "#FFFFFF",
        AccentHex = "#5865F2",
        OnAccentHex = "#FFFFFF",
        AccentDeepHex = "#4752C4",
        DangerHex = "#DA373C",
        DangerSoftHex = "#FCEFEF",
        DangerBorderHex = "#F6CFD0",
        ScrimHex = "#B3FFFFFF",
        PopoverShadow = "0 8 24 0 #2E060607",
        CardShadow = "0 2 6 0 #1A060607",
        PresenceOnlineHex = "#23A55A",
        PresenceAwayHex = "#F0B232",
        PresenceDndHex = "#DA373C",
        PresenceOfflineHex = "#80848E",
        RowRadius = 4,
        SurfaceRadius = 8,
    };

    /// <summary>
    /// Resolves the user's preferences to an actual palette. System defers
    /// to whatever Windows currently reports. The accent is laid over the
    /// palette last: only its hue is the user's choice, the shade comes from
    /// whether the palette is light or dark (see AccentPalette).
    /// </summary>
    public static ThemeDefinition Resolve(
        AppThemeMode mode,
        bool systemPrefersDark,
        bool reducedContrast = false,
        AppAccent accent = AppAccent.Blue,
        AppNamedTheme named = AppNamedTheme.None,
        AppArkAccent arkAccent = AppArkAccent.Citron,
        bool arkShadows = true)
    {
        // A named theme decides everything on its own: the mode, the accent
        // and the softening are left out, not lost — they are still saved,
        // and apply again once the choice goes back to None.
        if (NamedThemes.Find(named) is { } palette)
        {
            return FromPalette(palette);
        }

        var dark = mode switch
        {
            AppThemeMode.Light => false,
            AppThemeMode.Dark => true,
            _ => systemPrefersDark,
        };

        // Ark follows the mode and the softening like Almatter's own
        // palette, with its own pastel accent instead of the usual one.
        if (named == AppNamedTheme.Ark)
        {
            var ark = dark ? ArkDark : ArkLight;
            if (reducedContrast)
            {
                ark = ark.Softened();
            }
            var pastel = ArkAccentPalette.PastelHex(arkAccent);
            return ark with
            {
                AccentHex = pastel,
                OnAccentHex = ArkAccentPalette.OnPastelHex,
                AccentDeepHex = dark ? null : ArkAccentPalette.DeepHex(arkAccent),
                // Halfway to white: the usual 88 % fade would leave a pastel
                // all but invisible on the light sidebar.
                AccentSoftHex = dark ? null : Mix(pastel, "#FFFFFF", 0.45),
                CastsShadows = arkShadows,
            };
        }

        // Salon follows the mode and the softening too, with its one accent.
        if (named == AppNamedTheme.Salon)
        {
            var salon = dark ? SalonDark : SalonLight;
            return reducedContrast ? salon.Softened() : salon;
        }

        var theme = dark ? Dark : Light;
        if (reducedContrast)
        {
            theme = theme.Softened();
        }
        return theme with { AccentHex = AccentPalette.Hex(accent, theme.IsDark) };
    }

    /// <summary>
    /// A named theme's palette in Almatter's terms. Most tokens map one to
    /// one; the ones a palette file has no word for are derived from it,
    /// so that every theme gets them without anyone having to pick them:
    ///
    /// - the hover tint sits halfway between the panel and the palette's own
    ///   hover colour, which keeps Almatter's rule that hovering is lighter
    ///   than the inset fields (some palettes push their hover much further);
    /// - cards and popovers lift a little off the window toward the text on a
    ///   dark palette, and toward white on a light one, as Almatter's own do;
    /// - the error banner is the danger colour washed into the window;
    /// - shadows, radii and, unless the palette has a green, the "online"
    ///   dot are Almatter's own for the same lightness.
    /// </summary>
    public static ThemeDefinition FromPalette(NamedPalette p)
    {
        var basis = p.IsDark ? Dark : Light;
        return new ThemeDefinition
        {
            Name = p.Name,
            IsDark = p.IsDark,
            BgApp = p.Window,
            BgPanel = p.Panel,
            BgRail = p.Surface,
            BgHover = Mix(p.Panel, p.SurfaceHover, 0.5),
            Divider = p.Outline,
            DividerStrong = p.SurfaceActive,
            TextPrimary = p.Text,
            TextSecondary = p.Secondary,
            TextTertiary = p.Dim,
            CardBg = p.IsDark ? Mix(p.Window, p.Text, 0.05) : Mix(p.Window, "#FFFFFF", 0.6),
            AccentHex = p.Accent,
            OnAccentHex = p.OnAccent,
            DangerHex = p.Danger,
            DangerSoftHex = Mix(p.Window, p.Danger, p.IsDark ? 0.12 : 0.08),
            DangerBorderHex = Mix(p.Window, p.Danger, p.IsDark ? 0.32 : 0.24),
            ScrimHex = "#B3" + p.Window[1..],
            PopoverShadow = basis.PopoverShadow,
            CardShadow = basis.CardShadow,
            PresenceOnlineHex = p.Online ?? basis.PresenceOnlineHex,
            PresenceAwayHex = p.Warning,
            PresenceDndHex = p.Danger,
            PresenceOfflineHex = p.Dim,
            RowRadius = basis.RowRadius,
            SurfaceRadius = basis.SurfaceRadius,
            GroundTopHex = p.GroundTop,
            GroundBottomHex = p.GroundBottom,
        };
    }

    /// <summary>How far the surfaces move toward mid-grey: the light palette dims, the dark one lifts off pure black.</summary>
    private const double SurfaceSoftening = 0.05;

    /// <summary>How far the main text moves toward its own background.</summary>
    private const double TextSoftening = 0.17;

    /// <summary>
    /// A gentler version of this palette for long reading sessions.
    ///
    /// Only the MAIN text is softened, plus the surfaces under it. Secondary
    /// and tertiary text are deliberately left where they are: they are
    /// already quiet, and dimming everything by the same amount flattens the
    /// hierarchy and pushes secondary text below the 4,5:1 readability
    /// threshold. Softening the peak alone brings the levels closer together,
    /// which is what "less harsh" actually means.
    ///
    /// Measured on the two palettes: main text drops from 17,7:1 to 10,1:1
    /// (light) and 14,9:1 to 10,0:1 (dark) — roughly VS Code's own defaults,
    /// which is the comparison this was asked against — while secondary text
    /// stays at 5,7:1 and 6,8:1.
    ///
    /// The accent is untouched: it has to stay recognisable as the accent.
    /// AccentSoft and AccentInk follow automatically, since ColorTokens
    /// derives them from whatever panel and text colors it is handed.
    /// </summary>
    public ThemeDefinition Softened() => new()
    {
        Name = Name,
        IsDark = IsDark,
        BgApp = TowardMidGrey(BgApp),
        BgPanel = TowardMidGrey(BgPanel),
        BgRail = TowardMidGrey(BgRail),
        BgHover = TowardMidGrey(BgHover),
        Divider = TowardMidGrey(Divider),
        DividerStrong = TowardMidGrey(DividerStrong),
        TextPrimary = Mix(TextPrimary, BgApp, TextSoftening),
        TextSecondary = TextSecondary,
        TextTertiary = TextTertiary,
        CardBg = TowardMidGrey(CardBg),
        AccentHex = AccentHex,
        OnAccentHex = OnAccentHex,
        DangerHex = DangerHex,
        DangerSoftHex = DangerSoftHex,
        DangerBorderHex = DangerBorderHex,
        ScrimHex = ScrimHex,
        PopoverShadow = PopoverShadow,
        CardShadow = CardShadow,
        PresenceOnlineHex = PresenceOnlineHex,
        PresenceAwayHex = PresenceAwayHex,
        PresenceDndHex = PresenceDndHex,
        PresenceOfflineHex = PresenceOfflineHex,
        RowRadius = RowRadius,
        SurfaceRadius = SurfaceRadius,
        GroundTopHex = GroundTopHex is null ? null : TowardMidGrey(GroundTopHex),
        GroundBottomHex = GroundBottomHex is null ? null : TowardMidGrey(GroundBottomHex),
        AccentDeepHex = AccentDeepHex,
        AccentSoftHex = AccentSoftHex,
        FloatingPanels = FloatingPanels,
        WindowGroundHex = WindowGroundHex is null ? null : TowardMidGrey(WindowGroundHex),
        ArkStyle = ArkStyle,
        SelectedRowHex = SelectedRowHex is null ? null : TowardMidGrey(SelectedRowHex),
        CastsShadows = CastsShadows,
        SalonStyle = SalonStyle,
        ComposerHex = ComposerHex is null ? null : TowardMidGrey(ComposerHex),
        FooterHex = FooterHex is null ? null : TowardMidGrey(FooterHex),
    };

    private static string TowardMidGrey(string hex) => Mix(hex, "#808080", SurfaceSoftening);

    /// <summary>Blends two "#RRGGBB" strings. Alpha-prefixed values (the scrim) are never passed through here.</summary>
    internal static string Mix(string from, string to, double amount)
    {
        static int Channel(string hex, int index) => Convert.ToInt32(hex.Substring(1 + index * 2, 2), 16);

        var r = (int)Math.Round(Channel(from, 0) + (Channel(to, 0) - Channel(from, 0)) * amount);
        var g = (int)Math.Round(Channel(from, 1) + (Channel(to, 1) - Channel(from, 1)) * amount);
        var b = (int)Math.Round(Channel(from, 2) + (Channel(to, 2) - Channel(from, 2)) * amount);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
