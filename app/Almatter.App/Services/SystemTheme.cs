using Avalonia;
using Avalonia.Platform;

namespace Almatter.App.Services;

/// <summary>
/// Reads the OS light/dark preference. Avalonia's PlatformSettings covers
/// this on every backend (Windows, macOS, Linux), so nothing here is
/// Windows-specific — unlike the native title-bar tint in MainWindow.
/// </summary>
internal static class SystemTheme
{
    /// <summary>False when the platform can't say (no Application yet, or a backend with no such notion) — light is the safer assumption.</summary>
    public static bool PrefersDark =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
}
