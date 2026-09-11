namespace Almatter.App.Models;

/// <summary>
/// One of the two palettes — light or dark. Everything visual that varies
/// between them lives here: backgrounds, text, accent, semantic (danger,
/// presence) colors, the scrim and shadow used by overlays, and the corner
/// radii. Typography does NOT vary: the whole app is on one font stack.
///
/// The accent is deliberately per-palette rather than shared. #005FB8 is
/// the Windows system blue and is right on a light background, but far too
/// dark to read on #16181C — the dark palette lifts it to #4C9DF5.
/// </summary>
public sealed class ThemeDefinition
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

    /// <summary>Resolves the user's preference to an actual palette. System defers to whatever Windows currently reports.</summary>
    public static ThemeDefinition Resolve(AppThemeMode mode, bool systemPrefersDark) => mode switch
    {
        AppThemeMode.Light => Light,
        AppThemeMode.Dark => Dark,
        _ => systemPrefersDark ? Dark : Light,
    };
}
