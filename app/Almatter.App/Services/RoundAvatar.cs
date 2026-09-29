using System;
using SkiaSharp;

namespace Almatter.App.Services;

/// <summary>
/// A profile picture cut into a circle, as a PNG, for places outside the
/// window that can only take a finished image — a desktop notification.
/// Inside the window the circle is a clip on the brush instead.
///
/// Plain Skia, nothing OS-specific: whichever notifier a platform gets can
/// hand these bytes to its own notification system.
/// </summary>
internal static class RoundAvatar
{
    /// <summary>Null when the file isn't an image Skia can read.</summary>
    public static byte[]? ToPng(string path, int size)
    {
        using var source = SKImage.FromEncodedData(path);
        if (source is null)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var circle = new SKPath();
        circle.AddCircle(size / 2f, size / 2f, size / 2f);
        canvas.ClipPath(circle, antialias: true);

        // Square pictures are the norm, but crop the middle of anything else
        // rather than squash it.
        var side = Math.Min(source.Width, source.Height);
        var from = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        canvas.DrawImage(source, from, SKRect.Create(size, size), new SKSamplingOptions(SKCubicResampler.Mitchell));

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }
}
