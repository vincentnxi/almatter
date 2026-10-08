using System;
using System.Text;

namespace Almatter.App.Models;

/// <summary>
/// Finds the standard emoji inside a piece of message text. They travel as
/// ordinary characters, so the view has to pick them out itself to draw
/// them larger than the letters around them.
/// </summary>
public static class EmojiGlyphs
{
    private const char VariationSelector = '️';
    private const char Joiner = '‍';
    private const char Keycap = '⃣';

    /// <summary>
    /// How many chars of emoji start at <paramref name="index"/> — zero when
    /// what is there is ordinary text. Emoji that touch come back as one
    /// stretch, and a sequence (a flag, a skin tone, a family joined by
    /// U+200D) is never cut in the middle: split across two runs, it would
    /// stop drawing as a single picture.
    /// </summary>
    public static int LengthAt(string text, int index)
    {
        var i = index;
        var afterJoiner = false;
        var beforeJoiner = index;
        while (i < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                break;
            }
            var next = i + consumed;
            var selected = next < text.Length && text[next] == VariationSelector;

            if (IsPictograph(rune.Value) || ((selected || afterJoiner) && TakesSelector(rune.Value)))
            {
                i = next;
            }
            else if (IsKeycapBase(rune.Value) && KeycapEnd(text, next) is var end and > 0)
            {
                i = end;
            }
            else
            {
                break;
            }

            afterJoiner = false;
            while (i < text.Length)
            {
                if (text[i] is VariationSelector or Keycap)
                {
                    i++;
                }
                else if (text[i] == Joiner)
                {
                    afterJoiner = true;
                    beforeJoiner = i;
                    i++;
                }
                else if (IsTag(text, i))
                {
                    i += 2;
                }
                else
                {
                    break;
                }
            }
        }

        // A joiner with nothing to join stays out of the stretch.
        return (afterJoiner ? beforeJoiner : i) - index;
    }

    /// <summary>Where the next emoji starts at or after <paramref name="index"/>, or the length of the text when there is none.</summary>
    public static int NextStart(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            // Nothing below '#' starts an emoji, and most text is letters.
            if (text[i] >= '#' && !char.IsAsciiLetter(text[i]) && LengthAt(text, i) > 0)
            {
                return i;
            }
        }
        return text.Length;
    }

    /// <summary>Drawn as a picture with no selector needed — the Unicode Emoji_Presentation set.</summary>
    private static bool IsPictograph(int value) => value switch
    {
        >= 0x1F000 and <= 0x1F0FF => true,
        >= 0x1F170 and <= 0x1F251 => true,
        >= 0x1F300 and <= 0x1F6FF => true,
        >= 0x1F7E0 and <= 0x1F7EB => true,
        >= 0x1F900 and <= 0x1FAFF => true,
        0x231A or 0x231B or (>= 0x23E9 and <= 0x23EC) or 0x23F0 or 0x23F3 => true,
        0x25FD or 0x25FE or 0x2614 or 0x2615 or (>= 0x2648 and <= 0x2653) => true,
        0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB or 0x26BD or 0x26BE => true,
        0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or 0x26F2 or 0x26F3 => true,
        0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 => true,
        0x274C or 0x274E or (>= 0x2753 and <= 0x2755) or 0x2757 => true,
        (>= 0x2795 and <= 0x2797) or 0x27B0 or 0x27BF => true,
        0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
        _ => false,
    };

    /// <summary>
    /// Plain signs that become emoji only when U+FE0F follows: ❤ ☀ ✔ ™ and
    /// the arrows. Left alone without it, so a "→" or a "✓" typed as text
    /// keeps the size of the text.
    /// </summary>
    private static bool TakesSelector(int value) => value switch
    {
        0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 => true,
        >= 0x2194 and <= 0x21AA => true,
        >= 0x2300 and <= 0x23FF => true,
        0x24C2 => true,
        >= 0x25AA and <= 0x25FE => true,
        >= 0x2600 and <= 0x27BF => true,
        0x2934 or 0x2935 => true,
        >= 0x2B00 and <= 0x2BFF => true,
        0x3030 or 0x303D or 0x3297 or 0x3299 => true,
        _ => false,
    };

    private static bool IsKeycapBase(int value) => value is (>= '0' and <= '9') or '#' or '*';

    /// <summary>The index just past a keycap's closing U+20E3, or zero when the digit is only a digit.</summary>
    private static int KeycapEnd(string text, int index)
    {
        if (index < text.Length && text[index] == VariationSelector)
        {
            index++;
        }
        return index < text.Length && text[index] == Keycap ? index + 1 : 0;
    }

    /// <summary>The invisible tag letters (U+E0020–U+E007F) that spell out a regional flag such as Scotland's.</summary>
    private static bool IsTag(string text, int index) =>
        text[index] == '\uDB40' && index + 1 < text.Length && text[index + 1] is >= '\uDC20' and <= '\uDC7F';
}
