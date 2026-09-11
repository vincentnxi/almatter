//! Measurement tool: times the cheap core calls the UI poll makes on every tick,
//! to decide how fast that loop can reasonably run. Read-only in practice
//! (the mention queue is empty on an idle cache).
use std::sync::Mutex;
use std::time::Instant;

use almatter_core::db::Database;
use almatter_core::dispatch::dispatch;

#[tokio::main]
async fn main() {
    let path = std::env::args().nth(1).expect("usage: tick_cost <db path>");
    let db: &'static Mutex<Database> =
        Box::leak(Box::new(Mutex::new(Database::open(&path).expect("open cache"))));
    let channel: String = {
        let c = rusqlite::Connection::open(&path).unwrap();
        c.query_row(
            "SELECT channel_id FROM posts GROUP BY channel_id ORDER BY COUNT(*) DESC LIMIT 1",
            [],
            |r| r.get(0),
        )
        .unwrap()
    };

    let team: String = {
        let c = rusqlite::Connection::open(&path).unwrap();
        c.query_row("SELECT team_id FROM channels WHERE team_id <> '' LIMIT 1", [], |r| r.get(0))
            .unwrap_or_default()
    };
    let calls: Vec<(&str, String)> = vec![
        ("get_and_clear_mention_events", r#"{"command":"get_and_clear_mention_events"}"#.into()),
        ("get_cached_outbox", r#"{"command":"get_cached_outbox"}"#.into()),
        ("get_channel_revision", format!(r#"{{"command":"get_channel_revision","channel_id":"{channel}"}}"#)),
        ("get_typing_users", format!(r#"{{"command":"get_typing_users","channel_id":"{channel}"}}"#)),
        ("get_cached_channels", format!(r#"{{"command":"get_cached_channels","team_id":"{team}"}}"#)),
    ];

    const RUNS: u32 = 200;
    let mut total = 0.0;
    for (label, request) in &calls {
        let _ = dispatch(request, db).await; // warm up
        let start = Instant::now();
        for _ in 0..RUNS {
            std::hint::black_box(dispatch(request, db).await);
        }
        let ms = start.elapsed().as_secs_f64() * 1000.0 / RUNS as f64;
        total += ms;
        println!("{label:<32} {ms:>7.3} ms");
    }
    println!("{:<32} {total:>7.3} ms  (tick au repos, hors lecture complète)", "TOTAL");
}
