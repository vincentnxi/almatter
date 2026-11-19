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

    public static FontFamily Resolve(AppFontChoice choice, AppEmojiStyle emoji = AppEmojiStyle.System) =>
        FontFamily.Parse(WithEmoji(choice switch
        {
            AppFontChoice.System => SystemStack,
            AppFontChoice.Lato => LatoStack,
            _ => MattermostStack,
        }, emoji));

    /// <summary>
    /// Slots the emoji font in right after the first family of the stack:
    /// the letters still come from the chosen typeface, and whatever it lacks
    /// is looked up in Twemoji before the system fallbacks get a chance to
    /// answer with their own emoji. Never first: the emoji font also carries
    /// the digits, # and * that start keycap emoji, and would take those over.
    /// </summary>
    private static string WithEmoji(string stack, AppEmojiStyle emoji)
    {
        if (emoji != AppEmojiStyle.Twemoji)
        {
            return stack;
        }
        var comma = stack.IndexOf(',');
        return comma < 0
            ? $"{stack},{TwemojiFamily}"
            : $"{stack[..comma]},{TwemojiFamily}{stack[comma..]}";
    }
}
