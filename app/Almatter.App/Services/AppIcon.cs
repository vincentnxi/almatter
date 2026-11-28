using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Almatter.App.Models;

namespace Almatter.App.Services;

/// <summary>
/// Puts the logo in the tint chosen in Apparence (see AppIconTint) wherever
/// the app shows it.
///
/// Windows, their taskbar buttons and Alt+Tab take the window's own icon,
/// swapped on the spot. Shortcuts can't follow the executable's built-in
/// icon, which is fixed once installed, so on Windows the installer points
/// them at a separate file next to the app, almatter-icon.ico; the app keeps
/// that file in the chosen tint and tells Explorer to redraw. The
/// notification-area icon is read from the same file (see WindowsTrayNotifier).
/// </summary>
internal static class AppIcon
{
    /// <summary>The file the installer's shortcuts point at. Must match installer/Almatter.iss.</summary>
    public const string ShortcutIconFileName = "almatter-icon.ico";

    private static readonly Dictionary<AppIconTint, WindowIcon> WindowIcons = [];

    public static WindowIcon ForWindows(AppIconTint tint)
    {
        tint = AppIconTints.Known(tint);
        if (!WindowIcons.TryGetValue(tint, out var icon))
        {
            using var stream = AssetLoader.Open(AppIconTints.ResourceUri(tint));
            icon = new WindowIcon(stream);
            WindowIcons[tint] = icon;
        }
        return icon;
    }

    /// <summary>Gives every open window the tint's icon. Each window also takes it when it is created.</summary>
    public static void ApplyToOpenWindows(AppIconTint tint)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }
        var icon = ForWindows(tint);
        foreach (var window in desktop.Windows.ToList())
        {
            window.Icon = icon;
        }
    }

    /// <summary>
    /// Brings almatter-icon.ico next to the executable to the chosen tint
    /// and returns its path — or null off Windows, or when the folder can't
    /// be written (the shortcuts then keep their last picture, and the
    /// notification area falls back to the executable's icon).
    /// </summary>
    public static string? SyncShortcutIcon(AppIconTint tint)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        var path = Path.Combine(AppContext.BaseDirectory, ShortcutIconFileName);
        try
        {
            byte[] wanted;
            using (var stream = AssetLoader.Open(AppIconTints.ResourceUri(AppIconTints.Known(tint))))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                wanted = copy.ToArray();
            }

            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(wanted))
            {
                return path;
            }

            // Written beside it, then swapped in whole: Explorer may be
            // reading the file at any moment and must never see half of it.
            var temporary = path + ".new";
            File.WriteAllBytes(temporary, wanted);
            File.Move(temporary, path, overwrite: true);

            // Explorer keeps shortcut pictures in its icon cache; this tells
            // it to look again.
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return path;
        }
        catch (Exception ex)
        {
            Diagnostics.CrashLogger.Write("app icon: shortcut icon not updated", ex);
            return File.Exists(path) ? path : null;
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
