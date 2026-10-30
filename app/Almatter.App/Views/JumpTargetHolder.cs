using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Almatter.App.Views;

/// <summary>
/// Brings the message a jump aimed at (a search result, a link, a pinned
/// message) to the middle of the view, and keeps it there for a few seconds.
///
/// Why it's needed: the list is virtualized, so the first scroll lands on a
/// position computed from estimated heights. The rows around the target are
/// then built with their real heights over the next passes, and some keep
/// growing after that — a custom emoji or a link preview picture arriving a
/// second later. Each change makes the list re-estimate the hundreds of rows
/// it hasn't built, its total height swings, and the same scroll offset now
/// points somewhere else (see ReadingPositionKeeper — up to 89 messages
/// away). Centering once and stopping as soon as one pass found nothing to
/// fix left the view wherever the next late change threw it, sometimes far
/// from the message, which was then no longer even built and so never
/// brought back.
///
/// So the target is held, not just scrolled to: every layout pass during the
/// hold puts it back in the middle, bringing it back into view first if it
/// was thrown out. The hold ends after a few seconds, as soon as the reader
/// scrolls, clicks or types in the list, or when something else moves the view
/// on purpose (back to the newest message).
/// </summary>
internal sealed class JumpTargetHolder
{
    /// <summary>Long enough for the pictures around the target to arrive on an ordinary connection.</summary>
    private static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(6);

    /// <summary>Opening or closing a message brings in no pictures of its own; this only has to outlast the rows around it being rebuilt.</summary>
    private static readonly TimeSpan HoldInPlaceFor = TimeSpan.FromSeconds(2);

    /// <summary>A safety net: a view that still needs correcting after this many moves is being moved by something else, and fighting it would be worse.</summary>
    private const int MaxCorrections = 60;

    private readonly ScrollViewer _scroller;
    private readonly ItemsControl _items;

    private object? _target;
    /// <summary>Where the target's top is held, from the view's top edge; null to center it.</summary>
    private double? _topAt;
    private DateTime _holdUntil;
    private int _correctionsLeft;
    private bool _correcting;
    private bool _correctionQueued;

    public JumpTargetHolder(ScrollViewer scroller, ItemsControl items)
    {
        _scroller = scroller;
        _items = items;

        // Tunnelled and with handled events too: a click lands on a message's
        // own buttons first, and the scroll bar marks its presses as handled.
        _scroller.AddHandler(InputElement.PointerWheelChangedEvent, OnReaderInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        _scroller.AddHandler(InputElement.PointerPressedEvent, OnReaderInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        _scroller.AddHandler(InputElement.KeyDownEvent, OnReaderInput, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Centers this message (one of the list's items) and holds it there. Replaces any hold already running.</summary>
    public void Hold(object target) => Start(target, topAt: null, HoldFor);

    /// <summary>
    /// Holds this message's top where it is now, through a change to its own
    /// height that is about to happen — a long message opened or closed. With
    /// <paramref name="keepTopInView"/>, a top that has scrolled above the
    /// view is brought back to the view's top edge instead: closing a message
    /// read down to its end would otherwise leave it entirely out of sight.
    /// Call it just before the change, while the message is still on screen.
    /// </summary>
    public void HoldInPlace(object target, bool keepTopInView)
    {
        if (_items.ContainerFromItem(target) is not { } container
            || container.TranslatePoint(default, _scroller)?.Y is not { } y)
        {
            return;
        }
        Start(target, keepTopInView ? Math.Max(y, 0) : y, HoldInPlaceFor);
    }

    private void Start(object target, double? topAt, TimeSpan holdFor)
    {
        Release();
        _target = target;
        _topAt = topAt;
        _holdUntil = DateTime.UtcNow + holdFor;
        _correctionsLeft = MaxCorrections;
        _scroller.LayoutUpdated += OnLayoutUpdated;
        Correct();
    }

    /// <summary>Lets the view go. Safe to call when nothing is held.</summary>
    public void Release()
    {
        _scroller.LayoutUpdated -= OnLayoutUpdated;
        _target = null;
    }

    private void OnReaderInput(object? sender, RoutedEventArgs e) => Release();

    private void OnLayoutUpdated(object? sender, EventArgs e) => QueueCorrection();

    /// <summary>
    /// Corrects once the layout has settled rather than from inside
    /// LayoutUpdated. Measured on a headless replica of the list: called
    /// from the event, the correction read the target's position before the
    /// panel had finished moving it and found nothing to fix — rows growing
    /// pushed the target to the bottom edge and it stayed there. And the
    /// pass ScrollIntoView runs on the spot was swallowed by the re-entry
    /// guard below, so after the first jump nothing ever centered the
    /// target: it stayed stuck to the top of the view.
    /// </summary>
    private void QueueCorrection()
    {
        if (_correctionQueued || _target is null)
        {
            return;
        }
        _correctionQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _correctionQueued = false;
            Correct();
        }, DispatcherPriority.Background);
    }

    private void Correct()
    {
        // ScrollIntoView runs a whole layout pass on the spot, raising
        // LayoutUpdated again while this is still on the stack — the same
        // recursion that once overflowed the stack in ReadingPositionKeeper.
        if (_correcting || _target is null)
        {
            return;
        }

        if (DateTime.UtcNow >= _holdUntil || _correctionsLeft <= 0
            || _items.ItemsSource is not IList list || list.IndexOf(_target) < 0)
        {
            // Time's up, or the list was rebuilt (another channel opened) and
            // the message is no longer in it.
            Release();
            return;
        }

        var moved = false;
        _correcting = true;
        try
        {
            if (CorrectOnce())
            {
                _correctionsLeft--;
                moved = true;
            }
        }
        finally
        {
            _correcting = false;
        }

        // Check the result once it has been laid out, whether or not the
        // move happens to raise a layout pass of its own.
        if (moved)
        {
            QueueCorrection();
        }
    }

    /// <summary>One step toward the target sitting in the middle of the view. True when it moved the view.</summary>
    private bool CorrectOnce()
    {
        var container = _items.ContainerFromItem(_target!);
        if (container is null || !container.IsVisible)
        {
            // Not built: either the first jump, or a later swing carried the
            // view away from it. Bring it back; the next pass centers it.
            _items.ScrollIntoView(_target!);
            return true;
        }

        var top = container.TranslatePoint(default, _scroller);
        if (top is null)
        {
            return false;
        }

        var bottom = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        var wanted = _topAt is { } topAt
            ? _scroller.Offset.Y + top.Value.Y - topAt
            : _scroller.Offset.Y + top.Value.Y - (_scroller.Viewport.Height / 2) + (container.Bounds.Height / 2);
        var targetY = Math.Clamp(wanted, 0, bottom);
        if (Math.Abs(targetY - _scroller.Offset.Y) < 1)
        {
            return false;
        }
        _scroller.Offset = new Vector(_scroller.Offset.X, targetY);
        return true;
    }
}
