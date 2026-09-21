using System;
using System.IO;

namespace Almatter.App.Diagnostics;

/// <summary>
/// Writes any exception that would otherwise crash the app silently to a log
/// file, so a report of "the window disappeared and nothing happened" can
/// actually be diagnosed.
/// </summary>
internal static class CrashLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Almatter",
        "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? ex) => Write(source, ex?.ToString() ?? "(no exception)");

    /// <summary>
    /// How large the log is allowed to get before the old half is thrown
    /// away. This file is appended to on every swallowed failure and on a
    /// few routine events besides, with nothing that ever shortened it — it
    /// had reached 800 KB of a session's history, which is both wasted disk
    /// and unreadable when something actually needs diagnosing. Keeping one
    /// previous file means a crash is still readable after the rotation that
    /// immediately follows it.
    /// </summary>
    private const long MaxLogBytes = 256 * 1024;

    private static readonly string PreviousLogPath = LogPath + ".1";

    private static void RotateIfLarge()
    {
        try
        {
            if (new FileInfo(LogPath) is { Exists: true } info && info.Length > MaxLogBytes)
            {
                File.Move(LogPath, PreviousLogPath, overwrite: true);
            }
        }
        catch
        {
            // Another process holding the file, a permissions problem — the
            // log just keeps growing, which is still better than failing here.
        }
    }

    /// <summary>Plain-text diagnostic note, same file/format as an exception — for tracking down a silently-swallowed failure without one to attach.</summary>
    public static void Write(string source, string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            RotateIfLarge();
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never itself crash the app.
        }
    }
}
