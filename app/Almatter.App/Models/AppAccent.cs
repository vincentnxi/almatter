namespace Almatter.App.Models;

/// <summary>
/// The accent color picked in the settings panel. It tints the selected
/// sidebar row, links, mentions, badges and the primary buttons — everything
/// that reads AccentBrush or one of the tokens derived from it.
///
/// The numbers are what settings.json stores, so they never change meaning:
/// a new color takes the next free one.
/// </summary>
public enum AppAccent
{
    Blue = 0,
    Teal = 1,
    Green = 2,
    Amber = 3,
    Rose = 4,
    Violet = 5,
}

/// <summary>
/// The actual colors behind each <see cref="AppAccent"/>, one per palette
/// for the same reason the original blue has two (see ThemeDefinition): a
/// color dark enough to carry white text on a light ground is too dark to
/// read on the dark one.
///
/// Deliberately muted, and kept close to one another in lightness, so
/// switching accent changes the hue of the interface and nothing else. The
/// light ones all clear 4,5:1 against white, which is what both link text on
/// the conversation and white text on a button need. No red or orange: those
/// would read as the danger color, and as an alert on a mention badge.
/// </summary>
public static class AccentPalette
{
    public static string Hex(AppAccent accent, bool isDark) => accent switch
    {
        AppAccent.Teal => isDark ? "#3DB8B0" : "#0A7A75",
        AppAccent.Green => isDark ? "#5DBB7E" : "#1F7A45",
        AppAccent.Amber => isDark ? "#E0A94A" : "#9A6200",
        AppAccent.Rose => isDark ? "#E583A8" : "#B03468",
        AppAccent.Violet => isDark ? "#A58BF0" : "#6747C7",
        _ => isDark ? ThemeDefinition.Dark.AccentHex : ThemeDefinition.Light.AccentHex,
    };
}
