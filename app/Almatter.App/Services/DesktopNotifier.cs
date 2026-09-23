using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Almatter.App.Services;

/// <summary>
/// Desktop notifications for mentions, with a way to hear that one was clicked.
///
/// There is no cross-platform API for this — Avalonia has none, and each OS
/// does it differently — so the window talks to this interface and each
/// platform gets its own implementation. Windows is the only one written so
/// far; elsewhere <see cref="Create"/> returns a notifier that does nothing,
/// and the app runs the same, just without desktop notifications. See
/// docs/PLATFORM_PORTABILITY.md.
/// </summary>
internal interface IDesktopNotifier : IDisposable
{
    /// <summary>The most recent notification was clicked. Raised on the UI thread.</summary>
    event EventHandler? Clicked;

    /// <summary>
    /// A <paramref name="personal"/> notification — aimed at this user, not
    /// just a message in one of their channels — has to stand out from the
    /// rest. No notification system lets an app colour its text, so it gets
    /// a 🔔 in front of its title (emoji are drawn in colour). Whether it
    /// makes a sound is the caller's call: by default only personal ones do,
    /// unless the user asked for a sound on every message.
    /// </summary>
    void Show(string title, string text, bool personal, bool sound);
}

internal static class DesktopNotifier
{
    public static IDesktopNotifier Create(Window window)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new WindowsTrayNotifier(window);
            }
            catch (Exception ex)
            {
                Diagnostics.CrashLogger.Write("notifications: tray icon unavailable", ex);
            }
        }
        return new NoDesktopNotifier();
    }

    private sealed class NoDesktopNotifier : IDesktopNotifier
    {
        public event EventHandler? Clicked { add { } remove { } }
        public void Show(string title, string text, bool personal, bool sound) { }
        public void Dispose() { }
    }
}

/// <summary>
/// A notification-area icon and its balloon, through the Windows shell's own
/// Shell_NotifyIcon — the same call System.Windows.Forms.NotifyIcon makes
/// internally. Using it directly is what lets the app drop Windows Forms
/// altogether: loading WinForms for this one icon cost about 13 MB of memory,
/// and it doesn't exist on Linux or macOS anyway. On Windows 10 and 11 the
/// balloon is shown as a regular toast notification.
///
/// Clicks come back as a window message, received through Avalonia's hook
/// on the main window's message procedure — no hidden window of our own.
/// Plain DllImport with marshalled structs; no unsafe code.
/// </summary>
internal sealed class WindowsTrayNotifier : IDesktopNotifier
{
    private const int IconId = 1;

    /// <summary>The message the shell sends back for this icon. Anything in the WM_APP range is ours to choose.</summary>
    private const uint CallbackMessage = 0x8000 + 0x4D;   // WM_APP + 'M'

    private const int NIM_ADD = 0x0;
    private const int NIM_MODIFY = 0x1;
    private const int NIM_DELETE = 0x2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;
    private const int NIIF_INFO = 0x01;
    private const int NIIF_NOSOUND = 0x10;
    private const int NIN_BALLOONUSERCLICK = 0x0400 + 5;   // WM_USER + 5

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;

    /// <summary>False when <see cref="_icon"/> is the shared system icon, which must never be destroyed.</summary>
    private readonly bool _ownsIcon;

    /// <summary>
    /// Sent to every top-level window when Explorer restarts. The notification
    /// area is rebuilt from scratch at that point and forgets every icon, so
    /// ours has to be added again or notifications silently stop.
    /// </summary>
    private readonly uint _taskbarCreatedMessage;

    /// <summary>Held in a field: the native side keeps calling this delegate, and a delegate only reachable from native code would be garbage-collected out from under it.</summary>
    private readonly Win32Properties.CustomWndProcHookCallback _hook;

    private bool _disposed;

    public event EventHandler? Clicked;

    public WindowsTrayNotifier(Window window)
    {
        _window = window;
        _hwnd = window.TryGetPlatformHandle()?.Handle
            ?? throw new InvalidOperationException("The window has no native handle yet.");

        var exePath = Environment.ProcessPath ?? "";
        _icon = ExtractIconW(IntPtr.Zero, exePath, 0);
        _ownsIcon = _icon != IntPtr.Zero;

        // Windows accepts a notification-area icon with no image, then
        // silently drops every notification sent through it — nothing
        // fails, nothing shows. Almatter.exe carries its own icon, but an
        // executable built without one (seen with a test harness) would lose
        // all mention notifications without a trace, so fall back to the
        // stock application icon.
        if (_icon == IntPtr.Zero)
        {
            _icon = LoadIconW(IntPtr.Zero, IdiApplication);
        }

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        _hook = OnWindowMessage;
        Win32Properties.AddWndProcHookCallback(window, _hook);

        if (!AddIcon())
        {
            Dispose();
            throw new InvalidOperationException("Shell_NotifyIcon refused to add the icon.");
        }
    }

    public void Show(string title, string text, bool personal, bool sound)
    {
        if (_disposed)
        {
            return;
        }

        var data = NewData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Truncate(personal ? "🔔 " + title : title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = sound ? NIIF_INFO : NIIF_INFO | NIIF_NOSOUND;
        data.uTimeoutOrVersion = 6000;
        if (!Shell_NotifyIconW(NIM_MODIFY, ref data))
        {
            Diagnostics.CrashLogger.Write("notifications", $"Shell_NotifyIconW refused the notification (error {Marshal.GetLastPInvokeError()})");
        }
    }

    private bool AddIcon()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "Almatter";
        return Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // The low word carries the event whichever notify-icon version is in force.
            if ((lParam.ToInt64() & 0xFFFF) == NIN_BALLOONUSERCLICK)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
            handled = true;
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            AddIcon();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var data = NewData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        Win32Properties.RemoveWndProcHookCallback(_window, _hook);
        if (_ownsIcon)
        {
            DestroyIcon(_icon);
        }
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>The struct's text fields have fixed capacities, terminator included.</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr instance, string exeFileName, int iconIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private static readonly IntPtr IdiApplication = new(32512);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);
}
