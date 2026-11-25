using System;
using System.Runtime.InteropServices;
using Almatter.App.Localization;
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
/// (the same mechanism Teams/Outlook use for their unread badges) — a thin
/// wrapper around the Shell's <c>ITaskbarList3::SetOverlayIcon</c>, since no
/// managed API exposes this.
///
/// The COM object is reached by hand rather than through <c>[ComImport]</c>:
/// built-in COM interop does not exist under Native AOT, and the generated
/// alternative (<c>[GeneratedComInterface]</c>) needs AllowUnsafeBlocks. So
/// the object comes from <c>CoCreateInstance</c>, and each method is read
/// from its vtable slot and called through a delegate — all safe code, and
/// the same in a normal build as in an AOT one.
/// </summary>
internal static class TaskbarBadge
{
    private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private static readonly Guid IidTaskbarList3 = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");

    // Positions in the ITaskbarList3 vtable, which Windows fixed for good in
    // Windows 7: IUnknown 0-2, ITaskbarList 3-7 (HrInit first),
    // ITaskbarList2 8, then ITaskbarList3 9-20 with SetOverlayIcon tenth.
    private const int HrInitSlot = 3;
    private const int SetOverlayIconSlot = 18;

    private const uint ClsctxInprocServer = 1;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HrInitFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetOverlayIconFn(
        IntPtr self, IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? description);

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

    // The taskbar object and its one method we call. Kept for the life of the
    // process, as the [ComImport] wrapper this replaces was: Windows releases
    // it when the process exits.
    private static IntPtr _taskbarList;
    private static SetOverlayIconFn? _setOverlayIcon;
    private static bool _initFailed;

    private static SetOverlayIconFn? GetSetOverlayIcon()
    {
        if (_setOverlayIcon is not null || _initFailed)
        {
            return _setOverlayIcon;
        }
        var instance = IntPtr.Zero;
        try
        {
            var clsid = ClsidTaskbarList;
            var iid = IidTaskbarList3;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out instance) < 0
                || instance == IntPtr.Zero)
            {
                _initFailed = true;
                return null;
            }

            var vtable = Marshal.ReadIntPtr(instance);
            var hrInit = Marshal.GetDelegateForFunctionPointer<HrInitFn>(
                Marshal.ReadIntPtr(vtable, HrInitSlot * IntPtr.Size));
            if (hrInit(instance) < 0)
            {
                _initFailed = true;
                return null;
            }

            _setOverlayIcon = Marshal.GetDelegateForFunctionPointer<SetOverlayIconFn>(
                Marshal.ReadIntPtr(vtable, SetOverlayIconSlot * IntPtr.Size));
            _taskbarList = instance;
            instance = IntPtr.Zero; // ownership kept in _taskbarList
        }
        catch
        {
            _initFailed = true;
        }
        finally
        {
            // Only reached with a live pointer when setup failed part-way.
            if (instance != IntPtr.Zero)
            {
                Marshal.Release(instance);
            }
        }
        return _setOverlayIcon;
    }

    /// <summary>Best-effort — a failure here (an unusual shell, a locked-down environment, ...) should never take down the app, it just means no badge.</summary>
    public static void Update(IntPtr hwnd, TaskbarBadgeKind kind, int count = 0)
    {
        // The taskbar button is a Windows shell object; elsewhere there is
        // no badge for now (see docs/PLATFORM_PORTABILITY.md).
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (GetSetOverlayIcon() is not { } setOverlayIcon || hwnd == IntPtr.Zero)
            {
                return;
            }

            if (kind == TaskbarBadgeKind.None)
            {
                setOverlayIcon(_taskbarList, hwnd, IntPtr.Zero, null);
                return;
            }

            var hIcon = BuildIcon(kind == TaskbarBadgeKind.Dot ? null : count);
            if (hIcon == IntPtr.Zero)
            {
                return;
            }
            try
            {
                var description = kind == TaskbarBadgeKind.Dot ? Loc.S.TaskbarNewMessages : Loc.S.TaskbarNotifications(count);
                setOverlayIcon(_taskbarList, hwnd, hIcon, description);
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
