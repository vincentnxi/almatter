using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Almatter.App.Services;

/// <summary>
/// Recovers the links in rich text being pasted into a composer.
///
/// A plain paste only gets the clipboard's text, but copying a hyperlinked
/// phrase ("click here" from a web page or an email) commonly puts just the
/// visible words there — the URL only exists in the clipboard's HTML, as the
/// anchor's href. This reads that HTML and turns each link into Markdown
/// "[text](url)", which Mattermost renders as the same clickable words.
///
/// Uses Avalonia's clipboard, which works on every platform; it used to go
/// through Windows Forms, which the app no longer loads.
/// </summary>
internal static class LinkAwarePaste
{
    /// <summary>The clipboard's text with its links kept as Markdown — or null when the clipboard has no HTML, or HTML with no link in it, and an ordinary paste is all that's needed.</summary>
    public static async Task<string?> TryReadTextWithLinksAsync(IClipboard clipboard)
    {
        if (await clipboard.TryGetValueAsync(HtmlClipboardFormat) is not { Length: > 0 } bytes)
        {
            return null;
        }
        return ExtractTextPreservingLinks(DecodeHtml(bytes));
    }

    /// <summary>
    /// Rich text copied from a browser or a mail client, under the name each
    /// platform gives it. Read as raw bytes rather than as a string: the
    /// Windows format is UTF-8, and letting a generic text conversion guess
    /// at the encoding is how accented labels come out mangled.
    /// </summary>
    private static readonly DataFormat<byte[]> HtmlClipboardFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "HTML Format"
        : OperatingSystem.IsMacOS() ? "public.html"
        : "text/html");

    internal static string DecodeHtml(byte[] bytes)
    {
        // Firefox on Linux hands text/html over as UTF-16 with a byte-order
        // mark; everything else observed is UTF-8.
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static readonly Regex AnchorTagRegex = new(
        @"<a\b[^>]*\bhref\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Windows' clipboard HTML format (CF_HTML) is the whole descriptor —
    /// a "Version:0.9 / StartHTML:.../ EndHTML:..." header followed by the
    /// actual markup — not just the fragment, and the clipboard hands the
    /// whole thing back verbatim. The real content
    /// is standardized as sitting between "&lt;!--StartFragment--&gt;" and
    /// "&lt;!--EndFragment--&gt;" comments, which sidesteps needing to
    /// parse the header's byte offsets (awkward together with Unicode).
    /// </summary>
    private static string ExtractHtmlFragment(string cfHtml)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = cfHtml.IndexOf(startMarker, StringComparison.Ordinal);
        var end = cfHtml.IndexOf(endMarker, StringComparison.Ordinal);
        return start >= 0 && end > start
            ? cfHtml[(start + startMarker.Length)..end]
            : cfHtml;
    }

    /// <summary>Null means "no link found" — the caller falls back to the normal plain-text paste in that case.</summary>
    internal static string? ExtractTextPreservingLinks(string cfHtml)
    {
        var html = ExtractHtmlFragment(cfHtml);
        var matches = AnchorTagRegex.Matches(html);
        if (matches.Count == 0)
        {
            return null;
        }

        var result = new StringBuilder();
        var lastEnd = 0;
        foreach (Match match in matches)
        {
            result.Append(HtmlFragmentToText(html[lastEnd..match.Index]));
            var href = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            var innerText = System.Net.WebUtility.HtmlDecode(HtmlFragmentToText(match.Groups[2].Value)).Trim();
            result.Append(innerText.Length == 0 || string.Equals(innerText, href, StringComparison.OrdinalIgnoreCase)
                ? href
                // The label can itself contain "[" or "]" (e.g. a page title
                // like "[Comparatif] ..."), which would otherwise break out
                // of the Markdown link early — escaped the same way
                // Markdown escapes any other literal special character.
                : $"[{innerText.Replace("[", "\\[").Replace("]", "\\]")}]({href})");
            lastEnd = match.Index + match.Length;
        }
        result.Append(HtmlFragmentToText(html[lastEnd..]));

        var text = System.Net.WebUtility.HtmlDecode(result.ToString());
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string HtmlFragmentToText(string html)
    {
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|li|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(html, "<[^>]+>", "");
    }
}
