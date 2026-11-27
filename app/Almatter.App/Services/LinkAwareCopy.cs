using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Almatter.App.Models;

namespace Almatter.App.Services;

/// <summary>
/// Copies selected message text with its links still attached.
///
/// A plain copy only puts the visible words on the clipboard, so a link
/// written as "[le compte rendu](https://…)" came out as "le compte rendu"
/// and the address was lost. This puts the same text there as well as HTML
/// with each link as an anchor — what a browser does — so Word, Outlook or a
/// browser paste a clickable link, and Almatter's own composer turns it back
/// into Markdown (see LinkAwarePaste).
/// </summary>
internal static class LinkAwareCopy
{
    /// <summary>
    /// The part of a block's segments from position start to end (the
    /// laid-out text's positions, where a custom emoji's picture counts as one
    /// character), as plain text and as an HTML fragment — or null when no
    /// link falls inside it, and an ordinary copy does just as well.
    /// </summary>
    internal static (string Text, string Html)? Build(IReadOnlyList<MessageTextSegment> segments, int start, int end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var text = new StringBuilder();
        var html = new StringBuilder();
        var hasLink = false;
        var position = 0;

        foreach (var segment in segments)
        {
            var drawnAsPicture = segment is CustomEmojiSegment { Image: not null };
            var length = drawnAsPicture ? 1 : segment.Text.Length;
            var from = Math.Max(start, position);
            var to = Math.Min(end, position + length);
            position += length;
            if (to <= from)
            {
                continue;
            }

            // A picture is copied as the shortcode it was written as.
            var piece = drawnAsPicture ? segment.Text : segment.Text[(from - (position - length))..(to - (position - length))];
            text.Append(piece);

            var encoded = WebUtility.HtmlEncode(piece).Replace("\n", "<br>");
            if (segment is LinkSegment link && IsSafeToLink(link.LinkUrl))
            {
                hasLink = true;
                html.Append("<a href=\"").Append(WebUtility.HtmlEncode(link.LinkUrl)).Append("\">").Append(encoded).Append("</a>");
            }
            else
            {
                html.Append(encoded);
            }
        }

        return hasLink ? (text.ToString(), html.ToString()) : null;
    }

    /// <summary>Only ordinary web and mail addresses become anchors; anything else (javascript:, file:…) is copied as its text.</summary>
    private static bool IsSafeToLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);

    public static async Task CopyAsync(IClipboard clipboard, string text, string htmlFragment)
    {
        var item = new DataTransferItem();
        item.SetText(text);
        item.Set(LinkAwarePaste.HtmlClipboardFormat, Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? ToCfHtml(htmlFragment) : "<meta charset=\"utf-8\">" + htmlFragment));
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    /// <summary>
    /// Windows' clipboard HTML (CF_HTML): a header giving, in UTF-8 bytes,
    /// where the document and the copied fragment start and end, followed by
    /// the document. The offsets are written on ten digits so the header's
    /// own length is known before they are.
    /// </summary>
    internal static string ToCfHtml(string fragment)
    {
        const string headerTemplate =
            "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string before = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
        const string after = "<!--EndFragment-->\r\n</body></html>";

        var headerLength = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(before);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(after);
        return string.Format(headerTemplate, headerLength, endHtml, startFragment, endFragment) + before + fragment + after;
    }
}
