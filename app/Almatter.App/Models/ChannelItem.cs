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

    /// <summary>
    /// Three levels, not two. A channel with something waiting is at full
    /// strength; one that's been read steps back to secondary. Weight alone
    /// was doing all the work before, which left every row the same colour
    /// and the same brightness — the eye had nothing to scan for. Most of
    /// the gain here comes from quietening what's already read rather than
    /// from shouting about what isn't.
    /// </summary>
    public IBrush NameBrush => IsSelected
        ? ColorTokens.AccentInk
        : HasUnread ? ColorTokens.TextPrimary : ColorTokens.TextSecondary;

    /// <summary>
    /// The icon rises with an unread rather than the read one dropping away.
    /// Tertiary measures 2,8:1 against the light sidebar, under the 3:1 that
    /// non-text needs to stay legible — so a read row keeps exactly the icon
    /// weight it always had, and only the unread ones gain.
    /// </summary>
    public IBrush IconBrush => IsSelected
        ? ColorTokens.AccentInk
        : HasUnread ? ColorTokens.TextPrimary : ColorTokens.TextSecondary;

    public FontWeight NameWeight => IsSelected || HasUnread ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>
    /// An accent dot for "something here, but nobody called your name". It
    /// takes the mention badge's slot, so a channel doesn't shift sideways
    /// when an ordinary unread turns into a mention.
    /// </summary>
    public bool ShowUnreadDot => HasUnread && !HasMentions;

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

    partial void OnHasUnreadChanged(bool value)
    {
        OnPropertyChanged(nameof(NameWeight));
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(IconBrush));
        OnPropertyChanged(nameof(ShowUnreadDot));
    }

    partial void OnMentionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMentions));
        OnPropertyChanged(nameof(ShowUnreadDot));
    }
}
