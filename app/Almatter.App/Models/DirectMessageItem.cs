using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public sealed partial class DirectMessageItem : ObservableObject, IChannelListItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Initials { get; init; }
    public required string AvatarHex { get; init; }

    /// <summary>True for a group DM (3+ participants) — no single presence dot to show, and the avatar falls back to a generic group glyph instead of one person's initials/photo.</summary>
    public bool IsGroup { get; init; }

    /// <summary>The person on the other end of a 1:1 conversation — empty for a group. Kept so presence can be refreshed in place.</summary>
    public string OtherUserId { get; init; } = "";

    [ObservableProperty]
    public partial PresenceStatus Presence { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Bold name — set whenever there's any unread message (matches the official client's "something happened" indicator).</summary>
    [ObservableProperty]
    public partial bool HasUnread { get; set; }

    /// <summary>True while shown in the Favoris section instead of the regular Messages privés list.</summary>
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool ShowDropIndicatorAbove { get; set; }

    [ObservableProperty]
    public partial bool ShowDropIndicatorBelow { get; set; }

    /// <summary>Unread mentions — drives the accent-colored number badge. The server counts every DM message as a mention (there's no @ needed in a 1:1), so this is effectively "unread DM count".</summary>
    [ObservableProperty]
    public partial int MentionCount { get; set; }

    public bool HasMentions => MentionCount > 0;

    [ObservableProperty]
    public partial IBrush? AvatarImageBrush { get; set; }

    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);
    public IBrush PresenceBrush => ColorTokens.Presence(Presence);
    public IBrush RowBackground => IsSelected ? ColorTokens.AccentSoft : Brushes.Transparent;

    /// <summary>Same three levels as a channel row — see ChannelItem.NameBrush for why read rows step back rather than unread ones shouting louder.</summary>
    public IBrush NameBrush => IsSelected
        ? ColorTokens.AccentInk
        : HasUnread ? ColorTokens.TextPrimary : ColorTokens.TextSecondary;

    public FontWeight NameWeight => IsSelected || HasUnread ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>Rarely seen on a 1:1, where the server counts every message as a mention — it's the group conversations this catches.</summary>
    public bool ShowUnreadDot => HasUnread && !HasMentions;

    /// <summary>Called after the accent color changes so an already-built row repaints without needing to be reselected.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(NameBrush));
    }

    partial void OnPresenceChanged(PresenceStatus value) => OnPropertyChanged(nameof(PresenceBrush));

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(NameWeight));
    }

    partial void OnHasUnreadChanged(bool value)
    {
        OnPropertyChanged(nameof(NameWeight));
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(ShowUnreadDot));
    }

    partial void OnMentionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMentions));
        OnPropertyChanged(nameof(ShowUnreadDot));
    }
}
