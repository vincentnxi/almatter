using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// The formatting buttons shown above a composer. Each writes Markdown into
/// its Target text box around the selection (see MarkdownFormatter).
///
/// The keyboard shortcuts are hooked up here too, so they work whether or
/// not the bar is showing — hiding it is about room, not about turning
/// formatting off.
/// </summary>
public partial class FormattingToolbar : UserControl
{
    private TextBox? _target;

    public FormattingToolbar()
    {
        InitializeComponent();
    }

    /// <summary>The composer this bar formats. Set once from the window's code-behind.</summary>
    public TextBox? Target
    {
        get => _target;
        set
        {
            _target?.RemoveHandler(KeyDownEvent, OnTargetKeyDown);
            _target = value;
            // Tunnel, like the composer's own Enter handling, so the text box
            // doesn't get to act on the key first.
            _target?.AddHandler(KeyDownEvent, OnTargetKeyDown, RoutingStrategies.Tunnel);
        }
    }

    public void Apply(FormatAction action)
    {
        if (_target is not { } box)
        {
            return;
        }

        var result = MarkdownFormatter.Apply(box.Text ?? "", box.SelectionStart, box.SelectionEnd, action);
        box.Text = result.Text;
        // The caret goes first: setting it collapses any selection.
        box.CaretIndex = result.SelectionEnd;
        box.SelectionStart = result.SelectionStart;
        box.SelectionEnd = result.SelectionEnd;
        box.Focus();
    }

    private void OnFormatClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<FormatAction>(tag, out var action))
        {
            Apply(action);
        }
    }

    /// <summary>The same shortcuts as the official client.</summary>
    private void OnTargetKeyDown(object? sender, KeyEventArgs e)
    {
        FormatAction? action = (e.Key, e.KeyModifiers) switch
        {
            (Key.B, KeyModifiers.Control) => FormatAction.Bold,
            (Key.I, KeyModifiers.Control) => FormatAction.Italic,
            (Key.X, KeyModifiers.Control | KeyModifiers.Shift) => FormatAction.Strikethrough,
            (Key.K, KeyModifiers.Control | KeyModifiers.Alt) => FormatAction.Link,
            _ => null,
        };
        if (action is { } chosen)
        {
            e.Handled = true;
            Apply(chosen);
        }
    }
}
