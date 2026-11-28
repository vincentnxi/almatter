using Almatter.App.Models;

namespace Almatter.App.Tests;

public class MarkdownFormatterTests
{
    private static FormatResult Apply(string text, int start, int end, FormatAction action) =>
        MarkdownFormatter.Apply(text, start, end, action);

    [Fact]
    public void BoldWrapsTheSelection()
    {
        Assert.Equal(new FormatResult("un **mot** ici", 5, 8), Apply("un mot ici", 3, 6, FormatAction.Bold));
    }

    [Fact]
    public void BoldTwiceGivesTheTextBack()
    {
        var once = Apply("un mot ici", 3, 6, FormatAction.Bold);
        var twice = Apply(once.Text, once.SelectionStart, once.SelectionEnd, FormatAction.Bold);
        Assert.Equal(new FormatResult("un mot ici", 3, 6), twice);
    }

    [Fact]
    public void TrailingSpaceOfADoubleClickedWordStaysOutside()
    {
        Assert.Equal("**mot** ici", Apply("mot ici", 0, 4, FormatAction.Bold).Text);
    }

    [Fact]
    public void NothingSelectedDropsAnEmptyPairWithTheCaretInside()
    {
        Assert.Equal(new FormatResult("a****", 3, 3), Apply("a", 1, 1, FormatAction.Bold));
    }

    [Fact]
    public void ItalicInsideBoldIsToldApart()
    {
        // "**mot**" is bold, not italic: italicising it adds a marker.
        Assert.Equal("***mot***", Apply("**mot**", 2, 5, FormatAction.Italic).Text);
    }

    [Fact]
    public void EachSelectedLineGetsItsOwnPair()
    {
        Assert.Equal("**un**\n**deux**", Apply("un\ndeux", 0, 7, FormatAction.Bold).Text);
    }

    [Fact]
    public void BulletListPrefixesEveryLine()
    {
        Assert.Equal("- un\n- deux", Apply("un\ndeux", 0, 7, FormatAction.BulletList).Text);
    }

    [Fact]
    public void NumberedListReplacesBullets()
    {
        Assert.Equal("1. un\n2. deux", Apply("- un\n- deux", 0, 11, FormatAction.NumberedList).Text);
    }

    [Fact]
    public void QuoteTwiceGivesTheTextBack()
    {
        var once = Apply("texte", 0, 5, FormatAction.Quote);
        Assert.Equal("> texte", once.Text);
        Assert.Equal("texte", Apply(once.Text, 0, once.Text.Length, FormatAction.Quote).Text);
    }

    [Fact]
    public void SelectionOutOfRangeIsClamped()
    {
        Assert.Equal("**abc**", Apply("abc", -5, 99, FormatAction.Bold).Text);
    }

    [Theory]
    [InlineData(FormatAction.Bold)]
    [InlineData(FormatAction.Italic)]
    [InlineData(FormatAction.Strikethrough)]
    [InlineData(FormatAction.Code)]
    [InlineData(FormatAction.Heading)]
    [InlineData(FormatAction.Quote)]
    [InlineData(FormatAction.BulletList)]
    [InlineData(FormatAction.NumberedList)]
    [InlineData(FormatAction.CodeBlock)]
    [InlineData(FormatAction.Link)]
    [InlineData(FormatAction.Rule)]
    [InlineData(FormatAction.Table)]
    public void EveryActionWorksOnAnEmptyComposer(FormatAction action)
    {
        var result = Apply("", 0, 0, action);
        Assert.InRange(result.SelectionStart, 0, result.Text.Length);
        Assert.InRange(result.SelectionEnd, result.SelectionStart, result.Text.Length);
    }

    // What the toolbar writes, the message view must read back.
    [Fact]
    public void FormattedTextParsesBack()
    {
        var bold = Apply("mot", 0, 3, FormatAction.Bold).Text;
        Assert.Contains(MessageTextParser.ParseInline(bold), s => s.Text == "mot" && s.Style.HasFlag(TextStyle.Bold));

        var heading = Apply("Titre", 0, 5, FormatAction.Heading).Text;
        Assert.IsType<HeadingBlock>(Assert.Single(MessageTextParser.ParseBlocks(heading)));

        var rule = Apply("", 0, 0, FormatAction.Rule).Text;
        Assert.Contains(MessageTextParser.ParseBlocks(rule), b => b is RuleBlock);

        var table = Apply("", 0, 0, FormatAction.Table).Text;
        Assert.Contains(MessageTextParser.ParseBlocks(table), b => b is TableBlock);
    }
}
