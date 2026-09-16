using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// One block of message text drawn as a single run of formatted text, so a
/// bold word or a link in the middle of a sentence wraps with the sentence.
///
/// This replaces a WrapPanel holding one control per segment, which could
/// only break between segments: with a long sentence either side of a bold
/// word, the word ended up alone on its own row. One control per block is
/// also fewer controls per visible message than that was.
///
/// Links are ordinary underlined text, found under the pointer by hit-testing
/// the laid-out text, which keeps them selectable along with the sentence.
/// </summary>
public sealed class MessageInlineText : SelectableTextBlock
{
    public static readonly StyledProperty<IReadOnlyList<MessageTextSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<MessageInlineText, IReadOnlyList<MessageTextSegment>?>(nameof(Segments));

    /// <summary>Run with the URL of a clicked link.</summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<MessageInlineText, ICommand?>(nameof(LinkCommand));

    /// <summary>The user's message text size; the drawn size is this times FontScale.</summary>
    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<MessageInlineText, double>(nameof(BaseFontSize), 14.5);

    public static readonly StyledProperty<double> FontScaleProperty =
        AvaloniaProperty.Register<MessageInlineText, double>(nameof(FontScale), 1.0);

    /// <summary>
    /// Code is drawn in the first of these the machine has. Cascadia Mono and
    /// Consolas ship with Windows, Menlo with macOS, DejaVu Sans Mono and
    /// Liberation Mono with most Linux desktops.
    /// </summary>
    public static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Courier New");

    /// <summary>Same ratio as MainViewModel.InlineEmojiSize.</summary>
    private const double EmojiScale = 1.35;

    private readonly List<LinkRange> _links = [];
    private readonly List<Image> _emojiImages = [];
    private readonly List<CustomEmojiSegment> _pendingEmoji = [];
    private IBrush? _accentBrush;
    private IBrush? _codeBackgroundBrush;
    private string? _pressedLink;
    private bool _showingHandCursor;

    public MessageInlineText()
    {
        TextWrapping = TextWrapping.Wrap;
        FontSize = BaseFontSize * FontScale;

        // Rebuilt when the theme swaps these, since a Run can't hold a
        // DynamicResource of its own.
        this.GetResourceObservable("AccentBrush").Subscribe(new ResourceObserver(value =>
        {
            if (!ReferenceEquals(value, _accentBrush))
            {
                _accentBrush = value as IBrush;
                Rebuild();
            }
        }));
        this.GetResourceObservable("BgPanelBrush").Subscribe(new ResourceObserver(value =>
        {
            if (!ReferenceEquals(value, _codeBackgroundBrush))
            {
                _codeBackgroundBrush = value as IBrush;
                Rebuild();
            }
        }));

        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
    }

    /// <summary>Picks up SelectableTextBlock's theme — selection colors, the I-beam cursor, the copy menu.</summary>
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public IReadOnlyList<MessageTextSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    public double FontScale
    {
        get => GetValue(FontScaleProperty);
        set => SetValue(FontScaleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SegmentsProperty)
        {
            Rebuild();
        }
        else if (change.Property == BaseFontSizeProperty || change.Property == FontScaleProperty)
        {
            FontSize = BaseFontSize * FontScale;
        }
        else if (change.Property == FontSizeProperty)
        {
            foreach (var image in _emojiImages)
            {
                image.Height = FontSize * EmojiScale;
            }
        }
    }

    private void Rebuild()
    {
        foreach (var pending in _pendingEmoji)
        {
            pending.PropertyChanged -= OnEmojiImageArrived;
        }
        _pendingEmoji.Clear();
        _links.Clear();
        _emojiImages.Clear();
        _pressedLink = null;

        var inlines = new InlineCollection();
        var position = 0;

        foreach (var segment in Segments ?? [])
        {
            if (segment is CustomEmojiSegment { Image: { } bitmap })
            {
                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    Height = FontSize * EmojiScale,
                    Margin = new Thickness(1, 0),
                };
                _emojiImages.Add(image);
                inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
                // An embedded control takes up one character position in the laid-out text.
                position += 1;
                continue;
            }

            if (segment is CustomEmojiSegment emoji)
            {
                // Shown as its shortcode until the picture lands, then redrawn.
                emoji.PropertyChanged += OnEmojiImageArrived;
                _pendingEmoji.Add(emoji);
            }

            var run = new Run(segment.Text);
            var decorations = new TextDecorationCollection();

            switch (segment)
            {
                case LinkSegment link:
                    run.Foreground = _accentBrush;
                    decorations.AddRange(Avalonia.Media.TextDecorations.Underline);
                    _links.Add(new LinkRange(position, position + segment.Text.Length, link.LinkUrl));
                    break;
                case MentionSegment:
                    run.Foreground = _accentBrush;
                    run.FontWeight = FontWeight.SemiBold;
                    break;
            }

            if (segment.Style.HasFlag(TextStyle.Bold))
            {
                run.FontWeight = FontWeight.Bold;
            }
            if (segment.Style.HasFlag(TextStyle.Italic))
            {
                run.FontStyle = FontStyle.Italic;
            }
            if (segment.Style.HasFlag(TextStyle.Strikethrough))
            {
                decorations.AddRange(Avalonia.Media.TextDecorations.Strikethrough);
            }
            if (segment.Style.HasFlag(TextStyle.Code))
            {
                run.FontFamily = MonospaceFont;
                run.Background = _codeBackgroundBrush;
            }
            if (decorations.Count > 0)
            {
                run.TextDecorations = decorations;
            }

            inlines.Add(run);
            position += segment.Text.Length;
        }

        Inlines = inlines;
    }

    /// <summary>
    /// The segments outlive this control (they belong to the message), so a
    /// control scrolled out of the list mustn't stay subscribed to them —
    /// that would keep it alive until some emoji finished downloading.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var pending in _pendingEmoji)
        {
            pending.PropertyChanged -= OnEmojiImageArrived;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_pendingEmoji.Exists(pending => pending.Image is not null))
        {
            // A picture arrived while this was off screen.
            Rebuild();
            return;
        }
        foreach (var pending in _pendingEmoji)
        {
            pending.PropertyChanged -= OnEmojiImageArrived;
            pending.PropertyChanged += OnEmojiImageArrived;
        }
    }

    private void OnEmojiImageArrived(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomEmojiSegment.Image))
        {
            Rebuild();
        }
    }

    private string? LinkAt(Point point)
    {
        if (_links.Count == 0)
        {
            return null;
        }

        // Checked against the boxes each link is drawn in, not with
        // HitTestPoint: its IsInside is false on every line after the first,
        // which left anything a paragraph had wrapped onto a later line —
        // including the end of a long URL — unclickable.
        var local = new Point(point.X - Padding.Left, point.Y - Padding.Top);
        foreach (var link in _links)
        {
            foreach (var box in TextLayout.HitTestTextRange(link.Start, link.End - link.Start))
            {
                if (box.Contains(local))
                {
                    return link.Url;
                }
            }
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        // A double click selects a word, even inside a link; only a single click follows it.
        _pressedLink = point.Properties.IsLeftButtonPressed && e.ClickCount == 1 ? LinkAt(point.Position) : null;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var url = _pressedLink;
        _pressedLink = null;
        if (url is null || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }
        // Dragging across a link to select it isn't a click on it.
        if (SelectionStart != SelectionEnd || LinkAt(e.GetPosition(this)) != url)
        {
            return;
        }
        if (LinkCommand?.CanExecute(url) == true)
        {
            LinkCommand.Execute(url);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHandCursor(LinkAt(e.GetPosition(this)) is not null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHandCursor(false);
    }

    private void SetHandCursor(bool hand)
    {
        if (hand == _showingHandCursor)
        {
            return;
        }
        _showingHandCursor = hand;
        if (hand)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            // Back to whatever the theme gives selectable text.
            ClearValue(CursorProperty);
        }
    }

    /// <summary>A right click on a link offers to copy it; anywhere else, the usual menu.</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!e.TryGetPosition(this, out var point) || LinkAt(point) is not { } url)
        {
            return;
        }

        e.Handled = true;
        var copy = new MenuItem { Header = "Copier le lien" };
        copy.Click += async (_, _) =>
        {
            // The window's clipboard rather than the menu's: the menu lives in
            // a popup, which is a separate top level that may not offer one.
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                return;
            }
            try
            {
                await clipboard.SetTextAsync(url);
            }
            catch
            {
                // Not worth an error banner just for a failed copy.
            }
        };
        var menu = new MenuFlyout();
        menu.Items.Add(copy);
        menu.ShowAt(this, showAtPointer: true);
    }

    private readonly record struct LinkRange(int Start, int End, string Url);

    private sealed class ResourceObserver(Action<object?> onNext) : IObserver<object?>
    {
        public void OnNext(object? value) => onNext(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
