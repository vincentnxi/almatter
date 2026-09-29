namespace Almatter.App.Models;

/// <summary>
/// Cutting text short without cutting an emoji in half. An emoji, and the
/// rarer ideographs, take two UTF-16 characters; a cut between them leaves the
/// first behind on its own, which is drawn as a replacement box — right in
/// front of the "…" that says the text was shortened. Emoji come up constantly
/// in chat, and the notification excerpt, a reply's quote and the tray balloon
/// all cut messages at a fixed length.
/// </summary>
public static class TextTruncation
{
    /// <summary>
    /// The first <paramref name="maxLength"/> characters of <paramref name="text"/> — one fewer when the
    /// cut would otherwise fall inside a surrogate pair. The text itself when it is short enough.
    /// </summary>
    public static string Head(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var end = maxLength;
        if (end > 0 && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
        {
            end--;
        }
        return text[..end];
    }
}
