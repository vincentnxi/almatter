using System;
using Avalonia;
using Avalonia.Controls;

namespace Almatter.App.Views;

/// <summary>
/// Keeps a conversation on its newest message for as long as the reader is
/// there, whatever grows above it.
///
/// Why it's needed: a row doesn't always have its final height when it is
/// first shown. A custom emoji is drawn as its ":shortcode:" until the
/// picture arrives, then as an image taller than a line of text; the row
/// grows, the scroll offset stays the same number, and the last message
/// slides out of view — a small jump on arriving in a channel, but one that
/// has to be scrolled back by hand every time. Scrolling to the end once
/// can't cover that: the growth comes later, and from any row.
///
/// So "the reader is at the bottom" is remembered rather than re-measured
/// after the fact: it only stops being true when the view moves up and
/// away from the end, and while it is true, any change in the height of the
/// content or of the view is followed down.
/// </summary>
internal sealed class BottomFollower
{
    private readonly ScrollViewer _scroller;
    private readonly double _slack;
    private bool _atBottom = true;

    public BottomFollower(ScrollViewer scroller, double slack)
    {
        _scroller = scroller;
        _slack = slack;
        _scroller.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var bottom = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        var distance = bottom - _scroller.Offset.Y;

        if (distance <= _slack)
        {
            _atBottom = true;
            return;
        }

        if (e.OffsetDelta.Y < 0)
        {
            // Moved up and away from the end: the reader scrolling back, or
            // a jump to an older message. From here the view is theirs.
            _atBottom = false;
            return;
        }

        if (_atBottom && (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0))
        {
            // Raises this again, which then finds the view at the bottom.
            _scroller.Offset = new Vector(_scroller.Offset.X, bottom);
        }
    }
}
