using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public enum ChannelKind
{
    Public,
    Private,
}

public sealed partial class ChannelItem : ObservableObject, IChannelListItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ChannelKind Kind { get; init; } = ChannelKind.Public;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Bold name — set whenever there's any unread message, mention or not (matches the official client: bold for "something happened", a number badge only for mentions).</summary>
    [ObservableProperty]
    public partial bool HasUnread { get; set; }

    /// <summary>Unread mentions, including @channel/@all/@here — drives the accent-colored number badge.</summary>
    [ObservableProperty]
    public partial int MentionCount { get; set; }

    /// <summary>True while shown in the Favoris section instead of the regular Canaux list.</summary>
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool ShowDropIndicatorAbove { get; set; }

    [ObservableProperty]
    public partial bool ShowDropIndicatorBelow { get; set; }

    public bool HasMentions => MentionCount > 0;
    public bool IsPrivate => Kind == ChannelKind.Private;

    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;
    public IBrush NameBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextPrimary;
    public IBrush IconBrush => IsSelected ? ColorTokens.AccentInk : ColorTokens.TextSecondary;
    public FontWeight NameWeight => IsSelected || HasUnread ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>Called after the accent color changes so an already-built row repaints without needing to be reselected.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(IconBrush));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(IconBrush));
        OnPropertyChanged(nameof(NameWeight));
    }

    partial void OnHasUnreadChanged(bool value) => OnPropertyChanged(nameof(NameWeight));

    partial void OnMentionCountChanged(int value) => OnPropertyChanged(nameof(HasMentions));
}
