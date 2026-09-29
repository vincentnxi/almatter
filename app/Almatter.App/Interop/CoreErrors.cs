using System.Text.RegularExpressions;
using Almatter.App.Localization;

namespace Almatter.App.Interop;

/// <summary>
/// The core reports its failures as plain English sentences, and most go on
/// screen as they are: they are technical and rare. The two a user actually
/// runs into — a response cut off mid-way, one the app cannot read — are
/// recognised here and replaced by the interface's own wording, in the
/// interface language.
///
/// The sentences are matched exactly as almatter-core's ApiError writes
/// them (core/almatter-core/src/api/client.rs). If one changes there and
/// not here, the only harm is the English sentence showing as it is.
/// </summary>
internal static partial class CoreErrors
{
    private const string Interrupted = "The connection to the server dropped while the response was arriving.";

    /// <summary>The server refused the session token (ApiError::SessionExpired).</summary>
    private const string SessionExpired = "Your session has expired. Please sign in again.";

    [GeneratedRegex(@"^The server sent a response this app cannot read \((?<path>.*)\)\.$")]
    private static partial Regex UnexpectedResponse();

    /// <summary>
    /// Whether the core is saying the session token is no good. Unlike the
    /// two other wordings, this one is more than a sentence to translate: the
    /// app answers it by going back to the sign-in screen.
    /// </summary>
    public static bool IsSessionExpired(string message) => message == SessionExpired;

    public static string Translate(string message)
    {
        if (message == Interrupted)
        {
            return Loc.S.ErrorConnectionInterrupted;
        }
        if (message == SessionExpired)
        {
            return Loc.S.ErrorSessionExpired;
        }
        var unexpected = UnexpectedResponse().Match(message);
        if (unexpected.Success)
        {
            return Loc.S.ErrorUnreadableResponse(unexpected.Groups["path"].Value);
        }
        return message;
    }
}
