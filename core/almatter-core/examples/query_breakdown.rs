//! Measurement tool: splits the cost of loading a channel's cached posts into its
//! parts, to see what is worth fixing. Read-only.
use rusqlite::{Connection, OpenFlags};
use std::time::Instant;

const MAIN_SQL: &str = "SELECT p.id, p.channel_id, p.root_id, p.user_id, p.message, p.create_at,
        (SELECT COUNT(*) FROM posts r WHERE r.root_id = p.id) AS reply_count, p.edit_at, p.embeds_json
     FROM posts p WHERE p.channel_id = ?1 ORDER BY p.create_at ASC";

const MAIN_SQL_NO_SUBQUERY: &str = "SELECT p.id, p.message, p.create_at, p.edit_at, p.embeds_json
     FROM posts p WHERE p.channel_id = ?1 ORDER BY p.create_at ASC";

fn bench(label: &str, runs: u32, mut f: impl FnMut()) -> f64 {
    let start = Instant::now();
    for _ in 0..runs { f(); }
    let ms = start.elapsed().as_secs_f64() * 1000.0 / runs as f64;
    println!("{label:<48} {ms:>8.3} ms");
    ms
}

fn main() -> rusqlite::Result<()> {
    let path = std::env::args().nth(1).expect("usage: query_breakdown <db path>");
    let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;
    let channel: String = conn.query_row(
        "SELECT channel_id FROM posts GROUP BY channel_id ORDER BY COUNT(*) DESC LIMIT 1", [], |r| r.get(0))?;
    let n: i64 = conn.query_row("SELECT COUNT(*) FROM posts WHERE channel_id = ?1", [&channel], |r| r.get(0))?;
    println!("canal : {n} posts\n");

    let ids: Vec<String> = {
        let mut s = conn.prepare(MAIN_SQL)?;
        let rows = s.query_map([&channel], |r| r.get::<_, String>(0))?;
        rows.collect::<rusqlite::Result<_>>()?
    };

    bench("SELECT principal (avec sous-requête reply_count)", 50, || {
        let mut s = conn.prepare(MAIN_SQL).unwrap();
        let rows = s.query_map([&channel], |r| r.get::<_, String>(0)).unwrap();
        std::hint::black_box(rows.collect::<rusqlite::Result<Vec<String>>>().unwrap());
    });
    bench("SELECT principal (sans la sous-requête)", 50, || {
        let mut s = conn.prepare(MAIN_SQL_NO_SUBQUERY).unwrap();
        let rows = s.query_map([&channel], |r| r.get::<_, String>(0)).unwrap();
        std::hint::black_box(rows.collect::<rusqlite::Result<Vec<String>>>().unwrap());
    });
    bench("métadonnées N+1, prepare à chaque post (actuel)", 50, || {
        for id in &ids {
            let mut s = conn.prepare("SELECT emoji_name, user_id FROM reactions WHERE post_id = ?1").unwrap();
            std::hint::black_box(s.query_map([id], |r| r.get::<_, String>(0)).unwrap().count());
            let mut s = conn.prepare("SELECT id, name, size FROM files WHERE post_id = ?1").unwrap();
            std::hint::black_box(s.query_map([id], |r| r.get::<_, String>(0)).unwrap().count());
        }
    });
    bench("métadonnées N+1, prepare une seule fois", 50, || {
        let mut sr = conn.prepare_cached("SELECT emoji_name, user_id FROM reactions WHERE post_id = ?1").unwrap();
        let mut sf = conn.prepare_cached("SELECT id, name, size FROM files WHERE post_id = ?1").unwrap();
        for id in &ids {
            std::hint::black_box(sr.query_map([id], |r| r.get::<_, String>(0)).unwrap().count());
            std::hint::black_box(sf.query_map([id], |r| r.get::<_, String>(0)).unwrap().count());
        }
    });
    bench("métadonnées en 2 requêtes groupées (JOIN)", 50, || {
        let mut s = conn.prepare_cached(
            "SELECT r.post_id, r.emoji_name, r.user_id FROM reactions r
             JOIN posts p ON p.id = r.post_id WHERE p.channel_id = ?1").unwrap();
        std::hint::black_box(s.query_map([&channel], |r| r.get::<_, String>(0)).unwrap().count());
        let mut s = conn.prepare_cached(
            "SELECT f.post_id, f.id, f.name, f.size FROM files f
             JOIN posts p ON p.id = f.post_id WHERE p.channel_id = ?1").unwrap();
        std::hint::black_box(s.query_map([&channel], |r| r.get::<_, String>(0)).unwrap().count());
    });
    Ok(())
}
