using Almatter.App.Models;

namespace Almatter.App.Tests;

public class MentionTextHelperTests
{
    [Theory]
    [InlineData("@ali", 4, 0, "ali")]
    [InlineData("salut @bo", 9, 6, "bo")]
    [InlineData("salut @", 7, 6, "")]
    public void FindsTheMentionBeingTyped(string text, int caret, int expectedStart, string expectedQuery)
    {
        Assert.True(MentionTextHelper.TryGetMentionQuery(text, caret, out var start, out var query));
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedQuery, query);
    }

    [Theory]
    [InlineData("alice@example", 13)] // an email address
    [InlineData("@alice fini", 11)]    // the caret has left the name
    [InlineData("", 0)]
    [InlineData("@a", 5)]              // caret past the end
    public void IgnoresWhatIsNotAMentionInProgress(string text, int caret)
    {
        Assert.False(MentionTextHelper.TryGetMentionQuery(text, caret, out _, out _));
    }

    [Fact]
    public void ApplyMentionReplacesThePartialName()
    {
        var result = MentionTextHelper.ApplyMention("salut @al ça va", 6, 9, "alice", out var caret);
        Assert.Equal("salut @alice  ça va", result);
        Assert.Equal(13, caret);
    }

    [Theory]
    [InlineData("merci @Alice", true)]
    [InlineData("merci @alice.", true)]
    [InlineData("@channel réunion", true)]
    [InlineData("@here", true)]
    [InlineData("@all", true)]
    [InlineData("merci @alicette", false)]
    [InlineData("alice@example.com", false)]
    [InlineData("`@alice`", false)]
    [InlineData("```\n@alice\n```", false)]
    [InlineData("", false)]
    public void MentionsUserFollowsTheServerRules(string text, bool expected)
    {
        Assert.Equal(expected, MentionTextHelper.MentionsUser(text, "alice"));
    }
}
