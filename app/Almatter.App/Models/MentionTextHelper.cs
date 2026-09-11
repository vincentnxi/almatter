namespace Almatter.App.Models;

/// <summary>
/// Caret-aware @mention detection for the composer — scans backward from
/// the caret for an "@word" token currently being typed, the same boundary
/// rule <see cref="MessageTextParser"/> uses for rendering (an "@" must
/// start the text or follow whitespace, so an email's "@domain" half never
/// triggers it).
/// </summary>
public static class MentionTextHelper
{
    /// <summary>A query longer than this stopped looking like a mention attempt a while ago — no point still searching for it.</summary>
    private const int MaxQueryLength = 32;

    public static bool TryGetMentionQuery(string text, int caretIndex, out int mentionStart, out string query)
    {
        mentionStart = -1;
        query = "";

        if (caretIndex <= 0 || caretIndex > text.Length)
        {
            return false;
        }

        var i = caretIndex - 1;
        while (i >= 0 && !char.IsWhiteSpace(text[i]) && text[i] != '@')
        {
            i--;
        }

        if (i < 0 || text[i] != '@')
        {
            return false;
        }

        if (i > 0 && !char.IsWhiteSpace(text[i - 1]))
        {
            return false;
        }

        var candidate = text[(i + 1)..caretIndex];
        if (candidate.Length > MaxQueryLength)
        {
            return false;
        }

        mentionStart = i;
        query = candidate;
        return true;
    }

    /// <summary>Replaces the "@partial" token at [mentionStart, caretIndex) with "@username " — the same insertion the official client makes on accepting a suggestion.</summary>
    public static string ApplyMention(string text, int mentionStart, int caretIndex, string username, out int newCaretIndex)
    {
        var replacement = "@" + username + " ";
        newCaretIndex = mentionStart + replacement.Length;
        return text[..mentionStart] + replacement + text[caretIndex..];
    }
}
