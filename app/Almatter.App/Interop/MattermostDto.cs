using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Almatter.App.Interop;

// Field names mirror almatter-core's dispatch.rs response shapes exactly
// (snake_case, matching the Rust structs' serde output) — these are wire
// DTOs, not UI models.

public sealed class TeamDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
}

public sealed class ChannelDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("team_id")] public string TeamId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "O"; // O public, P private, D direct, G group
    [JsonPropertyName("total_msg_count")] public long TotalMsgCount { get; set; }
    [JsonPropertyName("last_post_at")] public long LastPostAt { get; set; }
    /// <summary>How many of TotalMsgCount this user has actually seen — merged in server-side from the channel-membership endpoint.</summary>
    [JsonPropertyName("msg_count")] public long MsgCount { get; set; }
    /// <summary>Unread messages that are actual mentions (including @channel/@all/@here) — same merge as MsgCount.</summary>
    [JsonPropertyName("mention_count")] public long MentionCount { get; set; }
    /// <summary>Server-side mute (notify_props.mark_unread == "mention") — same merge as MsgCount.</summary>
    [JsonPropertyName("is_muted")] public bool IsMuted { get; set; }

    public bool IsPublicOrPrivate => Type is "O" or "P";
    public bool IsPrivate => Type == "P";
}

public sealed class UserDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = "";
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; set; } = "";

    /// <summary>Mirrors Mattermost's own fallback order: nickname, then full name, then username.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(Nickname)) return Nickname;
            var full = $"{FirstName} {LastName}".Trim();
            return full.Length > 0 ? full : Username;
        }
    }

    public string Initials
    {
        get
        {
            var name = DisplayName;
            if (name.Length == 0) return "?";
            var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
                : name[..System.Math.Min(2, name.Length)].ToUpperInvariant();
        }
    }
}

public sealed class PostDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = "";
    [JsonPropertyName("root_id")] public string RootId { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("create_at")] public long CreateAt { get; set; }
    [JsonPropertyName("reply_count")] public long ReplyCount { get; set; }
    [JsonPropertyName("edit_at")] public long EditAt { get; set; }
    [JsonPropertyName("is_pinned")] public bool IsPinned { get; set; }
    [JsonPropertyName("metadata")] public PostMetadataDto Metadata { get; set; } = new();
}

public sealed class PostMetadataDto
{
    [JsonPropertyName("reactions")] public List<ReactionDto> Reactions { get; set; } = [];
    [JsonPropertyName("files")] public List<FileDto> Files { get; set; } = [];
    [JsonPropertyName("embeds")] public List<EmbedDto> Embeds { get; set; } = [];
}

public sealed class EmbedDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("data")] public OpenGraphDataDto? Data { get; set; }
}

public sealed class OpenGraphDataDto
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("site_name")] public string SiteName { get; set; } = "";
    [JsonPropertyName("images")] public List<OpenGraphImageDto> Images { get; set; } = [];
}

public sealed class OpenGraphImageDto
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("secure_url")] public string SecureUrl { get; set; } = "";
}

public sealed class ReactionDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("emoji_name")] public string EmojiName { get; set; } = "";
}

public sealed class FileDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

internal sealed class Envelope<TData>
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("data")] public TData? Data { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

internal sealed class LoginData
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("user")] public UserDto User { get; set; } = new();
}

internal sealed class TeamsData
{
    [JsonPropertyName("teams")] public List<TeamDto> Teams { get; set; } = [];
}

internal sealed class ChannelsData
{
    [JsonPropertyName("channels")] public List<ChannelDto> Channels { get; set; } = [];
}

/// <summary>A public channel on the team, joined or not — what the browse panel lists.</summary>
public sealed class PublicChannelDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("purpose")] public string Purpose { get; set; } = "";
    [JsonPropertyName("total_msg_count")] public long TotalMsgCount { get; set; }
}

/// <summary>
/// A cheap fingerprint of a channel's (or thread's) cached contents — what
/// the polling loop compares instead of re-reading every message. See
/// MattermostService.GetChannelRevisionAsync.
/// </summary>
internal sealed class RevisionData
{
    [JsonPropertyName("count")] public long Count { get; set; }
    [JsonPropertyName("last_create_at")] public long LastCreateAt { get; set; }
    [JsonPropertyName("last_edit_at")] public long LastEditAt { get; set; }

    /// <summary>Reaction count and the highest local write stamp. Posts don't change when someone reacts, so without these a live reaction would be cached and never shown.</summary>
    [JsonPropertyName("reactions")] public long Reactions { get; set; }
    [JsonPropertyName("last_reaction_seq")] public long LastReactionSeq { get; set; }

    /// <summary>How many posts are pinned. Its own term because pinning moves neither create_at nor edit_at — without it, a colleague pinning something would go unnoticed until the channel was reopened.</summary>
    [JsonPropertyName("pinned")] public long Pinned { get; set; }

    public (long, long, long, long, long, long) AsKey() =>
        (Count, LastCreateAt, LastEditAt, Reactions, LastReactionSeq, Pinned);
}

internal sealed class PublicChannelsData
{
    [JsonPropertyName("channels")] public List<PublicChannelDto> Channels { get; set; } = [];
}

internal sealed class PostsData
{
    [JsonPropertyName("posts")] public List<PostDto> Posts { get; set; } = [];
}

internal sealed class UsersData
{
    [JsonPropertyName("users")] public List<UserDto> Users { get; set; } = [];
}

public sealed class UserStatusDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "offline";
}

internal sealed class StatusesData
{
    [JsonPropertyName("statuses")] public List<UserStatusDto> Statuses { get; set; } = [];
}

internal sealed class UserIdsData
{
    [JsonPropertyName("user_ids")] public List<string> UserIds { get; set; } = [];
}

public sealed class CustomEmojiDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

internal sealed class CustomEmojiData
{
    [JsonPropertyName("emoji")] public List<CustomEmojiDto> Emoji { get; set; } = [];
}

internal sealed class PostData
{
    [JsonPropertyName("post")] public PostDto Post { get; set; } = new();
}

internal sealed class EmojiImageData
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

internal sealed class AvatarImageData
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

internal sealed class LinkPreviewImageData
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

internal sealed class ChannelData
{
    [JsonPropertyName("channel")] public ChannelDto Channel { get; set; } = new();
}

internal sealed class FileData
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

internal sealed class FavoritesData
{
    [JsonPropertyName("channel_ids")] public List<string> ChannelIds { get; set; } = [];
}

internal sealed class ChannelAliasesData
{
    /// <summary>Channel id → the name this user gave it.</summary>
    [JsonPropertyName("aliases")] public Dictionary<string, string> Aliases { get; set; } = [];
}

public sealed class OutboxItemDto
{
    [JsonPropertyName("local_id")] public string LocalId { get; set; } = "";
    [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = "";
    [JsonPropertyName("root_id")] public string? RootId { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("attempt_count")] public long AttemptCount { get; set; }
}

internal sealed class OutboxData
{
    [JsonPropertyName("items")] public List<OutboxItemDto> Items { get; set; } = [];
}

internal sealed class SendMessageData
{
    [JsonPropertyName("queued")] public bool Queued { get; set; }
    [JsonPropertyName("post")] public PostDto? Post { get; set; }
}

internal sealed class UploadFileData
{
    [JsonPropertyName("file")] public FileDto File { get; set; } = new();
}

internal sealed class FlushOutboxData
{
    [JsonPropertyName("flushed")] public int Flushed { get; set; }
}

internal sealed class SearchData
{
    [JsonPropertyName("posts")] public List<PostDto> Posts { get; set; } = [];
}

public sealed class MentionEventDto
{
    [JsonPropertyName("post_id")] public string PostId { get; set; } = "";
    [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = "";
    [JsonPropertyName("author_id")] public string AuthorId { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("is_mention")] public bool IsMention { get; set; }
}

/// <summary>Someone reacted to one of this user's own messages. <see cref="Message"/> is that message's text, not the reaction.</summary>
public sealed class ReactionEventDto
{
    [JsonPropertyName("post_id")] public string PostId { get; set; } = "";
    [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("emoji_name")] public string EmojiName { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
}

internal sealed class MentionEventsData
{
    [JsonPropertyName("events")] public List<MentionEventDto> Events { get; set; } = [];
    [JsonPropertyName("reactions")] public List<ReactionEventDto> Reactions { get; set; } = [];
}
