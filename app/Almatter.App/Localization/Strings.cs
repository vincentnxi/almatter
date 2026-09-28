using System;

namespace Almatter.App.Localization;

/// <summary>
/// Every piece of text the interface shows, for one language. The texts
/// themselves live in Strings.French.cs and Strings.English.cs; this file
/// only says which ones exist.
///
/// Each one is <c>required</c>, so a language file that forgets one does
/// not build — the error names the text that is missing. A plain text is a
/// <c>string</c>; one that has something slotted into it (a name, a count)
/// is a small function, so each language can put the pieces in its own
/// order and handle its own plurals.
/// </summary>
public sealed partial class Strings
{
    // ── Login window ─────────────────────────────────────────────────
    public required string LoginWindowTitle { get; init; }
    public required string LoginHeading { get; init; }
    public required string LoginServer { get; init; }
    public required string LoginServerPlaceholder { get; init; }
    public required string LoginId { get; init; }
    public required string LoginIdPlaceholder { get; init; }
    public required string LoginPassword { get; init; }
    public required string LoginButton { get; init; }
    public required string LoginBusy { get; init; }
    public required Func<string, string> LoginFailed { get; init; }

    // ── Sidebar ──────────────────────────────────────────────────────
    public required string Search { get; init; }
    public required string FavoritesHeader { get; init; }
    public required string FavoritesEmptyHint { get; init; }
    public required string ChannelsHeader { get; init; }
    public required string BrowseChannels { get; init; }
    public required string DirectMessagesHeader { get; init; }
    public required string NewConversation { get; init; }
    public required string ChangeStatus { get; init; }
    public required string ChannelMute { get; init; }
    public required string ChannelUnmute { get; init; }
    public required string ChannelRename { get; init; }
    public required string ChannelRestoreName { get; init; }
    public required string ChannelMutedTip { get; init; }

    /// <summary>Followed by the name — in a tooltip and in the rename dialog.</summary>
    public required string OriginalNameLabel { get; init; }

    // ── Presence ─────────────────────────────────────────────────────
    public required string StatusOnline { get; init; }
    public required string StatusAway { get; init; }
    public required string StatusDoNotDisturb { get; init; }
    public required string StatusOffline { get; init; }

    // ── Conversation ─────────────────────────────────────────────────
    public required string PinnedMessages { get; init; }
    public required string SendMessage { get; init; }
    public required Func<string, string> WriteInChannel { get; init; }
    public required string Sending { get; init; }
    public required string Pinned { get; init; }
    public required string Edited { get; init; }
    public required string Open { get; init; }
    public required string Downloading { get; init; }
    public required Func<int, string> ReplyCount { get; init; }
    public required string Edit { get; init; }
    public required string Delete { get; init; }
    public required string AddReaction { get; init; }
    public required string Reply { get; init; }
    public required string PinToChannel { get; init; }
    public required string UnpinFromChannel { get; init; }
    public required string JumpToLatest { get; init; }
    public required string EditingMessage { get; init; }
    public required string Cancel { get; init; }
    public required string Close { get; init; }
    public required string Save { get; init; }
    public required string AttachFile { get; init; }
    public required string AttachFilesDialogTitle { get; init; }
    public required string InsertEmoji { get; init; }
    public required string Formatting { get; init; }
    public required string Thread { get; init; }
    public required string ReplyToThreadPlaceholder { get; init; }
    public required string ConnectingToServer { get; init; }
    public required string LoadingTeamsAndChannels { get; init; }
    public required string CopyLink { get; init; }
    public required string DeleteMessageQuestion { get; init; }
    public required string Today { get; init; }
    public required string Yesterday { get; init; }

    /// <summary>.NET date patterns for a day separator — dddd is the weekday, MMMM the month.</summary>
    public required string DaySeparatorFormat { get; init; }
    public required string DaySeparatorFormatWithYear { get; init; }

    /// <summary>The date and time next to a search result, which can be months old.</summary>
    public required string SearchResultTimeFormat { get; init; }

    /// <summary>Bytes, kilobytes, megabytes, gigabytes.</summary>
    public required string[] FileSizeUnits { get; init; }

    public required Func<string, string> TypingOne { get; init; }
    public required Func<string, string, string> TypingTwo { get; init; }
    public required string TypingMany { get; init; }

    // ── Reactions ────────────────────────────────────────────────────
    /// <summary>Stands in for this user in a list of who reacted.</summary>
    public required string You { get; init; }

    /// <summary>Closes a capped list of names: "and 3 others".</summary>
    public required Func<int, string> OtherPeople { get; init; }

    /// <summary>The names joined into one phrase: "Alice, Bob and Carol".</summary>
    public required Func<string, string, string> JoinLastName { get; init; }

    /// <summary>(who, includes you, how many people, emoji name) → the reaction pill's tooltip.</summary>
    public required Func<string, bool, int, string, string> ReactedWith { get; init; }

    // ── Formatting toolbar ───────────────────────────────────────────
    public required string FormatBold { get; init; }
    public required string FormatItalic { get; init; }
    public required string FormatStrikethrough { get; init; }
    public required string FormatHeading { get; init; }
    public required string FormatQuote { get; init; }
    public required string FormatBulletList { get; init; }
    public required string FormatNumberedList { get; init; }
    public required string FormatCode { get; init; }
    public required string FormatCodeBlock { get; init; }
    public required string FormatLink { get; init; }
    public required string FormatRule { get; init; }
    public required string FormatTable { get; init; }

    /// <summary>Typed into the message itself, selected, ready to be typed over.</summary>
    public required string LinkTextPlaceholder { get; init; }
    public required Func<int, string> TableColumn { get; init; }

    // ── Panels ───────────────────────────────────────────────────────
    public required string SearchPersonPlaceholder { get; init; }
    public required string Searching { get; init; }
    public required string NoPersonMatches { get; init; }
    public required string FilterByNamePlaceholder { get; init; }
    public required string LoadingChannels { get; init; }
    public required string NoChannelsToJoin { get; init; }
    public required string Join { get; init; }
    public required string SearchMessagePlaceholder { get; init; }
    public required string NoResults { get; init; }
    public required string NoPinnedMessages { get; init; }
    public required string GoToMessage { get; init; }
    public required string RenameForMe { get; init; }
    public required string RenamePlaceholder { get; init; }
    public required string RenameOnlyYou { get; init; }

    // ── Emoji picker ─────────────────────────────────────────────────
    public required string ChooseReaction { get; init; }
    public required string SearchEmojiPlaceholder { get; init; }
    public required string SkinToneHeader { get; init; }
    public required string NoEmojiMatches { get; init; }
    public required string MostUsedEmoji { get; init; }
    public required string StandardEmoji { get; init; }
    public required string CustomEmoji { get; init; }
    public required string SkinToneDefault { get; init; }
    public required string SkinToneLight { get; init; }
    public required string SkinToneMediumLight { get; init; }
    public required string SkinToneMedium { get; init; }
    public required string SkinToneMediumDark { get; init; }
    public required string SkinToneDark { get; init; }

    // ── Settings ─────────────────────────────────────────────────────
    public required string SettingsTitle { get; init; }
    public required string SettingsLanguage { get; init; }
    public required string SettingsAppearance { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeLight { get; init; }
    public required string ThemeDark { get; init; }
    public required string ThemeSystemHint { get; init; }
    public required string ReducedContrast { get; init; }
    public required string ReducedContrastHint { get; init; }
    public required string SettingsFont { get; init; }
    public required string FontSystem { get; init; }
    public required string FontMattermostHint { get; init; }
    public required string SettingsMessageFontSize { get; init; }
    public required string FontSizeSmall { get; init; }
    public required string FontSizeNormal { get; init; }
    public required string FontSizeLarge { get; init; }
    public required string SettingsNotifications { get; init; }
    public required string SoundForAllMessages { get; init; }
    public required string SoundForAllMessagesHint { get; init; }
    public required string SettingsLinks { get; init; }
    public required string LinkPreviews { get; init; }
    public required string LogOut { get; init; }

    // ── Notifications and taskbar ────────────────────────────────────
    public required string Someone { get; init; }
    public required Func<string, string> ReactedToYourMessage { get; init; }
    public required Func<string, string, string> ReactedToQuote { get; init; }
    public required string TaskbarNewMessages { get; init; }
    public required Func<int, string> TaskbarNotifications { get; init; }

    // ── Errors ───────────────────────────────────────────────────────
    public required Func<string, string> UnexpectedError { get; init; }
    public required string UnknownTeam { get; init; }
    public required string NoTeamFound { get; init; }
    public required string NoChannelFound { get; init; }
    public required string UnknownChannel { get; init; }
    public required string UnknownUser { get; init; }
    public required string ErrorConnectionInterrupted { get; init; }
    public required Func<string, string> ErrorUnreadableResponse { get; init; }
}
