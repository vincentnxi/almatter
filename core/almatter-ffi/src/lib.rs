//! The single point of contact with the .NET side. `almatter-core` is
//! `#![forbid(unsafe_code)]`; everything unavoidable about an `extern "C"`
//! boundary — raw pointers in and out, reclaiming ownership across
//! languages — is fenced off here instead, in as little code as possible.
//! Every Mattermost API call is exposed as ONE generic JSON-in/JSON-out
//! function (`almatter_core_call`) rather than one `extern "C"` function per
//! call, so this unsafe surface never grows as the API surface does — new
//! calls are added as `Request` variants in `almatter_core::dispatch`, which
//! is plain safe Rust.
//!
//! A panic that unwinds across an `extern "C"` boundary is undefined
//! behavior, so Rust aborts the whole process immediately rather than
//! letting it happen — with a GUI app built as `WinExe`, that means no
//! console, no dialog, nothing: the window just vanishes. `catch_unwind`
//! below turns any such panic into an ordinary `{"ok":false,...}` response
//! instead, in the one place it can — `AssertUnwindSafe` is a promise, not
//! an `unsafe` operation, so this doesn't touch the unsafe budget.
//!
//! The two functions taking a pointer are `unsafe fn`, which is what they
//! are: whether the address .NET hands over is valid is a promise only the
//! caller can keep. That word is also what clippy's `not_unsafe_ptr_arg_deref`
//! asks for, and without it a `cargo clippy` run stopped here — with an
//! error, not a warning — and never reached the rest of the workspace.
//! Marking them changes nothing for .NET: an `unsafe fn` exports the same
//! symbol under the same ABI, and the word means something only to a Rust
//! caller, of which these have none.
#![deny(unsafe_op_in_unsafe_fn)]

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic::AssertUnwindSafe;
use std::sync::{Mutex, OnceLock};

use tokio::runtime::Runtime;
use almatter_core::db::Database;

/// The worker threads Tokio runs futures on.
///
/// `Runtime::new()` starts one per processor, which on a twelve-core desktop
/// was twelve threads — each with its own stack and scheduler state — for a
/// workload that is a WebSocket, a handful of HTTP requests at a time and
/// some SQLite reads. None of it is CPU-bound; it is all waiting on a socket
/// or a file. Three is enough to keep those overlapping, and the rest were
/// pure overhead. The blocking pool is capped for the same reason: its
/// default ceiling is 512 threads, which nothing here is ever going to need.
const WORKER_THREADS: usize = 3;
const MAX_BLOCKING_THREADS: usize = 8;

fn runtime() -> &'static Runtime {
    static RUNTIME: OnceLock<Runtime> = OnceLock::new();
    RUNTIME.get_or_init(|| {
        tokio::runtime::Builder::new_multi_thread()
            .worker_threads(WORKER_THREADS)
            .max_blocking_threads(MAX_BLOCKING_THREADS)
            .enable_all()
            .build()
            .expect("failed to start the Tokio runtime")
    })
}

/// The one long-lived cache database for the running app, opened lazily on
/// first use at the platform's default data location. Left uninitialized on
/// failure (rather than poisoned), so a later call can retry.
fn db() -> Result<&'static Mutex<Database>, String> {
    static DB: OnceLock<Mutex<Database>> = OnceLock::new();
    if let Some(db) = DB.get() {
        return Ok(db);
    }
    let opened = Database::open_default().map_err(|e| format!("failed to open the local cache database: {e}"))?;
    Ok(DB.get_or_init(|| Mutex::new(opened)))
}

fn to_c_string(s: String) -> *mut c_char {
    CString::new(s)
        .unwrap_or_else(|_| {
            CString::new(r#"{"ok":false,"error":"response had an interior NUL byte"}"#)
                .expect("this literal has no interior NUL")
        })
        .into_raw()
}

fn error_json(message: &str) -> String {
    serde_json::json!({ "ok": false, "error": message }).to_string()
}

fn panic_message(panic: &(dyn std::any::Any + Send)) -> String {
    if let Some(s) = panic.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = panic.downcast_ref::<String>() {
        s.clone()
    } else {
        "unknown internal error".to_string()
    }
}

/// Proves the Rust <-> .NET bridge end to end: returns the crate version as a
/// C string the Avalonia side can P/Invoke and display.
#[no_mangle]
pub extern "C" fn almatter_core_version() -> *mut c_char {
    to_c_string(env!("CARGO_PKG_VERSION").to_string())
}

/// Frees a string previously returned by this crate. Must be called by the
/// .NET side for every `*mut c_char` it receives, exactly once.
///
/// # Safety
///
/// `ptr` must be null, or a pointer this crate returned and that has not
/// been freed yet. Freeing anything else, or the same pointer twice, is
/// undefined behavior.
#[no_mangle]
pub unsafe extern "C" fn almatter_core_free_string(ptr: *mut c_char) {
    if ptr.is_null() {
        return;
    }
    // SAFETY: `ptr` must have come from a `CString::into_raw` call in this
    // crate and must not have been freed already — the .NET wrapper upholds
    // this by calling it exactly once per pointer, right after consuming it.
    unsafe {
        drop(CString::from_raw(ptr));
    }
}

/// Every Mattermost API call from .NET goes through here as a JSON request
/// in, a JSON response out. See `almatter_core::dispatch` for the request
/// shapes.
///
/// # Safety
///
/// `request_json` must be null, or a valid null-terminated C string that
/// stays alive and unwritten for the whole call. The pointer is only read,
/// and never kept past the call returning.
#[no_mangle]
pub unsafe extern "C" fn almatter_core_call(request_json: *const c_char) -> *mut c_char {
    if request_json.is_null() {
        return to_c_string(error_json("null request"));
    }
    // SAFETY: the .NET caller must pass a valid, null-terminated UTF-8 C
    // string that it owns for the duration of this call; we only read it,
    // never hold onto the pointer past this function returning.
    let request = unsafe { CStr::from_ptr(request_json) };
    let request = match request.to_str() {
        Ok(s) => s.to_string(),
        Err(_) => return to_c_string(error_json("request was not valid UTF-8")),
    };

    let outcome = std::panic::catch_unwind(AssertUnwindSafe(|| -> Result<String, String> {
        let db = db()?;
        Ok(runtime().block_on(almatter_core::dispatch::dispatch(&request, db)))
    }));

    let response = match outcome {
        Ok(Ok(response)) => response,
        Ok(Err(open_db_error)) => error_json(&open_db_error),
        Err(panic) => error_json(&format!("internal error: {}", panic_message(&*panic))),
    };

    to_c_string(response)
}
