//! Business logic only: API client, SQLite cache, offline sync engine. No
//! `unsafe`, and none is allowed to creep in — the `extern "C"` boundary
//! that necessarily needs it lives in the separate `almatter-ffi` crate.
#![forbid(unsafe_code)]

pub mod api;
pub mod db;
pub mod dispatch;
pub mod models;
pub mod sync;
pub mod ws;
