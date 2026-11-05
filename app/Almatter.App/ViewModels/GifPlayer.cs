using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Almatter.App.ViewModels;

/// <summary>
/// Plays an attached animated GIF inside its thumbnail box, on request.
///
/// Built to cost as little as a moving picture can. Nothing is decoded ahead:
/// a 60-frame GIF decoded up front would be 60 full pictures of native
/// memory, invisible to the garbage collector. Instead one picture at the
/// GIF's own size is kept (each frame is drawn over the one before, the way
/// the format works) and shrunk into one of two small box-sized bitmaps,
/// which take turns on screen — swapping the bitmap is what tells the
/// renderer to draw again. That is three pictures in memory whatever the
/// frame count, all released when playback stops.
///
/// Only ever one at a time, started by a click and stopped by another, by
/// scrolling away, by minimising the window — see MainViewModel.ToggleGif.
/// Animated custom emoji are deliberately not played at all; that decision
/// is explained on GetCustomEmojiBitmapAsync and still stands.
/// </summary>
internal sealed class GifPlayer
{
    /// <summary>
    /// The largest GIF this will play, in pixels. Every frame is decoded at
    /// full size, so this is also the memory one playback may hold (32 MB)
    /// and roughly the work done per frame. Anything bigger opens in the
    /// system's own viewer instead, as before.
    /// </summary>
    private const long MaxPlayablePixels = 8_000_000;

    /// <summary>A GIF declares its own frame count; past this, playback loops over the first ones. Real ones rarely reach a few hundred.</summary>
    private const int MaxFrames = 2_000;

    private readonly SKCodec _codec;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly SKBitmap _canvas;
    private readonly WriteableBitmap[] _buffers;
    private readonly int _loops;
    private int _decodedFrame = -1;

    private GifPlayer(SKCodec codec, SKCodecFrameInfo[] frames, SKBitmap canvas, WriteableBitmap[] buffers)
    {
        _codec = codec;
        _frames = frames;
        _canvas = canvas;
        _buffers = buffers;
        // Skia: -1 = forever, 0 = played once, n = played once and repeated n times.
        _loops = codec.RepetitionCount < 0 ? int.MaxValue : codec.RepetitionCount + 1;
    }

    /// <summary>
    /// Null when the file is not an animated GIF this will play — a single
    /// frame, too large, or unreadable. The caller then opens it the usual way.
    /// </summary>
    public static GifPlayer? TryOpen(string path, double boxWidth, double boxHeight, double scaling)
    {
        var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }
        try
        {
            var info = codec.Info;
            if (codec.EncodedFormat != SKEncodedImageFormat.Gif
                || codec.FrameCount < 2
                || info.Width <= 0 || info.Height <= 0
                || (long)info.Width * info.Height > MaxPlayablePixels)
            {
                codec.Dispose();
                return null;
            }

            var frames = codec.FrameInfo;
            if (frames.Length > MaxFrames)
            {
                Array.Resize(ref frames, MaxFrames);
            }

            var canvas = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
            var pixelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(boxWidth * scaling)),
                Math.Max(1, (int)Math.Ceiling(boxHeight * scaling)));
            var buffers = new[]
            {
                new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
                new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
            };
            return new GifPlayer(codec, frames, canvas, buffers);
        }
        catch
        {
            codec.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Runs on a background thread until cancelled or the GIF's own loop
    /// count is used up, handing each finished frame to <paramref name="present"/>
    /// (which must hop to the UI thread itself). Releases the decoder when it
    /// ends; the two on-screen bitmaps are released by <see cref="ReleaseBuffers"/>,
    /// once the screen has stopped showing them.
    /// </summary>
    public async Task RunAsync(Action<Bitmap> present, CancellationToken cancellationToken)
    {
        try
        {
            var next = 0;
            for (var loop = 0; loop < _loops; loop++)
            {
                for (var index = 0; index < _frames.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!DecodeFrame(index))
                    {
                        return;
                    }
                    var buffer = _buffers[next];
                    next ^= 1;
                    DrawCovering(buffer);
                    present(buffer);

                    // Browsers treat a delay this short as "unset" and show
                    // the frame for a tenth of a second; GIFs are made for that.
                    var delay = _frames[index].Duration;
                    await Task.Delay(delay <= 10 ? 100 : delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _canvas.Dispose();
            _codec.Dispose();
        }
    }

    /// <summary>Releases the two box-sized bitmaps. Called a moment after the still thumbnail is back on screen, so the renderer is no longer drawing either.</summary>
    public void ReleaseBuffers()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Leaves frame <paramref name="index"/> in the canvas. A GIF frame is
    /// usually drawn over the one before it, which Skia names as the frame
    /// it needs; when that isn't the one already there (a frame meant to be
    /// undone, or playback starting over), the chain of frames it rests on
    /// is decoded first, oldest first.
    /// </summary>
    private bool DecodeFrame(int index)
    {
        var chain = new Stack<int>();
        for (var frame = index; frame != -1 && frame != _decodedFrame; frame = _frames[frame].RequiredFrame)
        {
            chain.Push(frame);
            if (chain.Count > _frames.Length)
            {
                return false; // A file whose frames point at each other in a circle.
            }
        }
        if (chain.Count == 0)
        {
            return true;
        }

        while (chain.TryPop(out var frame))
        {
            var required = _frames[frame].RequiredFrame;
            if (required == -1)
            {
                _canvas.Erase(SKColors.Transparent);
            }
            var result = _codec.GetPixels(_canvas.Info, _canvas.GetPixels(), new SKCodecOptions(frame, required));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                _decodedFrame = -1;
                return false;
            }
            _decodedFrame = frame;
        }
        return true;
    }

    /// <summary>The canvas, shrunk and cropped to fill the box — the same "cover" framing the still thumbnail has, so starting playback doesn't shift the picture.</summary>
    private void DrawCovering(WriteableBitmap target)
    {
        using var locked = target.Lock();
        var colorType = locked.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var info = new SKImageInfo(locked.Size.Width, locked.Size.Height, colorType, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, locked.Address, locked.RowBytes);
        if (surface is null)
        {
            return;
        }

        float sourceWidth = _canvas.Width, sourceHeight = _canvas.Height;
        float boxWidth = info.Width, boxHeight = info.Height;
        var scale = Math.Max(boxWidth / sourceWidth, boxHeight / sourceHeight);
        var cropWidth = boxWidth / scale;
        var cropHeight = boxHeight / scale;
        var source = SKRect.Create((sourceWidth - cropWidth) / 2, (sourceHeight - cropHeight) / 2, cropWidth, cropHeight);

        // Shares the canvas pixels rather than copying a full-size picture per frame.
        using var pixmap = _canvas.PeekPixels();
        using var image = SKImage.FromPixels(pixmap);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, source, SKRect.Create(boxWidth, boxHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        surface.Canvas.Flush();
    }
}
