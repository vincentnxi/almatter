namespace Almatter.App.Models;

/// <summary>Common shape of ChannelItem and DirectMessageItem needed by the sidebar's drag-and-drop-to-favorite (and reorder-within-Favoris) handling, which doesn't otherwise care which one it's dragging.</summary>
public interface IChannelListItem
{
    string Id { get; }
    bool IsFavorite { get; }

    /// <summary>Drives the thin insertion-line shown just above/below this row while it's the current drop target of a Favoris reorder drag.</summary>
    bool ShowDropIndicatorAbove { get; set; }
    bool ShowDropIndicatorBelow { get; set; }

    /// <summary>Set whenever there's any unread message, mention or not — used (alongside MentionCount) to drive the taskbar overlay badge across every channel/DM at once.</summary>
    bool HasUnread { get; }

    /// <summary>Unread mentions (for a DM, effectively unread message count — see DirectMessageItem).</summary>
    int MentionCount { get; }

    /// <summary>Muted via the sidebar's right-click menu — still updates normally, just never raises a notification or contributes to the taskbar badge.</summary>
    bool IsMuted { get; set; }
}
