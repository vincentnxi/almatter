using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// Draws a gradient theme's ground behind a whole window, as a stack of thin
/// solid strips rather than with a LinearGradientBrush.
///
/// Why: the app renders on the CPU on purpose, and there a gradient brush is
/// worked out pixel by pixel. Measured with the app's own Skia on a
/// 1462×1027 window: 3,8 ms per repaint for the brush against 0,08 ms for
/// the strips (9,6 ms against 0,15 ms at 2560×1440). Every scroll of the
/// conversation repaints most of the window, so the brush would have eaten a
/// large share of each frame. A vertical gradient is the same colour all
/// along a row, so strips of 4 px, each a little darker than the last, look
/// the same: the whole range is only some fifty shades of each channel.
///
/// Draws nothing for a theme without a gradient: the window's own solid
/// background shows through, as before.
/// </summary>
public sealed class GroundBackdrop : Control
{
    public static readonly StyledProperty<ThemeDefinition?> PaletteProperty =
        AvaloniaProperty.Register<GroundBackdrop, ThemeDefinition?>(nameof(Palette));

    /// <summary>Bound to the "GroundTheme" resource that ThemeResources.Apply sets.</summary>
    public ThemeDefinition? Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    private const double StripHeight = 4;

    /// <summary>Where the lighter top has mostly faded: 60 % of the way to the bottom colour at 40 % of the height.</summary>
    private const double MidStop = 0.4;
    private const double MidBlend = 0.6;

    static GroundBackdrop()
    {
        AffectsRender<GroundBackdrop>(PaletteProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GroundBackdrop>(false);
    }

    public override void Render(DrawingContext context)
    {
        if (Palette is not { GroundTopHex: { } topHex, GroundBottomHex: { } bottomHex })
        {
            return;
        }

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // Brushes are worked out once per theme and height, not on every
        // repaint: scrolling repaints often, resizing far less.
        if (_brushes is null || !ReferenceEquals(_brushesTheme, Palette) || _brushesHeight != size.Height)
        {
            _brushes = BuildStrips(Color.Parse(topHex), Color.Parse(bottomHex), size.Height);
            _brushesTheme = Palette;
            _brushesHeight = size.Height;
        }

        for (var i = 0; i < _brushes.Length; i++)
        {
            var y = i * StripHeight;
            // A pixel taller than the strip so no hairline gap can open
            // between two of them at a fractional display scale.
            var height = Math.Min(StripHeight + 1, size.Height - y);
            context.FillRectangle(_brushes[i], new Rect(0, y, size.Width, height));
        }
    }

    private IBrush[]? _brushes;
    private ThemeDefinition? _brushesTheme;
    private double _brushesHeight;

    private static IBrush[] BuildStrips(Color top, Color bottom, double totalHeight)
    {
        var mid = Lerp(top, bottom, MidBlend);
        var strips = new IBrush[(int)Math.Ceiling(totalHeight / StripHeight)];
        for (var i = 0; i < strips.Length; i++)
        {
            // The colour at the strip's middle, so the first and last strips
            // are as close to the ends as the strip height allows.
            var t = Math.Clamp((i * StripHeight + StripHeight / 2) / totalHeight, 0, 1);
            var colour = t < MidStop
                ? Lerp(top, mid, t / MidStop)
                : Lerp(mid, bottom, (t - MidStop) / (1 - MidStop));
            strips[i] = new ImmutableSolidColorBrush(colour);
        }
        return strips;
    }

    private static Color Lerp(Color from, Color to, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }
}
