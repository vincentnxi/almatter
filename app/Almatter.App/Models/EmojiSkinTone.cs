namespace Almatter.App.Models;

/// <summary>
/// The five Unicode skin-tone modifiers, plus the toneless default.
///
/// Only emoji depicting a person or a body part accept one — see
/// EmojiShortcodes.SupportsSkinTone. The chosen tone is a persisted
/// preference rather than a per-pick choice: the official clients work the
/// same way, and picking a tone on every single reaction would be tedious.
/// </summary>
public enum EmojiSkinTone
{
    Default = 0,
    Light = 1,
    MediumLight = 2,
    Medium = 3,
    MediumDark = 4,
    Dark = 5,
}
