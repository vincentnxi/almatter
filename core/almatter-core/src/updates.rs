//! Whether a newer Almatter has been published on GitHub, and fetching its
//! installer so the app can offer "restart to update".
//!
//! The installer is not code-signed, so Windows cannot vouch for it, and
//! GitHub alone is not enough either: whoever got hold of the author's
//! GitHub account could replace a release's file, and every copy of the app
//! would then run theirs without anyone clicking anything. So each release
//! carries a second file, `Almatter-Setup-<version>.exe.sig`: an Ed25519
//! signature made on the author's own PC with a key that never goes near
//! GitHub. The app holds the matching public key (`UPDATE_PUBLIC_KEYS`) and
//! keeps an installer only if its signature checks out. What is signed names
//! the version too, so an older, genuine installer cannot be passed off as a
//! newer one to take a machine back to a version with a known hole.

use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::Duration;

use ed25519_dalek::{Signature, VerifyingKey};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};

use crate::api::{download_external_file, fetch_external_document};

/// The repository's releases, newest first. Unauthenticated: GitHub allows 60
/// such requests an hour from one address, and the app makes one a day.
/// Thirty is GitHub's default page; the newest release is always on it.
const RELEASES_API: &str = "https://api.github.com/repos/vincentnxi/almatter/releases?per_page=30";

/// Where a release's own page lives. Built here from a checked tag rather
/// than taken from the answer's `html_url`: the app hands this address to the
/// system to open, so it must only ever be a page of this repository.
const RELEASE_PAGE: &str = "https://github.com/vincentnxi/almatter/releases/tag/";

/// Thirty releases with their notes come to a few hundred KB at most.
const MAX_RELEASES_BYTES: usize = 2 * 1024 * 1024;

#[derive(Debug, PartialEq, Eq, Serialize)]
pub struct NewerRelease {
    /// "0.15.0" — no leading "v".
    pub version: String,
    /// The release's page on GitHub.
    pub url: String,
}

#[derive(Deserialize)]
struct Release {
    tag_name: String,
    #[serde(default)]
    draft: bool,
}

/// The newest published release, if it is newer than `current_version`.
///
/// Pre-releases count: every Almatter version so far has been published as
/// one. That is also why this reads the whole list rather than GitHub's
/// "latest release", which leaves pre-releases out.
pub async fn check_for_update(current_version: &str) -> Result<Option<NewerRelease>, String> {
    let current = parse_version(current_version).ok_or_else(|| format!("not a version number: {current_version}"))?;
    let body = fetch_external_document(RELEASES_API, "application/vnd.github+json", MAX_RELEASES_BYTES)
        .await
        .map_err(|e| e.to_string())?;
    newest_after(current, &body)
}

/// Removes downloaded installers: once the app is up to date, one fetched
/// earlier has done its job (or belongs to a version since passed). Best
/// effort. Called by the dispatcher rather than `check_for_update`, so the
/// tests that ask GitHub leave the real app's folder alone.
pub fn forget_downloaded_updates() {
    let _ = std::fs::remove_dir_all(crate::db::update_download_dir());
}

/// The public halves of the keys allowed to sign an installer, in hex. More
/// than one only while moving to a new key: a version that knows both can
/// install updates signed with either. The secret half lives on the author's
/// PC, protected by Windows (tools/sign-update.ps1), with a backup copy kept
/// offline.
// Created 2026-11-26.
const UPDATE_PUBLIC_KEYS: &[&str] = &["7e49f51ea0c4d5f839f80e0284256ae76b9f02de9499c52240d7eb5535238641"];

/// Where a release's files are downloaded from. Built here from a checked
/// version, never taken from an answer, for the same reason as RELEASE_PAGE.
const RELEASE_DOWNLOAD: &str = "https://github.com/vincentnxi/almatter/releases/download/";

/// The installer is about 17 MB. Room to grow, not room for anything.
const MAX_INSTALLER_BYTES: u64 = 64 * 1024 * 1024;

/// A signature file is 129 bytes.
const MAX_SIGNATURE_BYTES: usize = 4 * 1024;

/// For the whole installer download. Generous, for a slow line: the read
/// timeout still cuts off a server that stops sending altogether.
const INSTALLER_DOWNLOAD_TIMEOUT: Duration = Duration::from_secs(15 * 60);

#[derive(Debug, PartialEq, Eq, Serialize)]
pub struct DownloadedUpdate {
    /// "0.18.0" — no leading "v".
    pub version: String,
    /// The checked installer, ready to run.
    pub path: PathBuf,
}

/// Downloads the installer of `version` and its signature, checks one
/// against the other, and returns where the installer is. An installer kept
/// from an earlier call is checked again rather than trusted, and fetched
/// afresh if it no longer passes, so the path returned is always of a file
/// that passed just now: the app calls this again right before running it.
pub async fn download_update(version: &str) -> Result<DownloadedUpdate, String> {
    download_update_with(version, &shipped_update_keys()?, &crate::db::update_download_dir()).await
}

async fn download_update_with(version: &str, keys: &[VerifyingKey], dir: &Path) -> Result<DownloadedUpdate, String> {
    let (major, minor, patch) = parse_version(version).ok_or_else(|| format!("not a version number: {version}"))?;
    // Rebuilt from the numbers: whatever came in, only digits and dots go
    // into the file names and addresses below.
    let version = format!("{major}.{minor}.{patch}");
    let installer_name = format!("Almatter-Setup-{version}.exe");
    let signature_name = format!("{installer_name}.sig");
    let installer = dir.join(&installer_name);
    let signature_file = dir.join(&signature_name);

    std::fs::create_dir_all(dir).map_err(|e| format!("cannot create {}: {e}", dir.display()))?;
    remove_all_but(dir, &[&installer_name, &signature_name]);

    if let (Ok(signature_text), true) = (std::fs::read(&signature_file), installer.is_file()) {
        if parse_signature(&signature_text)
            .and_then(|signature| check_installer(&installer, &version, &signature, keys))
            .is_ok()
        {
            return Ok(DownloadedUpdate { version, path: installer });
        }
    }
    let _ = std::fs::remove_file(&installer);
    let _ = std::fs::remove_file(&signature_file);

    let base = format!("{RELEASE_DOWNLOAD}v{version}/");
    let signature_text = fetch_external_document(&format!("{base}{signature_name}"), "application/octet-stream", MAX_SIGNATURE_BYTES)
        .await
        .map_err(|e| format!("signature download failed: {e}"))?;
    let signature = parse_signature(&signature_text)?;

    // Written under another name until it has passed, so nothing ever finds
    // an unchecked installer under the name of a checked one.
    let partial = dir.join(format!("{installer_name}.part"));
    let outcome = async {
        download_external_file(&format!("{base}{installer_name}"), &partial, MAX_INSTALLER_BYTES, INSTALLER_DOWNLOAD_TIMEOUT)
            .await
            .map_err(|e| format!("installer download failed: {e}"))?;
        check_installer(&partial, &version, &signature, keys)?;
        std::fs::write(&signature_file, &signature_text).map_err(|e| format!("cannot write {}: {e}", signature_file.display()))?;
        std::fs::rename(&partial, &installer).map_err(|e| format!("cannot move the installer into place: {e}"))
    }
    .await;
    if outcome.is_err() {
        let _ = std::fs::remove_file(&partial);
        let _ = std::fs::remove_file(&signature_file);
    }
    outcome.map(|()| DownloadedUpdate { version, path: installer })
}

/// The keys in `UPDATE_PUBLIC_KEYS`, read. Also what the signing tool checks
/// a new signature against before writing it.
pub fn shipped_update_keys() -> Result<Vec<VerifyingKey>, String> {
    if UPDATE_PUBLIC_KEYS.is_empty() {
        return Err("this build holds no key to check updates with".into());
    }
    UPDATE_PUBLIC_KEYS
        .iter()
        .map(|hex| {
            decode_hex::<32>(hex)
                .and_then(|bytes| VerifyingKey::from_bytes(&bytes).ok())
                .ok_or_else(|| format!("unreadable update key {hex}"))
        })
        .collect()
}

/// What the author's key signs for a release: the version and the
/// installer's SHA-256, in a form no other kind of message could take.
pub fn signed_message(version: &str, installer_sha256: &[u8; 32]) -> Vec<u8> {
    format!("Almatter installer\nversion {version}\nsha256 {}\n", encode_hex(installer_sha256)).into_bytes()
}

/// Whether `signature` is one of `keys` signing `signed_message(version, digest)`.
pub fn verify_installer_signature(
    version: &str,
    installer_sha256: &[u8; 32],
    signature: &Signature,
    keys: &[VerifyingKey],
) -> Result<(), String> {
    let message = signed_message(version, installer_sha256);
    // verify_strict: also refuses the odd signatures that plain verify lets
    // through, which only a forger would ever produce.
    if keys.iter().any(|key| key.verify_strict(&message, signature).is_ok()) {
        Ok(())
    } else {
        Err("the installer's signature does not match: it was not signed by Almatter's author, or not for this version".into())
    }
}

fn check_installer(path: &Path, version: &str, signature: &Signature, keys: &[VerifyingKey]) -> Result<(), String> {
    verify_installer_signature(version, &sha256_file(path)?, signature, keys)
}

/// The SHA-256 of a file, read in pieces rather than loaded whole.
pub fn sha256_file(path: &Path) -> Result<[u8; 32], String> {
    let mut file = std::fs::File::open(path).map_err(|e| format!("cannot read {}: {e}", path.display()))?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 64 * 1024];
    loop {
        let read = file.read(&mut buffer).map_err(|e| format!("cannot read {}: {e}", path.display()))?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hasher.finalize().into())
}

/// A `.sig` file: the 64-byte signature in hex, and a line end at most.
pub fn parse_signature(text: &[u8]) -> Result<Signature, String> {
    std::str::from_utf8(text)
        .ok()
        .and_then(|text| decode_hex::<64>(text.trim_end_matches(['\r', '\n'])))
        .map(|bytes| Signature::from_bytes(&bytes))
        .ok_or_else(|| "the signature file is not a signature".to_string())
}

/// Removes everything in `dir` but `keep`: installers of versions since
/// passed or skipped, and a download cut short.
fn remove_all_but(dir: &Path, keep: &[&str]) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        if !keep.iter().any(|name| entry.file_name() == *name) {
            let path = entry.path();
            let _ = if path.is_dir() { std::fs::remove_dir_all(&path) } else { std::fs::remove_file(&path) };
        }
    }
}

pub fn encode_hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

/// Exactly `N` bytes written as 2×N lowercase or uppercase hex digits.
pub fn decode_hex<const N: usize>(text: &str) -> Option<[u8; N]> {
    let text = text.as_bytes();
    if text.len() != N * 2 {
        return None;
    }
    let digit = |c: u8| (c as char).to_digit(16).map(|d| d as u8);
    let mut out = [0u8; N];
    for (i, pair) in text.as_chunks::<2>().0.iter().enumerate() {
        out[i] = digit(pair[0])? << 4 | digit(pair[1])?;
    }
    Some(out)
}

fn newest_after(current: Version, body: &[u8]) -> Result<Option<NewerRelease>, String> {
    let releases: Vec<Release> =
        serde_json::from_slice(body).map_err(|_| "GitHub sent a list of releases this app cannot read.".to_string())?;
    let newest = releases
        .iter()
        .filter(|release| !release.draft)
        .filter_map(|release| parse_version(&release.tag_name).map(|version| (version, release)))
        .max_by_key(|(version, _)| *version);
    Ok(match newest {
        Some((version, release)) if version > current => Some(NewerRelease {
            version: format!("{}.{}.{}", version.0, version.1, version.2),
            // Safe to append: parse_version accepted it, so it is an optional
            // "v" followed by digits and two dots, nothing else.
            url: format!("{RELEASE_PAGE}{}", release.tag_name),
        }),
        _ => None,
    })
}

type Version = (u32, u32, u32);

/// "0.14.0" or "v0.14.0", and nothing looser: any other tag (a test build,
/// "v1.0.0-beta", a typo) is simply not a release this checks against.
fn parse_version(text: &str) -> Option<Version> {
    let digits = text.strip_prefix('v').unwrap_or(text);
    let mut parts = digits.split('.');
    let mut next = || -> Option<u32> {
        let part = parts.next()?;
        if part.is_empty() || part.len() > 6 || !part.bytes().all(|b| b.is_ascii_digit()) {
            return None;
        }
        part.parse().ok()
    };
    let version = (next()?, next()?, next()?);
    parts.next().is_none().then_some(version)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn list(tags: &[(&str, bool)]) -> Vec<u8> {
        let releases: Vec<_> = tags
            .iter()
            .map(|(tag, draft)| serde_json::json!({ "tag_name": tag, "draft": draft, "prerelease": true, "body": "notes" }))
            .collect();
        serde_json::to_vec(&releases).unwrap()
    }

    #[test]
    fn finds_the_newest_release_even_when_listed_out_of_order() {
        let body = list(&[("v0.14.1", false), ("v0.15.0", false), ("v0.13.0", false)]);
        assert_eq!(
            newest_after((0, 14, 0), &body).unwrap(),
            Some(NewerRelease {
                version: "0.15.0".into(),
                url: "https://github.com/vincentnxi/almatter/releases/tag/v0.15.0".into(),
            })
        );
    }

    #[test]
    fn compares_numbers_not_text() {
        let body = list(&[("v0.10.0", false), ("v0.9.2", false)]);
        assert_eq!(newest_after((0, 9, 2), &body).unwrap().unwrap().version, "0.10.0");
    }

    #[test]
    fn nothing_when_already_up_to_date_or_ahead() {
        let body = list(&[("v0.14.0", false), ("v0.13.1", false)]);
        assert_eq!(newest_after((0, 14, 0), &body).unwrap(), None);
        assert_eq!(newest_after((0, 15, 0), &body).unwrap(), None);
    }

    #[test]
    fn ignores_drafts_and_tags_that_are_not_plain_versions() {
        let body = list(&[
            ("v0.20.0", true),
            ("v0.16.0-beta", false),
            ("nightly", false),
            ("v0.17", false),
            ("v0.18.0.1", false),
            ("v0.19.0/../../evil", false),
            ("v0.15.0", false),
        ]);
        assert_eq!(newest_after((0, 14, 0), &body).unwrap().unwrap().version, "0.15.0");
    }

    #[test]
    fn an_unreadable_answer_is_an_error_not_an_empty_list() {
        assert!(newest_after((0, 14, 0), b"<html>rate limited</html>").is_err());
        assert!(newest_after((0, 14, 0), b"{\"message\":\"Not Found\"}").is_err());
    }

    /// Against the real GitHub, so only on demand:
    /// `cargo test -p almatter-core updates -- --ignored`.
    #[tokio::test]
    #[ignore = "reaches api.github.com"]
    async fn the_real_release_list_is_read() {
        let newer = check_for_update("0.0.1").await.expect("GitHub answered").expect("a release exists");
        assert!(newer.url.starts_with(RELEASE_PAGE), "{}", newer.url);
        assert_eq!(check_for_update("999.0.0").await.expect("GitHub answered"), None);
    }

    /// 0.17.1 was published before installers were signed: its download
    /// must stop at the missing signature, leaving nothing behind.
    #[tokio::test]
    #[ignore = "reaches github.com"]
    async fn a_real_release_without_a_signature_is_not_downloaded() {
        let dir = scratch_dir("unsigned");
        let error = download_update_with("0.17.1", &shipped_update_keys().unwrap(), &dir).await.unwrap_err();
        assert!(error.starts_with("signature download failed"), "{error}");
        assert_eq!(std::fs::read_dir(&dir).unwrap().count(), 0);
        let _ = std::fs::remove_dir_all(&dir);
    }

    use ed25519_dalek::{Signer, SigningKey};

    fn test_key(seed: u8) -> SigningKey {
        SigningKey::from_bytes(&[seed; 32])
    }

    fn sign(key: &SigningKey, version: &str, installer: &[u8]) -> Signature {
        key.sign(&signed_message(version, &Sha256::digest(installer).into()))
    }

    #[test]
    fn a_genuine_signature_passes_and_anything_else_fails() {
        let key = test_key(7);
        let keys = [key.verifying_key()];
        let installer = b"pretend this is Almatter-Setup-0.18.0.exe";
        let digest: [u8; 32] = Sha256::digest(installer).into();
        let signature = sign(&key, "0.18.0", installer);

        assert!(verify_installer_signature("0.18.0", &digest, &signature, &keys).is_ok());
        // Another file under the same signature.
        let tampered: [u8; 32] = Sha256::digest(b"something else").into();
        assert!(verify_installer_signature("0.18.0", &tampered, &signature, &keys).is_err());
        // A genuine older installer passed off as a newer version.
        assert!(verify_installer_signature("0.19.0", &digest, &signature, &keys).is_err());
        // Signed by someone else.
        let stranger = sign(&test_key(8), "0.18.0", installer);
        assert!(verify_installer_signature("0.18.0", &digest, &stranger, &keys).is_err());
        // Any of several keys will do, while moving to a new one.
        let both = [test_key(8).verifying_key(), key.verifying_key()];
        assert!(verify_installer_signature("0.18.0", &digest, &signature, &both).is_ok());
        assert!(verify_installer_signature("0.18.0", &digest, &signature, &[]).is_err());
    }

    #[test]
    fn signature_files_are_read_strictly() {
        let signature = sign(&test_key(7), "0.18.0", b"x");
        let hex = encode_hex(&signature.to_bytes());
        assert_eq!(parse_signature(hex.as_bytes()).unwrap(), signature);
        assert_eq!(parse_signature(format!("{hex}\r\n").as_bytes()).unwrap(), signature);
        assert_eq!(parse_signature(hex.to_uppercase().as_bytes()).unwrap(), signature);
        for bad in [String::new(), hex[..126].to_string(), format!("{hex}00"), format!(" {hex}"), format!("{}zz", &hex[..126])] {
            assert!(parse_signature(bad.as_bytes()).is_err(), "{bad:?}");
        }
        assert!(parse_signature(b"<html>Not Found</html>").is_err());
    }

    #[test]
    fn the_signed_message_names_the_version_and_the_hash() {
        assert_eq!(
            String::from_utf8(signed_message("0.18.0", &[0xab; 32])).unwrap(),
            format!("Almatter installer\nversion 0.18.0\nsha256 {}\n", "ab".repeat(32))
        );
    }

    #[test]
    fn the_shipped_keys_are_readable() {
        // An empty list is caught here too: a release build must hold one.
        assert!(!shipped_update_keys().expect("every key parses").is_empty());
    }

    fn scratch_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("almatter-update-test-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// No network here: the copy already on disk is checked and handed back,
    /// and what does not belong in the folder is cleared away.
    #[tokio::test]
    async fn a_checked_copy_on_disk_is_used_again() {
        let dir = scratch_dir("kept");
        let key = test_key(7);
        let installer = b"pretend installer";
        std::fs::write(dir.join("Almatter-Setup-0.18.0.exe"), installer).unwrap();
        std::fs::write(dir.join("Almatter-Setup-0.18.0.exe.sig"), encode_hex(&sign(&key, "0.18.0", installer).to_bytes())).unwrap();
        std::fs::write(dir.join("Almatter-Setup-0.17.0.exe"), b"old").unwrap();
        std::fs::write(dir.join("Almatter-Setup-0.18.0.exe.part"), b"half").unwrap();

        let got = download_update_with("v0.18.0", &[key.verifying_key()], &dir).await.unwrap();
        assert_eq!(got, DownloadedUpdate { version: "0.18.0".into(), path: dir.join("Almatter-Setup-0.18.0.exe") });
        let mut left: Vec<_> = std::fs::read_dir(&dir).unwrap().map(|e| e.unwrap().file_name().into_string().unwrap()).collect();
        left.sort();
        assert_eq!(left, ["Almatter-Setup-0.18.0.exe", "Almatter-Setup-0.18.0.exe.sig"]);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    async fn a_version_that_is_not_a_plain_number_is_refused_before_anything() {
        let dir = scratch_dir("refused");
        for bad in ["0.18.0/../../x", "..", "0.18", ""] {
            assert!(download_update_with(bad, &[test_key(7).verifying_key()], &dir).await.is_err(), "{bad:?}");
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn version_numbers_are_parsed_strictly() {
        assert_eq!(parse_version("0.14.0"), Some((0, 14, 0)));
        assert_eq!(parse_version("v1.2.3"), Some((1, 2, 3)));
        for bad in ["", "v", "1.2", "1.2.3.4", "1..3", "1.2.x", "+1.2.3", "1.2.3 ", "V1.2.3", "1.2.9999999"] {
            assert_eq!(parse_version(bad), None, "{bad:?}");
        }
    }
}
