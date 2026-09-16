using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public sealed partial class MessageItem : ObservableObject
{
    public required string Id { get; init; }
    public required string AuthorUserId { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorInitials { get; init; }
    public required string AvatarHex { get; init; }
    public required string TimeLabel { get; init; }
    public required long CreateAtMillis { get; init; }
    public required string Text { get; set; }
    public bool IsMine { get; init; }

    /// <summary>The thread this message belongs to — null for a top-level message (not itself a reply). Set on the main list's copy of a message so its "Répondre" button opens the actual thread, not a fresh one rooted on the reply itself.</summary>
    public string? ThreadRootId { get; init; }

    /// <summary>What "Répondre" opens: the thread this message already belongs to, or itself if it isn't a reply yet — either way, exactly what OpenThreadCommand expects.</summary>
    public string ReplyTargetId => ThreadRootId ?? Id;

    /// <summary>
    /// A lightweight citation of the message this one replies to — the
    /// thread root's author and a truncated excerpt, resolved synchronously
    /// from whatever's already loaded when this row was built (see
    /// MainViewModel.PopulateMessages). Deliberately never filled in later:
    /// this row lives in a virtualized list, and a property that changes
    /// this row's height after it's already been realized visibly jumps the
    /// whole conversation (the same reason inline message editing was moved
    /// into the composer). Null — and no quote shown — when this isn't a
    /// reply, or its root wasn't in the currently-loaded batch.
    /// </summary>
    public string? QuotedAuthorName { get; init; }
    public string? QuotedText { get; init; }
    public bool HasQuotedMessage => QuotedAuthorName is not null;

    /// <summary>The server's opengraph preview for this message's first link, if any — see LinkPreviewItem.</summary>
    public LinkPreviewItem? LinkPreview { get; init; }
    public bool HasLinkPreview => LinkPreview is not null;

    /// <summary>True once this message has been edited (Mattermost's own edit_at &gt; 0) — shown as a small "(modifié)" tag after the text.</summary>
    [ObservableProperty]
    public partial bool IsEdited { get; set; }

    /// <summary>
    /// True while this specific message is loaded into the composer for
    /// editing — purely a background tint (see RowBackground) so it's
    /// visually clear which message is being edited without changing this
    /// row's own size at all. Editing itself happens in the composer, not
    /// inline here: an inline edit box used to change this row's height,
    /// and with the message list virtualized, that made the whole
    /// conversation visibly jump/scroll the moment you clicked "modifier".
    /// </summary>
    [ObservableProperty]
    public partial bool IsBeingEdited { get; set; }

    /// <summary>Applies a server-confirmed edit — clears the cached Blocks so it re-parses the new text next time it's bound.</summary>
    public void ApplyEditedText(string newText)
    {
        Text = newText;
        _blocks = null;
        IsEdited = true;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Blocks));
    }

    /// <summary>True when this message follows one from the same author less than two minutes ago — the avatar/name header is skipped, official-client style, so a quick back-to-back exchange doesn't repeat it for every line.</summary>
    public bool IsContinuation { get; init; }

    /// <summary>Extra breathing room above a new sender's first message so it doesn't crowd the previous person's last line — a continuation (same sender, grouped) stays snug underneath it instead.</summary>
    public Thickness RowPadding => IsContinuation ? new Thickness(0, 4, 8, 4) : new Thickness(0, 10, 8, 4);

    /// <summary>
    /// The day this message was sent ("Aujourd'hui", "Hier", "mardi 3 juin"),
    /// drawn as a separator line above it — null for a message falling on the
    /// same local day as the one before it, so exactly one separator appears
    /// per date change. Decided once, when the row is built (see
    /// MainViewModel.DateSeparatorFor): whether a row carries a separator at
    /// all is what sets its height, and with the list virtualized, a height
    /// that changes under the reader visibly jumps the conversation. Only the
    /// wording is refreshed later, when the app is left open past midnight and
    /// yesterday's "Aujourd'hui" has to become "Hier".
    /// </summary>
    [ObservableProperty]
    public partial string? DateSeparatorLabel { get; set; }

    public bool HasDateSeparator => DateSeparatorLabel is not null;

    public ObservableCollection<ReactionItem> Reactions { get; init; } = [];
    public ObservableCollection<AttachmentItem> Attachments { get; init; } = [];
    public int ThreadReplyCount { get; init; }
    public bool HasThreadReplies => ThreadReplyCount > 0;
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasReactions => Reactions.Count > 0;

    /// <summary>Pinned to the channel — drives the marker in the header and the wording of the pin action.</summary>
    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    /// <summary>
    /// A pinned message always shows its header, even when it would
    /// otherwise be grouped under the one above it: the pin marker lives
    /// there, and a pinned message with nowhere to say so isn't much use.
    /// The official client does the same.
    /// </summary>
    public bool ShowHeader => !IsContinuation || IsPinned;

    public string PinActionLabel => IsPinned ? "Détacher du canal" : "Épingler au canal";

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHeader));
        OnPropertyChanged(nameof(PinActionLabel));
    }

    /// <summary>True for an outbox entry not yet confirmed by the server — queued while offline, or just fired off and still in flight.</summary>
    public bool IsPending { get; init; }

    /// <summary>Briefly true right after jumping here from a search result, so the message is easy to spot — cleared automatically a couple of seconds later.</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }

    /// <summary>
    /// Fills in the picture for a custom emoji someone typed into the text.
    /// Supplied by the ViewModel, which owns the name-to-id table and the
    /// image cache; null leaves such an emoji showing as its shortcode.
    /// </summary>
    public Func<CustomEmojiSegment, Task>? EmojiImageResolver { get; init; }

    private IReadOnlyList<MessageBlock>? _blocks;

    /// <summary>
    /// Parsed on first bind rather than at construction: with the list
    /// virtualized, that confines the work to the messages actually on
    /// screen. Fetching any custom emoji in the text rides along for the
    /// same reason — an emoji in a message nobody has scrolled to is never
    /// downloaded.
    /// </summary>
    public IReadOnlyList<MessageBlock> Blocks => _blocks ??= BuildBlocks();

    private IReadOnlyList<MessageBlock> BuildBlocks()
    {
        var blocks = MessageTextParser.ParseBlocks(Text);
        if (EmojiImageResolver is null)
        {
            return blocks;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case InlineBlock inline:
                    ResolveEmoji(inline.Segments);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row)
                        {
                            ResolveEmoji(cell);
                        }
                    }
                    break;
            }
        }
        return blocks;
    }

    private void ResolveEmoji(IReadOnlyList<MessageTextSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment is CustomEmojiSegment custom)
            {
                _ = EmojiImageResolver!(custom);
            }
        }
    }

    public IBrush AvatarBrush => ColorTokens.Solid(AvatarHex);

    /// <summary>The author's real profile picture, once fetched — null until then (or forever, on a fetch failure), so the colored-initials circle stays as the fallback rather than an empty gap.</summary>
    [ObservableProperty]
    public partial IBrush? AvatarImageBrush { get; set; }

    /// <summary>
    /// The author's presence, drawn as the small colored dot on the corner
    /// of their avatar — the same indicator the DM sidebar carries, so a
    /// person reads the same in a channel, in a thread and in a private
    /// conversation. Deliberately nullable: null means "nobody has asked
    /// the server about this person yet" and hides the dot entirely, rather
    /// than painting a grey "offline" that would be a guess. Filled in and
    /// kept fresh by MainViewModel, which owns the presence table.
    ///
    /// The dot is drawn inside the avatar's own fixed-size box, so it never
    /// changes a row's height — which, with the message list virtualized,
    /// is what would otherwise make the conversation jump under the reader.
    /// </summary>
    [ObservableProperty]
    public partial PresenceStatus? Presence { get; set; }

    public bool HasPresence => Presence is not null;

    public IBrush PresenceBrush => ColorTokens.Presence(Presence ?? PresenceStatus.Offline);

    /// <summary>The presence in words, for the profile card behind the avatar.</summary>
    public string PresenceLabel => PresenceText.Label(Presence ?? PresenceStatus.Offline);

    partial void OnPresenceChanged(PresenceStatus? value) => RefreshPresence();

    /// <summary>Repaints the dot — after a presence change, and after a theme change, since the presence palette is per-theme.</summary>
    public void RefreshPresence()
    {
        OnPropertyChanged(nameof(HasPresence));
        OnPropertyChanged(nameof(PresenceBrush));
        OnPropertyChanged(nameof(PresenceLabel));
    }

    /// <summary>
    /// Drives the row's "tinted" style class rather than a brush: the row
    /// also has a pointer-over tint, and a class lets the accent tint win
    /// over it (see the Border.message-row styles) instead of the two
    /// fighting for the same locally-bound Background. The brush itself is
    /// a DynamicResource, so it follows a theme change on its own.
    /// </summary>
    public bool IsTinted => IsHighlighted || IsBeingEdited;

    /// <summary>A pending bubble is dimmed slightly so it visibly reads as "not confirmed yet" without needing its own layout.</summary>
    public double BodyOpacity => IsPending ? 0.6 : 1.0;

    partial void OnIsHighlightedChanged(bool value) => OnPropertyChanged(nameof(IsTinted));
    partial void OnIsBeingEditedChanged(bool value) => OnPropertyChanged(nameof(IsTinted));

    /// <summary>Called after Reactions is cleared and rebuilt in place, so the reactions row's visibility follows.</summary>
    public void NotifyReactionsChanged() => OnPropertyChanged(nameof(HasReactions));
}
