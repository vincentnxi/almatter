//! Whether a newer Almatter has been published on GitHub.
//!
//! Only ever tells; never downloads or installs anything. The app shows a
//! banner and, on a click, opens the release's page in the browser, where
//! the user downloads the installer as they always have. Running a file
//! fetched in the background would need a way to prove it really comes from
//! the author first, and the installer is not code-signed.

use serde::{Deserialize, Serialize};

use crate::api::fetch_external_document;

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

    #[test]
    fn version_numbers_are_parsed_strictly() {
        assert_eq!(parse_version("0.14.0"), Some((0, 14, 0)));
        assert_eq!(parse_version("v1.2.3"), Some((1, 2, 3)));
        for bad in ["", "v", "1.2", "1.2.3.4", "1..3", "1.2.x", "+1.2.3", "1.2.3 ", "V1.2.3", "1.2.9999999"] {
            assert_eq!(parse_version(bad), None, "{bad:?}");
        }
    }
}
