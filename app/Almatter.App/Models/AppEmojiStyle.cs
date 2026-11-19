namespace Almatter.App.Models;

/// <summary>
/// Which pictures standard emoji are drawn with. System is the default
/// because it costs nothing: the operating system already has an emoji font
/// loaded. Twemoji is there for the machines whose own font falls short, such
/// as Windows 10 (no emoji newer than 2019) or an older Linux, and for
/// Windows, which has no flags at all and spells "FR" where 🇫🇷 should be.
/// </summary>
public enum AppEmojiStyle
{
    /// <summary>The platform's emoji font: Segoe UI Emoji on Windows.</summary>
    System = 0,

    /// <summary>Twemoji, bundled as a vector font, identical on every machine.</summary>
    Twemoji = 1,
}
