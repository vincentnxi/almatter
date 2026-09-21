using System;
using System.Runtime.InteropServices;

namespace Almatter.App.Services;

/// <summary>
/// How long since the user last touched the keyboard or mouse anywhere on
/// the computer — not just in this window, since reading a conversation
/// while typing in another app still counts as being there. This is what
/// Mattermost's desktop client looks at too before telling the server the
/// user is active. Plain DllImport with a marshalled struct; no unsafe code.
/// </summary>
internal static class SystemIdle
{
    /// <summary>
    /// Time since the last input, or null where it can't be read (outside
    /// Windows for now, see docs/PLATFORM_PORTABILITY.md) — callers treat
    /// that as "active", which is how the app behaved before.
    /// </summary>
    public static TimeSpan? IdleTime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        // Both are milliseconds since boot and wrap around after ~49 days;
        // unsigned subtraction gives the right gap across a wrap.
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
