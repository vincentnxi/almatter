using System.Linq;
using System.Text;
using Almatter.App.Models;
using Almatter.App.Services;

namespace Almatter.App.Tests;

public class ClipboardLinkTests
{
    [Fact]
    public void CopyWithoutALinkIsLeftToTheOrdinaryCopy()
    {
        Assert.Null(LinkAwareCopy.Build(MessageTextParser.ParseInline("rien de spécial"), 0, 15));
    }

    [Fact]
    public void CopyPutsTheLinkInTheHtml()
    {
        var segments = MessageTextParser.ParseInline("voir [le CR](https://example.com/cr) demain");
        var copy = LinkAwareCopy.Build(segments, 0, 100);
        Assert.NotNull(copy);
        Assert.Equal("voir le CR demain", copy.Value.Text);
        Assert.Contains("<a href=\"https://example.com/cr\">le CR</a>", copy.Value.Html);
    }

    [Fact]
    public void CopiedTextIsHtmlEncoded()
    {
        var segments = MessageTextParser.ParseInline("<b>x</b> https://example.com/?a=1&b=2");
        var copy = LinkAwareCopy.Build(segments, 0, 200);
        Assert.NotNull(copy);
        Assert.DoesNotContain("<b>", copy.Value.Html);
        Assert.Contains("&amp;b=2", copy.Value.Html);
    }

    [Fact]
    public void SelectionMayBeGivenBackwards()
    {
        var segments = MessageTextParser.ParseInline("https://example.com");
        Assert.Equal(LinkAwareCopy.Build(segments, 0, 19), LinkAwareCopy.Build(segments, 19, 0));
    }

    [Fact]
    public void CfHtmlOffsetsPointAtTheFragment()
    {
        const string fragment = "<a href=\"https://example.com\">Café</a>";
        var bytes = Encoding.UTF8.GetBytes(LinkAwareCopy.ToCfHtml(fragment));
        var header = Encoding.UTF8.GetString(bytes);

        int Offset(string name)
        {
            var at = header.IndexOf(name + ":") + name.Length + 1;
            return int.Parse(header.Substring(at, 10));
        }

        var start = Offset("StartFragment");
        var end = Offset("EndFragment");
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), 6));
    }

    [Fact]
    public void PastedAnchorBecomesMarkdownLink()
    {
        var cfHtml = LinkAwareCopy.ToCfHtml("voir <a href=\"https://example.com/cr\">le CR</a> demain");
        Assert.Equal("voir [le CR](https://example.com/cr) demain", LinkAwarePaste.ExtractTextPreservingLinks(cfHtml));
    }

    [Fact]
    public void PastedLabelBracketsAreEscaped()
    {
        var text = LinkAwarePaste.ExtractTextPreservingLinks("<a href=\"https://example.com\">[Comparatif] offres</a>");
        Assert.Equal(@"[\[Comparatif\] offres](https://example.com)", text);
    }

    [Fact]
    public void PasteWithoutAnchorIsLeftToTheOrdinaryPaste()
    {
        Assert.Null(LinkAwarePaste.ExtractTextPreservingLinks("<p>juste du texte</p>"));
    }

    // Pasting stays text: a javascript: anchor from a web page may come
    // through as Markdown, but the message never turns it into a link.
    [Fact]
    public void PastedScriptLinkNeverBecomesClickable()
    {
        var text = LinkAwarePaste.ExtractTextPreservingLinks("<a href=\"javascript:alert(1)\">clic</a>");
        Assert.NotNull(text);
        Assert.DoesNotContain(MessageTextParser.ParseInline(text), s => s is LinkSegment);
    }

    [Fact]
    public void CopyThenPasteGivesTheMarkdownBack()
    {
        const string original = "voir [le CR](https://example.com/cr) demain";
        var copy = LinkAwareCopy.Build(MessageTextParser.ParseInline(original), 0, 100);
        Assert.NotNull(copy);
        Assert.Equal(original, LinkAwarePaste.ExtractTextPreservingLinks(LinkAwareCopy.ToCfHtml(copy.Value.Html)));
    }

    [Fact]
    public void Utf16HtmlFromFirefoxIsDecoded()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("<b>é</b>")).ToArray();
        Assert.Equal("<b>é</b>", LinkAwarePaste.DecodeHtml(bytes));
    }
}
