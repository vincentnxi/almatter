use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicI64, Ordering};

use rusqlite::{params, Connection, OptionalExtension};

use crate::models::{
    AuthenticatedUser, Channel, ChannelType, CustomEmoji, FileInfo, MentionEvent, Post, PostMetadata, Reaction,
    ReactionNotice, Team,
};

/// Local SQLite cache: the source of truth the UI reads from, kept warm by
/// API responses (and, later, the WebSocket event stream) and readable in
/// full while offline.
pub struct Database {
    conn: Connection,
}

impl Database {
    pub fn open(path: &str) -> rusqlite::Result<Self> {
        let conn = Connection::open(path)?;
        // Unlike most pragmas, `journal_mode` always returns the resulting
        // mode as a row — `pragma_update` rejects that ("did you mean to
        // call query?"), so this needs `pragma_update_and_check` instead.
        conn.pragma_update_and_check(None, "journal_mode", "WAL", |_row| Ok(()))?;
        // WAL already makes commits durable without a full fsync of the main
        // db file on every write; NORMAL (rather than the default FULL) skips
        // the extra fsync of the WAL file itself on each commit too, which is
        // still safe under WAL — SQLite's own docs recommend this pairing.
        conn.pragma_update(None, "synchronous", "NORMAL")?;
        // This is a cache mirroring eventually-consistent server data, not a
        // system enforcing its own integrity: a post can legitimately arrive
        // (e.g. over the WebSocket) before its channel or author has been
        // fetched. Enforced FKs would silently drop that data instead.
        conn.pragma_update(None, "foreign_keys", "OFF")?;

        // Start the reaction stamp counter above anything already stored, so
        // a restart cannot reissue a stamp an earlier run already used.
        if let Ok(highest) = conn.query_row(
            "SELECT COALESCE(MAX(write_seq), 0) FROM reactions",
            [],
            |row| row.get::<_, i64>(0),
        ) {
            REACTION_SEQ.fetch_max(highest, Ordering::SeqCst);
        }
        let db = Self { conn };
        db.init_schema()?;
        Ok(db)
    }

    pub fn open_in_memory() -> rusqlite::Result<Self> {
        let conn = Connection::open_in_memory()?;
        let db = Self { conn };
        db.init_schema()?;
        Ok(db)
    }

    /// Runs `f` inside a single SQLite transaction instead of letting each
    /// statement it issues commit (and fsync) on its own — the difference
    /// between warming the cache for a channel full of messages taking tens
    /// of milliseconds instead of hundreds. `&self` is enough (no `&mut`
    /// needed for rusqlite's own `Connection::transaction`) since this whole
    /// database is already behind a `Mutex` guarding against concurrent use.
    pub fn in_transaction<T>(&self, f: impl FnOnce() -> rusqlite::Result<T>) -> rusqlite::Result<T> {
        self.conn.execute_batch("BEGIN")?;
        match f() {
            Ok(value) => {
                self.conn.execute_batch("COMMIT")?;
                Ok(value)
            }
            Err(e) => {
                let _ = self.conn.execute_batch("ROLLBACK");
                Err(e)
            }
        }
    }

    /// The cache file used by the app: `<platform data dir>/Almatter/cache.sqlite3`,
    /// falling back to a relative path if the platform data dir can't be resolved.
    pub fn open_default() -> rusqlite::Result<Self> {
        let path = default_db_path();
        if let Some(parent) = path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        Self::open(&path.to_string_lossy())
    }

    fn init_schema(&self) -> rusqlite::Result<()> {
        self.conn.execute_batch(
            r#"
            CREATE TABLE IF NOT EXISTS teams (
                id           TEXT PRIMARY KEY,
                name         TEXT NOT NULL,
                display_name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS channels (
                id              TEXT PRIMARY KEY,
                team_id         TEXT NOT NULL,
                name            TEXT NOT NULL,
                display_name    TEXT NOT NULL,
                channel_type    TEXT NOT NULL,
                unread_count    INTEGER NOT NULL DEFAULT 0,
                mention_count   INTEGER NOT NULL DEFAULT 0,
                total_msg_count INTEGER NOT NULL DEFAULT 0,
                last_post_at    INTEGER NOT NULL DEFAULT 0,
                msg_count       INTEGER NOT NULL DEFAULT 0,
                is_muted        INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS users (
                id         TEXT PRIMARY KEY,
                username   TEXT NOT NULL,
                nickname   TEXT NOT NULL DEFAULT '',
                first_name TEXT NOT NULL DEFAULT '',
                last_name  TEXT NOT NULL DEFAULT '',
                presence   TEXT NOT NULL DEFAULT 'offline'
            );

            CREATE TABLE IF NOT EXISTS posts (
                id          TEXT PRIMARY KEY,
                channel_id  TEXT NOT NULL,
                root_id     TEXT NOT NULL DEFAULT '',
                user_id     TEXT NOT NULL,
                message     TEXT NOT NULL,
                create_at   INTEGER NOT NULL,
                reply_count INTEGER NOT NULL DEFAULT 0,
                edit_at     INTEGER NOT NULL DEFAULT 0,
                embeds_json TEXT NOT NULL DEFAULT '[]',
                is_pinned   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_posts_channel ON posts(channel_id, create_at);
            CREATE INDEX IF NOT EXISTS idx_posts_root ON posts(root_id);

            -- Kept in sync via triggers so local search (not wired into the
            -- UI yet) has a ready index once it lands.
            CREATE VIRTUAL TABLE IF NOT EXISTS posts_fts USING fts5(
                message, content='posts', content_rowid='rowid'
            );
            CREATE TRIGGER IF NOT EXISTS posts_ai AFTER INSERT ON posts BEGIN
                INSERT INTO posts_fts(rowid, message) VALUES (new.rowid, new.message);
            END;
            CREATE TRIGGER IF NOT EXISTS posts_ad AFTER DELETE ON posts BEGIN
                INSERT INTO posts_fts(posts_fts, rowid, message) VALUES ('delete', old.rowid, old.message);
            END;
            CREATE TRIGGER IF NOT EXISTS posts_au AFTER UPDATE ON posts BEGIN
                INSERT INTO posts_fts(posts_fts, rowid, message) VALUES ('delete', old.rowid, old.message);
                INSERT INTO posts_fts(rowid, message) VALUES (new.rowid, new.message);
            END;

            CREATE TABLE IF NOT EXISTS reactions (
                post_id    TEXT NOT NULL,
                emoji_name TEXT NOT NULL,
                user_id    TEXT NOT NULL,
                -- A strictly increasing stamp for the order this row was
                -- written locally. It exists purely so channel_revision can
                -- tell "someone swapped their reaction" from "nothing
                -- happened": a swap is a removal plus an addition, which
                -- leaves the count unchanged. Neither MAX(rowid) nor a clock
                -- works here — SQLite reuses the rowid of a deleted row, and
                -- a swap easily lands inside the same millisecond.
                write_seq  INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (post_id, emoji_name, user_id)
            );

            CREATE TABLE IF NOT EXISTS files (
                id      TEXT PRIMARY KEY,
                post_id TEXT NOT NULL,
                name    TEXT NOT NULL,
                size    INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_files_post ON files(post_id);

            -- Offline outbox: messages composed while disconnected, replayed on reconnect.
            CREATE TABLE IF NOT EXISTS outbox (
                local_id     TEXT PRIMARY KEY,
                channel_id   TEXT NOT NULL,
                root_id      TEXT,
                message      TEXT NOT NULL,
                created_at   INTEGER NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS custom_emoji (
                id   TEXT PRIMARY KEY,
                name TEXT NOT NULL
            );

            -- Mirrors the server's favorite_channel preferences, not an
            -- Almatter-only flag, so it's simply overwritten wholesale
            -- every time the real list is re-fetched.
            CREATE TABLE IF NOT EXISTS favorite_channels (
                channel_id TEXT PRIMARY KEY
            );

            -- A mention the WebSocket saw for the current user, waiting to
            -- be picked up (and cleared) by the UI's poll loop and turned
            -- into a desktop notification. INSERT OR IGNORE on the way in
            -- since the same post can occasionally be redelivered.
            CREATE TABLE IF NOT EXISTS mention_events (
                post_id    TEXT PRIMARY KEY,
                channel_id TEXT NOT NULL,
                author_id  TEXT NOT NULL,
                message    TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                is_mention INTEGER NOT NULL DEFAULT 0
            );

            -- Same queue, for someone reacting to one of this user's own
            -- messages. Kept apart from mention_events rather than folded
            -- into it: that table is keyed by post alone, and several people
            -- can react to the same message (or one person with several
            -- emoji) without any of those notifications swallowing another.
            CREATE TABLE IF NOT EXISTS reaction_events (
                post_id    TEXT NOT NULL,
                channel_id TEXT NOT NULL,
                user_id    TEXT NOT NULL,
                emoji_name TEXT NOT NULL,
                message    TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (post_id, user_id, emoji_name)
            );

            -- The last time each user was seen typing in each channel, per
            -- the WebSocket's `typing` events — read (and pruned of stale
            -- rows) by the UI's poll loop, never meant to persist beyond a
            -- few seconds' staleness. One row per (channel, user): a repeat
            -- typing event just bumps updated_at, it doesn't accumulate.
            CREATE TABLE IF NOT EXISTS typing_events (
                channel_id TEXT NOT NULL,
                user_id    TEXT NOT NULL,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY (channel_id, user_id)
            );

            -- A group DM's member list (`GET /channels/{id}/members`),
            -- needed to build its display name and avatars since unlike a
            -- 1:1 DM, a GM channel's `name` isn't parseable into user ids.
            -- Replaced wholesale on every fetch, same story as
            -- favorite_channels.
            CREATE TABLE IF NOT EXISTS channel_participants (
                channel_id TEXT NOT NULL,
                user_id    TEXT NOT NULL,
                PRIMARY KEY (channel_id, user_id)
            );
            "#,
        )?;

        // Lightweight migration for a database created before this column
        // existed (`CREATE TABLE IF NOT EXISTS` above only helps fresh
        // installs). Harmlessly fails with "duplicate column" once the
        // column is already there.
        let _ = self
            .conn
            .execute("ALTER TABLE posts ADD COLUMN reply_count INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE reactions ADD COLUMN write_seq INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE channels ADD COLUMN total_msg_count INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE channels ADD COLUMN last_post_at INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE channels ADD COLUMN msg_count INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE posts ADD COLUMN edit_at INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE mention_events ADD COLUMN is_mention INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE channels ADD COLUMN is_muted INTEGER NOT NULL DEFAULT 0", []);
        let _ = self
            .conn
            .execute("ALTER TABLE posts ADD COLUMN embeds_json TEXT NOT NULL DEFAULT '[]'", []);
        let _ = self
            .conn
            .execute("ALTER TABLE posts ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0", []);

        Ok(())
    }

    pub fn connection(&self) -> &Connection {
        &self.conn
    }

    // --- writes: called after every successful API fetch to keep the cache warm ---

    pub fn upsert_team(&self, team: &Team) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT INTO teams (id, name, display_name) VALUES (?1, ?2, ?3)
             ON CONFLICT(id) DO UPDATE SET name = excluded.name, display_name = excluded.display_name",
            params![team.id, team.name, team.display_name],
        )?;
        Ok(())
    }

    pub fn upsert_channel(&self, channel: &Channel) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT INTO channels (id, team_id, name, display_name, channel_type, total_msg_count, last_post_at, msg_count, mention_count, is_muted)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
             ON CONFLICT(id) DO UPDATE SET
                team_id = excluded.team_id,
                name = excluded.name,
                display_name = excluded.display_name,
                channel_type = excluded.channel_type,
                total_msg_count = excluded.total_msg_count,
                last_post_at = excluded.last_post_at,
                msg_count = excluded.msg_count,
                mention_count = excluded.mention_count,
                is_muted = excluded.is_muted",
            params![
                channel.id,
                channel.team_id,
                channel.name,
                channel.display_name,
                channel.channel_type.as_code(),
                channel.total_msg_count,
                channel.last_post_at,
                channel.msg_count,
                channel.mention_count,
                channel.is_muted,
            ],
        )?;
        Ok(())
    }

    pub fn upsert_user(&self, user: &AuthenticatedUser) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT INTO users (id, username, nickname, first_name, last_name)
             VALUES (?1, ?2, ?3, ?4, ?5)
             ON CONFLICT(id) DO UPDATE SET
                username = excluded.username,
                nickname = excluded.nickname,
                first_name = excluded.first_name,
                last_name = excluded.last_name",
            params![user.id, user.username, user.nickname, user.first_name, user.last_name],
        )?;
        Ok(())
    }

    pub fn upsert_post(&self, post: &Post) -> rusqlite::Result<()> {
        // Serialization only fails for types that can't round-trip through
        // JSON at all (NaN floats, non-string map keys) — never the case
        // here, so an empty-embeds fallback is just defensive, not expected
        // to actually trigger.
        let embeds_json = serde_json::to_string(&post.metadata.embeds).unwrap_or_else(|_| "[]".to_string());
        self.conn.execute(
            "INSERT INTO posts (id, channel_id, root_id, user_id, message, create_at, reply_count, edit_at, embeds_json, is_pinned)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
             ON CONFLICT(id) DO UPDATE SET
                message = excluded.message,
                root_id = excluded.root_id,
                reply_count = excluded.reply_count,
                edit_at = excluded.edit_at,
                embeds_json = excluded.embeds_json,
                is_pinned = excluded.is_pinned",
            params![
                post.id,
                post.channel_id,
                post.root_id,
                post.user_id,
                post.message,
                post.create_at,
                post.reply_count,
                post.edit_at,
                embeds_json,
                post.is_pinned,
            ],
        )?;

        // Reactions and files ride along on the post — replace-in-full on
        // every upsert so removed reactions actually disappear from the
        // cache instead of accumulating forever.
        self.conn.execute("DELETE FROM reactions WHERE post_id = ?1", params![post.id])?;
        for r in &post.metadata.reactions {
            self.conn.execute(
                "INSERT OR IGNORE INTO reactions (post_id, emoji_name, user_id, write_seq) VALUES (?1, ?2, ?3, ?4)",
                params![post.id, r.emoji_name, r.user_id, self.next_reaction_seq()],
            )?;
        }

        self.conn.execute("DELETE FROM files WHERE post_id = ?1", params![post.id])?;
        for f in &post.metadata.files {
            self.conn.execute(
                "INSERT OR REPLACE INTO files (id, post_id, name, size) VALUES (?1, ?2, ?3, ?4)",
                params![f.id, post.id, f.name, f.size],
            )?;
        }

        Ok(())
    }

    /// Removes a deleted message (and its reactions/files, since foreign
    /// keys aren't enforced — see `open`) from the cache, so it actually
    /// disappears from an already-painted channel/thread instead of lingering
    /// until the next full re-fetch overwrites it.
    /// Hands out the next write stamp for a reaction row. Seeded once per
    /// process from what the database already holds (see `open`), so a
    /// restart can never hand out a stamp that was used before; after that
    /// it is a plain in-memory counter, with no extra read per insert.
    fn next_reaction_seq(&self) -> i64 {
        REACTION_SEQ.fetch_add(1, Ordering::SeqCst) + 1
    }

    /// Records a reaction someone just added, from a live event. Ignored
    /// when the post isn't cached: the reactions table would otherwise
    /// collect rows pointing at nothing, and the next read of that post
    /// (once it IS cached) would show reactions it never fetched.
    pub fn add_cached_reaction(&self, post_id: &str, emoji_name: &str, user_id: &str) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT OR IGNORE INTO reactions (post_id, emoji_name, user_id, write_seq)
             SELECT ?1, ?2, ?3, ?4 WHERE EXISTS (SELECT 1 FROM posts WHERE id = ?1)",
            params![post_id, emoji_name, user_id, self.next_reaction_seq()],
        )?;
        Ok(())
    }

    pub fn remove_cached_reaction(&self, post_id: &str, emoji_name: &str, user_id: &str) -> rusqlite::Result<()> {
        self.conn.execute(
            "DELETE FROM reactions WHERE post_id = ?1 AND emoji_name = ?2 AND user_id = ?3",
            params![post_id, emoji_name, user_id],
        )?;
        Ok(())
    }

    /// Applies an edit to an already-cached post, touching only the text and
    /// the edit stamp. Deliberately NOT `upsert_post`: that one replaces a
    /// post's reactions and files wholesale from what it is handed, and a
    /// live `post_edited` event carries no metadata — so reusing it would
    /// silently wipe every reaction on the edited message.
    /// A no-op when the post isn't cached, which is the normal case for a
    /// channel this user has never opened.
    pub fn update_post_text(&self, post_id: &str, message: &str, edit_at: i64) -> rusqlite::Result<()> {
        self.conn.execute(
            "UPDATE posts SET message = ?2, edit_at = ?3 WHERE id = ?1",
            params![post_id, message, edit_at],
        )?;
        Ok(())
    }

    pub fn delete_post(&self, post_id: &str) -> rusqlite::Result<()> {
        self.conn.execute("DELETE FROM reactions WHERE post_id = ?1", params![post_id])?;
        self.conn.execute("DELETE FROM files WHERE post_id = ?1", params![post_id])?;
        self.conn.execute("DELETE FROM posts WHERE id = ?1", params![post_id])?;
        Ok(())
    }

    /// Overwrites the cached favorites list wholesale — this always follows
    /// a fresh fetch of the server's actual current list, so there's
    /// nothing to reconcile incrementally.
    pub fn replace_favorite_channels(&self, channel_ids: &[String]) -> rusqlite::Result<()> {
        self.conn.execute("DELETE FROM favorite_channels", [])?;
        for id in channel_ids {
            self.conn.execute(
                "INSERT OR IGNORE INTO favorite_channels (channel_id) VALUES (?1)",
                params![id],
            )?;
        }
        Ok(())
    }

    /// Optimistic single-channel update — used right after the app's own
    /// toggle call succeeds, so the cache doesn't need a full re-fetch just
    /// to reflect one change.
    pub fn set_favorite_channel(&self, channel_id: &str, is_favorite: bool) -> rusqlite::Result<()> {
        if is_favorite {
            self.conn.execute(
                "INSERT OR IGNORE INTO favorite_channels (channel_id) VALUES (?1)",
                params![channel_id],
            )?;
        } else {
            self.conn
                .execute("DELETE FROM favorite_channels WHERE channel_id = ?1", params![channel_id])?;
        }
        Ok(())
    }

    pub fn cached_favorite_channel_ids(&self) -> rusqlite::Result<Vec<String>> {
        let mut stmt = self.conn.prepare("SELECT channel_id FROM favorite_channels")?;
        let rows = stmt.query_map([], |row| row.get(0))?;
        rows.collect()
    }

    /// Overwrites one channel's cached member list wholesale — always
    /// follows a fresh `GET /channels/{id}/members` fetch, so (like
    /// favorites) there's nothing to reconcile incrementally.
    pub fn replace_channel_participants(&self, channel_id: &str, user_ids: &[String]) -> rusqlite::Result<()> {
        self.conn
            .execute("DELETE FROM channel_participants WHERE channel_id = ?1", params![channel_id])?;
        for user_id in user_ids {
            self.conn.execute(
                "INSERT OR IGNORE INTO channel_participants (channel_id, user_id) VALUES (?1, ?2)",
                params![channel_id, user_id],
            )?;
        }
        Ok(())
    }

    pub fn cached_channel_participants(&self, channel_id: &str) -> rusqlite::Result<Vec<String>> {
        let mut stmt = self
            .conn
            .prepare("SELECT user_id FROM channel_participants WHERE channel_id = ?1")?;
        let rows = stmt.query_map(params![channel_id], |row| row.get(0))?;
        rows.collect()
    }

    pub fn upsert_custom_emoji(&self, emoji: &CustomEmoji) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT INTO custom_emoji (id, name) VALUES (?1, ?2)
             ON CONFLICT(id) DO UPDATE SET name = excluded.name",
            params![emoji.id, emoji.name],
        )?;
        Ok(())
    }

    pub fn cached_custom_emoji(&self) -> rusqlite::Result<Vec<CustomEmoji>> {
        let mut stmt = self.conn.prepare("SELECT id, name FROM custom_emoji ORDER BY name")?;
        let rows = stmt.query_map([], |row| Ok(CustomEmoji { id: row.get(0)?, name: row.get(1)? }))?;
        rows.collect()
    }

    /// Called by the WebSocket handler for every live `posted` event —
    /// widens the unread gap (total_msg_count vs msg_count) that drives the
    /// sidebar's bold/badge state, the same way a real fetch of the
    /// channel-members endpoint would once it next runs. The user's own
    /// message counts as already read, so the gap stays put for it. Either
    /// way last_post_at moves forward, which is what orders the DM list by
    /// recent activity. A no-op if the channel isn't cached yet (nothing to
    /// widen the gap on).
    pub fn bump_channel_activity(
        &self,
        channel_id: &str,
        post_at: i64,
        own_post: bool,
        mentioned: bool,
    ) -> rusqlite::Result<()> {
        self.conn.execute(
            "UPDATE channels SET total_msg_count = total_msg_count + 1,
                msg_count = msg_count + ?3,
                mention_count = mention_count + ?2,
                last_post_at = MAX(last_post_at, ?4)
             WHERE id = ?1",
            params![
                channel_id,
                if mentioned { 1 } else { 0 },
                if own_post { 1 } else { 0 },
                post_at
            ],
        )?;
        Ok(())
    }

    /// Optimistic local update after a successful `mark_channel_viewed` —
    /// sets this channel's msg_count to its total_msg_count so it reads as
    /// fully caught up until the next real fetch confirms it.
    pub fn mark_channel_read(&self, channel_id: &str) -> rusqlite::Result<()> {
        self.conn.execute(
            "UPDATE channels SET msg_count = total_msg_count, mention_count = 0 WHERE id = ?1",
            params![channel_id],
        )?;
        Ok(())
    }

    /// The server's word on this user's read state for one channel, as
    /// carried by a live `post_unread` event (the channel was marked unread
    /// from another device).
    pub fn set_channel_read_state(&self, channel_id: &str, msg_count: i64, mention_count: i64) -> rusqlite::Result<()> {
        self.conn.execute(
            "UPDATE channels SET msg_count = ?2, mention_count = ?3 WHERE id = ?1",
            params![channel_id, msg_count, mention_count],
        )?;
        Ok(())
    }

    /// Called by the WebSocket handler the moment it sees a `posted` event
    /// mentioning the current user. `INSERT OR IGNORE` since the same post
    /// can occasionally be redelivered over the socket.
    pub fn enqueue_mention_event(
        &self,
        post_id: &str,
        channel_id: &str,
        author_id: &str,
        message: &str,
        created_at: i64,
        is_mention: bool,
    ) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT OR IGNORE INTO mention_events (post_id, channel_id, author_id, message, created_at, is_mention)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
            params![post_id, channel_id, author_id, message, created_at, is_mention],
        )?;
        Ok(())
    }

    /// Returns every queued notification and clears the queue — a drain, not
    /// a peek, since the UI's poll loop is the only reader and each one
    /// should only ever produce one notification.
    pub fn drain_mention_events(&self) -> rusqlite::Result<Vec<MentionEvent>> {
        let mut stmt = self.conn.prepare(
            "SELECT post_id, channel_id, author_id, message, created_at, is_mention
             FROM mention_events ORDER BY created_at ASC",
        )?;
        let rows = stmt.query_map([], |row| {
            Ok(MentionEvent {
                post_id: row.get(0)?,
                channel_id: row.get(1)?,
                author_id: row.get(2)?,
                message: row.get(3)?,
                created_at: row.get(4)?,
                is_mention: row.get(5)?,
            })
        })?;
        let events = rows.collect::<rusqlite::Result<Vec<_>>>()?;
        self.conn.execute("DELETE FROM mention_events", [])?;
        Ok(events)
    }

    /// Queues "someone reacted to your message". `INSERT OR IGNORE` for the
    /// same reason as mentions: the socket can redeliver an event.
    pub fn enqueue_reaction_event(
        &self,
        post_id: &str,
        channel_id: &str,
        user_id: &str,
        emoji_name: &str,
        message: &str,
        created_at: i64,
    ) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT OR IGNORE INTO reaction_events (post_id, channel_id, user_id, emoji_name, message, created_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
            params![post_id, channel_id, user_id, emoji_name, message, created_at],
        )?;
        Ok(())
    }

    /// Drops a queued reaction notification because the reaction itself was
    /// taken back. A misclick undone within the second or two before the
    /// UI's next poll shouldn't still light up the notification area.
    pub fn cancel_reaction_event(&self, post_id: &str, user_id: &str, emoji_name: &str) -> rusqlite::Result<()> {
        self.conn.execute(
            "DELETE FROM reaction_events WHERE post_id = ?1 AND user_id = ?2 AND emoji_name = ?3",
            params![post_id, user_id, emoji_name],
        )?;
        Ok(())
    }

    /// The reaction counterpart of `drain_mention_events` — also a drain,
    /// one notification per queued reaction and no more.
    pub fn drain_reaction_events(&self) -> rusqlite::Result<Vec<ReactionNotice>> {
        let mut stmt = self.conn.prepare(
            "SELECT post_id, channel_id, user_id, emoji_name, message, created_at
             FROM reaction_events ORDER BY created_at ASC",
        )?;
        let rows = stmt.query_map([], |row| {
            Ok(ReactionNotice {
                post_id: row.get(0)?,
                channel_id: row.get(1)?,
                user_id: row.get(2)?,
                emoji_name: row.get(3)?,
                message: row.get(4)?,
                created_at: row.get(5)?,
            })
        })?;
        let events = rows.collect::<rusqlite::Result<Vec<_>>>()?;
        self.conn.execute("DELETE FROM reaction_events", [])?;
        Ok(events)
    }

    /// Notes that `user_id` was just seen typing in `channel_id` — a repeat
    /// call for the same pair just bumps the timestamp rather than piling up.
    pub fn record_typing(&self, channel_id: &str, user_id: &str, now_millis: i64) -> rusqlite::Result<()> {
        self.conn.execute(
            "INSERT INTO typing_events (channel_id, user_id, updated_at) VALUES (?1, ?2, ?3)
             ON CONFLICT(channel_id, user_id) DO UPDATE SET updated_at = excluded.updated_at",
            params![channel_id, user_id, now_millis],
        )?;
        Ok(())
    }

    /// Who's currently typing in a channel — "currently" meaning a typing
    /// event arrived within `ttl_millis` of `now_millis`; Mattermost's own
    /// clients re-send one every couple of seconds while you keep typing, so
    /// a few seconds of TTL comfortably bridges the gap between them without
    /// leaving a stale "typing…" up long after someone actually stopped.
    /// Also opportunistically prunes rows older than that, so this table
    /// never grows unbounded over a long session.
    pub fn typing_users_for_channel(&self, channel_id: &str, now_millis: i64, ttl_millis: i64) -> rusqlite::Result<Vec<String>> {
        let cutoff = now_millis - ttl_millis;
        self.conn.execute("DELETE FROM typing_events WHERE updated_at < ?1", params![cutoff])?;
        let mut stmt = self
            .conn
            .prepare("SELECT user_id FROM typing_events WHERE channel_id = ?1 AND updated_at >= ?2")?;
        let rows = stmt.query_map(params![channel_id, cutoff], |row| row.get(0))?;
        rows.collect()
    }

    /// Fills in every post's `metadata` (reactions/files) from the cache —
    /// `cached_posts_for_channel`/`cached_thread_posts` return bare rows,
    /// this is the second pass that attaches what's related.
    ///
    /// Two queries for the whole batch rather than two per post. On a real
    /// 455-post channel the per-post version was 3.7 ms of the 5.8 ms the
    /// whole cached read cost — nearly two thirds of it — against 0.19 ms
    /// this way, because each post paid a fresh statement prepare and its
    /// own index descent. Chunked because every id becomes a bound
    /// parameter and SQLite caps how many one statement may carry.
    fn attach_metadata(&self, mut posts: Vec<Post>) -> rusqlite::Result<Vec<Post>> {
        if posts.is_empty() {
            return Ok(posts);
        }

        /// Well under SQLite's parameter ceiling, and large enough that a
        /// normal channel is one query rather than several.
        const CHUNK: usize = 400;

        let ids: Vec<String> = posts.iter().map(|p| p.id.clone()).collect();
        let mut reactions: HashMap<String, Vec<Reaction>> = HashMap::new();
        let mut files: HashMap<String, Vec<FileInfo>> = HashMap::new();

        for chunk in ids.chunks(CHUNK) {
            let placeholders = vec!["?"; chunk.len()].join(",");

            let mut stmt = self.conn.prepare_cached(&format!(
                "SELECT post_id, emoji_name, user_id FROM reactions WHERE post_id IN ({placeholders})"
            ))?;
            let rows = stmt.query_map(rusqlite::params_from_iter(chunk), |row| {
                Ok((
                    row.get::<_, String>(0)?,
                    Reaction { emoji_name: row.get(1)?, user_id: row.get(2)? },
                ))
            })?;
            for row in rows {
                let (post_id, reaction) = row?;
                reactions.entry(post_id).or_default().push(reaction);
            }

            let mut stmt = self.conn.prepare_cached(&format!(
                "SELECT post_id, id, name, size FROM files WHERE post_id IN ({placeholders})"
            ))?;
            let rows = stmt.query_map(rusqlite::params_from_iter(chunk), |row| {
                Ok((
                    row.get::<_, String>(0)?,
                    FileInfo { id: row.get(1)?, name: row.get(2)?, size: row.get(3)? },
                ))
            })?;
            for row in rows {
                let (post_id, file) = row?;
                files.entry(post_id).or_default().push(file);
            }
        }

        for post in &mut posts {
            post.metadata.reactions = reactions.remove(&post.id).unwrap_or_default();
            post.metadata.files = files.remove(&post.id).unwrap_or_default();
        }
        Ok(posts)
    }

    // --- reads: what makes the UI feel instant and work offline ---

    pub fn cached_teams(&self) -> rusqlite::Result<Vec<Team>> {
        let mut stmt = self
            .conn
            .prepare("SELECT id, name, display_name FROM teams ORDER BY display_name")?;
        let rows = stmt.query_map([], |row| {
            Ok(Team { id: row.get(0)?, name: row.get(1)?, display_name: row.get(2)? })
        })?;
        rows.collect()
    }

    /// DM/GM channels come back from the server with an empty `team_id` —
    /// they aren't scoped to any particular team — so `team_id = ''` is
    /// included alongside the requested team, or every direct message
    /// would silently disappear from a cache-only read (they're still
    /// present in a network read, since that fetches the whole
    /// per-team channel list as one batch regardless of each channel's
    /// own team_id).
    pub fn cached_channels_for_team(&self, team_id: &str) -> rusqlite::Result<Vec<Channel>> {
        let mut stmt = self.conn.prepare(
            "SELECT id, team_id, name, display_name, channel_type, total_msg_count, last_post_at, msg_count, mention_count, is_muted FROM channels
             WHERE team_id = ?1 OR team_id = '' ORDER BY display_name",
        )?;
        let rows = stmt.query_map(params![team_id], |row| {
            let channel_type: String = row.get(4)?;
            Ok(Channel {
                id: row.get(0)?,
                team_id: row.get(1)?,
                name: row.get(2)?,
                display_name: row.get(3)?,
                channel_type: ChannelType::from_code(&channel_type).unwrap_or(ChannelType::Public),
                total_msg_count: row.get(5)?,
                last_post_at: row.get(6)?,
                msg_count: row.get(7)?,
                mention_count: row.get(8)?,
                is_muted: row.get(9)?,
            })
        })?;
        rows.collect()
    }

    /// Direct toggle after a successful server-side mute/unmute — cheaper
    /// than waiting for the next full channel refetch to reflect it.
    pub fn set_channel_muted(&self, channel_id: &str, muted: bool) -> rusqlite::Result<()> {
        self.conn.execute("UPDATE channels SET is_muted = ?1 WHERE id = ?2", params![muted, channel_id])?;
        Ok(())
    }

    /// Keeps a channel's cached history bounded. Without this the posts
    /// table only ever grows: every read of a channel, every poll tick and
    /// every channel switch then costs a little more than the day before,
    /// which is what makes a client like this feel fine for a week and slow
    /// after a month. Only the newest `keep` posts of a channel survive.
    ///
    /// Thread replies are kept regardless of age when their root survives,
    /// so an open thread never loses its middle. Reactions and files go with
    /// the posts they belong to.
    pub fn prune_channel_posts(&self, channel_id: &str, keep: i64) -> rusqlite::Result<usize> {
        let doomed: Vec<String> = {
            let mut stmt = self.conn.prepare(
                "SELECT id FROM posts
                 WHERE channel_id = ?1
                   AND id NOT IN (
                       SELECT id FROM posts WHERE channel_id = ?1
                       ORDER BY create_at DESC LIMIT ?2
                   )
                   AND (root_id = '' OR root_id NOT IN (
                       SELECT id FROM posts WHERE channel_id = ?1
                       ORDER BY create_at DESC LIMIT ?2
                   ))",
            )?;
            let rows = stmt.query_map(params![channel_id, keep], |row| row.get(0))?;
            rows.collect::<rusqlite::Result<_>>()?
        };

        for id in &doomed {
            self.delete_post(id)?;
        }
        Ok(doomed.len())
    }

    /// A cheap fingerprint of what a channel currently holds, for the UI
    /// poll to ask "has anything changed?" without paying for the answer.
    /// Reading the channel in full costs ~2.3 ms and ~150 KB of JSON on a
    /// 455-post channel; this is ~0.06 ms and a dozen bytes, and the poll
    /// skips the full read whenever the fingerprint is unchanged — which is
    /// almost always, since most ticks happen with nobody posting.
    ///
    /// edit_at is in here alongside create_at, so a message edited by
    /// someone else now repaints too — it previously stayed stale until the
    /// channel was reopened. Reactions are deliberately NOT part of it:
    /// covering them would need a per-post lookup, the very cost this
    /// exists to avoid.
    pub fn channel_revision(&self, channel_id: &str) -> rusqlite::Result<ChannelRevision> {
        let (posts, last_create_at, last_edit_at, pinned) = self.conn.query_row(
            "SELECT COUNT(*), COALESCE(MAX(create_at), 0), COALESCE(MAX(edit_at), 0), COALESCE(SUM(is_pinned), 0)
             FROM posts WHERE channel_id = ?1",
            params![channel_id],
            |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?)),
        )?;
        let (reactions, last_reaction_seq) = self.reaction_fingerprint_for_channel(channel_id)?;
        Ok(ChannelRevision { posts, last_create_at, last_edit_at, reactions, last_reaction_seq, pinned })
    }

    /// Count plus highest rowid, so all three reaction moves are visible:
    /// an add changes both, a removal changes the count, and an add paired
    /// with a removal between two polls still changes the highest rowid.
    /// A plain count alone would let that last pair cancel out.
    fn reaction_fingerprint_for_channel(&self, channel_id: &str) -> rusqlite::Result<(i64, i64)> {
        self.conn.query_row(
            "SELECT COUNT(*), COALESCE(MAX(r.write_seq), 0)
             FROM reactions r JOIN posts p ON p.id = r.post_id
             WHERE p.channel_id = ?1",
            params![channel_id],
            |row| Ok((row.get(0)?, row.get(1)?)),
        )
    }

    /// Same idea as channel_revision, for the open thread panel.
    pub fn thread_revision(&self, root_id: &str) -> rusqlite::Result<ChannelRevision> {
        let (posts, last_create_at, last_edit_at, pinned) = self.conn.query_row(
            "SELECT COUNT(*), COALESCE(MAX(create_at), 0), COALESCE(MAX(edit_at), 0), COALESCE(SUM(is_pinned), 0)
             FROM posts WHERE id = ?1 OR root_id = ?1",
            params![root_id],
            |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?)),
        )?;
        let (reactions, last_reaction_seq) = self.conn.query_row(
            "SELECT COUNT(*), COALESCE(MAX(r.write_seq), 0)
             FROM reactions r JOIN posts p ON p.id = r.post_id
             WHERE p.id = ?1 OR p.root_id = ?1",
            params![root_id],
            |row| Ok((row.get(0)?, row.get(1)?)),
        )?;
        Ok(ChannelRevision { posts, last_create_at, last_edit_at, reactions, last_reaction_seq, pinned })
    }

    pub fn cached_posts_for_channel(&self, channel_id: &str) -> rusqlite::Result<Vec<Post>> {
        let mut stmt = self.conn.prepare(
            "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
                    (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json, p.is_pinned
             FROM posts p
             WHERE p.channel_id = ?1 ORDER BY p.create_at ASC",
        )?;
        let rows = stmt.query_map(params![channel_id], Self::post_from_row)?;
        self.attach_metadata(rows.collect::<rusqlite::Result<Vec<_>>>()?)
    }

    /// One message by id, straight from the cache. Reactions and files are
    /// deliberately not attached: the only caller wants the author and the
    /// text, to decide whether a reaction landed on this user's own message.
    pub fn cached_post(&self, post_id: &str) -> rusqlite::Result<Option<Post>> {
        self.conn
            .query_row(
                "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
                        (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json, p.is_pinned
                 FROM posts p WHERE p.id = ?1",
                params![post_id],
                Self::post_from_row,
            )
            .optional()
    }

    /// The root post plus all its replies, oldest first — what the thread
    /// panel shows.
    pub fn cached_thread_posts(&self, root_id: &str) -> rusqlite::Result<Vec<Post>> {
        let mut stmt = self.conn.prepare(
            "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
                    (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json, p.is_pinned
             FROM posts p
             WHERE p.id = ?1 OR p.root_id = ?1 ORDER BY p.create_at ASC",
        )?;
        let rows = stmt.query_map(params![root_id], Self::post_from_row)?;
        self.attach_metadata(rows.collect::<rusqlite::Result<Vec<_>>>()?)
    }

    /// The channel's pinned posts, newest first — the order the pinned panel
    /// reads in, and the same one the official client uses.
    pub fn cached_pinned_posts(&self, channel_id: &str) -> rusqlite::Result<Vec<Post>> {
        let mut stmt = self.conn.prepare(
            "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
                    (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json, p.is_pinned
             FROM posts p
             WHERE p.channel_id = ?1 AND p.is_pinned = 1 ORDER BY p.create_at DESC",
        )?;
        let rows = stmt.query_map(params![channel_id], Self::post_from_row)?;
        self.attach_metadata(rows.collect::<rusqlite::Result<Vec<_>>>()?)
    }

    /// Wipes the channel's pinned flags so a freshly fetched pinned list can
    /// be laid down over the top.
    ///
    /// The server's pinned endpoint reports only what IS pinned, never what
    /// stopped being — so a post someone else unpinned is recognised purely
    /// by its absence from the answer. Without this, an unpinned post would
    /// stay flagged locally until something else happened to refetch it.
    pub fn clear_pinned_for_channel(&self, channel_id: &str) -> rusqlite::Result<()> {
        self.conn.execute(
            "UPDATE posts SET is_pinned = 0 WHERE channel_id = ?1 AND is_pinned = 1",
            params![channel_id],
        )?;
        Ok(())
    }

    /// Local full-text search across every cached message, any channel or
    /// team — instant and works offline, but only covers what's actually
    /// been cached (recently viewed channels' recent history), unlike the
    /// server-side search this backs up.
    pub fn search_posts(&self, query: &str, limit: i64) -> rusqlite::Result<Vec<Post>> {
        let fts_query = Self::fts_match_query(query);
        if fts_query.is_empty() {
            return Ok(Vec::new());
        }
        let mut stmt = self.conn.prepare(
            "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
                    (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json, p.is_pinned
             FROM posts_fts f
             JOIN posts p ON p.rowid = f.rowid
             WHERE posts_fts MATCH ?1
             ORDER BY p.create_at DESC
             LIMIT ?2",
        )?;
        let rows = stmt.query_map(params![fts_query, limit], Self::post_from_row)?;
        self.attach_metadata(rows.collect::<rusqlite::Result<Vec<_>>>()?)
    }

    /// Turns free-form user input into a safe FTS5 MATCH expression: each
    /// word becomes a quoted prefix search, ANDed together. Quoting is what
    /// keeps this safe — it neutralizes FTS5's own query syntax (`-`, `*`,
    /// unbalanced `"`, ...) so a search for e.g. `C++ "great"` can't turn
    /// into a syntax error instead of a search.
    fn fts_match_query(raw: &str) -> String {
        raw.split_whitespace()
            .map(|word| format!("\"{}\"*", word.replace('"', "\"\"")))
            .collect::<Vec<_>>()
            .join(" ")
    }

    fn post_from_row(row: &rusqlite::Row) -> rusqlite::Result<Post> {
        let embeds_json: String = row.get(8)?;
        let embeds = serde_json::from_str(&embeds_json).unwrap_or_default();
        Ok(Post {
            id: row.get(0)?,
            channel_id: row.get(1)?,
            root_id: row.get(2)?,
            user_id: row.get(3)?,
            message: row.get(4)?,
            create_at: row.get(5)?,
            reply_count: row.get(6)?,
            edit_at: row.get(7)?,
            is_pinned: row.get(9)?,
            metadata: PostMetadata { embeds, ..Default::default() },
        })
    }

    pub fn cached_users(&self, user_ids: &[String]) -> rusqlite::Result<Vec<AuthenticatedUser>> {
        user_ids
            .iter()
            .filter_map(|id| self.cached_user(id).transpose())
            .collect()
    }

    pub fn cached_user(&self, user_id: &str) -> rusqlite::Result<Option<AuthenticatedUser>> {
        self.conn
            .query_row(
                "SELECT id, username, nickname, first_name, last_name FROM users WHERE id = ?1",
                params![user_id],
                |row| {
                    Ok(AuthenticatedUser {
                        id: row.get(0)?,
                        username: row.get(1)?,
                        email: String::new(),
                        nickname: row.get(2)?,
                        first_name: row.get(3)?,
                        last_name: row.get(4)?,
                    })
                },
            )
            .optional()
    }
}

/// Hands out strictly increasing stamps for reaction rows — see the
/// `write_seq` column. Process-wide rather than per-connection: there is one
/// cache database per process, and a single counter is what makes the
/// stamps comparable across every write that reaches it.
static REACTION_SEQ: AtomicI64 = AtomicI64::new(0);

/// A cheap fingerprint of a channel's (or thread's) cached contents. Every
/// field is derived from the data itself rather than maintained by hand, so
/// it cannot go stale because some write path forgot to bump a counter — a
/// fingerprint that misses a change means a message or a reaction that
/// never appears on screen.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ChannelRevision {
    pub posts: i64,
    pub last_create_at: i64,
    pub last_edit_at: i64,
    pub reactions: i64,
    pub last_reaction_seq: i64,
    /// How many of the channel's posts are pinned. Part of the fingerprint
    /// because pinning changes neither create_at nor edit_at, so nothing
    /// else here would move when a colleague pins something.
    pub pinned: i64,
}

fn app_data_dir() -> PathBuf {
    directories::ProjectDirs::from("com", "Almatter", "Almatter")
        .map(|dirs| dirs.data_dir().to_path_buf())
        .unwrap_or_else(|| PathBuf::from("."))
}

fn default_db_path() -> PathBuf {
    app_data_dir().join("cache.sqlite3")
}

/// Where cached custom-emoji images live, one file per emoji id — disk
/// rather than the SQLite cache, since the FFI boundary is JSON and images
/// are much cheaper to hand over as a file path than as base64.
pub fn emoji_image_dir() -> PathBuf {
    app_data_dir().join("emoji")
}

/// Where downloaded message attachments live, one subfolder per file id
/// (so the original filename — extension included, which matters for the
/// OS to know how to open it — can be kept as-is without name collisions
/// between different attachments that happen to share a filename).
pub fn downloads_dir() -> PathBuf {
    app_data_dir().join("files")
}

/// Where cached user avatar images live, one file per user id — same
/// reasoning as `emoji_image_dir`. Cached forever once fetched, same as
/// custom emoji images: good enough for a personal client, at the cost of
/// not picking up someone changing their avatar mid-session.
pub fn avatar_image_dir() -> PathBuf {
    app_data_dir().join("avatars")
}

/// Where cached link-preview images live, one file per (hashed) source
/// URL — same reasoning as `avatar_image_dir`, except the "id" here is an
/// arbitrary external URL rather than a Mattermost id, so it's hashed to a
/// filesystem-safe filename first (see dispatch.rs's `hash_url`).
pub fn link_preview_image_dir() -> PathBuf {
    app_data_dir().join("link-previews")
}

/// Same folder as the cache database — a plain-text diagnostic log for
/// things that would otherwise fail silently in the background (the
/// WebSocket connection, most notably), since this core has no console to
/// print to once running inside the WinExe app.
pub fn log_path() -> PathBuf {
    app_data_dir().join("core.log")
}

/// How large `core.log` may get before the old half is rotated away. Nothing
/// ever shortened this file, so it grew for as long as the app was installed.
/// One previous generation is kept, so a failure is still readable after the
/// rotation that happens to follow it.
const MAX_LOG_BYTES: u64 = 256 * 1024;

/// Best-effort append of one timestamped line — logging must never itself
/// cause a failure, so any error writing it is simply swallowed.
pub fn log(source: &str, message: &str) {
    use std::io::Write;
    // Unit tests share the real app's log path; their made-up failures
    // ("mention detected in channel c1") would read as real ones there.
    if cfg!(test) {
        return;
    }
    let now = chrono::Utc::now().to_rfc3339();
    let path = log_path();
    if let Some(parent) = path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    if std::fs::metadata(&path).is_ok_and(|m| m.len() > MAX_LOG_BYTES) {
        let _ = std::fs::rename(&path, path.with_extension("log.1"));
    }
    if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(&path) {
        let _ = writeln!(file, "[{now}] {source}: {message}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::AuthenticatedUser;

    #[test]
    fn opens_a_real_file_and_initializes_schema() {
        let dir = std::env::temp_dir().join(format!("almatter-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("cache.sqlite3");
        let db = Database::open(&path.to_string_lossy())
            .unwrap_or_else(|e| panic!("real-file db should open and init schema: {e}"));
        let count: i64 = db
            .connection()
            .query_row(
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='outbox'",
                [],
                |row| row.get(0),
            )
            .expect("query should succeed");
        assert_eq!(count, 1);
    }

    #[test]
    fn opens_and_initializes_schema() {
        let db = Database::open_in_memory().expect("in-memory db should open");
        let count: i64 = db
            .connection()
            .query_row(
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='outbox'",
                [],
                |row| row.get(0),
            )
            .expect("query should succeed");
        assert_eq!(count, 1);
    }

    #[test]
    fn round_trips_a_team_through_the_cache() {
        let db = Database::open_in_memory().unwrap();
        let team = Team { id: "t1".into(), name: "acme".into(), display_name: "Acme Corp".into() };
        db.upsert_team(&team).unwrap();

        let cached = db.cached_teams().unwrap();
        assert_eq!(cached.len(), 1);
        assert_eq!(cached[0].display_name, "Acme Corp");
    }

    #[test]
    fn upserting_a_team_twice_updates_rather_than_duplicates() {
        let db = Database::open_in_memory().unwrap();
        let mut team = Team { id: "t1".into(), name: "acme".into(), display_name: "Acme Corp".into() };
        db.upsert_team(&team).unwrap();
        team.display_name = "Acme Corp (renamed)".into();
        db.upsert_team(&team).unwrap();

        let cached = db.cached_teams().unwrap();
        assert_eq!(cached.len(), 1);
        assert_eq!(cached[0].display_name, "Acme Corp (renamed)");
    }

    #[test]
    fn round_trips_channels_scoped_to_a_team() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 42,
            last_post_at: 5000,
            msg_count: 30,
            mention_count: 2,
            is_muted: false,
        })
        .unwrap();

        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached.len(), 1);
        assert_eq!(cached[0].channel_type, ChannelType::Public);
        assert_eq!(cached[0].total_msg_count, 42);
        assert_eq!(cached[0].last_post_at, 5000);
        assert_eq!(cached[0].msg_count, 30);
        assert_eq!(cached[0].mention_count, 2);
    }

    #[test]
    fn cached_channels_for_team_also_returns_direct_messages() {
        // Mattermost sends DM/GM channels with an empty team_id — they
        // aren't scoped to any particular team — so a cache-only read for a
        // specific team must still include them, or the DM list would
        // disappear the moment something reads from the cache instead of
        // the network (which fetches the whole per-team channel list as one
        // batch and doesn't care about each channel's own team_id).
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 1,
            last_post_at: 1000,
            msg_count: 1,
            mention_count: 0,
            is_muted: false,
        })
        .unwrap();
        db.upsert_channel(&Channel {
            id: "dm1".into(),
            team_id: "".into(),
            name: "u1__u2".into(),
            display_name: "".into(),
            channel_type: ChannelType::Direct,
            total_msg_count: 3,
            last_post_at: 2000,
            msg_count: 2,
            mention_count: 1,
            is_muted: false,
        })
        .unwrap();

        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached.len(), 2);
        assert!(cached.iter().any(|c| c.id == "dm1"));
    }

    fn pinned_fixture(id: &str, channel_id: &str, create_at: i64, is_pinned: bool) -> Post {
        Post {
            id: id.into(),
            channel_id: channel_id.into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at,
            reply_count: 0,
            edit_at: 0,
            is_pinned,
            metadata: Default::default(),
        }
    }

    #[test]
    fn cached_pinned_posts_returns_only_pinned_newest_first() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&pinned_fixture("old", "c1", 1000, true)).unwrap();
        db.upsert_post(&pinned_fixture("plain", "c1", 2000, false)).unwrap();
        db.upsert_post(&pinned_fixture("new", "c1", 3000, true)).unwrap();
        db.upsert_post(&pinned_fixture("elsewhere", "c2", 4000, true)).unwrap();

        let pinned = db.cached_pinned_posts("c1").unwrap();
        let ids: Vec<&str> = pinned.iter().map(|p| p.id.as_str()).collect();
        assert_eq!(ids, vec!["new", "old"], "newest first, this channel only");
    }

    #[test]
    fn upsert_post_carries_the_pinned_flag_both_ways() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&pinned_fixture("p1", "c1", 1000, false)).unwrap();
        assert_eq!(db.cached_pinned_posts("c1").unwrap().len(), 0);

        db.upsert_post(&pinned_fixture("p1", "c1", 1000, true)).unwrap();
        assert_eq!(db.cached_pinned_posts("c1").unwrap().len(), 1);

        // An unpin arriving as a refetched post has to clear the flag again —
        // ON CONFLICT DO UPDATE must actually list is_pinned, or the post
        // would stay pinned locally for good.
        db.upsert_post(&pinned_fixture("p1", "c1", 1000, false)).unwrap();
        assert_eq!(db.cached_pinned_posts("c1").unwrap().len(), 0);
    }

    #[test]
    fn refetching_the_pinned_list_unpins_by_omission() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&pinned_fixture("kept", "c1", 1000, true)).unwrap();
        db.upsert_post(&pinned_fixture("dropped", "c1", 2000, true)).unwrap();
        db.upsert_post(&pinned_fixture("other_channel", "c2", 3000, true)).unwrap();

        // What the dispatcher does when the server answers with the pinned
        // list: clear the channel's flags, then lay the answer down over it.
        db.clear_pinned_for_channel("c1").unwrap();
        db.upsert_post(&pinned_fixture("kept", "c1", 1000, true)).unwrap();

        let ids: Vec<String> = db.cached_pinned_posts("c1").unwrap().into_iter().map(|p| p.id).collect();
        assert_eq!(ids, vec!["kept".to_string()], "a post missing from the answer loses its pin");

        // The clear is scoped to one channel — another channel's pins survive.
        assert_eq!(db.cached_pinned_posts("c2").unwrap().len(), 1);

        // And the post itself is still there, just no longer pinned.
        assert!(db.cached_posts_for_channel("c1").unwrap().iter().any(|p| p.id == "dropped"));
    }

    #[test]
    fn channel_revision_moves_when_a_post_is_pinned() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&pinned_fixture("p1", "c1", 1000, false)).unwrap();
        let before = db.channel_revision("c1").unwrap();

        // Pinning changes neither create_at nor edit_at, so without its own
        // term in the fingerprint the poll would never notice.
        db.upsert_post(&pinned_fixture("p1", "c1", 1000, true)).unwrap();
        let after = db.channel_revision("c1").unwrap();

        assert_eq!(before.posts, after.posts);
        assert_eq!(before.last_create_at, after.last_create_at);
        assert_eq!(before.last_edit_at, after.last_edit_at);
        assert_ne!(before.pinned, after.pinned, "the pinned count is what moves");
    }

    #[test]
    fn round_trips_posts_in_creation_order() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 0,
            last_post_at: 0,
            msg_count: 0,
            mention_count: 0,
            is_muted: false,
        })
        .unwrap();
        db.upsert_user(&AuthenticatedUser {
            id: "u1".into(),
            username: "jdupont".into(),
            email: "".into(),
            nickname: "".into(),
            first_name: "".into(),
            last_name: "".into(),
        })
        .unwrap();
        db.upsert_post(&Post { id: "p2".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "second".into(), create_at: 2000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();
        db.upsert_post(&Post { id: "p1".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "first".into(), create_at: 1000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        let posts = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(posts.len(), 2);
        assert_eq!(posts[0].message, "first");
        assert_eq!(posts[1].message, "second");
    }

    #[test]
    fn cached_thread_posts_returns_root_and_replies_only() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post { id: "root".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "root".into(), create_at: 1000, reply_count: 1, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();
        db.upsert_post(&Post { id: "reply".into(), channel_id: "c1".into(), root_id: "root".into(), user_id: "u2".into(), message: "reply".into(), create_at: 2000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();
        db.upsert_post(&Post { id: "other".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "unrelated".into(), create_at: 3000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        let thread = db.cached_thread_posts("root").unwrap();
        assert_eq!(thread.len(), 2);
        assert_eq!(thread[0].id, "root");
        assert_eq!(thread[1].id, "reply");
    }

    #[test]
    fn search_posts_matches_by_word_prefix_newest_first_and_ignores_others() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post { id: "p1".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "let's grab coffee tomorrow".into(), create_at: 1000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();
        db.upsert_post(&Post { id: "p2".into(), channel_id: "c2".into(), root_id: "".into(), user_id: "u2".into(), message: "the coffee machine is broken".into(), create_at: 2000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();
        db.upsert_post(&Post { id: "p3".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "unrelated message".into(), create_at: 3000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        // A partial word ("coff") should still match via prefix search, and
        // results should come back newest first, across every channel.
        let results = db.search_posts("coff", 50).unwrap();
        assert_eq!(results.len(), 2);
        assert_eq!(results[0].id, "p2");
        assert_eq!(results[1].id, "p1");
    }

    #[test]
    fn search_posts_with_special_characters_does_not_error() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post { id: "p1".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "does C++ compile?".into(), create_at: 1000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        // Raw FTS5 syntax characters (unbalanced quote, operators) must not
        // turn a search into a MATCH syntax error.
        let results = db.search_posts("C++ \"unterminated", 50).unwrap();
        assert!(results.is_empty() || results[0].id == "p1");

        assert!(db.search_posts("   ", 50).unwrap().is_empty());
    }

    /// Unlike a mention, several reaction notifications can be pending for
    /// the same message — one per person, per emoji — and none may swallow
    /// another. Only an exact repeat (a redelivered event) is ignored.
    #[test]
    fn reaction_events_queue_one_per_person_and_emoji() {
        let db = Database::open_in_memory().unwrap();
        db.enqueue_reaction_event("p1", "c1", "u1", "+1", "ship it", 1000).unwrap();
        db.enqueue_reaction_event("p1", "c1", "u2", "+1", "ship it", 1100).unwrap();
        db.enqueue_reaction_event("p1", "c1", "u1", "tada", "ship it", 1200).unwrap();
        db.enqueue_reaction_event("p1", "c1", "u1", "+1", "ship it", 1300).unwrap();

        let events = db.drain_reaction_events().unwrap();
        assert_eq!(events.len(), 3);
        assert_eq!(events[0].user_id, "u1");
        assert_eq!(events[0].emoji_name, "+1");
        assert_eq!(events[0].message, "ship it");
        assert!(db.drain_reaction_events().unwrap().is_empty(), "a drain empties the queue");
    }

    #[test]
    fn cancelling_a_reaction_event_leaves_the_others_queued() {
        let db = Database::open_in_memory().unwrap();
        db.enqueue_reaction_event("p1", "c1", "u1", "+1", "ship it", 1000).unwrap();
        db.enqueue_reaction_event("p1", "c1", "u2", "+1", "ship it", 1100).unwrap();

        db.cancel_reaction_event("p1", "u1", "+1").unwrap();

        let events = db.drain_reaction_events().unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].user_id, "u2");
    }

    #[test]
    fn mention_events_queue_drain_and_ignore_redelivery() {
        let db = Database::open_in_memory().unwrap();
        db.enqueue_mention_event("p1", "c1", "u1", "hey @you", 1000, true).unwrap();
        db.enqueue_mention_event("p2", "c1", "u2", "@you again", 2000, false).unwrap();
        // A redelivered copy of an already-queued mention must not duplicate it.
        db.enqueue_mention_event("p1", "c1", "u1", "hey @you", 1000, true).unwrap();

        let events = db.drain_mention_events().unwrap();
        assert_eq!(events.len(), 2);
        assert_eq!(events[0].post_id, "p1");
        assert!(events[0].is_mention);
        assert_eq!(events[1].post_id, "p2");
        assert!(!events[1].is_mention);

        // A drain empties the queue.
        assert!(db.drain_mention_events().unwrap().is_empty());
    }

    #[test]
    fn typing_users_for_channel_expires_stale_entries_and_scopes_by_channel() {
        let db = Database::open_in_memory().unwrap();
        db.record_typing("c1", "u1", 1000).unwrap();
        db.record_typing("c1", "u2", 1000).unwrap();
        db.record_typing("c2", "u3", 1000).unwrap();

        // Both still fresh (TTL 5000ms, only 1000ms old).
        let mut typing = db.typing_users_for_channel("c1", 2000, 5000).unwrap();
        typing.sort();
        assert_eq!(typing, vec!["u1".to_string(), "u2".to_string()]);
        // Scoped to the channel — c2's typer doesn't leak into c1's list.
        assert_eq!(db.typing_users_for_channel("c2", 2000, 5000).unwrap(), vec!["u3".to_string()]);

        // A repeat typing event bumps the timestamp instead of duplicating the row.
        db.record_typing("c1", "u1", 9000).unwrap();
        assert_eq!(db.typing_users_for_channel("c1", 9000, 5000).unwrap(), vec!["u1".to_string()]);

        // Now far enough past both TTLs that everything's expired (and pruned).
        assert!(db.typing_users_for_channel("c1", 50_000, 5000).unwrap().is_empty());
        assert!(db.typing_users_for_channel("c2", 50_000, 5000).unwrap().is_empty());
    }

    #[test]
    fn reply_count_is_computed_live_from_whats_actually_cached() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post { id: "root".into(), channel_id: "c1".into(), root_id: "".into(), user_id: "u1".into(), message: "root".into(), create_at: 1000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        let before = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(before[0].reply_count, 0);

        // A reply arriving later (e.g. over the WebSocket) needs no separate
        // bookkeeping step — the next read just sees it.
        db.upsert_post(&Post { id: "reply".into(), channel_id: "c1".into(), root_id: "root".into(), user_id: "u2".into(), message: "reply".into(), create_at: 2000, reply_count: 0, edit_at: 0, is_pinned: false, metadata: Default::default() }).unwrap();

        let after = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(after.iter().find(|p| p.id == "root").unwrap().reply_count, 1);
    }

    #[test]
    fn edit_at_round_trips_and_updates_on_re_upsert() {
        let db = Database::open_in_memory().unwrap();
        let mut post = Post {
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
        };
        db.upsert_post(&post).unwrap();
        assert_eq!(db.cached_posts_for_channel("c1").unwrap()[0].edit_at, 0);

        // An edit re-upserts the same post with a new edit_at — the cached
        // copy must pick that up, not just the message text.
        post.message = "hello (edited)".into();
        post.edit_at = 5000;
        db.upsert_post(&post).unwrap();

        let cached = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].message, "hello (edited)");
        assert_eq!(cached[0].edit_at, 5000);
    }

    #[test]
    fn embeds_round_trip_and_update_on_re_upsert() {
        use crate::models::{OpenGraphData, OpenGraphImage, PostEmbed, PostMetadata};

        let db = Database::open_in_memory().unwrap();
        let mut post = Post {
            id: "p1".into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "check this out https://example.com".into(),
            create_at: 1000,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: Default::default(),
        };
        db.upsert_post(&post).unwrap();
        assert!(db.cached_posts_for_channel("c1").unwrap()[0].metadata.embeds.is_empty());

        post.metadata = PostMetadata {
            embeds: vec![PostEmbed {
                embed_type: "opengraph".into(),
                url: "https://example.com".into(),
                data: Some(OpenGraphData {
                    title: "Example".into(),
                    description: "An example page".into(),
                    site_name: "Example.com".into(),
                    images: vec![OpenGraphImage { url: "https://example.com/img.png".into(), secure_url: "".into() }],
                }),
            }],
            ..Default::default()
        };
        db.upsert_post(&post).unwrap();

        let cached = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].metadata.embeds.len(), 1);
        let embed = &cached[0].metadata.embeds[0];
        assert_eq!(embed.embed_type, "opengraph");
        let data = embed.data.as_ref().unwrap();
        assert_eq!(data.title, "Example");
        assert_eq!(data.images[0].url, "https://example.com/img.png");
    }

    #[test]
    fn reactions_and_files_round_trip_and_replace_in_full() {
        use crate::models::{FileInfo, PostMetadata, Reaction};

        let db = Database::open_in_memory().unwrap();
        let post = Post {
            id: "p1".into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at: 1000,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: PostMetadata {
                reactions: vec![
                    Reaction { user_id: "u1".into(), emoji_name: "+1".into() },
                    Reaction { user_id: "u2".into(), emoji_name: "+1".into() },
                ],
                files: vec![FileInfo { id: "f1".into(), name: "diagram.png".into(), size: 2048 }],
                ..Default::default()
            },
        };
        db.upsert_post(&post).unwrap();

        let cached = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].metadata.reactions.len(), 2);
        assert_eq!(cached[0].metadata.files.len(), 1);
        assert_eq!(cached[0].metadata.files[0].name, "diagram.png");

        // Re-upserting with fewer reactions/files must drop the old ones,
        // not just add to them.
        let mut updated = post.clone();
        updated.metadata.reactions = vec![Reaction { user_id: "u1".into(), emoji_name: "+1".into() }];
        updated.metadata.files = vec![];
        db.upsert_post(&updated).unwrap();

        let cached = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached[0].metadata.reactions.len(), 1);
        assert!(cached[0].metadata.files.is_empty());
    }

    /// Metadata is loaded for the whole batch in two queries and then
    /// redistributed by post id, so the failure mode to guard against is
    /// one post inheriting another's reactions or files — or a post with
    /// none quietly picking some up.
    #[test]
    fn batched_metadata_is_attached_to_the_right_post() {
        use crate::models::{FileInfo, PostMetadata, Reaction};

        let db = Database::open_in_memory().unwrap();
        let post = |id: &str, create_at: i64, metadata: PostMetadata| Post {
            id: id.into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata,
        };

        db.upsert_post(&post("p1", 1000, PostMetadata {
            reactions: vec![
                Reaction { user_id: "u1".into(), emoji_name: "+1".into() },
                Reaction { user_id: "u2".into(), emoji_name: "tada".into() },
            ],
            files: vec![FileInfo { id: "f1".into(), name: "one.png".into(), size: 1 }],
            ..Default::default()
        })).unwrap();

        // Deliberately in the middle: it must stay empty.
        db.upsert_post(&post("p2", 2000, PostMetadata::default())).unwrap();

        db.upsert_post(&post("p3", 3000, PostMetadata {
            reactions: vec![Reaction { user_id: "u3".into(), emoji_name: "eyes".into() }],
            files: vec![
                FileInfo { id: "f2".into(), name: "two.png".into(), size: 2 },
                FileInfo { id: "f3".into(), name: "three.png".into(), size: 3 },
            ],
            ..Default::default()
        })).unwrap();

        let cached = db.cached_posts_for_channel("c1").unwrap();
        assert_eq!(cached.len(), 3);

        assert_eq!(cached[0].id, "p1");
        assert_eq!(cached[0].metadata.reactions.len(), 2);
        assert_eq!(cached[0].metadata.files.len(), 1);
        assert_eq!(cached[0].metadata.files[0].name, "one.png");

        assert_eq!(cached[1].id, "p2");
        assert!(cached[1].metadata.reactions.is_empty());
        assert!(cached[1].metadata.files.is_empty());

        assert_eq!(cached[2].id, "p3");
        assert_eq!(cached[2].metadata.reactions.len(), 1);
        assert_eq!(cached[2].metadata.reactions[0].emoji_name, "eyes");
        let mut names: Vec<&str> = cached[2].metadata.files.iter().map(|f| f.name.as_str()).collect();
        names.sort_unstable();
        assert_eq!(names, ["three.png", "two.png"]);
    }

    /// Pruning deletes cached history, so the risks are all about deleting
    /// the wrong thing: another channel's posts, or a reply whose root is
    /// still on screen.
    #[test]
    fn pruning_keeps_the_newest_and_spares_other_channels_and_live_threads() {
        let db = Database::open_in_memory().unwrap();
        let post = |id: &str, channel: &str, root: &str, create_at: i64| Post {
            id: id.into(),
            channel_id: channel.into(),
            root_id: root.into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: Default::default(),
        };

        // c1: five standalone posts, oldest first.
        for i in 1..=5 {
            db.upsert_post(&post(&format!("p{i}"), "c1", "", i as i64 * 100))
                .unwrap();
        }
        // An old reply hanging off the newest post — it must survive even
        // though its own create_at is ancient.
        db.upsert_post(&post("reply-old", "c1", "p5", 1)).unwrap();
        // Another channel, untouched by pruning c1.
        db.upsert_post(&post("other", "c2", "", 1)).unwrap();

        let removed = db.prune_channel_posts("c1", 2).unwrap();
        assert_eq!(removed, 3, "p1, p2 and p3 should go");

        let kept: Vec<String> = db
            .cached_posts_for_channel("c1")
            .unwrap()
            .into_iter()
            .map(|p| p.id)
            .collect();
        assert!(kept.contains(&"p4".to_string()));
        assert!(kept.contains(&"p5".to_string()));
        assert!(
            kept.contains(&"reply-old".to_string()),
            "a reply whose root survives must survive too, whatever its age"
        );
        assert!(!kept.contains(&"p1".to_string()));

        assert_eq!(db.cached_posts_for_channel("c2").unwrap().len(), 1, "another channel must be untouched");

        // Pruning a channel already under the limit changes nothing.
        assert_eq!(db.prune_channel_posts("c1", 1000).unwrap(), 0);
    }

    /// Live reactions depend entirely on the fingerprint noticing them: the
    /// posts themselves do not change when someone reacts, so without the
    /// reaction half the UI would cache the reaction and never repaint it.
    #[test]
    fn channel_revision_moves_when_a_reaction_is_added_or_removed() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post {
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
        })
        .unwrap();

        let before = db.channel_revision("c1").unwrap();

        db.add_cached_reaction("p1", "+1", "u2").unwrap();
        let after_add = db.channel_revision("c1").unwrap();
        assert_ne!(after_add, before, "an added reaction must move the fingerprint");

        db.remove_cached_reaction("p1", "+1", "u2").unwrap();
        let after_remove = db.channel_revision("c1").unwrap();
        assert_ne!(after_remove, after_add, "a removed reaction must move it too");

        // One added and one removed between two polls must not cancel out —
        // that is what the highest-rowid half of the fingerprint is for.
        db.add_cached_reaction("p1", "eyes", "u3").unwrap();
        let one_reaction = db.channel_revision("c1").unwrap();
        db.remove_cached_reaction("p1", "eyes", "u3").unwrap();
        db.add_cached_reaction("p1", "tada", "u4").unwrap();
        let still_one_reaction = db.channel_revision("c1").unwrap();
        assert_eq!(still_one_reaction.reactions, one_reaction.reactions, "same count, by construction");
        assert_ne!(still_one_reaction, one_reaction, "but the fingerprint must still differ");

        // A reaction on a post that was never cached is dropped rather than
        // left dangling for a future fetch of that post to inherit.
        db.add_cached_reaction("never-cached", "+1", "u2").unwrap();
        assert_eq!(db.channel_revision("c1").unwrap(), still_one_reaction);
    }

    /// The whole polling optimisation rests on this contract: the
    /// fingerprint must move whenever the channel's contents move, and stay
    /// put otherwise. A fingerprint that misses a change means a message
    /// that never appears.
    #[test]
    fn channel_revision_moves_on_add_edit_and_delete_and_is_otherwise_stable() {
        let db = Database::open_in_memory().unwrap();
        let post = |id: &str, create_at: i64, edit_at: i64| Post {
            id: id.into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at,
            reply_count: 0,
            edit_at,
            is_pinned: false,
            metadata: Default::default(),
        };

        let empty = db.channel_revision("c1").unwrap();
        assert_eq!(empty.posts, 0);
        assert_eq!(empty.reactions, 0);

        db.upsert_post(&post("p1", 1000, 0)).unwrap();
        let after_first = db.channel_revision("c1").unwrap();
        assert_ne!(after_first, empty, "a new post must move the fingerprint");

        // Reading again without touching anything must not move it — this is
        // what lets the poll skip the expensive read.
        assert_eq!(db.channel_revision("c1").unwrap(), after_first);

        db.upsert_post(&post("p2", 2000, 0)).unwrap();
        let after_second = db.channel_revision("c1").unwrap();
        assert_ne!(after_second, after_first);

        // An edit keeps the same id and create_at, so only edit_at can
        // reveal it. Without this the message stayed stale on screen.
        db.upsert_post(&post("p2", 2000, 2500)).unwrap();
        let after_edit = db.channel_revision("c1").unwrap();
        assert_ne!(after_edit, after_second, "an edit must move the fingerprint");

        db.delete_post("p2").unwrap();
        let after_delete = db.channel_revision("c1").unwrap();
        assert_ne!(after_delete, after_edit, "a deletion must move the fingerprint");

        // Another channel's traffic must not disturb this one.
        let mut other = post("p9", 9000, 0);
        other.channel_id = "c2".into();
        db.upsert_post(&other).unwrap();
        assert_eq!(db.channel_revision("c1").unwrap(), after_delete);
    }

    #[test]
    fn delete_post_removes_it_and_its_reactions_and_files() {
        use crate::models::{FileInfo, PostMetadata, Reaction};

        let db = Database::open_in_memory().unwrap();
        db.upsert_post(&Post {
            id: "p1".into(),
            channel_id: "c1".into(),
            root_id: "".into(),
            user_id: "u1".into(),
            message: "hello".into(),
            create_at: 1000,
            reply_count: 0,
            edit_at: 0,
            is_pinned: false,
            metadata: PostMetadata {
                reactions: vec![Reaction { user_id: "u1".into(), emoji_name: "+1".into() }],
                files: vec![FileInfo { id: "f1".into(), name: "diagram.png".into(), size: 2048 }],
                ..Default::default()
            },
        })
        .unwrap();
        assert_eq!(db.cached_posts_for_channel("c1").unwrap().len(), 1);

        db.delete_post("p1").unwrap();

        assert!(db.cached_posts_for_channel("c1").unwrap().is_empty());
        let reactions_left: i64 = db
            .conn
            .query_row("SELECT COUNT(*) FROM reactions WHERE post_id = 'p1'", [], |row| row.get(0))
            .unwrap();
        let files_left: i64 = db
            .conn
            .query_row("SELECT COUNT(*) FROM files WHERE post_id = 'p1'", [], |row| row.get(0))
            .unwrap();
        assert_eq!(reactions_left, 0);
        assert_eq!(files_left, 0);
    }

    #[test]
    fn mark_channel_read_zeroes_out_the_unread_gap() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 42,
            last_post_at: 5000,
            msg_count: 10,
            mention_count: 3,
            is_muted: false,
        })
        .unwrap();

        db.mark_channel_read("c1").unwrap();

        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached[0].msg_count, 42);
        assert_eq!(cached[0].mention_count, 0);
    }

    #[test]
    fn set_channel_muted_toggles_the_cached_flag() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 0,
            last_post_at: 0,
            msg_count: 0,
            mention_count: 0,
            is_muted: false,
        })
        .unwrap();

        db.set_channel_muted("c1", true).unwrap();
        assert!(db.cached_channels_for_team("t1").unwrap()[0].is_muted);

        db.set_channel_muted("c1", false).unwrap();
        assert!(!db.cached_channels_for_team("t1").unwrap()[0].is_muted);
    }

    #[test]
    fn bump_channel_activity_widens_the_unread_gap_live() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_team(&Team { id: "t1".into(), name: "acme".into(), display_name: "Acme".into() }).unwrap();
        db.upsert_channel(&Channel {
            id: "c1".into(),
            team_id: "t1".into(),
            name: "general".into(),
            display_name: "General".into(),
            channel_type: ChannelType::Public,
            total_msg_count: 10,
            last_post_at: 5000,
            msg_count: 10,
            mention_count: 0,
            is_muted: false,
        })
        .unwrap();

        db.bump_channel_activity("c1", 6000, false, false).unwrap();
        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached[0].total_msg_count, 11);
        assert_eq!(cached[0].msg_count, 10);
        assert_eq!(cached[0].mention_count, 0);
        assert_eq!(cached[0].last_post_at, 6000);

        db.bump_channel_activity("c1", 7000, false, true).unwrap();
        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached[0].total_msg_count, 12);
        assert_eq!(cached[0].mention_count, 1);
        assert_eq!(cached[0].last_post_at, 7000);

        // The user's own message moves the channel up without making it unread.
        db.bump_channel_activity("c1", 8000, true, false).unwrap();
        let cached = db.cached_channels_for_team("t1").unwrap();
        assert_eq!(cached[0].total_msg_count - cached[0].msg_count, 2);
        assert_eq!(cached[0].last_post_at, 8000);

        // A late-arriving older post never moves the channel back in time.
        db.bump_channel_activity("c1", 1000, false, false).unwrap();
        assert_eq!(db.cached_channels_for_team("t1").unwrap()[0].last_post_at, 8000);

        // A channel not yet cached is simply a no-op, not an error.
        db.bump_channel_activity("unknown-channel", 1000, false, true).unwrap();
    }

    #[test]
    fn favorite_channels_replace_wholesale_and_toggle_individually() {
        let db = Database::open_in_memory().unwrap();
        db.replace_favorite_channels(&["c1".into(), "c2".into()]).unwrap();
        assert_eq!(db.cached_favorite_channel_ids().unwrap().len(), 2);

        // A fresh fetch overwrites the whole set, not merges into it.
        db.replace_favorite_channels(&["c1".into()]).unwrap();
        assert_eq!(db.cached_favorite_channel_ids().unwrap(), vec!["c1".to_string()]);

        // The app's own toggle updates one row without needing a re-fetch.
        db.set_favorite_channel("c2", true).unwrap();
        let mut ids = db.cached_favorite_channel_ids().unwrap();
        ids.sort();
        assert_eq!(ids, vec!["c1".to_string(), "c2".to_string()]);

        db.set_favorite_channel("c1", false).unwrap();
        assert_eq!(db.cached_favorite_channel_ids().unwrap(), vec!["c2".to_string()]);
    }

    #[test]
    fn channel_participants_replace_wholesale_and_scope_by_channel() {
        let db = Database::open_in_memory().unwrap();
        db.replace_channel_participants("gm1", &["u1".into(), "u2".into(), "u3".into()])
            .unwrap();
        db.replace_channel_participants("gm2", &["u4".into()]).unwrap();

        let mut gm1 = db.cached_channel_participants("gm1").unwrap();
        gm1.sort();
        assert_eq!(gm1, vec!["u1".to_string(), "u2".to_string(), "u3".to_string()]);
        assert_eq!(db.cached_channel_participants("gm2").unwrap(), vec!["u4".to_string()]);

        // A fresh fetch overwrites gm1's whole set, not merges into it.
        db.replace_channel_participants("gm1", &["u1".into()]).unwrap();
        assert_eq!(db.cached_channel_participants("gm1").unwrap(), vec!["u1".to_string()]);
        // Untouched.
        assert_eq!(db.cached_channel_participants("gm2").unwrap(), vec!["u4".to_string()]);
    }

    #[test]
    fn round_trips_custom_emoji() {
        use crate::models::CustomEmoji;

        let db = Database::open_in_memory().unwrap();
        db.upsert_custom_emoji(&CustomEmoji { id: "e1".into(), name: "party-parrot".into() }).unwrap();
        db.upsert_custom_emoji(&CustomEmoji { id: "e1".into(), name: "party-parrot-renamed".into() }).unwrap();

        let cached = db.cached_custom_emoji().unwrap();
        assert_eq!(cached.len(), 1);
        assert_eq!(cached[0].name, "party-parrot-renamed");
    }

    #[test]
    fn cached_user_resolves_display_name() {
        let db = Database::open_in_memory().unwrap();
        db.upsert_user(&AuthenticatedUser {
            id: "u1".into(),
            username: "jdupont".into(),
            email: "".into(),
            nickname: "".into(),
            first_name: "Jean".into(),
            last_name: "Dupont".into(),
        })
        .unwrap();

        let user = db.cached_user("u1").unwrap().expect("user should be cached");
        assert_eq!(user.display_name(), "Jean Dupont");
        assert!(db.cached_user("missing").unwrap().is_none());
    }
}
