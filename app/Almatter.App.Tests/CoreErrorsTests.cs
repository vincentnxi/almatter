using System;
using System.IO;
using Almatter.App.Interop;

namespace Almatter.App.Tests;

/// <summary>
/// CoreErrors recognises the Rust core's error sentences word for word. These
/// tests read the core's source, so a sentence changed on one side only
/// fails here instead of silently showing in English.
/// </summary>
public class CoreErrorsTests
{
    private const string Interrupted = "The connection to the server dropped while the response was arriving.";
    private const string SessionExpired = "Your session has expired. Please sign in again.";

    private static string CoreClientSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "core", "almatter-core", "src", "api", "client.rs");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new FileNotFoundException("core/almatter-core/src/api/client.rs not found above the test folder");
    }

    [Theory]
    [InlineData(Interrupted)]
    [InlineData(SessionExpired)]
    [InlineData("The server sent a response this app cannot read ({path}).")]
    public void SentenceStillExistsInTheCore(string sentence)
    {
        Assert.Contains($"#[error(\"{sentence}\")]", CoreClientSource());
    }

    [Fact]
    public void KnownSentencesAreTranslated()
    {
        Assert.NotEqual(Interrupted, CoreErrors.Translate(Interrupted));
        Assert.NotEqual(SessionExpired, CoreErrors.Translate(SessionExpired));
        Assert.Contains("/api/v4/users/me", CoreErrors.Translate("The server sent a response this app cannot read (/api/v4/users/me)."));
    }

    [Fact]
    public void OtherSentencesPassThrough()
    {
        Assert.Equal("Something else.", CoreErrors.Translate("Something else."));
    }

    [Fact]
    public void OnlyTheExactSentenceMeansSessionExpired()
    {
        Assert.True(CoreErrors.IsSessionExpired(SessionExpired));
        Assert.False(CoreErrors.IsSessionExpired("Error: " + SessionExpired));
    }
}
