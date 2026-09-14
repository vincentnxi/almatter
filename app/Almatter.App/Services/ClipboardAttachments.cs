using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Almatter.App.Services;

/// <summary>
/// What a composer paste should attach rather than type: files copied in
/// the file explorer, or an image (a screenshot, "copy image" in a browser).
///
/// Uploading works from a path on disk, so a pasted image is first written
/// out as a PNG in a temporary folder of its own — the file name is what the
/// message shows, and a folder per paste keeps that name clean even when two
/// images are pasted within the same second.
/// </summary>
internal static class ClipboardAttachments
{
    private static readonly string TemporaryRoot = Path.Combine(Path.GetTempPath(), "Almatter", "pasted-images");

    /// <summary>The local paths to attach — empty when the clipboard holds nothing to attach and the paste is ordinary text.</summary>
    public static async Task<IReadOnlyList<string>> TryReadAsFilesAsync(IClipboard clipboard)
    {
        if (await clipboard.TryGetFilesAsync() is { Length: > 0 } items)
        {
            // Folders can't be attached; a copied selection with only folders pastes nothing.
            return items
                .OfType<IStorageFile>()
                .Select(f => f.TryGetLocalPath())
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList();
        }

        // Copying from Word or Excel puts a picture of the selection on the
        // clipboard next to its text — that's meant as text. Only a clipboard
        // with an image and no text at all is a pasted image.
        var formats = await clipboard.GetDataFormatsAsync();
        if (formats.Contains(DataFormat.Text) || !formats.Contains(DataFormat.Bitmap))
        {
            return [];
        }

        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null)
        {
            return [];
        }

        DeleteLeftoversFromEarlierSessions();
        var folder = Directory.CreateDirectory(Path.Combine(TemporaryRoot, Guid.NewGuid().ToString("N")));
        var path = Path.Combine(folder.FullName, $"image-{DateTime.Now:yyyy-MM-dd-HHmmss}.png");
        bitmap.Save(path);
        return [path];
    }

    /// <summary>Removes the temporary copies made for pasted images once they're uploaded; files pasted from elsewhere on disk are left alone.</summary>
    public static void DeleteTemporaryImages(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Path.GetDirectoryName(path) is not { } folder
                || !string.Equals(Path.GetDirectoryName(folder), TemporaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            TryDeleteFolder(folder);
        }
    }

    /// <summary>A crash between saving and uploading would otherwise leave the image behind for good.</summary>
    private static void DeleteLeftoversFromEarlierSessions()
    {
        if (!Directory.Exists(TemporaryRoot))
        {
            return;
        }
        foreach (var folder in Directory.EnumerateDirectories(TemporaryRoot))
        {
            if (Directory.GetCreationTime(folder) < DateTime.Now.AddDays(-1))
            {
                TryDeleteFolder(folder);
            }
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
