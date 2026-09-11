using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Almatter.App.Services;

internal enum TaskbarBadgeKind
{
    /// <summary>No badge — everything's been seen.</summary>
    None,
    /// <summary>A plain red dot — unread messages exist, but none are a mention/DM (which get the numbered badge instead).</summary>
    Dot,
    /// <summary>A red circle with a count — unread mentions + direct messages.</summary>
    Number,
}

/// <summary>
/// The small overlay icon Windows shows in the corner of a taskbar button
/// (the same mechanism Teams/Outlook use for their unread badges) — a COM
/// interop wrapper around the Shell's <c>ITaskbarList3::SetOverlayIcon</c>,
/// since no managed API exposes this. No <c>unsafe</c> needed: COM interop
/// marshaling is handled by the runtime, it's all ordinary P/Invoke-style
/// declarations.
/// </summary>
internal static class TaskbarBadge
{
    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarInstance
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3 — every slot must stay in exactly this order, COM
        // interop maps methods to the vtable positionally.
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int tbpFlags);
        [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        [PreserveSig] int UnregisterTab(IntPtr hwndTab);
        [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint tbatFlags);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButtons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButtons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
        [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
        [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static ITaskbarList3? _taskbarList;
    private static bool _initFailed;

    private static ITaskbarList3? GetTaskbarList()
    {
        if (_taskbarList is not null || _initFailed)
        {
            return _taskbarList;
        }
        try
        {
            var instance = (ITaskbarList3)new TaskbarInstance();
            instance.HrInit();
            _taskbarList = instance;
        }
        catch
        {
            _initFailed = true;
        }
        return _taskbarList;
    }

    /// <summary>Best-effort — a failure here (an unusual shell, a locked-down environment, ...) should never take down the app, it just means no badge.</summary>
    public static void Update(IntPtr hwnd, TaskbarBadgeKind kind, int count = 0)
    {
        try
        {
            if (GetTaskbarList() is not { } taskbar || hwnd == IntPtr.Zero)
            {
                return;
            }

            if (kind == TaskbarBadgeKind.None)
            {
                taskbar.SetOverlayIcon(hwnd, IntPtr.Zero, null);
                return;
            }

            using var bitmap = kind == TaskbarBadgeKind.Dot ? BuildDotBitmap() : BuildNumberBitmap(count);
            var hIcon = bitmap.GetHicon();
            try
            {
                var description = kind == TaskbarBadgeKind.Dot ? "Nouveaux messages" : $"{count} notification(s)";
                taskbar.SetOverlayIcon(hwnd, hIcon, description);
            }
            finally
            {
                // The shell copies the icon's bitmap data on the call above,
                // so it's safe (and necessary, to avoid leaking a GDI handle)
                // to destroy this one immediately afterward.
                DestroyIcon(hIcon);
            }
        }
        catch
        {
        }
    }

    private static Bitmap BuildDotBitmap()
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(220, 38, 38));
        using var border = new Pen(Color.White, 2.5f);
        g.FillEllipse(fill, 4, 4, 24, 24);
        g.DrawEllipse(border, 4, 4, 24, 24);
        return bitmap;
    }

    private static Bitmap BuildNumberBitmap(int count)
    {
        var label = count > 99 ? "99+" : count.ToString();
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(220, 38, 38));
        using var border = new Pen(Color.White, 2.5f);
        g.FillEllipse(fill, 1, 1, 30, 30);
        g.DrawEllipse(border, 1, 1, 30, 30);

        var fontSize = label.Length > 2 ? 11f : label.Length > 1 ? 13f : 16f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(label, font, textBrush, new RectangleF(0, 0, 32, 32), format);

        return bitmap;
    }
}
