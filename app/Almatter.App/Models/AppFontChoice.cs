using Avalonia.Media;

namespace Almatter.App.Models;

/// <summary>
/// Which typeface the app renders in. Mattermost is the default so anyone
/// arriving from the official client lands on the lettering they already
/// know; System is there because the native Windows UI font is tighter and
/// will also give a genuinely native look on macOS and Linux later.
/// </summary>
public enum AppFontChoice
{
    /// <summary>Open Sans — the official Mattermost client's own body font, bundled with the app so it renders identically everywhere.</summary>
    Mattermost = 0,

    /// <summary>The platform UI font (Segoe UI Variable Text on Windows), falling back to the bundled Inter elsewhere.</summary>
    System = 1,
}

/// <summary>The two font stacks, and how a choice maps to one.</summary>
public static class AppFonts
{
    /// <summary>Bundled from Google Fonts under the SIL OFL 1.1 (see licenses/OpenSans-OFL.txt), so it renders identically on every machine.</summary>
    public const string MattermostStack = "avares://Almatter/Assets/Fonts#Open Sans,Segoe UI Variable Text,Segoe UI,Inter";

    /// <summary>
    /// "Segoe UI Variable" on its own is NOT an installed family name on
    /// Windows 11 — the real ones are the optical sizes Small/Text/Display,
    /// and asking for the bare name silently falls through to whatever the
    /// renderer picks next. Text is the size meant for body copy.
    /// </summary>
    public const string SystemStack = "Segoe UI Variable Text,Segoe UI,Inter";

    public static FontFamily Resolve(AppFontChoice choice) =>
        FontFamily.Parse(choice == AppFontChoice.System ? SystemStack : MattermostStack);
}
