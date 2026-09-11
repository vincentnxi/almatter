use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

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

static STARTED: AtomicBool = AtomicBool::new(false);

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
    tokio::spawn(async move {
        // A dropped connection just ends this task for now; the app keeps
        // working off the cache. Automatic reconnection is future work.
        if let Err(e) = run(base_url, token, user_id, db).await {
            crate::db::log("ws", &format!("connection ended: {e}"));
        }
    });
}

async fn run(base_url: String, token: String, user_id: String, db: &'static Mutex<Database>) -> Result<(), WsError> {
    let url = websocket_url(&base_url);
    crate::db::log("ws", &format!("connecting to {url}"));
    let (stream, _response) = match tokio_tungstenite::connect_async(&url).await {
        Ok(connected) => connected,
        Err(e) => {
            crate::db::log("ws", &format!("connect failed: {e}"));
            // Never got as far as registering WS_SENDER/WS_STOP, but STARTED
            // was already set by `start` before this task was spawned — undo
            // that so a later `start` call can actually retry instead of
            // silently no-opping forever.
            STARTED.store(false, Ordering::SeqCst);
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
                    Some(Ok(_)) => {}
                    Some(Err(e)) => {
                        crate::db::log("ws", &format!("stream error: {e}"));
                        break;
                    }
                    None => break,
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
                break;
            }
        }
    }
    crate::db::log("ws", &format!("stream ended after {event_count} events"));
    *WS_SENDER.lock().expect("ws sender mutex poisoned") = None;
    *WS_STOP.lock().expect("ws stop mutex poisoned") = None;
    // Whether this ended by an explicit `stop()` or the connection just
    // dropped on its own, nothing is listening anymore — a later `start`
    // must be able to open a fresh connection rather than see a stale
    // "already running" flag and silently no-op.
    STARTED.store(false, Ordering::SeqCst);

    Ok(())
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
    if envelope.event.as_deref() == Some("typing") {
        handle_typing_event(&envelope, user_id, db);
        return;
    }
    if envelope.event.as_deref() != Some("posted") {
        return;
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
