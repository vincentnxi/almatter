use crate::db::Database;
use rusqlite::params;
use serde::Serialize;

/// One queued message, composed while offline (or while a send attempt
/// failed for a connectivity reason) and waiting to be replayed.
#[derive(Debug, Clone, Serialize)]
pub struct OutboxMessage {
    pub local_id: String,
    pub channel_id: String,
    pub root_id: Option<String>,
    pub message: String,
    pub created_at: i64,
    pub attempt_count: i64,
}

/// Owns the offline outbox: messages composed while disconnected are queued
/// here and replayed against the API once the connection returns.
pub struct SyncEngine<'a> {
    db: &'a Database,
}

impl<'a> SyncEngine<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn enqueue_outbox_message(
        &self,
        local_id: &str,
        channel_id: &str,
        root_id: Option<&str>,
        message: &str,
        created_at: i64,
    ) -> rusqlite::Result<()> {
        self.db.connection().execute(
            "INSERT INTO outbox (local_id, channel_id, root_id, message, created_at) \
             VALUES (?1, ?2, ?3, ?4, ?5)",
            params![local_id, channel_id, root_id, message, created_at],
        )?;
        Ok(())
    }

    pub fn pending_count(&self) -> rusqlite::Result<i64> {
        self.db
            .connection()
            .query_row("SELECT count(*) FROM outbox", [], |row| row.get(0))
    }

    /// Every queued message, oldest first — what both the UI (to show
    /// pending bubbles) and flush_pending (to replay them in order) read.
    pub fn pending_messages(&self) -> rusqlite::Result<Vec<OutboxMessage>> {
        let mut stmt = self.db.connection().prepare(
            "SELECT local_id, channel_id, root_id, message, created_at, attempt_count \
             FROM outbox ORDER BY created_at ASC",
        )?;
        let rows = stmt.query_map([], |row| {
            Ok(OutboxMessage {
                local_id: row.get(0)?,
                channel_id: row.get(1)?,
                root_id: row.get(2)?,
                message: row.get(3)?,
                created_at: row.get(4)?,
                attempt_count: row.get(5)?,
            })
        })?;
        rows.collect()
    }

    pub fn remove_outbox_message(&self, local_id: &str) -> rusqlite::Result<()> {
        self.db
            .connection()
            .execute("DELETE FROM outbox WHERE local_id = ?1", params![local_id])?;
        Ok(())
    }

    pub fn bump_attempt_count(&self, local_id: &str) -> rusqlite::Result<()> {
        self.db.connection().execute(
            "UPDATE outbox SET attempt_count = attempt_count + 1 WHERE local_id = ?1",
            params![local_id],
        )?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn queues_a_message_while_offline() {
        let db = Database::open_in_memory().unwrap();
        let sync = SyncEngine::new(&db);
        sync.enqueue_outbox_message("local-1", "chan-1", None, "hello", 0)
            .unwrap();
        assert_eq!(sync.pending_count().unwrap(), 1);
    }

    #[test]
    fn pending_messages_round_trip_and_can_be_removed() {
        let db = Database::open_in_memory().unwrap();
        let sync = SyncEngine::new(&db);
        sync.enqueue_outbox_message("local-1", "chan-1", None, "hello", 1000)
            .unwrap();
        sync.enqueue_outbox_message("local-2", "chan-1", Some("root-1"), "a reply", 2000)
            .unwrap();

        let pending = sync.pending_messages().unwrap();
        assert_eq!(pending.len(), 2);
        assert_eq!(pending[0].local_id, "local-1");
        assert_eq!(pending[0].root_id, None);
        assert_eq!(pending[1].root_id, Some("root-1".to_string()));

        sync.bump_attempt_count("local-1").unwrap();
        let pending = sync.pending_messages().unwrap();
        assert_eq!(pending[0].attempt_count, 1);

        sync.remove_outbox_message("local-1").unwrap();
        assert_eq!(sync.pending_count().unwrap(), 1);
    }
}
