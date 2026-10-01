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

    /// <summary>Lato — a little narrower than Open Sans, with a much firmer bold. Bundled in two weights only, regular and bold: it has no semi-bold, so semi-bold labels draw in bold.</summary>
    Lato = 2,
}

/// <summary>The font stacks, and how a choice maps to one.</summary>
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

    /// <summary>
    /// Bundled under the SIL OFL 1.1 (see licenses/Lato-OFL.txt). The files
    /// are Lato 1.104, the edition Google Fonts serves to websites, not the
    /// 2.015 in its repository: 60 KB a weight instead of 650, and a bold as
    /// firm as the one people know from the web. It covers Western European
    /// and Polish only; anything else falls through to the rest of the stack.
    /// </summary>
    public const string LatoStack = "avares://Almatter/Assets/Fonts#Lato,Segoe UI Variable Text,Segoe UI,Inter";

    public static FontFamily Resolve(AppFontChoice choice) =>
        FontFamily.Parse(choice switch
        {
            AppFontChoice.System => SystemStack,
            AppFontChoice.Lato => LatoStack,
            _ => MattermostStack,
        });
}
