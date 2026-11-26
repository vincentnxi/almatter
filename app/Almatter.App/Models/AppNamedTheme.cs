namespace Almatter.App.Models;

/// <summary>
/// A named theme picked in the settings panel, or None for Almatter's own
/// palette. The palettes themselves are in <see cref="NamedThemes"/>.
///
/// The numbers are what settings.json stores, so they are permanent: never
/// renumber one, and never reuse the number of a theme that was removed. A
/// number the app no longer knows (a removed theme, a settings file from a
/// newer version, or one edited by hand) simply finds no palette, and the
/// app falls back to its own.
///
/// Every theme only changes colours: the layout, shapes, fonts and
/// behaviour are Almatter's own under all of them.
/// </summary>
public enum AppNamedTheme
{
    None = 0,

    // Removed 2026-11-25, numbers never to be reused: Catppuccin = 1, CatppuccinLatte = 2,
    // RosePine = 4, RosePineMoon = 5, RosePineDawn = 6, TokyoNight = 7,
    // Expresso = 8, Ark = 13.
    Nord = 3,
    Cardamome = 9,
    Creme = 10,
    Crepuscule = 11,
    Aube = 12,

    /// <summary>
    /// Slate greys and a blue-violet accent. Unlike the palettes above it
    /// follows the light/dark mode and the contrast setting, so it is not in
    /// <see cref="NamedThemes.All"/>; its accent is its own and not a choice.
    /// </summary>
    Salon = 14,

    /// <summary>
    /// A graphite sidebar beside a very light grey conversation, with a brick
    /// red accent. Light whatever the mode; follows the contrast setting.
    /// Not in <see cref="NamedThemes.All"/> either: see ThemeDefinition.Graphite.
    /// </summary>
    Graphite = 15,
}
