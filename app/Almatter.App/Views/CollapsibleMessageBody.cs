using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Almatter.App.Views;

/// <summary>
/// Holds a message's text and, when it runs very long, cuts it to a fixed
/// height with the bottom faded out, so one long message doesn't take over
/// the whole conversation. The buttons that open and close it sit next to
/// this in the window's XAML, shown through ShowsExpand / ShowsCollapse.
///
/// Judged on the height the text actually takes on screen, not on a count
/// of lines — the same rule as the official client: a code block, a table
/// or a heading weighs what it shows, and narrowing the window can push a
/// message over the limit.
///
/// Whether it is open is kept on the message (IsExpanded is bound to it),
/// not here: the list is virtualized, and this control is reused for
/// another message as soon as it scrolls out of view.
/// </summary>
public sealed class CollapsibleMessageBody : Decorator
{
    /// <summary>How tall a long message is cut to — the official client's figure.</summary>
    public const double CollapsedHeight = 600;

    /// <summary>
    /// A message is only cut when this much more would be hidden. Cutting
    /// one that runs a few lines past the limit would hide those lines
    /// behind a fade and a button for almost nothing.
    /// </summary>
    private const double MinHiddenHeight = 100;

    /// <summary>The strip at the very bottom left clear for the "Afficher la suite" button.</summary>
    private const double ButtonStripHeight = 36;

    /// <summary>How far above the button strip the text starts fading out.</summary>
    private const double FadeHeight = 90;

    /// <summary>
    /// Opaque down to the fade, then clear over the button strip. Immutable,
    /// so one instance can serve every message.
    /// </summary>
    private static readonly IBrush FadeMask = new ImmutableLinearGradientBrush(
        [
            new ImmutableGradientStop(0, Colors.Black),
            new ImmutableGradientStop((CollapsedHeight - ButtonStripHeight - FadeHeight) / CollapsedHeight, Colors.Black),
            new ImmutableGradientStop((CollapsedHeight - ButtonStripHeight) / CollapsedHeight, Colors.Transparent),
        ],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleMessageBody, bool>(nameof(IsExpanded));

    public static readonly DirectProperty<CollapsibleMessageBody, bool> ShowsExpandProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleMessageBody, bool>(nameof(ShowsExpand), o => o.ShowsExpand);

    public static readonly DirectProperty<CollapsibleMessageBody, bool> ShowsCollapseProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleMessageBody, bool>(nameof(ShowsCollapse), o => o.ShowsCollapse);

    private bool _showsExpand;
    private bool _showsCollapse;

    static CollapsibleMessageBody()
    {
        AffectsMeasure<CollapsibleMessageBody>(IsExpandedProperty);
    }

    /// <summary>True when the reader has opened the whole message.</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The message is cut right now: the "Afficher la suite" button belongs over its fade.</summary>
    public bool ShowsExpand
    {
        get => _showsExpand;
        private set => SetAndRaise(ShowsExpandProperty, ref _showsExpand, value);
    }

    /// <summary>The message is long enough to be cut and has been opened: the "Réduire" button belongs under it.</summary>
    public bool ShowsCollapse
    {
        get => _showsCollapse;
        private set => SetAndRaise(ShowsCollapseProperty, ref _showsCollapse, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = Child;
        if (child is null)
        {
            ShowsExpand = false;
            ShowsCollapse = false;
            return default;
        }

        // The full height first, whatever room is offered: that is what
        // decides whether the message is long.
        child.Measure(availableSize.WithHeight(double.PositiveInfinity));
        var full = child.DesiredSize;

        var isLong = full.Height > CollapsedHeight + MinHiddenHeight;
        var cut = isLong && !IsExpanded;
        ShowsExpand = cut;
        ShowsCollapse = isLong && IsExpanded;

        // The mask costs a drawing layer, so only the messages actually cut wear it.
        ClipToBounds = cut;
        OpacityMask = cut ? FadeMask : null;

        return cut ? full.WithHeight(CollapsedHeight) : full;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Laid out at its full height even when cut, so the text keeps the
        // same line breaks open or closed; the clip hides the rest.
        var child = Child;
        child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, child.DesiredSize.Height)));
        return finalSize;
    }
}
