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
