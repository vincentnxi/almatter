namespace Almatter.App.Models;

/// <summary>
/// What the user picked in the settings panel. Only two palettes actually
/// exist (see <see cref="ThemeDefinition"/>); System means "whichever one
/// Windows is currently set to", re-evaluated live whenever Windows flips.
/// A named theme (<see cref="AppNamedTheme"/>), while one is chosen, takes
/// precedence over this setting.
/// </summary>
public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}
