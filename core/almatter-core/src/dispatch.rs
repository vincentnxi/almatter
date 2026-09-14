//! JSON request/response dispatcher. This is the entire surface the FFI
//! layer needs to expose: one function in, one function out. Adding a new
//! Mattermost call means adding a `Request` variant here — safe Rust, fully
//! unit-testable with plain strings — rather than a new `extern "C"`
//! function in `almatter-ffi`.
//!
//! Takes the cache database as a parameter rather than reaching for a
//! process-global one, so it stays testable with a throwaway in-memory
//! database — the caller (almatter-ffi, for the real app) owns the one
//! long-lived instance.

use std::sync::Mutex;

use serde::Deserialize;
use serde_json::{json, Value};

use crate::api::{fetch_external_bytes, ApiError, MattermostClient};
use crate::db::Database;
use crate::sync::SyncEngine;

#[derive(Deserialize)]
#[serde(tag = "command", rename_all = "snake_case")]
enum Request {
    Login {
        base_url: String,
        login_id: String,
        password: String,
    },
    GetTeams {
        base_url: String,
        token: String,
    },
    GetChannels {
        base_url: String,
        token: String,
        team_id: String,
    },
    GetPosts {
        base_url: String,
        token: String,
        channel_id: String,
    },
    GetUsers {
        base_url: String,
        token: String,
        user_ids: Vec<String>,
    },
    /// Reads straight from the local cache — no network — for instant
    /// display and offline reading.
    GetCachedTeams,
    GetCachedChannels {
        team_id: String,
    },
    GetCachedPosts {
        channel_id: String,
    },
    GetCachedUsers {
        user_ids: Vec<String>,
    },
    GetThread {
        base_url: String,
        token: String,
        root_id: String,
    },
    GetCachedThread {
        root_id: String,
    },
    GetStatuses {
        base_url: String,
        token: String,
        user_ids: Vec<String>,
    },
    /// Sets the logged-in user's own presence — one of Mattermost's own
    /// status strings: "online", "away", "dnd", "offline".
    SetStatus {
        base_url: String,
        token: String,
        user_id: String,
        status: String,
    },
    /// Starts (once) the background WebSocket connection that keeps the
    /// cache warm with new messages in real time. `user_id` is who to watch
    /// for in each post's mentions, to queue desktop notifications.
    StartWebSocket {
        base_url: String,
        token: String,
        user_id: String,
    },
    /// Ends the live connection (if any) — called on logout so the old
    /// session's socket doesn't keep running under a now-stale token, and
    /// so a following StartWebSocket for whoever logs in next isn't a
    /// silent no-op.
    StopWebSocket,
    AddReaction {
        base_url: String,
        token: String,
        user_id: String,
        post_id: String,
        emoji_name: String,
    },
    RemoveReaction {
        base_url: String,
        token: String,
        user_id: String,
        post_id: String,
        emoji_name: String,
    },
    GetCustomEmoji {
        base_url: String,
        token: String,
    },
    /// The channel's pinned posts, straight from the server — also reconciles
    /// the cache's pinned flags against the answer.
    GetPinnedPosts {
        base_url: String,
        token: String,
        channel_id: String,
    },
    GetCachedPinnedPosts {
        channel_id: String,
    },
    PinPost {
        base_url: String,
        token: String,
        post_id: String,
    },
    UnpinPost {
        base_url: String,
        token: String,
        post_id: String,
    },
    GetCachedCustomEmoji,
    /// Downloads (once — cached to disk after that) one custom emoji's
    /// image and returns the local file path for the UI to load directly.
    GetEmojiImage {
        base_url: String,
        token: String,
        emoji_id: String,
    },
    /// Downloads (once — cached to disk after that) one user's profile
    /// picture and returns the local file path — same shape as
    /// GetEmojiImage, just a different endpoint.
    GetUserAvatar {
        base_url: String,
        token: String,
        user_id: String,
    },
    /// Downloads (once — cached to disk after that) a link preview's
    /// og:image and returns the local file path. Unlike GetEmojiImage/
    /// GetUserAvatar this doesn't need base_url/token: the image lives on
    /// whatever external site the link points to, not this Mattermost
    /// server, so the request must never carry its auth token.
    GetLinkPreviewImage {
        url: String,
    },
    /// Downloads (once — cached to disk after that) one message attachment
    /// and returns the local file path, so the UI can open it with the OS's
    /// own default handler for that file type.
    GetFile {
        base_url: String,
        token: String,
        file_id: String,
        file_name: String,
    },
    GetFavorites {
        base_url: String,
        token: String,
        user_id: String,
    },
    GetCachedFavorites,
    SetFavorite {
        base_url: String,
        token: String,
        user_id: String,
        channel_id: String,
        is_favorite: bool,
    },
    /// Opens (or resolves the existing) 1:1 direct-message channel with
    /// `other_user_id` — driven from a message's avatar popover ("Envoyer
    /// un message") rather than the DM sidebar. Warms the cache with the
    /// channel so it's immediately selectable.
    OpenDirectMessage {
        base_url: String,
        token: String,
        user_id: String,
        other_user_id: String,
    },
    /// Mutes/unmutes a channel — server-side, so it's reflected in the
    /// official clients too, not just this one.
    SetChannelMuted {
        base_url: String,
        token: String,
        user_id: String,
        channel_id: String,
        muted: bool,
    },
    /// Called whenever the user opens a channel — clears its unread badge
    /// server-side (and locally in the cache) rather than just hiding it in
    /// the UI, so it doesn't reappear on the next fetch.
    MarkChannelViewed {
        base_url: String,
        token: String,
        channel_id: String,
    },
    /// Sends a message — a reply if `root_id` is set. `local_id` is the
    /// caller's own id for this attempt (used to identify the queued entry
    /// if it has to go in the outbox, not sent to the server). A message
    /// with `file_ids` (already-uploaded attachments, see `UploadFile`
    /// below) never goes into the offline outbox on failure — there's no
    /// way to replay the attachment later, so it's a hard error instead.
    SendMessage {
        base_url: String,
        token: String,
        channel_id: String,
        root_id: Option<String>,
        local_id: String,
        message: String,
        #[serde(default)]
        file_ids: Vec<String>,
    },
    /// Uploads one file to attach to a message that hasn't been sent yet —
    /// requires an active connection (see `SendMessage`'s doc comment).
    /// `file_path` is a local path already on disk (the UI's own file
    /// picker result), read here rather than the caller sending raw bytes
    /// through JSON.
    UploadFile {
        base_url: String,
        token: String,
        channel_id: String,
        file_path: String,
    },
    /// Edits an already-sent message. Server-enforced permission (only the
    /// author, normally) — this doesn't check that itself.
    EditMessage {
        base_url: String,
        token: String,
        post_id: String,
        message: String,
    },
    /// Deletes an already-sent message. Same permission note as EditMessage.
    DeleteMessage {
        base_url: String,
        token: String,
        post_id: String,
    },
    /// Every message currently queued (any channel) — what the UI merges
    /// into the message list as pending bubbles.
    GetCachedOutbox,
    /// Tries to replay every queued message in order, stopping at the
    /// first connectivity failure (no point burning through the rest of
    /// the queue if the network is the problem) — called on a timer by the
    /// UI's existing poll loop rather than from a background task here.
    FlushOutbox {
        base_url: String,
        token: String,
    },
    /// Local full-text search over whatever's already cached — instant and
    /// works offline, but only covers channels this session has actually
    /// loaded. SearchMessages below is the real, complete search.
    SearchCachedMessages {
        query: String,
    },
    /// Searches the whole team on the server (full history, every channel
    /// this user can see) and warms the cache with whatever comes back.
    SearchMessages {
        base_url: String,
        token: String,
        team_id: String,
        query: String,
    },
    /// "Jump to this message" — a window of posts around one specific post,
    /// for when it's a search hit far outside the channel's normal
    /// most-recent-messages view.
    GetPostsAroundMessage {
        base_url: String,
        token: String,
        channel_id: String,
        post_id: String,
    },
    /// Every mention the WebSocket has queued (any channel) since the last
    /// time this was called — a drain, not a peek. Polled by the UI to
    /// raise desktop notifications.
    GetAndClearMentionEvents,
    /// Who's currently typing in a channel, per the WebSocket's `typing`
    /// events — cache-only, polled periodically by the UI. See
    /// `Database::typing_users_for_channel` for the expiry/TTL behavior.
    GetTypingUsers {
        channel_id: String,
    },
    /// Tells the server this user is typing — the send-side counterpart to
    /// GetTypingUsers. `parent_id` is the thread root when this is a reply,
    /// empty otherwise. No base_url/token: it just pushes onto the
    /// already-open WebSocket connection, not a fresh HTTP call.
    SendTyping {
        channel_id: String,
        #[serde(default)]
        parent_id: String,
    },
    /// A group DM's participant list — resolves and caches it, since unlike
    /// a 1:1 DM, a GM channel's `name` isn't parseable into user ids.
    GetChannelMembers {
        base_url: String,
        token: String,
        channel_id: String,
    },
    GetCachedChannelMembers {
        channel_id: String,
    },
    /// Drives the composer's @mention autocomplete popup — users matching
    /// `term` who are members of `channel_id`. Also warms the user cache
    /// with whatever comes back, same as GetUsers.
    SearchUsers {
        base_url: String,
        token: String,
        channel_id: String,
        term: String,
    },
    /// Every public channel on the team — the "browse channels" panel.
    /// Network-only: this is a discovery list, not part of the sidebar's
    /// cache-first path, and a stale copy would offer channels that no
    /// longer exist.
    GetPublicChannels {
        base_url: String,
        token: String,
        team_id: String,
    },
    JoinChannel {
        base_url: String,
        token: String,
        channel_id: String,
        user_id: String,
    },
    /// Cheap "has this channel changed?" probe for the UI's polling loop —
    /// a fingerprint, not the content. See Database::channel_revision.
    GetChannelRevision {
        channel_id: String,
    },
    GetThreadRevision {
        root_id: String,
    },
    /// Users anywhere on the team — the "start a conversation" panel. An
    /// empty `term` lists the team instead of searching. Warms the user
    /// cache with whatever comes back, same as SearchUsers.
    SearchTeamUsers {
        base_url: String,
        token: String,
        team_id: String,
        term: String,
    },
}

/// How many posts a channel keeps locally. The cache is a "make the app
/// feel instant and work offline" store, not an archive: the server remains
/// the history. Left unbounded, every cached read, poll tick and channel
/// switch grew a little more expensive forever.
const CHANNEL_HISTORY_LIMIT: i64 = 1_000;

pub async fn dispatch(request_json: &str, db: &'static Mutex<Database>) -> String {
    let response = handle(request_json, db).await;
    serde_json::to_string(&response)
        .unwrap_or_else(|_| r#"{"ok":false,"error":"internal serialization error"}"#.to_string())
}

async fn handle(request_json: &str, db: &'static Mutex<Database>) -> Value {
    let request: Request = match serde_json::from_str(request_json) {
        Ok(r) => r,
        Err(e) => return json!({ "ok": false, "error": format!("invalid request: {e}") }),
    };

    let result: Result<Value, String> = match request {
        Request::Login { base_url, login_id, password } => {
            MattermostClient::login(&base_url, &login_id, &password)
                .await
                .map(|(token, user)| {
                    cache_write(db, |cache| cache.upsert_user(&user));
                    json!({ "token": token, "user": user })
                })
                .map_err(|e| e.to_string())
        }
        Request::GetTeams { base_url, token } => MattermostClient::new(base_url)
            .with_token(token)
            .get_teams()
            .await
            .map(|teams| {
                cache_write(db, |cache| {
                    for team in &teams {
                        cache.upsert_team(team)?;
                    }
                    Ok(())
                });
                json!({ "teams": teams })
            })
            .map_err(|e| e.to_string()),
        Request::GetChannels { base_url, token, team_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            refresh_team_channels(&client, db, &team_id)
                .await
                .map(|channels| json!({ "channels": channels }))
                .map_err(|e| e.to_string())
        }
        Request::GetPosts { base_url, token, channel_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_posts(&channel_id)
            .await
            .map(|posts| {
                cache_write(db, |cache| {
                    for post in &posts {
                        cache.upsert_post(post)?;
                    }
                    // Opportunistic, and here rather than on every single
                    // insert: opening a channel is already the moment the
                    // user waits for its contents, and it is the only point
                    // where an unbounded backlog actually matters.
                    cache.prune_channel_posts(&channel_id, CHANNEL_HISTORY_LIMIT)?;
                    Ok(())
                });
                json!({ "posts": posts })
            })
            .map_err(|e| e.to_string()),
        Request::GetUsers { base_url, token, user_ids } => MattermostClient::new(base_url)
            .with_token(token)
            .get_users_by_ids(&user_ids)
            .await
            .map(|users| {
                cache_write(db, |cache| {
                    for user in &users {
                        cache.upsert_user(user)?;
                    }
                    Ok(())
                });
                json!({ "users": users })
            })
            .map_err(|e| e.to_string()),
        Request::GetCachedTeams => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_teams()
            .map(|teams| json!({ "teams": teams }))
            .map_err(|e| e.to_string()),
        Request::GetCachedChannels { team_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_channels_for_team(&team_id)
            .map(|channels| json!({ "channels": channels }))
            .map_err(|e| e.to_string()),
        Request::GetCachedPosts { channel_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_posts_for_channel(&channel_id)
            .map(|posts| json!({ "posts": posts }))
            .map_err(|e| e.to_string()),
        Request::GetCachedUsers { user_ids } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_users(&user_ids)
            .map(|users| json!({ "users": users }))
            .map_err(|e| e.to_string()),
        Request::StartWebSocket { base_url, token, user_id } => {
            crate::ws::start(base_url, token, user_id, db);
            Ok(json!({}))
        }
        Request::StopWebSocket => {
            crate::ws::stop();
            Ok(json!({}))
        }
        Request::GetThread { base_url, token, root_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_thread(&root_id)
            .await
            .map(|posts| {
                cache_write(db, |cache| {
                    for post in &posts {
                        cache.upsert_post(post)?;
                    }
                    Ok(())
                });
                json!({ "posts": posts })
            })
            .map_err(|e| e.to_string()),
        Request::GetCachedThread { root_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_thread_posts(&root_id)
            .map(|posts| json!({ "posts": posts }))
            .map_err(|e| e.to_string()),
        Request::GetStatuses { base_url, token, user_ids } => MattermostClient::new(base_url)
            .with_token(token)
            .get_statuses_by_ids(&user_ids)
            .await
            .map(|statuses| json!({ "statuses": statuses }))
            .map_err(|e| e.to_string()),
        Request::SetStatus { base_url, token, user_id, status } => MattermostClient::new(base_url)
            .with_token(token)
            .set_status(&user_id, &status)
            .await
            .map(|()| json!({}))
            .map_err(|e| e.to_string()),
        Request::AddReaction { base_url, token, user_id, post_id, emoji_name } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.add_reaction(&user_id, &post_id, &emoji_name).await {
                Ok(()) => refetch_post_into_cache(&client, db, &post_id).await,
                Err(e) => Err(e.to_string()),
            }
        }
        Request::RemoveReaction { base_url, token, user_id, post_id, emoji_name } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.remove_reaction(&user_id, &post_id, &emoji_name).await {
                Ok(()) => refetch_post_into_cache(&client, db, &post_id).await,
                Err(e) => Err(e.to_string()),
            }
        }
        Request::GetPinnedPosts { base_url, token, channel_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_pinned_posts(&channel_id)
            .await
            .map(|posts| {
                cache_write(db, |cache| {
                    // Clear before writing, inside the one transaction: the
                    // answer says what is pinned, so anything no longer in it
                    // has to lose its flag by omission.
                    cache.clear_pinned_for_channel(&channel_id)?;
                    for post in &posts {
                        cache.upsert_post(post)?;
                    }
                    Ok(())
                });
                json!({ "posts": posts })
            })
            .map_err(|e| e.to_string()),
        Request::GetCachedPinnedPosts { channel_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_pinned_posts(&channel_id)
            .map(|posts| json!({ "posts": posts }))
            .map_err(|e| e.to_string()),
        Request::PinPost { base_url, token, post_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.pin_post(&post_id).await {
                Ok(()) => refetch_post_into_cache(&client, db, &post_id).await,
                Err(e) => Err(e.to_string()),
            }
        }
        Request::UnpinPost { base_url, token, post_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.unpin_post(&post_id).await {
                Ok(()) => refetch_post_into_cache(&client, db, &post_id).await,
                Err(e) => Err(e.to_string()),
            }
        }
        Request::GetCustomEmoji { base_url, token } => MattermostClient::new(base_url)
            .with_token(token)
            .get_custom_emoji_list()
            .await
            .map(|emoji| {
                cache_write(db, |cache| {
                    for e in &emoji {
                        cache.upsert_custom_emoji(e)?;
                    }
                    Ok(())
                });
                json!({ "emoji": emoji })
            })
            .map_err(|e| e.to_string()),
        Request::GetCachedCustomEmoji => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_custom_emoji()
            .map(|emoji| json!({ "emoji": emoji }))
            .map_err(|e| e.to_string()),
        Request::MarkChannelViewed { base_url, token, channel_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.mark_channel_viewed(&channel_id).await {
                Ok(()) => {
                    cache_write(db, |cache| cache.mark_channel_read(&channel_id));
                    Ok(json!({}))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::SendMessage { base_url, token, channel_id, root_id, local_id, message, file_ids } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.create_post(&channel_id, &message, root_id.as_deref(), &file_ids).await {
                Ok(post) => {
                    cache_write(db, |cache| cache.upsert_post(&post));
                    Ok(json!({ "queued": false, "post": post }))
                }
                // The server is reachable but rejected the message outright
                // (permissions, validation, ...) — retrying later won't
                // help, so this surfaces as a real error instead of queuing.
                Err(e @ ApiError::Server { .. }) => Err(e.to_string()),
                // A message with attachments can't be queued for later replay
                // (see the SendMessage doc comment) — any failure is final.
                Err(e) if !file_ids.is_empty() => Err(e.to_string()),
                // Anything else (timeout, DNS failure, connection refused —
                // "we're offline or the server is unreachable") queues the
                // message for FlushOutbox to retry once connectivity returns.
                Err(_) => {
                    let created_at = chrono::Utc::now().timestamp_millis();
                    let cache = db.lock().expect("cache db mutex poisoned");
                    match SyncEngine::new(&cache).enqueue_outbox_message(
                        &local_id,
                        &channel_id,
                        root_id.as_deref(),
                        &message,
                        created_at,
                    ) {
                        Ok(()) => Ok(json!({ "queued": true, "local_id": local_id })),
                        Err(e) => Err(e.to_string()),
                    }
                }
            }
        }
        Request::UploadFile { base_url, token, channel_id, file_path } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match read_file_bytes(file_path.clone()).await {
                Ok(bytes) => {
                    let file_name = std::path::Path::new(&file_path)
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or(file_path);
                    client
                        .upload_file(&channel_id, &file_name, bytes)
                        .await
                        .map(|info| json!({ "file": info }))
                        .map_err(|e| e.to_string())
                }
                Err(e) => Err(e),
            }
        }
        Request::EditMessage { base_url, token, post_id, message } => {
            let client = MattermostClient::new(base_url).with_token(token);
            client
                .update_post(&post_id, &message)
                .await
                .map(|post| {
                    cache_write(db, |cache| cache.upsert_post(&post));
                    json!({ "post": post })
                })
                .map_err(|e| e.to_string())
        }
        Request::DeleteMessage { base_url, token, post_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.delete_post(&post_id).await {
                Ok(()) => {
                    cache_write(db, |cache| cache.delete_post(&post_id));
                    Ok(json!({}))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::GetCachedOutbox => {
            let cache = db.lock().expect("cache db mutex poisoned");
            SyncEngine::new(&cache)
                .pending_messages()
                .map(|items| json!({ "items": items }))
                .map_err(|e| e.to_string())
        }
        Request::FlushOutbox { base_url, token } => {
            let pending = {
                let cache = db.lock().expect("cache db mutex poisoned");
                SyncEngine::new(&cache).pending_messages()
            };
            match pending {
                Ok(pending) => {
                    let client = MattermostClient::new(base_url).with_token(token);
                    let mut flushed = 0;
                    for item in pending {
                        match client.create_post(&item.channel_id, &item.message, item.root_id.as_deref(), &[]).await {
                            Ok(post) => {
                                cache_write(db, |cache| cache.upsert_post(&post));
                                cache_write(db, |cache| SyncEngine::new(cache).remove_outbox_message(&item.local_id));
                                flushed += 1;
                            }
                            // The server permanently rejected this specific message
                            // (bad content, no permission, channel/thread gone) —
                            // drop it rather than retrying forever, but keep going:
                            // one bad message shouldn't block the rest of the queue.
                            Err(e) if e.is_permanent_rejection() => {
                                cache_write(db, |cache| SyncEngine::new(cache).remove_outbox_message(&item.local_id));
                            }
                            // Anything else (offline, an expired session, a rate
                            // limit, a transient server error) isn't this message's
                            // fault — stop here and give the whole queue another
                            // chance next tick instead of silently discarding
                            // messages the user actually typed.
                            Err(_) => break,
                        }
                    }
                    Ok(json!({ "flushed": flushed }))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::SearchCachedMessages { query } => db
            .lock()
            .expect("cache db mutex poisoned")
            .search_posts(&query, 50)
            .map(|posts| json!({ "posts": posts }))
            .map_err(|e| e.to_string()),
        Request::SearchMessages { base_url, token, team_id, query } => MattermostClient::new(base_url)
            .with_token(token)
            .search_posts(&team_id, &query)
            .await
            .map(|posts| {
                cache_write(db, |cache| {
                    for post in &posts {
                        cache.upsert_post(post)?;
                    }
                    Ok(())
                });
                json!({ "posts": posts })
            })
            .map_err(|e| e.to_string()),
        Request::GetPostsAroundMessage { base_url, token, channel_id, post_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_posts_around(&channel_id, &post_id)
            .await
            .map(|posts| {
                cache_write(db, |cache| {
                    for post in &posts {
                        cache.upsert_post(post)?;
                    }
                    Ok(())
                });
                json!({ "posts": posts })
            })
            .map_err(|e| e.to_string()),
        Request::GetAndClearMentionEvents => db
            .lock()
            .expect("cache db mutex poisoned")
            .drain_mention_events()
            .map(|events| json!({ "events": events }))
            .map_err(|e| e.to_string()),
        Request::SendTyping { channel_id, parent_id } => {
            crate::ws::send_typing(&channel_id, &parent_id);
            Ok(json!({}))
        }
        Request::GetTypingUsers { channel_id } => {
            // Matches how often Mattermost's own clients re-send a typing
            // event while you keep typing — long enough to bridge the gap
            // between two of them, short enough that the indicator doesn't
            // linger well after someone's actually stopped.
            const TYPING_TTL_MILLIS: i64 = 6000;
            let now = chrono::Utc::now().timestamp_millis();
            db.lock()
                .expect("cache db mutex poisoned")
                .typing_users_for_channel(&channel_id, now, TYPING_TTL_MILLIS)
                .map(|user_ids| json!({ "user_ids": user_ids }))
                .map_err(|e| e.to_string())
        }
        Request::GetEmojiImage { base_url, token, emoji_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            ensure_emoji_image_cached(&client, &emoji_id)
                .await
                .map(|path| json!({ "path": path }))
        }
        Request::GetUserAvatar { base_url, token, user_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            ensure_avatar_cached(&client, &user_id)
                .await
                .map(|path| json!({ "path": path }))
        }
        Request::GetLinkPreviewImage { url } => ensure_link_preview_image_cached(&url)
            .await
            .map(|path| json!({ "path": path })),
        Request::GetFile { base_url, token, file_id, file_name } => {
            let client = MattermostClient::new(base_url).with_token(token);
            ensure_file_cached(&client, &file_id, &file_name)
                .await
                .map(|path| json!({ "path": path }))
        }
        Request::GetFavorites { base_url, token, user_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_favorite_channel_ids(&user_id)
            .await
            .map(|ids| {
                cache_write(db, |cache| cache.replace_favorite_channels(&ids));
                json!({ "channel_ids": ids })
            })
            .map_err(|e| e.to_string()),
        Request::GetCachedFavorites => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_favorite_channel_ids()
            .map(|ids| json!({ "channel_ids": ids }))
            .map_err(|e| e.to_string()),
        Request::SetFavorite { base_url, token, user_id, channel_id, is_favorite } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.set_favorite_channel(&user_id, &channel_id, is_favorite).await {
                Ok(()) => {
                    cache_write(db, |cache| cache.set_favorite_channel(&channel_id, is_favorite));
                    Ok(json!({}))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::OpenDirectMessage { base_url, token, user_id, other_user_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.create_direct_channel(&user_id, &other_user_id).await {
                Ok(channel) => {
                    cache_write(db, |cache| cache.upsert_channel(&channel));
                    Ok(json!({ "channel": channel }))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::SetChannelMuted { base_url, token, user_id, channel_id, muted } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.set_channel_muted(&user_id, &channel_id, muted).await {
                Ok(()) => {
                    cache_write(db, |cache| cache.set_channel_muted(&channel_id, muted));
                    Ok(json!({}))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::GetChannelMembers { base_url, token, channel_id } => {
            let client = MattermostClient::new(base_url).with_token(token);
            match client.get_channel_members(&channel_id).await {
                Ok(members) => {
                    let user_ids: Vec<String> = members.into_iter().map(|m| m.user_id).collect();
                    cache_write(db, |cache| cache.replace_channel_participants(&channel_id, &user_ids));
                    Ok(json!({ "user_ids": user_ids }))
                }
                Err(e) => Err(e.to_string()),
            }
        }
        Request::GetCachedChannelMembers { channel_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_channel_participants(&channel_id)
            .map(|user_ids| json!({ "user_ids": user_ids }))
            .map_err(|e| e.to_string()),
        Request::SearchUsers { base_url, token, channel_id, term } => MattermostClient::new(base_url)
            .with_token(token)
            .search_users(&channel_id, &term)
            .await
            .map(|users| {
                cache_write(db, |cache| {
                    for user in &users {
                        cache.upsert_user(user)?;
                    }
                    Ok(())
                });
                json!({ "users": users })
            })
            .map_err(|e| e.to_string()),
        Request::GetPublicChannels { base_url, token, team_id } => MattermostClient::new(base_url)
            .with_token(token)
            .get_public_channels_for_team(&team_id)
            .await
            .map(|channels| json!({ "channels": channels }))
            .map_err(|e| e.to_string()),
        Request::JoinChannel { base_url, token, channel_id, user_id } => MattermostClient::new(base_url)
            .with_token(token)
            .join_channel(&channel_id, &user_id)
            .await
            .map(|()| json!({ "joined": true }))
            .map_err(|e| e.to_string()),
        Request::GetChannelRevision { channel_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .channel_revision(&channel_id)
            .map(|r| {
                json!({
                    "count": r.posts,
                    "last_create_at": r.last_create_at,
                    "last_edit_at": r.last_edit_at,
                    "reactions": r.reactions,
                    "last_reaction_seq": r.last_reaction_seq
                })
            })
            .map_err(|e| e.to_string()),
        Request::GetThreadRevision { root_id } => db
            .lock()
            .expect("cache db mutex poisoned")
            .thread_revision(&root_id)
            .map(|r| {
                json!({
                    "count": r.posts,
                    "last_create_at": r.last_create_at,
                    "last_edit_at": r.last_edit_at,
                    "reactions": r.reactions,
                    "last_reaction_seq": r.last_reaction_seq
                })
            })
            .map_err(|e| e.to_string()),
        Request::SearchTeamUsers { base_url, token, team_id, term } => MattermostClient::new(base_url)
            .with_token(token)
            .search_team_users(&team_id, &term)
            .await
            .map(|users| {
                cache_write(db, |cache| {
                    for user in &users {
                        cache.upsert_user(user)?;
                    }
                    Ok(())
                });
                json!({ "users": users })
            })
            .map_err(|e| e.to_string()),
    };

    match result {
        Ok(data) => json!({ "ok": true, "data": data }),
        Err(e) => json!({ "ok": false, "error": e }),
    }
}

/// Best-effort cache write: a failure here (disk full, odd permissions)
/// should never take down a successful network response.
fn cache_write(db: &Mutex<Database>, f: impl FnOnce(&Database) -> rusqlite::Result<()>) {
    let cache = db.lock().expect("cache db mutex poisoned");
    // One transaction for the whole write instead of one implicit,
    // separately-committed transaction per statement `f` issues (e.g. one
    // per post when caching a channel's worth of messages) — see
    // Database::in_transaction.
    if let Err(e) = cache.in_transaction(|| f(&cache)) {
        eprintln!("almatter-core: failed to update local cache: {e}");
    }
}

/// Fetches a team's channels together with this user's read state for each
/// one, and caches the result. Shared by `get_channels` and by the
/// WebSocket's reconnect, which uses it to catch up on channels that were
/// read on another device while the connection was down.
pub(crate) async fn refresh_team_channels(
    client: &MattermostClient,
    db: &Mutex<Database>,
    team_id: &str,
) -> Result<Vec<crate::models::Channel>, ApiError> {
    let mut channels = client.get_channels_for_team(team_id).await?;

    // Per-user read state comes from a separate endpoint — a failure here
    // must NOT fall through to caching these channels with their
    // freshly-fetched (and therefore defaulted 0/0/false)
    // msg_count/mention_count/is_muted: that would clobber whatever was
    // genuinely cached for them already, silently unmuting channels and
    // resetting unread/mention counts on a transient network hiccup.
    // Falling back to the previous cache is a safe-ish default for a channel
    // that's never been cached before too (0/0/false, "looks unread") — no
    // worse than not having the feature at all.
    let merged = if let Ok(members) = client.get_channel_members_for_team(team_id).await {
        let by_channel: std::collections::HashMap<String, (i64, i64, bool)> = members
            .into_iter()
            .map(|m| {
                let muted = m.is_muted();
                (m.channel_id, (m.msg_count, m.mention_count, muted))
            })
            .collect();
        for channel in &mut channels {
            if let Some(&(msg_count, mention_count, is_muted)) = by_channel.get(&channel.id) {
                channel.msg_count = msg_count;
                channel.mention_count = mention_count;
                channel.is_muted = is_muted;
            }
        }
        true
    } else {
        false
    };

    if !merged {
        let previously_cached: std::collections::HashMap<String, crate::models::Channel> = db
            .lock()
            .expect("cache db mutex poisoned")
            .cached_channels_for_team(team_id)
            .unwrap_or_default()
            .into_iter()
            .map(|c| (c.id.clone(), c))
            .collect();
        for channel in &mut channels {
            if let Some(prev) = previously_cached.get(&channel.id) {
                channel.msg_count = prev.msg_count;
                channel.mention_count = prev.mention_count;
                channel.is_muted = prev.is_muted;
            }
        }
    }

    cache_write(db, |cache| {
        for channel in &channels {
            cache.upsert_channel(channel)?;
        }
        Ok(())
    });
    Ok(channels)
}

/// After a reaction is added/removed, re-fetching the single post is simpler
/// than teaching the cache to patch just its reactions list in place — and
/// reuses `upsert_post`'s existing replace-in-full behavior for reactions.
async fn refetch_post_into_cache(
    client: &MattermostClient,
    db: &Mutex<Database>,
    post_id: &str,
) -> Result<Value, String> {
    let post = client.get_post(post_id).await.map_err(|e| e.to_string())?;
    cache_write(db, |cache| cache.upsert_post(&post));
    Ok(json!({ "post": post }))
}

/// Downloads one custom emoji's image only if it isn't already cached to
/// disk, returning the local file path either way.
///
/// A reaction resolves its emoji's image by calling this once per unique
/// emoji, and a channel full of history can resolve dozens of them within
/// milliseconds of each other (see MainViewModel.BuildReactions on the C#
/// side) — the filesystem checks below used to run as plain blocking calls
/// right inside this `async fn`, which quietly stalls whichever Tokio
/// worker thread picked up the task for as long as the disk I/O takes.
/// With only as many worker threads as CPU cores, a burst of those was
/// enough to starve the runtime and delay unrelated concurrent requests
/// (like the channel's own post fetch) by hundreds of milliseconds.
/// `spawn_blocking` moves this onto Tokio's separate, much larger blocking
/// thread pool instead, so it can't stall the async workers.
async fn ensure_emoji_image_cached(client: &MattermostClient, emoji_id: &str) -> Result<String, String> {
    let dir = crate::db::emoji_image_dir();
    let path = dir.join(emoji_id);
    let already_cached = check_cached(dir, path.clone()).await?;
    if !already_cached {
        let bytes = client.get_emoji_image(emoji_id).await.map_err(|e| e.to_string())?;
        write_atomically(path.clone(), bytes).await?;
    }
    Ok(path.to_string_lossy().to_string())
}

/// Same shape as `ensure_emoji_image_cached`, for a user's profile picture.
async fn ensure_avatar_cached(client: &MattermostClient, user_id: &str) -> Result<String, String> {
    let dir = crate::db::avatar_image_dir();
    let path = dir.join(user_id);
    let already_cached = check_cached(dir, path.clone()).await?;
    if !already_cached {
        let bytes = client.get_user_avatar(user_id).await.map_err(|e| e.to_string())?;
        write_atomically(path.clone(), bytes).await?;
    }
    Ok(path.to_string_lossy().to_string())
}

/// Same shape as `ensure_avatar_cached`, for a link preview's og:image —
/// fetched with `fetch_external_bytes` (no auth token, see its own doc
/// comment) since the target is some external site, not this Mattermost
/// server. `url` is hashed to a filesystem-safe filename since it's an
/// arbitrary external URL rather than a Mattermost id.
async fn ensure_link_preview_image_cached(url: &str) -> Result<String, String> {
    let dir = crate::db::link_preview_image_dir();
    let path = dir.join(hash_url(url));
    let already_cached = check_cached(dir, path.clone()).await?;
    if !already_cached {
        let bytes = fetch_external_bytes(url).await.map_err(|e| e.to_string())?;
        write_atomically(path.clone(), bytes).await?;
    }
    Ok(path.to_string_lossy().to_string())
}

fn hash_url(url: &str) -> String {
    use std::hash::{Hash, Hasher};
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    url.hash(&mut hasher);
    format!("{:x}", hasher.finish())
}

/// The blocking half of `ensure_*_cached`: makes sure `dir` exists and
/// reports whether `path` is already there — run on Tokio's blocking pool,
/// see `ensure_emoji_image_cached` for why.
async fn check_cached(dir: std::path::PathBuf, path: std::path::PathBuf) -> Result<bool, String> {
    tokio::task::spawn_blocking(move || -> Result<bool, String> {
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        Ok(path.exists())
    })
    .await
    .map_err(|e| e.to_string())?
}

/// Reads a local file the UI's own file picker chose, for `UploadFile` —
/// on Tokio's blocking pool rather than directly in an async fn, same
/// reasoning as `check_cached`/`write_atomically`.
async fn read_file_bytes(path: String) -> Result<Vec<u8>, String> {
    tokio::task::spawn_blocking(move || std::fs::read(path).map_err(|e| e.to_string()))
        .await
        .map_err(|e| e.to_string())?
}

/// Writes via a per-call temp file plus a rename into place, rather than a
/// direct write — the same emoji/attachment can legitimately be requested
/// twice at once (e.g. the same custom emoji reacted on several messages
/// resolves concurrently), and a plain `fs::write` racing with itself can
/// leave a truncated file at the final path that then fails to decode
/// forever, since a later call sees the (corrupt) file already "exists".
/// A rename is atomic, so whichever writer finishes last still leaves a
/// complete, valid file behind. Run on Tokio's blocking pool — see
/// `ensure_emoji_image_cached` for why.
async fn write_atomically(path: std::path::PathBuf, bytes: Vec<u8>) -> Result<(), String> {
    tokio::task::spawn_blocking(move || -> Result<(), String> {
        static COUNTER: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        // Process id alone isn't enough — the concurrent calls this guards
        // against are two async tasks in the *same* process.
        let n = COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let tmp_path = path.with_extension(format!("tmp-{}-{n}", std::process::id()));
        std::fs::write(&tmp_path, &bytes).map_err(|e| e.to_string())?;
        std::fs::rename(&tmp_path, &path).map_err(|e| e.to_string())
    })
    .await
    .map_err(|e| e.to_string())?
}

/// Downloads one attachment only if it isn't already cached to disk,
/// returning the local file path either way — kept under a per-file-id
/// subfolder so the original filename (extension included) is preserved,
/// which is what lets the OS pick the right app to open it with.
/// `file_name` is server-supplied (a Mattermost `FileInfo.name`, set by
/// whoever uploaded the file — any user on the server) and must never be
/// trusted as a literal path segment: a crafted name like "../../evil.exe"
/// would otherwise let a download land outside the intended per-file-id
/// folder, and the app hands this same path to the OS's default handler
/// once downloaded. `Path::file_name()` keeps only the last component, so
/// that traversal collapses to "evil.exe" instead of escaping. Falls back
/// to `fallback` (the file id, at the call site) for a name that's empty
/// or made entirely of "." / ".." segments (`file_name()` returns `None`
/// for those).
fn sanitize_file_name(file_name: &str, fallback: &str) -> String {
    std::path::Path::new(file_name)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .filter(|n| !n.is_empty())
        .unwrap_or_else(|| fallback.to_string())
}

async fn ensure_file_cached(
    client: &MattermostClient,
    file_id: &str,
    file_name: &str,
) -> Result<String, String> {
    let dir = crate::db::downloads_dir().join(file_id);
    let path = dir.join(sanitize_file_name(file_name, file_id));
    let already_cached = check_cached(dir, path.clone()).await?;
    if !already_cached {
        let bytes = client.get_file_bytes(file_id).await.map_err(|e| e.to_string())?;
        write_atomically(path.clone(), bytes).await?;
    }
    Ok(path.to_string_lossy().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanitize_file_name_strips_path_traversal() {
        // "/" is recognized as a separator by Rust's Path on every target
        // this app builds for (Windows accepts it as an alt separator too),
        // so this holds regardless of platform.
        assert_eq!(sanitize_file_name("../../evil.exe", "f1"), "evil.exe");
        assert_eq!(sanitize_file_name("a/b/../../evil.exe", "f1"), "evil.exe");
        assert_eq!(sanitize_file_name("diagram.png", "f1"), "diagram.png");
        assert_eq!(sanitize_file_name("", "f1"), "f1");
        assert_eq!(sanitize_file_name("..", "f1"), "f1");
        assert_eq!(sanitize_file_name("../..", "f1"), "f1");
    }

    /// Leaked on purpose: `dispatch` requires `'static` (real production
    /// callers always pass the one process-wide database), and tests are
    /// short-lived processes where leaking a small in-memory db is fine.
    fn test_db() -> &'static Mutex<Database> {
        Box::leak(Box::new(Mutex::new(Database::open_in_memory().expect("in-memory db should open"))))
    }

    #[tokio::test]
    async fn rejects_malformed_request() {
        let db = test_db();
        let response = dispatch("not json", db).await;
        assert!(response.contains(r#""ok":false"#));
    }

    #[tokio::test]
    async fn rejects_unreachable_server_with_an_ok_false_response_not_a_panic() {
        let db = test_db();
        let response = dispatch(
            r#"{"command":"get_teams","base_url":"http://127.0.0.1:1","token":"x"}"#,
            db,
        )
        .await;
        assert!(response.contains(r#""ok":false"#));
    }

    /// The two browse-and-join commands reach a handler at all — i.e. their
    /// serde tag really is the snake_case spelling the C# side sends. A
    /// wrong tag would surface as "unknown variant", which is
    /// indistinguishable from a network failure once it's an ok:false.
    #[tokio::test]
    async fn browse_and_join_commands_are_routed_not_rejected_as_unknown_variants() {
        let db = test_db();
        for payload in [
            r#"{"command":"get_public_channels","base_url":"http://127.0.0.1:1","token":"x","team_id":"t1"}"#,
            r#"{"command":"join_channel","base_url":"http://127.0.0.1:1","token":"x","channel_id":"c1","user_id":"u1"}"#,
            r#"{"command":"search_team_users","base_url":"http://127.0.0.1:1","token":"x","team_id":"t1","term":""}"#,
        ] {
            let response = dispatch(payload, db).await;
            assert!(response.contains(r#""ok":false"#), "expected a failed call, got {response}");
            assert!(
                !response.contains("unknown variant"),
                "command was not routed to a handler: {response}"
            );
        }
    }

    /// A message sent while unreachable queues instead of erroring, and
    /// shows up in the cached outbox — the whole point of the feature.
    #[tokio::test]
    async fn sending_a_message_while_offline_queues_it_instead_of_failing() {
        let db = test_db();
        let response = dispatch(
            r#"{"command":"send_message","base_url":"http://127.0.0.1:1","token":"x",
                "channel_id":"c1","root_id":null,"local_id":"local-1","message":"hello"}"#,
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains(r#""queued":true"#));

        let outbox = dispatch(r#"{"command":"get_cached_outbox"}"#, db).await;
        assert!(outbox.contains("local-1"));
        assert!(outbox.contains("hello"));
    }

    #[tokio::test]
    async fn flush_outbox_drops_a_permanently_rejected_message() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let db = test_db();
        dispatch(
            r#"{"command":"send_message","base_url":"http://127.0.0.1:1","token":"x",
                "channel_id":"c1","root_id":null,"local_id":"local-1","message":"hello"}"#,
            db,
        )
        .await;

        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/posts"))
            .respond_with(ResponseTemplate::new(400).set_body_json(serde_json::json!({ "message": "invalid" })))
            .mount(&server)
            .await;

        let response = dispatch(
            &format!(r#"{{"command":"flush_outbox","base_url":"{}","token":"x"}}"#, server.uri()),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));

        let outbox = dispatch(r#"{"command":"get_cached_outbox"}"#, db).await;
        assert!(!outbox.contains("local-1"), "a permanently-rejected message should be dropped: {outbox}");
    }

    #[tokio::test]
    async fn flush_outbox_keeps_a_message_queued_after_a_transient_server_error() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let db = test_db();
        dispatch(
            r#"{"command":"send_message","base_url":"http://127.0.0.1:1","token":"x",
                "channel_id":"c1","root_id":null,"local_id":"local-1","message":"hello"}"#,
            db,
        )
        .await;

        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/posts"))
            .respond_with(ResponseTemplate::new(503).set_body_json(serde_json::json!({ "message": "unavailable" })))
            .mount(&server)
            .await;

        let response = dispatch(
            &format!(r#"{{"command":"flush_outbox","base_url":"{}","token":"x"}}"#, server.uri()),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));

        let outbox = dispatch(r#"{"command":"get_cached_outbox"}"#, db).await;
        assert!(outbox.contains("local-1"), "a transient failure must not drop the queued message: {outbox}");
    }

    /// Unlike a plain text message, one with attachments has nothing to
    /// replay later if the server is unreachable — the file was only ever
    /// uploaded, never stored locally — so this must fail outright instead
    /// of silently landing in the offline outbox.
    #[tokio::test]
    async fn sending_a_message_with_attachments_while_offline_fails_instead_of_queuing() {
        let db = test_db();
        let response = dispatch(
            r#"{"command":"send_message","base_url":"http://127.0.0.1:1","token":"x",
                "channel_id":"c1","root_id":null,"local_id":"local-1","message":"hello",
                "file_ids":["f1"]}"#,
            db,
        )
        .await;
        assert!(response.contains(r#""ok":false"#));

        let outbox = dispatch(r#"{"command":"get_cached_outbox"}"#, db).await;
        assert!(!outbox.contains("local-1"));
    }

    #[tokio::test]
    async fn search_cached_messages_finds_a_word_already_in_the_cache() {
        use crate::models::Post;

        let db = test_db();
        db.lock().unwrap().upsert_post(&Post {
            id: "p1".into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "let's grab coffee tomorrow".into(),
            create_at: 1000,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: Default::default(),
        }).unwrap();

        let response = dispatch(r#"{"command":"search_cached_messages","query":"coffee"}"#, db).await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("p1"));
    }

    #[tokio::test]
    async fn cached_reads_start_out_empty_but_dont_error() {
        let db = test_db();
        let response = dispatch(r#"{"command":"get_cached_teams"}"#, db).await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains(r#""teams":[]"#));
    }

    /// The whole point of the cache: a network fetch persists what it saw,
    /// and a later *offline* read (no server involved at all) sees it too.
    #[tokio::test]
    async fn a_network_fetch_warms_the_cache_for_a_later_offline_read() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "t1", "name": "acme", "display_name": "Acme Corp", "type": "O" }
            ])))
            .mount(&server)
            .await;

        let db = test_db();

        let network_response = dispatch(
            &format!(r#"{{"command":"get_teams","base_url":"{}","token":"tok"}}"#, server.uri()),
            db,
        )
        .await;
        assert!(network_response.contains(r#""ok":true"#));
        assert!(network_response.contains("Acme Corp"));

        drop(server); // the offline read below must not need the network at all

        let cached_response = dispatch(r#"{"command":"get_cached_teams"}"#, db).await;
        assert!(cached_response.contains(r#""ok":true"#));
        assert!(cached_response.contains("Acme Corp"));
    }

    /// A channel's msg_count/mention_count/is_muted come from a *second*
    /// endpoint (get_channel_members_for_team), merged onto the channel
    /// list fetched from the first. If that second call fails, the
    /// channels must keep whatever was already cached for them rather than
    /// being overwritten with the freshly-fetched (and therefore defaulted
    /// 0/0/false) values.
    #[tokio::test]
    async fn get_channels_preserves_cached_mute_and_mention_state_when_the_member_fetch_fails() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let db = test_db();
        let channel_body = serde_json::json!([
            { "id": "c1", "team_id": "t1", "name": "general", "display_name": "General", "type": "O" }
        ]);

        // First fetch: both endpoints succeed, establishing a cached muted
        // channel with a real mention count.
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels"))
            .respond_with(ResponseTemplate::new(200).set_body_json(&channel_body))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels/members"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "channel_id": "c1", "user_id": "u1", "msg_count": 5, "mention_count": 2,
                  "notify_props": { "mark_unread": "mention" } }
            ])))
            .mount(&server)
            .await;

        let response = dispatch(
            &format!(r#"{{"command":"get_channels","base_url":"{}","token":"tok","team_id":"t1"}}"#, server.uri()),
            db,
        )
        .await;
        assert!(response.contains(r#""is_muted":true"#), "expected muted after first fetch: {response}");
        assert!(response.contains(r#""mention_count":2"#));

        // Second fetch: the channel list succeeds again, but the member
        // (read-state) endpoint now fails — the channel must stay muted
        // with its mention count intact, not reset to 0/false.
        let server2 = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels"))
            .respond_with(ResponseTemplate::new(200).set_body_json(&channel_body))
            .mount(&server2)
            .await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels/members"))
            .respond_with(ResponseTemplate::new(500))
            .mount(&server2)
            .await;

        let response2 = dispatch(
            &format!(r#"{{"command":"get_channels","base_url":"{}","token":"tok","team_id":"t1"}}"#, server2.uri()),
            db,
        )
        .await;
        assert!(response2.contains(r#""ok":true"#));
        assert!(response2.contains(r#""is_muted":true"#), "mute state must survive a failed member fetch: {response2}");
        assert!(response2.contains(r#""mention_count":2"#), "mention count must survive a failed member fetch: {response2}");
    }

    /// Adding a reaction re-fetches the post so the response (and the
    /// cache) immediately reflect the new reaction — not just a bare "ok".
    #[tokio::test]
    async fn adding_a_reaction_refetches_the_post_and_warms_the_cache() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/reactions"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "user_id": "u1", "post_id": "p1", "emoji_name": "+1"
            })))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "id": "p1", "channel_id": "c1", "user_id": "u2", "message": "hi", "create_at": 1000,
                "metadata": { "reactions": [{ "user_id": "u1", "emoji_name": "+1" }] }
            })))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"add_reaction","base_url":"{}","token":"tok","user_id":"u1","post_id":"p1","emoji_name":"+1"}}"#,
                server.uri()
            ),
            db,
        )
        .await;

        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("+1"));

        let cached = db.lock().unwrap().cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].metadata.reactions.len(), 1);
    }

    #[tokio::test]
    async fn send_typing_is_ok_even_with_no_active_websocket() {
        let db = test_db();
        let response = dispatch(r#"{"command":"send_typing","channel_id":"c1"}"#, db).await;
        assert!(response.contains(r#""ok":true"#));
    }

    #[tokio::test]
    async fn open_direct_message_creates_and_caches_the_channel() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/channels/direct"))
            .respond_with(ResponseTemplate::new(201).set_body_json(serde_json::json!({
                "id": "dm1", "team_id": "", "name": "u1__u2", "display_name": "", "type": "D"
            })))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"open_direct_message","base_url":"{}","token":"tok","user_id":"u1","other_user_id":"u2"}}"#,
                server.uri()
            ),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("dm1"));

        // Warmed the cache, findable via GetCachedChannels for its team ("" for a DM).
        let cached = dispatch(r#"{"command":"get_cached_channels","team_id":""}"#, db).await;
        assert!(cached.contains("dm1"));
    }

    #[tokio::test]
    async fn stop_websocket_is_ok_even_with_no_active_connection() {
        let db = test_db();
        let response = dispatch(r#"{"command":"stop_web_socket"}"#, db).await;
        assert!(response.contains(r#""ok":true"#), "unexpected response: {response}");
    }

    #[tokio::test]
    async fn get_typing_users_returns_whats_currently_recorded() {
        let db = test_db();
        db.lock().unwrap().record_typing("c1", "u1", chrono::Utc::now().timestamp_millis()).unwrap();

        let response = dispatch(r#"{"command":"get_typing_users","channel_id":"c1"}"#, db).await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("u1"));

        let response = dispatch(r#"{"command":"get_typing_users","channel_id":"c2"}"#, db).await;
        assert!(response.contains(r#""user_ids":[]"#));
    }

    #[tokio::test]
    async fn set_status_dispatches_to_the_expected_route() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/users/u1/status"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!(
                { "user_id": "u1", "status": "dnd" }
            )))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"set_status","base_url":"{}","token":"tok","user_id":"u1","status":"dnd"}}"#,
                server.uri()
            ),
            db,
        )
        .await;

        assert!(response.contains(r#""ok":true"#));
    }

    #[tokio::test]
    async fn set_channel_muted_dispatches_to_the_expected_route() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/channels/c1/members/u1/notify_props"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({ "mark_unread": "mention" })))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"set_channel_muted","base_url":"{}","token":"tok","user_id":"u1","channel_id":"c1","muted":true}}"#,
                server.uri()
            ),
            db,
        )
        .await;

        assert!(response.contains(r#""ok":true"#));
    }

    #[tokio::test]
    async fn get_channel_members_fetches_caches_and_serves_from_cache() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/channels/gm1/members"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "channel_id": "gm1", "user_id": "u1" },
                { "channel_id": "gm1", "user_id": "u2" }
            ])))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"get_channel_members","base_url":"{}","token":"tok","channel_id":"gm1"}}"#,
                server.uri()
            ),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("u1"));
        assert!(response.contains("u2"));

        // Now served from the cache without hitting the network again.
        let response = dispatch(r#"{"command":"get_cached_channel_members","channel_id":"gm1"}"#, db).await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("u1"));
        assert!(response.contains("u2"));
    }

    #[tokio::test]
    async fn search_users_fetches_and_warms_the_user_cache() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/search"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "u1", "username": "jdupont", "nickname": "", "first_name": "Jean", "last_name": "Dupont" }
            ])))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"search_users","base_url":"{}","token":"tok","channel_id":"c1","term":"vin"}}"#,
                server.uri()
            ),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));
        assert!(response.contains("jdupont"));

        // Warmed the user cache, same as GetUsers.
        let cached = db.lock().unwrap().cached_users(&["u1".to_string()]).unwrap();
        assert_eq!(cached.len(), 1);
        assert_eq!(cached[0].username, "jdupont");
    }

    #[tokio::test]
    async fn edit_message_updates_the_cache_with_the_new_text() {
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "edited", "create_at": 1000
            })))
            .mount(&server)
            .await;

        let db = test_db();
        let response = dispatch(
            &format!(
                r#"{{"command":"edit_message","base_url":"{}","token":"tok","post_id":"p1","message":"edited"}}"#,
                server.uri()
            ),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));

        let cached = db.lock().unwrap().cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].message, "edited");
    }

    #[tokio::test]
    async fn delete_message_removes_it_from_the_cache() {
        use crate::models::Post;
        use wiremock::matchers::{method, path};
        use wiremock::{Mock, MockServer, ResponseTemplate};

        let server = MockServer::start().await;
        Mock::given(method("DELETE"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;

        let db = test_db();
        db.lock().unwrap().upsert_post(&Post {
            id: "p1".into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at: 1000,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: Default::default(),
        }).unwrap();

        let response = dispatch(
            &format!(
                r#"{{"command":"delete_message","base_url":"{}","token":"tok","post_id":"p1"}}"#,
                server.uri()
            ),
            db,
        )
        .await;
        assert!(response.contains(r#""ok":true"#));

        assert!(db.lock().unwrap().cached_posts_for_channel("c1").unwrap().is_empty());
    }
}
