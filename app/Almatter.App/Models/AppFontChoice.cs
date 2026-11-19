using System.Collections.Generic;
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

    /// <summary>
    /// Ark's small labels: section headings, times, dates. Bundled under the
    /// SIL OFL 1.1 (see licenses/Geist-OFL.txt), in a folder of its own,
    /// outside Assets/Fonts, for the reason given on TwemojiFamily: asking
    /// for a family of a folder loads the whole folder, so next to Open Sans
    /// it would sit in memory for every theme. Ark's body text follows the
    /// font choice like every other theme.
    /// </summary>
    public const string ArkMonoStack = "avares://Almatter/Assets/GeistMonoFont#Geist Mono,Cascadia Mono,Consolas";

    /// <summary>
    /// Twemoji's artwork (CC-BY 4.0, see licenses/Twemoji-CC-BY-4.0.txt),
    /// turned into a font by .github/workflows/build-twemoji-font.yml.
    ///
    /// Two things about it are deliberate and easy to undo by mistake:
    ///
    /// It lives in Assets/EmojiFont, not Assets/Fonts. Asking Avalonia for one
    /// family of a folder loads every font in that folder and its subfolders,
    /// so next to Open Sans it would sit in memory for everyone, even with
    /// this style switched off.
    ///
    /// It is a COLRv0 font: emoji made of stacked flat-coloured shapes, the
    /// format of Segoe UI Emoji. Tried and rejected on Avalonia 12.1.5 /
    /// SkiaSharp 3.119: Noto's COLRv1 edition (every emoji drawn as nothing)
    /// and its bitmap edition (visibly pixelated at message size).
    /// </summary>
    public const string TwemojiFamily = "avares://Almatter/Assets/EmojiFont#Twemoji Almatter";

    /// <summary>
    /// Handed to Avalonia's FontManagerOptions at startup (Program.cs) and
    /// filled or emptied by Resolve. Avalonia keeps this very list and reads
    /// it on every character lookup, so changing it takes effect at once.
    /// </summary>
    public static readonly List<FontFallback> EmojiFallbacks = [];

    /// <summary>The bare stack, for drawing a font's name in that font. Leaves the emoji fallback alone.</summary>
    public static FontFamily Preview(AppFontChoice choice) => FontFamily.Parse(StackFor(choice));

    private static string StackFor(AppFontChoice choice) => choice switch
    {
        AppFontChoice.System => SystemStack,
        AppFontChoice.Lato => LatoStack,
        _ => MattermostStack,
    };

    /// <summary>
    /// The app's font for the user's settings. Also switches the Twemoji
    /// fallback on or off, so it is only for applying those settings.
    /// </summary>
    public static FontFamily Resolve(AppFontChoice choice, AppEmojiStyle emoji)
    {
        var stack = StackFor(choice);
        if (emoji != AppEmojiStyle.Twemoji)
        {
            EmojiFallbacks.Clear();
            return FontFamily.Parse(stack);
        }

        if (EmojiFallbacks.Count == 0)
        {
            EmojiFallbacks.Add(new FontFallback { FontFamily = new FontFamily(TwemojiFamily) });
        }

        // Twemoji is reached through EmojiFallbacks, not through the stack.
        // Inside the stack it only works behind a bundled font: behind a
        // system one, Avalonia hands missing characters straight to Windows
        // (so Segoe UI Emoji answered), and behind "Segoe UI Variable Text",
        // which it doesn't match as a family name, Twemoji became the main
        // font and drew every space a full emoji wide. Probed on Avalonia
        // 12.1.5. It stays named at the very end, where it is never reached,
        // only so that the family differs from the plain one and the text
        // already on screen is laid out again with the new fallback.
        return FontFamily.Parse($"{stack},{TwemojiFamily}");
    }
}
