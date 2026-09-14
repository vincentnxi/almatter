using System;
using System.Runtime.InteropServices;
using SkiaSharp;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

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

            var hIcon = BuildIcon(kind == TaskbarBadgeKind.Dot ? null : count);
            if (hIcon == IntPtr.Zero)
            {
                return;
            }
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

    internal const int IconSize = 32;

    /// <summary>
    /// Draws the badge — a red dot, or a red circle with the count — as raw
    /// pixels. Skia rather than GDI+ (System.Drawing): Avalonia already
    /// renders with Skia, so this adds nothing to load, where System.Drawing
    /// was one of the reasons the app had to pull in Windows Forms. It also
    /// means the drawing is reusable as-is for a macOS dock badge later.
    ///
    /// Straight (unpremultiplied) BGRA, rows top to bottom — the layout
    /// CreateBitmap expects for a 32-bit icon bitmap, and what GDI+ used to
    /// hand the shell, so the badge's anti-aliased edge looks as before.
    /// </summary>
    internal static byte[] RenderPixels(int? count)
    {
        var info = new SKImageInfo(IconSize, IconSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var fill = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var border = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };

            if (count is null)
            {
                canvas.DrawCircle(16, 16, 12, fill);
                canvas.DrawCircle(16, 16, 12, border);
            }
            else
            {
                canvas.DrawCircle(16, 16, 15, fill);
                canvas.DrawCircle(16, 16, 15, border);

                var label = count > 99 ? "99+" : count.Value.ToString();
                var size = label.Length > 2 ? 11f : label.Length > 1 ? 13f : 16f;
                using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
                using var font = new SKFont(typeface, size) { Edging = SKFontEdging.Antialias };
                using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };

                // Centre on the glyphs' own height: the baseline sits half the
                // ascent-to-descent span below the middle.
                font.GetFontMetrics(out var metrics);
                var baseline = 16 - (metrics.Ascent + metrics.Descent) / 2;
                canvas.DrawText(label, 16, baseline, SKTextAlign.Center, font, text);
            }
        }
        return bitmap.Bytes;
    }

    /// <summary>A Windows icon handle for the badge, or zero on failure. The caller destroys it.</summary>
    private static IntPtr BuildIcon(int? count)
    {
        var color = CreateBitmap(IconSize, IconSize, 1, 32, RenderPixels(count));

        // Every icon needs a 1-bit mask as well; with a 32-bit colour bitmap
        // the shell draws from its alpha channel and the mask goes unused, so
        // an all-clear one will do. Rows are padded to 16 bits: 32 px → 4 bytes.
        var mask = CreateBitmap(IconSize, IconSize, 1, 1, new byte[IconSize * 4]);
        try
        {
            if (color == IntPtr.Zero || mask == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            // The icon keeps its own copies of both bitmaps.
            if (color != IntPtr.Zero) DeleteObject(color);
            if (mask != IntPtr.Zero) DeleteObject(mask);
        }
    }
}
