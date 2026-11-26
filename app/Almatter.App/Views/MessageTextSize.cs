using Almatter.App.Models;
using Avalonia;
using Avalonia.Data.Converters;

namespace Almatter.App.Views;

/// <summary>
/// The text size Markdown blocks are drawn at, inherited down the tree: the
/// window sets it to the user's message size, and a place that shows blocks
/// smaller (the channel header's bubble) sets its own. The block templates
/// in MainWindow's Window.DataTemplates read it from where they are drawn,
/// so one set of templates serves both.
///
/// It replaces scaling the bubble with a LayoutTransformControl, which,
/// inside the bubble's ScrollViewer, could lay the content out one line tall
/// after a reopening and hide everything but the first line.
/// </summary>
public static class MessageTextSize
{
    public static readonly AttachedProperty<double> ValueProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, double>(
            "Value", typeof(MessageTextSize), AppSettings.DefaultMessageFontSize, inherits: true);

    public static double GetValue(StyledElement element) => element.GetValue(ValueProperty);

    public static void SetValue(StyledElement element, double value) => element.SetValue(ValueProperty, value);

    /// <summary>
    /// A touch below the message size, following it: the channel header's
    /// bubble, 13 at the default 15.5. Never under 10, the smallest
    /// message size is already small.
    /// </summary>
    public static readonly IValueConverter Smaller =
        new FuncValueConverter<double, double>(size => System.Math.Max(10, size - 2.5));

    /// <summary>The line height that goes with a text size — for a list marker, which has to line up with its item's text.</summary>
    public static readonly IValueConverter ToLineHeight =
        new FuncValueConverter<double, double>(size => size * MessageInlineText.LineHeightRatio);
}
