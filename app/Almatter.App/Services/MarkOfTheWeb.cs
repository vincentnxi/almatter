using System;
using System.IO;

namespace Almatter.App.Services;

/// <summary>
/// Marks a downloaded attachment as coming from the Internet, the way a
/// browser marks what it downloads.
///
/// Windows reads this mark — a small "Zone.Identifier" stream attached to the
/// file — before opening it: SmartScreen asks before running a program, Office
/// opens the document in Protected View with its macros blocked, and the files
/// taken out of a marked archive are marked in turn. An attachment fetched by
/// this app carried no mark, so any of them opened as if the user had written
/// it themselves.
///
/// Windows only, like the mark itself; elsewhere this does nothing. See
/// docs/PLATFORM_PORTABILITY.md.
/// </summary>
internal static class MarkOfTheWeb
{
    /// <summary>Zone 3 is "Internet". "about:internet" is the address Windows itself writes when the real one doesn't matter.</summary>
    private const string InternetZone = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=about:internet\r\n";

    public static void Apply(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var stream = path + ":Zone.Identifier";
        try
        {
            // Already marked: the cached copy is opened again and again, and
            // rewriting the mark each time would only be a chance to fail.
            if (!File.Exists(stream))
            {
                File.WriteAllText(stream, InternetZone);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A drive with no room for the mark (FAT32, a network share): the
            // file still opens, just without Windows' extra caution.
            Diagnostics.CrashLogger.Write("attachments: could not mark a download as coming from the Internet", ex);
        }
    }
}
