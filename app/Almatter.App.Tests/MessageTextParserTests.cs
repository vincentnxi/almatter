using System.Diagnostics;
using System.Linq;
using Almatter.App.Models;

namespace Almatter.App.Tests;

public class MessageTextParserTests
{
    private static T Single<T>(string text) where T : MessageTextSegment =>
        Assert.IsType<T>(Assert.Single(MessageTextParser.ParseInline(text), s => s is T));

    [Fact]
    public void PlainSentenceIsOneSegment()
    {
        var segment = Assert.Single(MessageTextParser.ParseInline("bonjour tout le monde"));
        Assert.IsType<PlainTextSegment>(segment);
        Assert.Equal("bonjour tout le monde", segment.Text);
    }

    [Fact]
    public void EmptyTextGivesOneEmptySegment()
    {
        var segment = Assert.Single(MessageTextParser.ParseInline(""));
        Assert.Equal("", segment.Text);
    }

    [Fact]
    public void BareUrlBecomesLink()
    {
        var link = Single<LinkSegment>("voir https://example.com/page ici");
        Assert.Equal("https://example.com/page", link.Text);
        Assert.Equal("https://example.com/page", link.LinkUrl);
    }

    [Fact]
    public void MarkdownLinkShowsLabelAndKeepsAddress()
    {
        var link = Single<LinkSegment>("[le compte rendu](https://example.com/cr)");
        Assert.Equal("le compte rendu", link.Text);
        Assert.Equal("https://example.com/cr", link.LinkUrl);
    }

    [Fact]
    public void MarkdownLinkAddressMayHoldParentheses()
    {
        var link = Single<LinkSegment>("[Foo](https://en.wikipedia.org/wiki/Foo_(bar))");
        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", link.LinkUrl);
    }

    [Fact]
    public void EscapedBracketsInLabelDoNotEndTheLink()
    {
        var link = Single<LinkSegment>(@"[\[Comparatif\] Les offres](https://example.com/c)");
        Assert.Equal("https://example.com/c", link.LinkUrl);
        Assert.Contains("Comparatif", link.Text);
    }

    [Theory]
    [InlineData("[clic](javascript:alert(1))")]
    [InlineData("[clic](file:///C:/Windows/System32/calc.exe)")]
    [InlineData("[clic](data:text/html,<script>alert(1)</script>)")]
    public void OnlyWebAddressesBecomeLinks(string text)
    {
        Assert.DoesNotContain(MessageTextParser.ParseInline(text), s => s is LinkSegment);
    }

    [Fact]
    public void MentionIsRecognised()
    {
        var mention = Single<MentionSegment>("merci @alice !");
        Assert.Equal("@alice", mention.Text);
    }

    [Fact]
    public void EmailAddressIsNotAMention()
    {
        Assert.DoesNotContain(MessageTextParser.ParseInline("écris à alice@example.com"), s => s is MentionSegment);
    }

    [Fact]
    public void BoldAndItalicCarryTheirStyle()
    {
        var segments = MessageTextParser.ParseInline("un **gras** et un *italique*");
        Assert.Contains(segments, s => s.Text == "gras" && s.Style.HasFlag(TextStyle.Bold));
        Assert.Contains(segments, s => s.Text == "italique" && s.Style.HasFlag(TextStyle.Italic));
    }

    [Fact]
    public void NothingIsParsedInsideInlineCode()
    {
        var segments = MessageTextParser.ParseInline("`@alice https://example.com`");
        Assert.DoesNotContain(segments, s => s is LinkSegment or MentionSegment);
        Assert.All(segments.Where(s => s.Text.Length > 0), s => Assert.True(s.Style.HasFlag(TextStyle.Code)));
    }

    [Fact]
    public void UnknownShortcodeIsACustomEmoji()
    {
        var emoji = Single<CustomEmojiSegment>("bravo :almatter_party: !");
        Assert.Equal("almatter_party", emoji.EmojiName);
        Assert.Equal(":almatter_party:", emoji.Text);
    }

    [Fact]
    public void ColonsInsideAUrlAreNotAnEmoji()
    {
        Assert.DoesNotContain(MessageTextParser.ParseInline("https://example.com/a:b:c"), s => s is CustomEmojiSegment);
    }

    // A message anyone on the server can post must not freeze the window:
    // "[" followed by a run of backslashes once took seconds (see the
    // remark on LinkAtPattern).
    [Fact]
    public void PathologicalLabelParsesQuickly()
    {
        var text = "[" + new string('\\', 5000);
        var watch = Stopwatch.StartNew();
        MessageTextParser.ParseInline(text);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void BlocksAreReadLineByLine()
    {
        var blocks = MessageTextParser.ParseBlocks("# Titre\n> citation\n- élément\n---\nligne");
        Assert.Collection(blocks,
            b => Assert.Equal(1, Assert.IsType<HeadingBlock>(b).Level),
            b => Assert.IsType<QuoteBlock>(b),
            b => Assert.IsType<ListItemBlock>(b),
            b => Assert.IsType<RuleBlock>(b),
            b => Assert.IsType<ParagraphBlock>(b));
    }

    [Fact]
    public void HeadingKeepsAHashThatIsPartOfTheText()
    {
        var heading = Assert.IsType<HeadingBlock>(Assert.Single(MessageTextParser.ParseBlocks("# C#")));
        Assert.Equal("C#", string.Concat(heading.Segments.Select(s => s.Text)));
    }

    [Fact]
    public void CodeFenceKeepsItsContentAsTyped()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(MessageTextParser.ParseBlocks("```\n**pas gras** @alice\n```")));
        Assert.Equal("**pas gras** @alice", code.Code);
    }

    [Fact]
    public void BoldAtLineStartIsNotAListItem()
    {
        Assert.IsType<ParagraphBlock>(Assert.Single(MessageTextParser.ParseBlocks("**important**")));
    }

    [Fact]
    public void PipeTableBecomesRows()
    {
        var table = Assert.IsType<TableBlock>(Assert.Single(MessageTextParser.ParseBlocks("| a | b |\n|---|---|\n| 1 | 2 |")));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[1].Count);
    }

    [Fact]
    public void PlainTextDropsTheMarkdown()
    {
        Assert.Equal("urgent : voir le lien", MessageTextParser.ToPlainText("**urgent** : voir [le lien](https://example.com)"));
    }
}
