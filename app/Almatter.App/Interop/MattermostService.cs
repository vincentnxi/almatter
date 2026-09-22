using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Almatter.App.Interop;

/// <summary>
/// The only door the rest of the app uses to reach a Mattermost server —
/// wraps the raw JSON-in/JSON-out <see cref="NativeCore.Call"/> bridge with
/// typed requests/responses and runs the (blocking) native call off the UI
/// thread.
/// </summary>
public sealed class MattermostService
{
    public async Task<(string Token, UserDto User)> LoginAsync(string baseUrl, string loginId, string password)
    {
        var request = new JsonObject
        {
            ["command"] = "login",
            ["base_url"] = baseUrl,
            ["login_id"] = loginId,
            ["password"] = password,
        };
        var data = await CallAsync<LoginData>(request);
        return (data.Token, data.User);
    }

    public async Task<List<TeamDto>> GetTeamsAsync(string baseUrl, string token)
    {
        var request = new JsonObject
        {
            ["command"] = "get_teams",
            ["base_url"] = baseUrl,
            ["token"] = token,
        };
        var data = await CallAsync<TeamsData>(request);
        return data.Teams;
    }

    public async Task<List<ChannelDto>> GetChannelsAsync(string baseUrl, string token, string teamId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_channels",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["team_id"] = teamId,
        };
        var data = await CallAsync<ChannelsData>(request);
        return data.Channels;
    }

    public async Task<List<PostDto>> GetPostsAsync(string baseUrl, string token, string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_posts",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
        };
        var data = await CallAsync<PostsData>(request);
        return data.Posts;
    }

    public async Task<List<UserDto>> GetUsersAsync(string baseUrl, string token, IEnumerable<string> userIds)
    {
        var request = new JsonObject
        {
            ["command"] = "get_users",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_ids"] = new JsonArray(userIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        };
        var data = await CallAsync<UsersData>(request);
        return data.Users;
    }

    /// <summary>Drives the composer's @mention autocomplete popup — users matching <paramref name="term"/> who are members of <paramref name="channelId"/>.</summary>
    public async Task<List<UserDto>> SearchUsersAsync(string baseUrl, string token, string channelId, string term)
    {
        var request = new JsonObject
        {
            ["command"] = "search_users",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
            ["term"] = term,
        };
        var data = await CallAsync<UsersData>(request);
        return data.Users;
    }

    /// <summary>Reads straight from the local SQLite cache — no network — for instant display and offline reading.</summary>
    public async Task<List<TeamDto>> GetCachedTeamsAsync()
    {
        var data = await CallAsync<TeamsData>(new JsonObject { ["command"] = "get_cached_teams" });
        return data.Teams;
    }

    public async Task<List<ChannelDto>> GetCachedChannelsAsync(string teamId)
    {
        var request = new JsonObject { ["command"] = "get_cached_channels", ["team_id"] = teamId };
        var data = await CallAsync<ChannelsData>(request);
        return data.Channels;
    }

    public async Task<List<PostDto>> GetCachedPostsAsync(string channelId)
    {
        var request = new JsonObject { ["command"] = "get_cached_posts", ["channel_id"] = channelId };
        var data = await CallAsync<PostsData>(request);
        return data.Posts;
    }

    public async Task<List<PostDto>> GetThreadAsync(string baseUrl, string token, string rootId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_thread",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["root_id"] = rootId,
        };
        var data = await CallAsync<PostsData>(request);
        return data.Posts;
    }

    public async Task<List<PostDto>> GetCachedThreadAsync(string rootId)
    {
        var request = new JsonObject { ["command"] = "get_cached_thread", ["root_id"] = rootId };
        var data = await CallAsync<PostsData>(request);
        return data.Posts;
    }

    /// <summary>Presence isn't cached — it's asked for fresh every time, since it's meaningless once stale.</summary>
    public async Task<List<UserStatusDto>> GetStatusesAsync(string baseUrl, string token, IEnumerable<string> userIds)
    {
        var request = new JsonObject
        {
            ["command"] = "get_statuses",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_ids"] = new JsonArray(userIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        };
        var data = await CallAsync<StatusesData>(request);
        return data.Statuses;
    }

    /// <summary>Sets this user's own presence — <paramref name="status"/> is one of Mattermost's own status strings ("online", "away", "dnd", "offline").</summary>
    public async Task SetStatusAsync(string baseUrl, string token, string userId, string status)
    {
        var request = new JsonObject
        {
            ["command"] = "set_status",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["status"] = status,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Starts (once per process) the background WebSocket connection that keeps the cache warm in real time, and watches for mentions of the given user.</summary>
    public async Task StartWebSocketAsync(string baseUrl, string token, string userId)
    {
        var request = new JsonObject
        {
            ["command"] = "start_web_socket",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Ends the live connection (if any) — call on logout so the old session's socket doesn't keep running under a now-stale token.</summary>
    public async Task StopWebSocketAsync()
    {
        await CallAsync<JsonObject>(new JsonObject { ["command"] = "stop_web_socket" });
    }

    /// <summary>
    /// Everything the WebSocket has seen worth notifying about since the
    /// last call — messages, and reactions to this user's own messages — as
    /// a drain, not a peek. Both come back together because the poll loop
    /// wants them on the same tick.
    /// </summary>
    public async Task<(List<MentionEventDto> Mentions, List<ReactionEventDto> Reactions)> GetAndClearNotificationEventsAsync()
    {
        var data = await CallAsync<MentionEventsData>(new JsonObject { ["command"] = "get_and_clear_mention_events" });
        return (data.Events, data.Reactions);
    }

    public async Task<List<UserDto>> GetCachedUsersAsync(IEnumerable<string> userIds)
    {
        var request = new JsonObject
        {
            ["command"] = "get_cached_users",
            ["user_ids"] = new JsonArray(userIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        };
        var data = await CallAsync<UsersData>(request);
        return data.Users;
    }

    /// <summary>The channel's pinned posts, from the server — this also reconciles the cache, since a post missing from the answer is one that was unpinned.</summary>
    public async Task<List<PostDto>> GetPinnedPostsAsync(string baseUrl, string token, string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_pinned_posts",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
        };
        return (await CallAsync<PostsData>(request)).Posts;
    }

    public async Task<List<PostDto>> GetCachedPinnedPostsAsync(string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_cached_pinned_posts",
            ["channel_id"] = channelId,
        };
        return (await CallAsync<PostsData>(request)).Posts;
    }

    /// <summary>Pins or unpins a post, returning the re-read post so the caller can update what it shows from the server's own answer rather than assuming.</summary>
    public async Task<PostDto> SetPostPinnedAsync(string baseUrl, string token, string postId, bool pinned)
    {
        var request = new JsonObject
        {
            ["command"] = pinned ? "pin_post" : "unpin_post",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["post_id"] = postId,
        };
        var data = await CallAsync<PostData>(request);
        return data.Post;
    }

    public async Task<PostDto> AddReactionAsync(string baseUrl, string token, string userId, string postId, string emojiName)
    {
        var request = new JsonObject
        {
            ["command"] = "add_reaction",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["post_id"] = postId,
            ["emoji_name"] = emojiName,
        };
        var data = await CallAsync<PostData>(request);
        return data.Post;
    }

    public async Task<PostDto> RemoveReactionAsync(string baseUrl, string token, string userId, string postId, string emojiName)
    {
        var request = new JsonObject
        {
            ["command"] = "remove_reaction",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["post_id"] = postId,
            ["emoji_name"] = emojiName,
        };
        var data = await CallAsync<PostData>(request);
        return data.Post;
    }

    public async Task<List<CustomEmojiDto>> GetCustomEmojiAsync(string baseUrl, string token)
    {
        var request = new JsonObject { ["command"] = "get_custom_emoji", ["base_url"] = baseUrl, ["token"] = token };
        var data = await CallAsync<CustomEmojiData>(request);
        return data.Emoji;
    }

    public async Task<List<CustomEmojiDto>> GetCachedCustomEmojiAsync()
    {
        var data = await CallAsync<CustomEmojiData>(new JsonObject { ["command"] = "get_cached_custom_emoji" });
        return data.Emoji;
    }

    /// <summary>Downloads (once — cached to disk after that) one custom emoji's image and returns its local file path.</summary>
    public async Task<string> GetEmojiImagePathAsync(string baseUrl, string token, string emojiId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_emoji_image",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["emoji_id"] = emojiId,
        };
        var data = await CallAsync<EmojiImageData>(request);
        return data.Path;
    }

    /// <summary>Downloads (once — cached to disk after that) one user's profile picture and returns its local file path. Mattermost always returns something here, even for a user with no custom avatar (a generated default).</summary>
    public async Task<string> GetUserAvatarPathAsync(string baseUrl, string token, string userId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_user_avatar",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
        };
        var data = await CallAsync<AvatarImageData>(request);
        return data.Path;
    }

    /// <summary>Downloads (once — cached to disk after that) a link preview's og:image and returns the local file path. No base_url/token: the image is on whatever external site the link points to, not this Mattermost server.</summary>
    public async Task<string> GetLinkPreviewImagePathAsync(string url)
    {
        var request = new JsonObject { ["command"] = "get_link_preview_image", ["url"] = url };
        var data = await CallAsync<LinkPreviewImageData>(request);
        return data.Path;
    }

    /// <summary>Downloads (once — cached to disk after that) one message attachment and returns its local file path.</summary>
    public async Task<string> GetFilePathAsync(string baseUrl, string token, string fileId, string fileName)
    {
        var request = new JsonObject
        {
            ["command"] = "get_file",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["file_id"] = fileId,
            ["file_name"] = fileName,
        };
        var data = await CallAsync<FileData>(request);
        return data.Path;
    }

    public async Task<List<string>> GetFavoritesAsync(string baseUrl, string token, string userId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_favorites",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
        };
        var data = await CallAsync<FavoritesData>(request);
        return data.ChannelIds;
    }

    public async Task<List<string>> GetCachedFavoritesAsync()
    {
        var data = await CallAsync<FavoritesData>(new JsonObject { ["command"] = "get_cached_favorites" });
        return data.ChannelIds;
    }

    /// <summary>Favorites live in the server's own preferences store (same one the official clients use), so this stays in sync across devices.</summary>
    public async Task SetFavoriteAsync(string baseUrl, string token, string userId, string channelId, bool isFavorite)
    {
        var request = new JsonObject
        {
            ["command"] = "set_favorite",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["channel_id"] = channelId,
            ["is_favorite"] = isFavorite,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Opens (or resolves the existing) 1:1 direct-message channel with <paramref name="otherUserId"/> — e.g. from a message's avatar popover.</summary>
    public async Task<ChannelDto> OpenDirectMessageAsync(string baseUrl, string token, string userId, string otherUserId)
    {
        var request = new JsonObject
        {
            ["command"] = "open_direct_message",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["other_user_id"] = otherUserId,
        };
        var data = await CallAsync<ChannelData>(request);
        return data.Channel;
    }

    /// <summary>Mutes/unmutes a channel server-side (notify_props.mark_unread) — same preference the official clients use.</summary>
    public async Task SetChannelMutedAsync(string baseUrl, string token, string userId, string channelId, bool muted)
    {
        var request = new JsonObject
        {
            ["command"] = "set_channel_muted",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["user_id"] = userId,
            ["channel_id"] = channelId,
            ["muted"] = muted,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Every public channel on the team, joined or not. Network-only: a discovery list, deliberately not served from the cache.</summary>
    public async Task<List<PublicChannelDto>> GetPublicChannelsAsync(string baseUrl, string token, string teamId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_public_channels",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["team_id"] = teamId,
        };
        var data = await CallAsync<PublicChannelsData>(request);
        return data.Channels;
    }

    /// <summary>
    /// A cheap "has anything changed?" probe, so the polling loop doesn't
    /// re-read (and re-marshal, and re-deserialize) an entire channel every
    /// couple of seconds just to discover that nothing moved. Measured on a
    /// 455-message channel: ~0.06 ms and a handful of bytes here, against
    /// ~2.3 ms and 150 KB for the full read.
    /// </summary>
    public async Task<(long, long, long, long, long, long)> GetChannelRevisionAsync(string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_channel_revision",
            ["channel_id"] = channelId,
        };
        return (await CallAsync<RevisionData>(request)).AsKey();
    }

    /// <summary>Same probe for the open thread panel.</summary>
    public async Task<(long, long, long, long, long, long)> GetThreadRevisionAsync(string rootId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_thread_revision",
            ["root_id"] = rootId,
        };
        return (await CallAsync<RevisionData>(request)).AsKey();
    }

    /// <summary>Users anywhere on the team — "start a conversation with anyone". An empty <paramref name="term"/> lists the team instead of searching.</summary>
    public async Task<List<UserDto>> SearchTeamUsersAsync(string baseUrl, string token, string teamId, string term)
    {
        var request = new JsonObject
        {
            ["command"] = "search_team_users",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["team_id"] = teamId,
            ["term"] = term,
        };
        var data = await CallAsync<UsersData>(request);
        return data.Users;
    }

    /// <summary>Joins a public channel. Safe to call for a channel already joined — the server just returns the existing membership.</summary>
    public async Task JoinChannelAsync(string baseUrl, string token, string channelId, string userId)
    {
        var request = new JsonObject
        {
            ["command"] = "join_channel",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
            ["user_id"] = userId,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Clears a channel's unread badge server-side (and in the local cache) — call whenever the user opens it.</summary>
    public async Task MarkChannelViewedAsync(string baseUrl, string token, string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "mark_channel_viewed",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>
    /// Tries to send immediately; if that fails for connectivity reasons
    /// the core queues it and this still returns normally (Queued=true,
    /// Post=null) rather than throwing — a genuine server-side rejection
    /// (permissions, validation) still throws MattermostServiceException.
    /// </summary>
    public async Task<(bool Queued, PostDto? Post)> SendMessageAsync(
        string baseUrl, string token, string channelId, string? rootId, string localId, string message,
        IEnumerable<string>? fileIds = null)
    {
        var request = new JsonObject
        {
            ["command"] = "send_message",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
            ["root_id"] = rootId,
            ["local_id"] = localId,
            ["message"] = message,
        };
        if (fileIds is not null)
        {
            request["file_ids"] = new JsonArray(fileIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }
        var data = await CallAsync<SendMessageData>(request);
        return (data.Queued, data.Post);
    }

    /// <summary>
    /// Uploads a local file to attach to a message that hasn't been sent
    /// yet — call this once per picked file, then pass the returned id(s)
    /// as <c>fileIds</c> to <see cref="SendMessageAsync"/>. Unlike sending
    /// a plain text message, this has no offline fallback: it either
    /// succeeds or throws.
    /// </summary>
    public async Task<FileDto> UploadFileAsync(string baseUrl, string token, string channelId, string filePath)
    {
        var request = new JsonObject
        {
            ["command"] = "upload_file",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
            ["file_path"] = filePath,
        };
        var data = await CallAsync<UploadFileData>(request);
        return data.File;
    }

    /// <summary>Edits an already-sent message's text — the server enforces who's actually allowed to.</summary>
    public async Task<PostDto> EditMessageAsync(string baseUrl, string token, string postId, string message)
    {
        var request = new JsonObject
        {
            ["command"] = "edit_message",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["post_id"] = postId,
            ["message"] = message,
        };
        var data = await CallAsync<PostData>(request);
        return data.Post;
    }

    /// <summary>Deletes an already-sent message — same permission note as <see cref="EditMessageAsync"/>.</summary>
    public async Task DeleteMessageAsync(string baseUrl, string token, string postId)
    {
        var request = new JsonObject
        {
            ["command"] = "delete_message",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["post_id"] = postId,
        };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Every message currently queued (any channel) — merged into the message list as pending bubbles.</summary>
    public async Task<List<OutboxItemDto>> GetCachedOutboxAsync()
    {
        var data = await CallAsync<OutboxData>(new JsonObject { ["command"] = "get_cached_outbox" });
        return data.Items;
    }

    /// <summary>Who's currently typing in a channel, per the WebSocket's own typing events — cache-only, meant to be polled every couple of seconds.</summary>
    public async Task<List<string>> GetTypingUsersAsync(string channelId)
    {
        var request = new JsonObject { ["command"] = "get_typing_users", ["channel_id"] = channelId };
        var data = await CallAsync<UserIdsData>(request);
        return data.UserIds;
    }

    /// <summary>The send-side counterpart to GetTypingUsersAsync — tells the server this user is typing. <paramref name="parentId"/> is the thread root when replying, empty for a plain channel message.</summary>
    public async Task SendTypingAsync(string channelId, string parentId = "")
    {
        var request = new JsonObject { ["command"] = "send_typing", ["channel_id"] = channelId, ["parent_id"] = parentId };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>Tells the server whether this user is at the keyboard — without it the server marks them away after five minutes, however much they use the app.</summary>
    public async Task SendActiveStatusAsync(bool isActive)
    {
        var request = new JsonObject { ["command"] = "send_active_status", ["is_active"] = isActive };
        await CallAsync<JsonObject>(request);
    }

    /// <summary>A group DM's participant list — resolves and caches it, since (unlike a 1:1 DM) a GM channel's name isn't parseable into user ids.</summary>
    public async Task<List<string>> GetChannelMembersAsync(string baseUrl, string token, string channelId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_channel_members",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
        };
        var data = await CallAsync<UserIdsData>(request);
        return data.UserIds;
    }

    public async Task<List<string>> GetCachedChannelMembersAsync(string channelId)
    {
        var request = new JsonObject { ["command"] = "get_cached_channel_members", ["channel_id"] = channelId };
        var data = await CallAsync<UserIdsData>(request);
        return data.UserIds;
    }

    /// <summary>Tries to replay every queued message; returns how many actually went through.</summary>
    public async Task<int> FlushOutboxAsync(string baseUrl, string token)
    {
        var request = new JsonObject { ["command"] = "flush_outbox", ["base_url"] = baseUrl, ["token"] = token };
        var data = await CallAsync<FlushOutboxData>(request);
        return data.Flushed;
    }

    /// <summary>Instant, local-only search — only finds what's already in the cache (recently viewed channels' recent history).</summary>
    public async Task<List<PostDto>> SearchCachedMessagesAsync(string query)
    {
        var request = new JsonObject { ["command"] = "search_cached_messages", ["query"] = query };
        var data = await CallAsync<SearchData>(request);
        return data.Posts;
    }

    /// <summary>The real search — the whole team, full history — warming the cache with whatever it finds.</summary>
    public async Task<List<PostDto>> SearchMessagesAsync(string baseUrl, string token, string teamId, string query)
    {
        var request = new JsonObject
        {
            ["command"] = "search_messages",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["team_id"] = teamId,
            ["query"] = query,
        };
        var data = await CallAsync<SearchData>(request);
        return data.Posts;
    }

    /// <summary>"Jump to this message" — the posts immediately around one specific post, for when it's far outside the channel's normal recent view (e.g. a search hit).</summary>
    public async Task<List<PostDto>> GetPostsAroundMessageAsync(string baseUrl, string token, string channelId, string postId)
    {
        var request = new JsonObject
        {
            ["command"] = "get_posts_around_message",
            ["base_url"] = baseUrl,
            ["token"] = token,
            ["channel_id"] = channelId,
            ["post_id"] = postId,
        };
        var data = await CallAsync<SearchData>(request);
        return data.Posts;
    }

    private static async Task<TData> CallAsync<TData>(JsonObject request)
    {
        var requestJson = request.ToJsonString();
        var responseJson = await Task.Run(() => NativeCore.Call(requestJson));

        var envelope = JsonSerializer.Deserialize<Envelope<TData>>(responseJson)
            ?? throw new MattermostServiceException("Empty response from the core.");

        if (!envelope.Ok || envelope.Data is null)
        {
            throw new MattermostServiceException(envelope.Error ?? "Unknown error from the core.");
        }

        return envelope.Data;
    }
}
