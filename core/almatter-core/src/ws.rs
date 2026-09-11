use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use serde::Deserialize;
use serde_json::json;
use thiserror::Error;
use tokio::sync::mpsc;
use tokio_tungstenite::tungstenite::Message;

use crate::api::MattermostClient;
use crate::db::Database;
use crate::models::Post;

#[derive(Debug, Error)]
pub enum WsError {
    #[error("websocket connection failed: {0}")]
    Connect(#[from] tokio_tungstenite::tungstenite::Error),
}

fn websocket_url(base_url: &str) -> String {
    let url = base_url.trim_end_matches('/');
    let url = url.replacen("https://", "wss://", 1);
    let url = url.replacen("http://", "ws://", 1);
    format!("{url}/api/v4/websocket")
}

/// The subset of a Mattermost WebSocket event this app currently acts on.
/// `data.post` and `data.mentions` are each themselves a JSON-encoded
/// *string*, not a nested value — that's how the server sends them.
#[derive(Deserialize)]
struct EventEnvelope {
    event: Option<String>,
    data: Option<EventData>,
    /// Only `typing` events carry the channel here rather than in `data` —
    /// a wire-format quirk of that one event type.
    broadcast: Option<Broadcast>,
}

#[derive(Deserialize)]
struct EventData {
    post: Option<String>,
    /// Set on reaction_added / reaction_removed: the reaction itself,
    /// JSON-encoded. It carries the post id, so applying it needs no extra
    /// round trip to fetch the message.
    reaction: Option<String>,
    /// Present only when this post mentions someone — a JSON-encoded array
    /// of the mentioned users' ids.
    mentions: Option<String>,
    /// Set on a `typing` event: who's typing. Absent (not this user's own
    /// typing being echoed back) on every other event type this app reads.
    user_id: Option<String>,
}

#[derive(Deserialize)]
struct Broadcast {
    channel_id: Option<String>,
}

/// One `reaction_added`/`reaction_removed` payload.
#[derive(Deserialize)]
struct ReactionEvent {
    post_id: String,
    user_id: String,
    emoji_name: String,
}

static STARTED: AtomicBool = AtomicBool::new(false);

/// Set by `stop` so the reconnect loop can tell a deliberate shutdown
/// (logout) from a connection that merely dropped — one must stay down, the
/// other must come back.
static SHUTDOWN: AtomicBool = AtomicBool::new(false);

/// Bumped by every `start`. A reconnect loop carries the generation it was
/// born with and retires the moment a newer one exists. SHUTDOWN alone is
/// not enough: logging out during a backoff sleep, then straight back in,
/// clears SHUTDOWN before the sleeping loop wakes — it would reconnect
/// under the old token alongside the new loop, and every live message would
/// be handled (and notified) twice.
static GENERATION: AtomicU64 = AtomicU64::new(0);

/// How long to wait before the first reconnect attempt, and the ceiling the
/// backoff climbs to. A laptop waking up or a Wi-Fi change should recover in
/// about a second; a server that is genuinely down should not be hammered.
const RECONNECT_MIN: Duration = Duration::from_secs(1);
const RECONNECT_MAX: Duration = Duration::from_secs(60);

/// Mattermost's own clients ping on this cadence, and for the same reason:
/// servers and the proxies in front of them close connections that look
/// idle. Without it the socket dies quietly after a few minutes of silence —
/// which is exactly how a "Connection reset without closing handshake"
/// shows up in the log.
const HEARTBEAT: Duration = Duration::from_secs(30);

/// The live connection's outbound half — `None` until `run` has connected
/// (or after it's dropped), so `send_typing` before login/while offline is
/// just a harmless no-op rather than something callers need to guard against.
static WS_SENDER: Mutex<Option<mpsc::UnboundedSender<String>>> = Mutex::new(None);

/// Tells the current connection's task to shut itself down — `stop`'s way
/// of reaching into `run`'s select loop from outside it. `None` when
/// there's nothing running (mirrors WS_SENDER).
static WS_STOP: Mutex<Option<mpsc::UnboundedSender<()>>> = Mutex::new(None);

/// Ends the current connection (if any) and clears `STARTED`, so a
/// following `start` call actually opens a fresh one instead of being a
/// no-op — without this, logging out and back in (same process, same as
/// switching accounts) left the old session's connection running forever
/// under its now-stale token, with no new one for whoever logs in next.
pub fn stop() {
    SHUTDOWN.store(true, Ordering::SeqCst);
    if let Some(tx) = WS_STOP.lock().expect("ws stop mutex poisoned").take() {
        let _ = tx.send(());
    }
    STARTED.store(false, Ordering::SeqCst);
}

fn typing_action_message(channel_id: &str, parent_id: &str) -> String {
    json!({
        "seq": 2,
        "action": "user_typing",
        "data": { "channel_id": channel_id, "parent_id": parent_id }
    })
    .to_string()
}

/// Tells the server this user is typing in `channel_id` (`parent_id` set
/// when it's a thread reply) — the send-side counterpart to
/// `handle_typing_event` below. Best-effort: if the WebSocket isn't
/// connected right now, this silently does nothing rather than erroring,
/// same spirit as every other "nice to have, not load-bearing" live feature
/// in this app.
pub fn send_typing(channel_id: &str, parent_id: &str) {
    if let Some(tx) = WS_SENDER.lock().expect("ws sender mutex poisoned").as_ref() {
        let _ = tx.send(typing_action_message(channel_id, parent_id));
    }
}

/// Starts the background connection once per process (repeat calls, e.g. a
/// second login in the same run, are no-ops). Keeps the local cache warm in
/// real time: every `posted` event is written straight to SQLite, so the
/// next cache read — the UI polls it periodically — sees it without another
/// network round trip. `user_id` is who to watch for in each post's
/// `mentions` list, to queue a desktop notification.
pub fn start(base_url: String, token: String, user_id: String, db: &'static Mutex<Database>) {
    if STARTED.swap(true, Ordering::SeqCst) {
        return;
    }
    SHUTDOWN.store(false, Ordering::SeqCst);
    let generation = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;
    tokio::spawn(async move {
        // A dropped connection is the normal case, not the exception: a
        // laptop sleeping, a Wi-Fi hop, a proxy timing the socket out. Left
        // unhandled it is silent — the UI keeps reading its cache and looks
        // fine while live messages, and every mention notification with
        // them, quietly stop arriving. So reconnect, backing off so a server
        // that is genuinely down is not hammered.
        let mut backoff = RECONNECT_MIN;
        loop {
            match run(base_url.clone(), token.clone(), user_id.clone(), db).await {
                Ok(Ended::ByRequest) => break,
                Ok(Ended::Dropped) => {
                    // It connected and ran, so the server is reachable and
                    // the credentials work: start over from the short delay.
                    backoff = RECONNECT_MIN;
                }
                Err(e) => crate::db::log("ws", &format!("connection ended: {e}")),
            }

            if SHUTDOWN.load(Ordering::SeqCst) || GENERATION.load(Ordering::SeqCst) != generation {
                break;
            }
            crate::db::log("ws", &format!("reconnecting in {}s", backoff.as_secs()));
            tokio::time::sleep(backoff).await;
            if SHUTDOWN.load(Ordering::SeqCst) || GENERATION.load(Ordering::SeqCst) != generation {
                break;
            }
            backoff = (backoff * 2).min(RECONNECT_MAX);
        }
        crate::db::log("ws", "reconnect loop finished");
        // Only the newest loop owns the flag — an outgoing one must not
        // clear it out from under its replacement.
        if GENERATION.load(Ordering::SeqCst) == generation {
            STARTED.store(false, Ordering::SeqCst);
        }
    });
}

/// Why a connection attempt came back — the reconnect loop has to tell a
/// deliberate logout from a socket that simply died.
enum Ended {
    ByRequest,
    Dropped,
}

async fn run(base_url: String, token: String, user_id: String, db: &'static Mutex<Database>) -> Result<Ended, WsError> {
    let url = websocket_url(&base_url);
    crate::db::log("ws", &format!("connecting to {url}"));
    let (stream, _response) = match tokio_tungstenite::connect_async(&url).await {
        Ok(connected) => connected,
        Err(e) => {
            crate::db::log("ws", &format!("connect failed: {e}"));
            // STARTED stays set: the reconnect loop above owns it and is
            // about to try again. Clearing it here would let a concurrent
            // `start` open a second, competing connection.
            return Err(e.into());
        }
    };
    crate::db::log("ws", "connected, sending auth challenge");

    // Split so this task can read incoming events and forward outbound
    // ones (send_typing, below) concurrently on the same connection.
    let (mut write, mut read) = stream.split();

    let auth = json!({
        "seq": 1,
        "action": "authentication_challenge",
        "data": { "token": token }
    });
    let _ = write.send(Message::Text(auth.to_string())).await;

    let (tx, mut rx) = mpsc::unbounded_channel::<String>();
    *WS_SENDER.lock().expect("ws sender mutex poisoned") = Some(tx);
    let (stop_tx, mut stop_rx) = mpsc::unbounded_channel::<()>();
    *WS_STOP.lock().expect("ws stop mutex poisoned") = Some(stop_tx);

    let client = MattermostClient::new(base_url).with_token(token);

    let mut event_count = 0u64;
    let mut ended = Ended::Dropped;
    // Fires immediately on its first tick, so consume that one now and let
    // the real cadence start a full interval from here.
    let mut heartbeat = tokio::time::interval(HEARTBEAT);
    heartbeat.tick().await;
    loop {
        tokio::select! {
            item = read.next() => {
                match item {
                    Some(Ok(Message::Text(text))) => {
                        event_count += 1;
                        if event_count <= 5 || event_count % 50 == 0 {
                            crate::db::log("ws", &format!("event #{event_count}: {}", truncate(&text, 200)));
                        }
                        handle_event(&text, &user_id, &client, db).await;
                    }
                    // The stream is split, so tungstenite cannot answer a
                    // server ping by itself — its automatic pong would have
                    // to go out through the write half, which lives here.
                    // Unanswered pings are read as a dead peer and the
                    // server hangs up.
                    Some(Ok(Message::Ping(payload))) => {
                        if write.send(Message::Pong(payload)).await.is_err() {
                            break;
                        }
                    }
                    Some(Ok(Message::Close(_))) => {
                        crate::db::log("ws", "server closed the connection");
                        break;
                    }
                    Some(Ok(_)) => {}
                    Some(Err(e)) => {
                        crate::db::log("ws", &format!("stream error: {e}"));
                        break;
                    }
                    None => break,
                }
            }
            _ = heartbeat.tick() => {
                // Keeps the socket visibly alive to the server and to
                // whatever proxy sits in between, and fails fast when the
                // link is already gone — a send error here is how a
                // half-open connection gets noticed at all.
                if write.send(Message::Ping(Vec::new())).await.is_err() {
                    crate::db::log("ws", "heartbeat failed, connection is gone");
                    break;
                }
            }
            outgoing = rx.recv() => {
                match outgoing {
                    Some(text) => { let _ = write.send(Message::Text(text)).await; }
                    None => {} // sender side never drops while this task is alive
                }
            }
            _ = stop_rx.recv() => {
                crate::db::log("ws", "stopped (logout)");
                ended = Ended::ByRequest;
                break;
            }
        }
    }
    crate::db::log("ws", &format!("stream ended after {event_count} events"));
    *WS_SENDER.lock().expect("ws sender mutex poisoned") = None;
    *WS_STOP.lock().expect("ws stop mutex poisoned") = None;
    // STARTED deliberately stays set: the reconnect loop in `start` owns it
    // for the whole lifetime of the retry sequence, and clears it only once
    // it gives up or is told to stop. Clearing it here would let a
    // concurrent `start` open a second connection alongside the retry.

    Ok(ended)
}

fn truncate(s: &str, max: usize) -> &str {
    match s.char_indices().nth(max) {
        Some((idx, _)) => &s[..idx],
        None => s,
    }
}

async fn handle_event(text: &str, user_id: &str, client: &MattermostClient, db: &'static Mutex<Database>) {
    let Ok(envelope) = serde_json::from_str::<EventEnvelope>(text) else {
        return;
    };
    match envelope.event.as_deref() {
        Some("typing") => {
            handle_typing_event(&envelope, user_id, db);
            return;
        }
        // Someone else corrected a message. Without this the old text sat
        // there, "(modifié)" and all, until the channel was reopened.
        Some("post_edited") => {
            if let Some(post) = envelope.data.as_ref().and_then(|d| d.post.as_deref())
                .and_then(|json| serde_json::from_str::<Post>(json).ok())
            {
                let cache = db.lock().expect("cache db mutex poisoned");
                if let Err(e) = cache.update_post_text(&post.id, &post.message, post.edit_at) {
                    crate::db::log("ws", &format!("failed to apply an edit: {e}"));
                }
            }
            return;
        }
        // Same story for a deletion: the message stayed on screen.
        Some("post_deleted") => {
            if let Some(post) = envelope.data.as_ref().and_then(|d| d.post.as_deref())
                .and_then(|json| serde_json::from_str::<Post>(json).ok())
            {
                let cache = db.lock().expect("cache db mutex poisoned");
                if let Err(e) = cache.delete_post(&post.id) {
                    crate::db::log("ws", &format!("failed to apply a deletion: {e}"));
                }
            }
            return;
        }
        // Someone reacted. The payload carries the post id, the user and
        // the emoji, so this is a direct cache write with no refetch — which
        // is what makes it cheap enough to apply on every event.
        Some("reaction_added") | Some("reaction_removed") => {
            let added = envelope.event.as_deref() == Some("reaction_added");
            if let Some(reaction) = envelope.data.as_ref().and_then(|d| d.reaction.as_deref())
                .and_then(|json| serde_json::from_str::<ReactionEvent>(json).ok())
            {
                let cache = db.lock().expect("cache db mutex poisoned");
                let result = if added {
                    cache.add_cached_reaction(&reaction.post_id, &reaction.emoji_name, &reaction.user_id)
                } else {
                    cache.remove_cached_reaction(&reaction.post_id, &reaction.emoji_name, &reaction.user_id)
                };
                if let Err(e) = result {
                    crate::db::log("ws", &format!("failed to apply a reaction: {e}"));
                }
            }
            return;
        }
        Some("posted") => {}
        _ => return,
    }
    let Some(data) = envelope.data.as_ref() else {
        return;
    };
    let Some(post_json) = data.post.as_deref() else {
        return;
    };
    let Ok(post) = serde_json::from_str::<Post>(post_json) else {
        return;
    };

    let author_already_cached = {
        let cache = db.lock().expect("cache db mutex poisoned");
        if let Err(e) = cache.upsert_post(&post) {
            eprintln!("almatter-core: failed to cache a live post: {e}");
        }
        // Reply counts are computed live from the posts table on every read
        // (see Database::cached_posts_for_channel), so a newly-cached reply
        // is reflected the moment it's inserted — nothing more to do here.
        cache.cached_user(&post.user_id).ok().flatten().is_some()
    };

    if !author_already_cached {
        if let Ok(users) = client.get_users_by_ids(std::slice::from_ref(&post.user_id)).await {
            let cache = db.lock().expect("cache db mutex poisoned");
            for user in &users {
                let _ = cache.upsert_user(user);
            }
        }
    }

    let mentioned = data
        .mentions
        .as_deref()
        .and_then(|raw| serde_json::from_str::<Vec<String>>(raw).ok())
        .is_some_and(|ids| ids.iter().any(|id| id == user_id));

    // Widens the channel's own unread gap live — someone else's message
    // shouldn't need a full channel-list refetch (or a restart) before the
    // sidebar's bold/badge state notices it. The post's own author never
    // needs this: their own message doesn't bump anything for their own
    // outbound side of the conversation view.
    if post.user_id != user_id {
        let cache = db.lock().expect("cache db mutex poisoned");
        if let Err(e) = cache.bump_channel_activity(&post.channel_id, mentioned) {
            crate::db::log("ws", &format!("failed to bump channel activity: {e}"));
        }
    }

    // Every message from someone else queues a notification event now, not
    // just mentions/DMs — the UI decides how loud to be about it (a mention
    // or DM still gets its own distinct wording) but even a plain channel
    // message the user isn't currently looking at should raise *something*.
    if post.user_id != user_id {
        if mentioned {
            crate::db::log("ws", &format!("mention detected in channel {}", post.channel_id));
        }
        let cache = db.lock().expect("cache db mutex poisoned");
        match cache.enqueue_mention_event(&post.id, &post.channel_id, &post.user_id, &post.message, post.create_at, mentioned) {
            Ok(()) => {}
            Err(e) => crate::db::log("ws", &format!("failed to queue notification: {e}")),
        }
    }
}

/// Records that someone (never the watched user themself — Mattermost
/// shouldn't echo your own typing back to you, but this stays defensive
/// about it anyway) is typing in a channel, with a timestamp — the UI's own
/// poll loop reads (and expires) this via `Database::typing_users_for_channel`.
/// Deliberately not persisted as anything more permanent than that: typing
/// state is meaningless the moment it goes stale.
fn handle_typing_event(envelope: &EventEnvelope, user_id: &str, db: &'static Mutex<Database>) {
    let Some(typing_user_id) = envelope.data.as_ref().and_then(|d| d.user_id.as_deref()) else {
        return;
    };
    if typing_user_id == user_id {
        return;
    }
    let Some(channel_id) = envelope.broadcast.as_ref().and_then(|b| b.channel_id.as_deref()) else {
        return;
    };

    let now = chrono::Utc::now().timestamp_millis();
    let cache = db.lock().expect("cache db mutex poisoned");
    if let Err(e) = cache.record_typing(channel_id, typing_user_id, now) {
        crate::db::log("ws", &format!("failed to record typing: {e}"));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn converts_http_base_urls_to_websocket_urls() {
        assert_eq!(websocket_url("https://example.com"), "wss://example.com/api/v4/websocket");
        assert_eq!(websocket_url("http://localhost:8065/"), "ws://localhost:8065/api/v4/websocket");
    }

    #[tokio::test]
    async fn handle_event_ignores_non_posted_events() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        handle_event(r#"{"event":"typing","data":{}}"#, "me", &client, db).await;

        let cache = db.lock().unwrap();
        assert!(cache.cached_posts_for_channel("any").unwrap().is_empty());
    }

    #[tokio::test]
    async fn handle_event_caches_a_posted_event() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let post = json!({
            "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "hi", "create_at": 1000
        });
        let event = json!({
            "event": "posted",
            "data": { "post": post.to_string() }
        });

        handle_event(&event.to_string(), "me", &client, db).await;

        let cache = db.lock().unwrap();
        let posts = cache.cached_posts_for_channel("c1").unwrap();
        assert_eq!(posts.len(), 1);
        assert_eq!(posts[0].message, "hi");
    }

    /// An edit must change the text and nothing else. The trap is reusing
    /// `upsert_post`, which replaces reactions and files from whatever it is
    /// handed — and a live edit event carries no metadata, so every reaction
    /// on the message would silently disappear.
    #[tokio::test]
    async fn handle_event_applies_an_edit_without_losing_reactions() {
        use crate::models::{PostMetadata, Reaction};

        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        db.lock()
            .unwrap()
            .upsert_post(&Post {
                id: "p1".into(),
                channel_id: "c1".into(),
                root_id: "".into(),
                user_id: "u1".into(),
                message: "typo heer".into(),
                create_at: 1000,
                reply_count: 0,
                edit_at: 0,
                metadata: PostMetadata {
                    reactions: vec![Reaction { user_id: "u2".into(), emoji_name: "+1".into() }],
                    ..Default::default()
                },
            })
            .unwrap();

        let edited = json!({
            "id": "p1", "channel_id": "c1", "user_id": "u1",
            "message": "typo here", "create_at": 1000, "edit_at": 2000
        });
        handle_event(
            &json!({ "event": "post_edited", "data": { "post": edited.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;

        let cache = db.lock().unwrap();
        let posts = cache.cached_posts_for_channel("c1").unwrap();
        assert_eq!(posts.len(), 1);
        assert_eq!(posts[0].message, "typo here");
        assert_eq!(posts[0].edit_at, 2000);
        assert_eq!(posts[0].metadata.reactions.len(), 1, "the edit must not wipe reactions");
    }

    /// A reaction event carries the reaction, not the message — so it must
    /// be applied without refetching the post, and must leave the post's
    /// own text alone.
    #[tokio::test]
    async fn handle_event_applies_a_reaction_from_someone_else() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let post = json!({
            "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "ship it", "create_at": 1000
        });
        handle_event(
            &json!({ "event": "posted", "data": { "post": post.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;

        let reaction = json!({ "post_id": "p1", "user_id": "u2", "emoji_name": "tada" });
        handle_event(
            &json!({ "event": "reaction_added", "data": { "reaction": reaction.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;

        {
            let cache = db.lock().unwrap();
            let posts = cache.cached_posts_for_channel("c1").unwrap();
            assert_eq!(posts[0].metadata.reactions.len(), 1);
            assert_eq!(posts[0].metadata.reactions[0].emoji_name, "tada");
            assert_eq!(posts[0].message, "ship it", "the message itself must be untouched");
        }

        handle_event(
            &json!({ "event": "reaction_removed", "data": { "reaction": reaction.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;

        let cache = db.lock().unwrap();
        let posts = cache.cached_posts_for_channel("c1").unwrap();
        assert!(posts[0].metadata.reactions.is_empty());
    }

    #[tokio::test]
    async fn handle_event_applies_a_deletion() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let post = json!({
            "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "oops", "create_at": 1000
        });
        handle_event(
            &json!({ "event": "posted", "data": { "post": post.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;
        assert_eq!(db.lock().unwrap().cached_posts_for_channel("c1").unwrap().len(), 1);

        handle_event(
            &json!({ "event": "post_deleted", "data": { "post": post.to_string() } }).to_string(),
            "me",
            &client,
            db,
        )
        .await;
        assert!(db.lock().unwrap().cached_posts_for_channel("c1").unwrap().is_empty());
    }

    #[tokio::test]
    async fn handle_event_queues_a_notification_for_every_message_flagging_real_mentions() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let post = json!({
            "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "hey @me", "create_at": 1000
        });
        let event = json!({
            "event": "posted",
            "data": { "post": post.to_string(), "mentions": json!(["me", "someone-else"]).to_string() }
        });

        // Not mentioned — still queued, but flagged as a plain message.
        handle_event(&event.to_string(), "not-me", &client, db).await;
        let events = db.lock().unwrap().drain_mention_events().unwrap();
        assert_eq!(events.len(), 1);
        assert!(!events[0].is_mention);

        // Mentioned — queued and flagged as a real mention.
        handle_event(&event.to_string(), "me", &client, db).await;
        let events = db.lock().unwrap().drain_mention_events().unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].post_id, "p1");
        assert_eq!(events[0].message, "hey @me");
        assert!(events[0].is_mention);
    }

    #[tokio::test]
    async fn handle_event_never_queues_a_notification_for_the_users_own_post() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let post = json!({
            "id": "p1", "channel_id": "c1", "user_id": "me", "message": "hello", "create_at": 1000
        });
        let event = json!({ "event": "posted", "data": { "post": post.to_string() } });

        handle_event(&event.to_string(), "me", &client, db).await;
        assert!(db.lock().unwrap().drain_mention_events().unwrap().is_empty());
    }

    #[test]
    fn typing_action_message_has_the_expected_shape() {
        let msg = typing_action_message("c1", "root1");
        let value: serde_json::Value = serde_json::from_str(&msg).unwrap();
        assert_eq!(value["action"], "user_typing");
        assert_eq!(value["data"]["channel_id"], "c1");
        assert_eq!(value["data"]["parent_id"], "root1");
    }

    #[test]
    fn stop_is_a_harmless_no_op_without_an_active_connection() {
        // No live WebSocket in this test process, so WS_STOP stays None —
        // this just confirms that doesn't panic (e.g. logging out before
        // ever logging in, or logging out twice).
        stop();
    }

    #[test]
    fn send_typing_is_a_harmless_no_op_without_an_active_connection() {
        // No live WebSocket in this test process, so WS_SENDER stays None —
        // this just confirms that doesn't panic (e.g. calling it while
        // logged out or offline).
        send_typing("c1", "");
    }

    #[tokio::test]
    async fn handle_event_records_someone_else_typing_but_not_the_watched_users_own() {
        let db: &'static Mutex<Database> =
            Box::leak(Box::new(Mutex::new(Database::open_in_memory().unwrap())));
        let client = MattermostClient::new("http://127.0.0.1:1");

        let event = json!({
            "event": "typing",
            "data": { "parent_id": "", "user_id": "someone-else" },
            "broadcast": { "channel_id": "c1" }
        });
        handle_event(&event.to_string(), "me", &client, db).await;

        let typing = db.lock().unwrap().typing_users_for_channel("c1", 9_999_999_999_999, i64::MAX).unwrap();
        assert_eq!(typing, vec!["someone-else".to_string()]);

        // The watched user's own typing (echoed back, if it ever is) must not be recorded.
        let own_event = json!({
            "event": "typing",
            "data": { "parent_id": "", "user_id": "me" },
            "broadcast": { "channel_id": "c1" }
        });
        handle_event(&own_event.to_string(), "me", &client, db).await;
        let typing = db.lock().unwrap().typing_users_for_channel("c1", 9_999_999_999_999, i64::MAX).unwrap();
        assert_eq!(typing, vec!["someone-else".to_string()]);
    }
}
