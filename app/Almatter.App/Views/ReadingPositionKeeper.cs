using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Almatter.App.Views;

/// <summary>
/// Holds the conversation still while a message already on screen changes
/// height — its first reaction appearing, its header coming back when it's
/// pinned, an edit that adds a line.
///
/// Why it's needed: the list is virtualized, so it only knows the real
/// height of the handful of rows it has built, and estimates everything
/// else from them. When one of those rows grows, the estimate for the
/// hundreds of rows above is recomputed, the list's total height swings by
/// thousands of pixels, and the scroll offset — which stays the same number
/// — now points somewhere else entirely. Measured in isolation: a first
/// reaction added mid-conversation moved the view 89 messages up.
///
/// Avalonia's own scroll anchoring doesn't help: the virtualized panel
/// recycles the anchor row before the anchoring can use it (measured too —
/// still 82 messages). So this does it by hand: note which message is at
/// the top of the view and where, let the change lay out, then put that
/// message back exactly where it was.
///
/// At the very bottom of the conversation the rule is different: staying on
/// the newest message matters more than keeping the top row still, so the
/// view is simply held at the bottom.
/// </summary>
internal sealed class ReadingPositionKeeper
{
    /// <summary>A realized anchor needs one correction; one that was recycled needs a scroll-into-view first, then one more. Past that, something else is moving the view and fighting it would be worse.</summary>
    private const int MaxCorrections = 4;

    private readonly ScrollViewer _scroller;
    private readonly ItemsControl _items;
    private readonly double _stickToBottomSlack;

    private object? _anchorItem;
    private double _anchorY;
    private bool _stickToBottom;
    private int _correctionsLeft;
    private bool _pending;

    public ReadingPositionKeeper(ScrollViewer scroller, ItemsControl items, double stickToBottomSlack)
    {
        _scroller = scroller;
        _items = items;
        _stickToBottomSlack = stickToBottomSlack;
    }

    /// <summary>
    /// Call immediately before changing something that can alter the height
    /// of a row already on screen — synchronously, with no await between
    /// this and the change, so the next layout pass is the one that applies it.
    /// </summary>
    public void Capture()
    {
        // Several changes in one burst (a poll updating three messages) keep
        // the position from before the first: re-capturing mid-burst would
        // lock in a view that has already jumped.
        if (_pending)
        {
            return;
        }

        _stickToBottom = DistanceFromBottom() <= _stickToBottomSlack;
        if (!_stickToBottom)
        {
            (_anchorItem, _anchorY) = FindTopRow();
            if (_anchorItem is null)
            {
                return;
            }
        }

        _pending = true;
        _correctionsLeft = MaxCorrections;
        _scroller.LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // Each correction triggers another layout pass, which lands back
        // here; stop as soon as a pass finds nothing left to fix.
        if (!CorrectOnce() || --_correctionsLeft <= 0)
        {
            _scroller.LayoutUpdated -= OnLayoutUpdated;
            _pending = false;
            _anchorItem = null;
        }
    }

    /// <summary>One step toward the captured position. True when it moved the view and another pass should check the result.</summary>
    private bool CorrectOnce()
    {
        if (_stickToBottom)
        {
            var bottom = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
            if (Math.Abs(_scroller.Offset.Y - bottom) < 0.5)
            {
                return false;
            }
            _scroller.Offset = new Vector(_scroller.Offset.X, bottom);
            return true;
        }

        var container = _items.ContainerFromItem(_anchorItem!);
        if (container is null || !container.IsVisible)
        {
            // The jump carried the view far enough that the anchor row was
            // recycled. Bring it back first; the next pass fine-tunes it.
            if (_items.ItemsSource is IList list && list.IndexOf(_anchorItem) >= 0)
            {
                _items.ScrollIntoView(_anchorItem!);
                return true;
            }
            return false;   // the message itself is gone (deleted) — nothing to hold on to
        }

        var y = container.TranslatePoint(default, _scroller)?.Y;
        if (y is null)
        {
            return false;
        }

        var delta = y.Value - _anchorY;
        if (Math.Abs(delta) < 0.5)
        {
            return false;
        }
        _scroller.Offset = new Vector(_scroller.Offset.X, _scroller.Offset.Y + delta);
        return true;
    }

    /// <summary>The message whose row is highest in the view, and how far from the view's top edge it sits (negative when partly scrolled past).</summary>
    private (object? Item, double Y) FindTopRow()
    {
        var panel = _items.ItemsPanelRoot;
        if (panel is null)
        {
            return (null, 0);
        }

        object? best = null;
        var bestY = double.MaxValue;
        foreach (var child in panel.Children)
        {
            if (!child.IsVisible || child.DataContext is null)
            {
                continue;
            }
            var top = child.TranslatePoint(default, _scroller);
            if (top is null)
            {
                continue;
            }
            var y = top.Value.Y;
            var visible = y + child.Bounds.Height > 0 && y < _scroller.Viewport.Height;
            if (visible && y < bestY)
            {
                best = child.DataContext;
                bestY = y;
            }
        }
        return (best, bestY);
    }

    private double DistanceFromBottom() =>
        _scroller.Extent.Height - _scroller.Viewport.Height - _scroller.Offset.Y;
}
