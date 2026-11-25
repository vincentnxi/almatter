using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Almatter.App.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Almatter.App.Services;

/// <summary>
/// Keeps Almatter to one running copy per user and session. Two copies would
/// share the same cache database, settings and session file, each polling the
/// server and writing over the other; launching the app again (Start menu,
/// desktop icon, a development build next to the installed one) now brings
/// the window already open to the front instead.
///
/// The first copy holds a named mutex for as long as it runs and listens on
/// a named pipe. A later copy finds the mutex taken, sends the one word
/// "activate" down the pipe and exits before opening any window. Both ends
/// of the pipe are limited to the current user (PipeOptions.CurrentUserOnly):
/// another account can neither send to it nor pose as it. The listener acts
/// on that exact word and nothing else, so the most another program running
/// as the same user could do with it is bring Almatter's window forward.
/// </summary>
internal static class SingleInstance
{
    private const string ActivateMessage = "activate";

    // Per user and per session, hashed so that on Linux, where the pipe is
    // a socket file under /tmp that every account can list, the name does
    // not spell out the user name.
    private static readonly string Key = ComputeKey();
    private static string MutexName => @"Local\Almatter-" + Key;
    private static string PipeName => "Almatter-" + Key;

    // Held, never released, until the process ends: Windows then frees it
    // (as "abandoned" if the app crashed, which TryClaim accepts).
    private static Mutex? _mutex;

    /// <summary>
    /// True when this is the only running copy, which then owns the right to
    /// run until it exits. False when another copy already runs.
    /// </summary>
    public static bool TryClaim()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: false, MutexName);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The copy that held it ended without releasing it (a crash,
                // a kill from Task Manager): the mutex is ours now.
                owned = true;
            }

            if (!owned)
            {
                mutex.Dispose();
                return false;
            }
            _mutex = mutex;
            return true;
        }
        catch (Exception ex)
        {
            // Unable to tell: starting is better than refusing to start.
            CrashLogger.Write("single instance: claim", ex);
            return true;
        }
    }

    /// <summary>
    /// Called by a second copy before it exits: asks the running one to bring
    /// its window to the front. Best effort, since this copy closes either way.
    /// </summary>
    public static void SignalRunningInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // The first copy may itself be starting up and not listening yet.
            pipe.Connect(TimeSpan.FromSeconds(5));

            // Windows lets a process take the foreground only right after the
            // user acted on it. This copy was just launched by the user, so it
            // may pass that right on to the running one, and only to it;
            // otherwise its window would merely flash in the taskbar.
            if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId))
            {
                AllowSetForegroundWindow(serverProcessId);
            }

            pipe.Write(Encoding.ASCII.GetBytes(ActivateMessage));
            pipe.Flush();
            CrashLogger.Write("single instance", "Almatter is already running: asked it to come to the front, this copy exits");
        }
        catch (Exception ex)
        {
            CrashLogger.Write("single instance: signal", ex);
        }
    }

    /// <summary>
    /// Started once by the copy that claimed the mutex: waits for later
    /// copies and calls <paramref name="activate"/> (on a background thread)
    /// each time one asks.
    /// </summary>
    public static void StartListening(Action activate)
    {
        if (_mutex is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var failures = 0;
            while (true)
            {
                try
                {
                    // One connection at a time, from the current user only.
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync();
                    failures = 0;

                    // Read one byte more than the message, so that anything
                    // longer is told apart from it; a silent client is cut
                    // off rather than left holding the pipe.
                    var buffer = new byte[ActivateMessage.Length + 1];
                    var read = 0;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    while (read < buffer.Length)
                    {
                        var count = await server.ReadAsync(buffer.AsMemory(read), timeout.Token);
                        if (count == 0)
                        {
                            break;
                        }
                        read += count;
                    }

                    if (read == ActivateMessage.Length
                        && Encoding.ASCII.GetString(buffer, 0, read) == ActivateMessage)
                    {
                        activate();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("single instance: listen", ex);
                    // A pipe that cannot be created at all would otherwise
                    // fill the log in a tight loop.
                    if (++failures >= 3)
                    {
                        CrashLogger.Write("single instance", "giving up listening; a second launch will not bring this window forward");
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        });
    }

    /// <summary>
    /// Restores a minimised window to the state it had before (maximised
    /// stays maximised). Windows only: false elsewhere, where the caller
    /// falls back on Avalonia's WindowState.
    /// </summary>
    public static bool RestoreWindow(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return false;
        }
        // ShowWindow's result says whether the window was visible before,
        // not whether it worked: nothing to check.
        ShowWindow(hwnd, SwRestore);
        return true;
    }

    private static string ComputeKey()
    {
        string identity;
        try
        {
            identity = OperatingSystem.IsWindows()
                ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName
                : Environment.UserName;
        }
        catch
        {
            identity = Environment.UserName;
        }

        int session;
        try
        {
            using var self = Process.GetCurrentProcess();
            session = self.SessionId;
        }
        catch
        {
            session = 0;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}|{session}"));
        return Convert.ToHexString(hash, 0, 8);
    }

    private const int SwRestore = 9;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
