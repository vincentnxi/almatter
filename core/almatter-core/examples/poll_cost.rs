//! Measurement tool: times the core call the UI polling loop
//! repeats forever, against a copy of the real cache, and compares it with a
//! minimal "has anything changed?" probe.
//! Run: cargo run -p almatter-core --release --example poll_cost -- <db copy> <channel_id>

use std::sync::Mutex;
use std::time::Instant;

use almatter_core::db::Database;
use almatter_core::dispatch::dispatch;

#[tokio::main]
async fn main() {
    let path = std::env::args().nth(1).expect("usage: poll_cost <db path> <channel_id>");
    let channel = {
        let c = rusqlite::Connection::open(&path).expect("open for pick");
        c.query_row("SELECT channel_id FROM posts GROUP BY channel_id ORDER BY COUNT(*) DESC LIMIT 1", [], |r| r.get::<_, String>(0)).expect("busiest channel")
    };
    println!("canal mesuré : {channel}");

    let db: &'static Mutex<Database> =
        Box::leak(Box::new(Mutex::new(Database::open(&path).expect("open cache"))));

    let request = format!(r#"{{"command":"get_cached_posts","channel_id":"{channel}"}}"#);

    // Warm up: first call pays page-cache and statement costs once.
    let warm = dispatch(&request, db).await;
    println!("charge utile JSON : {:.1} Ko", warm.len() as f64 / 1024.0);

    const RUNS: u32 = 50;
    let start = Instant::now();
    for _ in 0..RUNS {
        std::hint::black_box(dispatch(&request, db).await);
    }
    let full = start.elapsed() / RUNS;
    println!("get_cached_posts        : {:>8.2} ms / appel", full.as_secs_f64() * 1000.0);

    // The real new command, measured end to end through the dispatcher
    // (JSON envelope included), not just the raw SQL.
    let probe_req = format!(r#"{{"command":"get_channel_revision","channel_id":"{channel}"}}"#);
    let probe_out = dispatch(&probe_req, db).await;
    println!("réponse sonde         : {} octets  {}", probe_out.len(), probe_out);
    let start = Instant::now();
    for _ in 0..RUNS {
        std::hint::black_box(dispatch(&probe_req, db).await);
    }
    let via_dispatch = start.elapsed() / RUNS;
    println!("get_channel_revision    : {:>8.3} ms / appel", via_dispatch.as_secs_f64() * 1000.0);

    // What a cheap change-probe would cost instead: one aggregate row.
    let start = Instant::now();
    for _ in 0..RUNS {
        let guard = db.lock().unwrap();
        let probe: (i64, i64, i64) = guard
            .connection()
            .query_row(
                "SELECT COUNT(*), COALESCE(MAX(create_at),0), COALESCE(MAX(edit_at),0)
                 FROM posts WHERE channel_id = ?1",
                [&channel],
                |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)),
            )
            .unwrap();
        std::hint::black_box(probe);
    }
    let probe = start.elapsed() / RUNS;
    println!("sonde COUNT/MAX         : {:>8.3} ms / appel", probe.as_secs_f64() * 1000.0);
    println!("rapport                 : {:>8.0}x", full.as_secs_f64() / probe.as_secs_f64());
}
