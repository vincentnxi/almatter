using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// A Markdown table: columns as wide as their widest cell, a tinted header
/// row, hairlines between cells. Scrolls sideways when it's wider than the
/// conversation rather than squeezing every column into unreadable slivers;
/// with no vertical scrolling of its own, the mouse wheel still scrolls the
/// conversation.
/// </summary>
public sealed class MessageTable : ScrollViewer
{
    public static readonly StyledProperty<TableBlock?> TableProperty =
        AvaloniaProperty.Register<MessageTable, TableBlock?>(nameof(Table));

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        MessageInlineText.LinkCommandProperty.AddOwner<MessageTable>();

    public static readonly StyledProperty<double> BaseFontSizeProperty =
        MessageInlineText.BaseFontSizeProperty.AddOwner<MessageTable>();

    public MessageTable()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    protected override System.Type StyleKeyOverride => typeof(ScrollViewer);

    public TableBlock? Table
    {
        get => GetValue(TableProperty);
        set => SetValue(TableProperty, value);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TableProperty)
        {
            Content = Table is null ? null : BuildGrid(Table);
        }
    }

    private Control BuildGrid(TableBlock table)
    {
        var grid = new Grid();
        var columns = table.Rows.Count > 0 ? table.Rows[0].Count : 0;
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < columns; c++)
            {
                var text = new MessageInlineText
                {
                    Segments = table.Rows[r][c],
                    TextWrapping = TextWrapping.NoWrap,
                    FontWeight = r == 0 ? FontWeight.Bold : FontWeight.Normal,
                };
                text.Bind(MessageInlineText.LinkCommandProperty, this.GetObservable(LinkCommandProperty));
                text.Bind(MessageInlineText.BaseFontSizeProperty, this.GetObservable(BaseFontSizeProperty));

                // Each cell draws its right and bottom edge; the outer border
                // supplies the top and left ones.
                var cell = new Border
                {
                    Padding = new Thickness(10, 5),
                    BorderThickness = new Thickness(0, 0, c < columns - 1 ? 1 : 0, r < table.Rows.Count - 1 ? 1 : 0),
                    Child = text,
                };
                cell.Bind(Border.BorderBrushProperty, cell.GetResourceObservable("DividerBrush"));
                if (r == 0)
                {
                    cell.Bind(Border.BackgroundProperty, cell.GetResourceObservable("BgPanelBrush"));
                }

                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        var outline = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = grid,
        };
        outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable("DividerBrush"));
        return outline;
    }
}
