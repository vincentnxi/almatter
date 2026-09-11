//! Measurement tool: prints what the local cache actually holds,
//! so performance work is driven by real numbers rather than guesses.
//! Opens the database read-only, safe to run while the app is live (WAL).
//! Run: cargo run -p almatter-core --example cache_stats -- <path to cache.sqlite3>

use rusqlite::{Connection, OpenFlags};

fn main() -> rusqlite::Result<()> {
    let path = std::env::args().nth(1).expect("usage: cache_stats <db path>");
    let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;

    let page_count: i64 = conn.query_row("PRAGMA page_count", [], |r| r.get(0))?;
    let page_size: i64 = conn.query_row("PRAGMA page_size", [], |r| r.get(0))?;
    println!("taille base : {:.2} Mo", (page_count * page_size) as f64 / 1_048_576.0);

    for table in [
        "posts", "channels", "users", "reactions", "files", "custom_emoji",
        "mention_events", "typing_events", "channel_participants", "outbox",
    ] {
        let n: i64 = conn.query_row(&format!("SELECT COUNT(*) FROM {table}"), [], |r| r.get(0))?;
        println!("{table:22} {n:>8}");
    }

    println!("\nposts par canal (10 premiers) :");
    let mut stmt = conn.prepare(
        "SELECT p.channel_id, COALESCE(c.display_name, '?'), COUNT(*) AS n,
                SUM(LENGTH(p.message)), SUM(LENGTH(p.embeds_json))
         FROM posts p LEFT JOIN channels c ON c.id = p.channel_id
         GROUP BY p.channel_id ORDER BY n DESC LIMIT 10",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok((
            r.get::<_, String>(0)?,
            r.get::<_, String>(1)?,
            r.get::<_, i64>(2)?,
            r.get::<_, i64>(3)?,
            r.get::<_, i64>(4)?,
        ))
    })?;
    for row in rows {
        let (_id, name, n, msg_bytes, embed_bytes) = row?;
        println!(
            "  {n:>6} posts  {:>8.1} Ko texte  {:>8.1} Ko embeds   {name}",
            msg_bytes as f64 / 1024.0,
            embed_bytes as f64 / 1024.0
        );
    }
    Ok(())
}
