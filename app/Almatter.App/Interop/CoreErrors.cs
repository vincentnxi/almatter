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

    [GeneratedRegex(@"^The server sent a response this app cannot read \((?<path>.*)\)\.$")]
    private static partial Regex UnexpectedResponse();

    public static string Translate(string message)
    {
        if (message == Interrupted)
        {
            return Loc.S.ErrorConnectionInterrupted;
        }
        var unexpected = UnexpectedResponse().Match(message);
        if (unexpected.Success)
        {
            return Loc.S.ErrorUnreadableResponse(unexpected.Groups["path"].Value);
        }
        return message;
    }
}
