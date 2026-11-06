namespace Almatter.App.Models;

/// <summary>
/// A named theme picked in the settings panel, or None for Almatter's own
/// palette. The palettes themselves are in <see cref="NamedThemes"/>.
///
/// The numbers are what settings.json stores, so they are permanent: never
/// renumber one, and never reuse the number of a theme that was removed. A
/// number the app no longer knows (a settings file from a newer version, or
/// one edited by hand) simply finds no palette, and the app falls back to
/// its own.
/// </summary>
public enum AppNamedTheme
{
    None = 0,
    Catppuccin = 1,
    CatppuccinLatte = 2,
    Nord = 3,
    RosePine = 4,
    RosePineMoon = 5,
    RosePineDawn = 6,
    TokyoNight = 7,
    Expresso = 8,
    Cardamome = 9,
    Creme = 10,
}
