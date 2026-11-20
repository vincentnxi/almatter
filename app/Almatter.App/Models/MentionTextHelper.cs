using System;

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

    /// <summary>
    /// Whether a message names this user: "@username" (any case), or one of
    /// the channel-wide mentions @channel, @all and @here — what makes the
    /// server count it as a mention. Code (fenced blocks and `inline`) is
    /// skipped, as Mattermost does. A trailing full stop ends the name
    /// ("thanks @alice.") although dots are allowed inside one ("@a.b").
    /// </summary>
    public static bool MentionsUser(string text, string username)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(username) || !text.Contains('@'))
        {
            return false;
        }

        try
        {
            var prose = CodeSpans.Replace(text, " ");
            foreach (System.Text.RegularExpressions.Match match in MentionToken.Matches(prose))
            {
                var name = match.Groups[1].Value.TrimEnd('.');
                if (name.Equals(username, StringComparison.OrdinalIgnoreCase)
                    || name.Equals("channel", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("all", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("here", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // Not worth more than a missing tint.
            return false;
        }
    }

    // Both patterns are linear (no nested repetition), and a timeout caps
    // them anyway: the text comes from other people.
    private static readonly System.Text.RegularExpressions.Regex CodeSpans = new(
        "```.*?(```|$)|`[^`\\n]*`",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static readonly System.Text.RegularExpressions.Regex MentionToken = new(
        "(?<![A-Za-z0-9_.@-])@([A-Za-z0-9_.-]+)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    /// <summary>Replaces the "@partial" token at [mentionStart, caretIndex) with "@username " — the same insertion the official client makes on accepting a suggestion.</summary>
    public static string ApplyMention(string text, int mentionStart, int caretIndex, string username, out int newCaretIndex)
    {
        var replacement = "@" + username + " ";
        newCaretIndex = mentionStart + replacement.Length;
        return text[..mentionStart] + replacement + text[caretIndex..];
    }
}
