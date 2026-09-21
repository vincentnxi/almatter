using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Almatter.App.ViewModels;

/// <summary>
/// Decodes an image with only as many pixels as the box it's shown in needs.
///
/// A link preview card shows its image in a 420 × 160 box, cropped to fill.
/// Decoding the source at full resolution kept pixels nobody could see: a
/// 1985 × 1501 og:image is 11 MB of native memory, displayed in a strip that
/// needs about 0.3 MB. Measured over the images actually cached here, full
/// resolution came to 418 MB for 142 previews.
///
/// Skia (via SkiaSharp, which Avalonia already renders with on every
/// platform) reads the image's dimensions from its header without decoding
/// it, so the decode size can be chosen up front — and a source that's
/// already smaller than the box is decoded as-is rather than blown up.
/// </summary>
internal static class CardImageDecoder
{
    /// <param name="scaling">The display's scale factor — a 420-pixel box on a 150 % screen is 630 physical pixels wide.</param>
    public static Bitmap DecodeToCover(string path, double boxWidth, double boxHeight, double scaling)
    {
        var needWidth = boxWidth * scaling;
        var needHeight = boxHeight * scaling;

        int sourceWidth, sourceHeight;
        using (var codec = SKCodec.Create(path))
        {
            if (codec is null)
            {
                // Not something Skia can read the header of — let the full
                // decoder try, and report the failure as it always did.
                return new Bitmap(path);
            }
            sourceWidth = codec.Info.Width;
            sourceHeight = codec.Info.Height;
        }

        // "Cover": the image fills the box in both directions and the excess
        // is cropped, so the larger of the two ratios decides.
        var scale = Math.Max(needWidth / sourceWidth, needHeight / sourceHeight);
        if (scale >= 1)
        {
            return new Bitmap(path);
        }

        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, Math.Max(1, (int)Math.Ceiling(sourceWidth * scale)), BitmapInterpolationMode.HighQuality);
    }

    /// <summary>
    /// Decodes a small square icon — an avatar or a custom emoji — at the
    /// size it is actually drawn at, not the size it was uploaded at.
    ///
    /// The server hands both of these back at 128 x 128 whatever they were
    /// uploaded as, and every one of them was being decoded at that size and
    /// kept forever: 64 KB of native memory each, for pictures drawn in a
    /// 40-pixel circle or a 23-pixel reaction pill. A server with a few
    /// thousand custom emoji would have grown that cache without limit.
    ///
    /// The floor of 1.5 on the scale factor is headroom: the window can be
    /// dragged from a 100 % screen to a 150 % one without anything being
    /// re-decoded, and an icon decoded for the smaller screen would look
    /// soft there. Above 150 % the real scaling is used.
    /// </summary>
    public static Bitmap DecodeToFit(string path, double logicalSize, double scaling)
    {
        var want = (int)Math.Ceiling(logicalSize * Math.Max(scaling, 1.5));

        int sourceWidth;
        using (var codec = SKCodec.Create(path))
        {
            if (codec is null)
            {
                return new Bitmap(path);
            }
            sourceWidth = codec.Info.Width;
        }

        // Already smaller than the box — blowing it up would cost memory and
        // gain nothing the renderer can't do at draw time.
        if (sourceWidth <= want)
        {
            return new Bitmap(path);
        }

        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, want, BitmapInterpolationMode.HighQuality);
    }
}
