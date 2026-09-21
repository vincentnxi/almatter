using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace Almatter.App.ViewModels;

/// <summary>
/// A least-recently-used cache of decoded images, capped by the memory their
/// pixels take rather than by how many there are.
///
/// Why a byte budget: a decoded image costs width × height × 4 bytes of native
/// memory whatever its file size, and that memory is invisible to .NET's
/// garbage collector. The link-preview cache this first replaced had no limit
/// at all, and was measured holding 142 images at 418 MB after an afternoon of
/// channel switching — most of the app's footprint. The custom-emoji and
/// avatar caches were the same shape and are capped here too: a server with a
/// large emoji set kept every one of them for the whole session.
///
/// Evicting only drops the cache's reference; the image is deliberately NOT
/// disposed. It may still be on screen in a message that's showing, and
/// drawing a disposed image fails in the renderer. Once nothing references it
/// any more, SkiaSharp's finalizer releases the pixels.
/// </summary>
internal sealed class BoundedBitmapCache
{
    private readonly long _budgetBytes;
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = [];
    private readonly LinkedList<Entry> _mostRecentFirst = new();

    private readonly record struct Entry(string Key, Bitmap Bitmap, long Bytes);

    public BoundedBitmapCache(long budgetBytes) => _budgetBytes = budgetBytes;

    /// <summary>Decoded pixel memory currently held, in bytes.</summary>
    public long Bytes { get; private set; }

    public int Count => _byKey.Count;

    public bool TryGet(string key, out Bitmap bitmap)
    {
        if (_byKey.TryGetValue(key, out var node))
        {
            _mostRecentFirst.Remove(node);
            _mostRecentFirst.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
        bitmap = null!;
        return false;
    }

    public void Add(string key, Bitmap bitmap)
    {
        if (_byKey.TryGetValue(key, out var existing))
        {
            _mostRecentFirst.Remove(existing);
            _byKey.Remove(key);
            Bytes -= existing.Value.Bytes;
        }

        var bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        var node = _mostRecentFirst.AddFirst(new Entry(key, bitmap, bytes));
        _byKey[key] = node;
        Bytes += bytes;

        // Oldest first, but never the image just added — a single image larger
        // than the whole budget should still be usable by the message asking for it.
        while (Bytes > _budgetBytes && _mostRecentFirst.Last is { } oldest && oldest != node)
        {
            _mostRecentFirst.RemoveLast();
            _byKey.Remove(oldest.Value.Key);
            Bytes -= oldest.Value.Bytes;
        }
    }
}
