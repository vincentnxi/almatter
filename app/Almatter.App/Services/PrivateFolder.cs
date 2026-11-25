using System;
using System.IO;
using Almatter.App.Diagnostics;

namespace Almatter.App.Services;

/// <summary>
/// The app's own folder (settings.json, crash.log; the remembered session on
/// Windows) closed to other accounts on Linux and macOS: created as
/// rwx------ rather than left to the umask, which makes it readable by every
/// account whenever the home folder is too (Debian, Ubuntu before 21.04).
/// The core does the same for its cache folder (db.rs, make_private).
/// Windows needs nothing: AppData already admits only the user, SYSTEM and
/// the administrators.
/// </summary>
internal static class PrivateFolder
{
    public static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Almatter");

    /// <summary>Called first thing at startup, before anything writes there. Best effort.</summary>
    public static void Secure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(AppFolder);
            File.SetUnixFileMode(AppFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLogger.Write("private folder: could not restrict the app folder to its owner", ex);
        }
    }
}
