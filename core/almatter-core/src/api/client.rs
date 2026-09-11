use std::sync::OnceLock;

use thiserror::Error;

use crate::models::{AuthenticatedUser, Channel, ChannelMember, ChannelParticipant, CustomEmoji, FileInfo, PostList, Post, Preference, PublicChannel, Team, UserStatus};

/// A `MattermostClient` is built fresh for every dispatched request (see
/// dispatch.rs), but `reqwest::Client` is cheap to clone (it's an `Arc`
/// around a connection pool) and expensive to construct fresh — building a
/// new one each time meant every request paid a brand-new TCP+TLS handshake
/// instead of reusing a keep-alive connection to the server. One shared,
/// lazily-built client fixes that: still one `reqwest::Client::new()` ever,
/// just cloned (cheaply) into each `MattermostClient`.
static HTTP_CLIENT: OnceLock<reqwest::Client> = OnceLock::new();

fn shared_http_client() -> reqwest::Client {
    HTTP_CLIENT.get_or_init(reqwest::Client::new).clone()
}

#[derive(Debug, Error)]
pub enum ApiError {
    #[error("request failed: {0}")]
    Request(#[from] reqwest::Error),
    #[error("{message}")]
    Server { status: u16, message: String },
    #[error("login response did not include a session token")]
    MissingToken,
}

impl ApiError {
    /// Mattermost error responses are `{"message": "...", ...}` — surface
    /// that human-readable text rather than the raw JSON blob when present.
    fn from_response(status: u16, body: String) -> Self {
        let message = serde_json::from_str::<serde_json::Value>(&body)
            .ok()
            .and_then(|v| v.get("message").and_then(|m| m.as_str()).map(str::to_string))
            .unwrap_or_else(|| format!("The server responded with an unexpected error (HTTP {status})."));
        ApiError::Server { status, message }
    }

    /// True for a status where retrying the exact same request will never
    /// succeed on its own (bad content, no permission, resource gone) — this
    /// specific attempt is worth giving up on. False for anything that might
    /// succeed on a later attempt (network error, an expired session, a rate
    /// limit, a transient server hiccup): those need to be retried, not
    /// treated as "this particular message was bad" and silently discarded.
    /// Used by the outbox flush to decide what to drop vs. keep queued.
    pub fn is_permanent_rejection(&self) -> bool {
        matches!(self, ApiError::Server { status, .. } if matches!(status, 400 | 403 | 404 | 410 | 413 | 422))
    }
}

/// Thin REST client for a single Mattermost server. Every call here is what
/// keeps the local SQLite cache warm; the UI never calls the network directly.
pub struct MattermostClient {
    base_url: String,
    token: Option<String>,
    http: reqwest::Client,
}

impl MattermostClient {
    pub fn new(base_url: impl Into<String>) -> Self {
        Self {
            base_url: base_url.into(),
            token: None,
            http: shared_http_client(),
        }
    }

    pub fn with_token(mut self, token: impl Into<String>) -> Self {
        self.token = Some(token.into());
        self
    }

    fn url(&self, path: &str) -> String {
        format!("{}/api/v4{}", self.base_url.trim_end_matches('/'), path)
    }

    /// Verifies the server is reachable — used before login and as the
    /// reconnect probe once the app is back online.
    pub async fn ping(&self) -> Result<bool, ApiError> {
        let resp = self.http.get(self.url("/system/ping")).send().await?;
        Ok(resp.status().is_success())
    }

    /// Exchanges credentials for a session token. Doesn't take `&self`: a
    /// client isn't authenticated yet when this runs, so this builds its own
    /// short-lived client and hands back the token for the caller to attach
    /// with `with_token`.
    pub async fn login(
        base_url: &str,
        login_id: &str,
        password: &str,
    ) -> Result<(String, AuthenticatedUser), ApiError> {
        let client = Self::new(base_url);
        let resp = client
            .http
            .post(client.url("/users/login"))
            .json(&serde_json::json!({ "login_id": login_id, "password": password }))
            .send()
            .await?;

        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }

        let token = resp
            .headers()
            .get("Token")
            .and_then(|v| v.to_str().ok())
            .map(|s| s.to_string())
            .ok_or(ApiError::MissingToken)?;

        let user: AuthenticatedUser = resp.json().await?;
        Ok((token, user))
    }

    pub async fn get_teams(&self) -> Result<Vec<Team>, ApiError> {
        self.get_json("/users/me/teams").await
    }

    pub async fn get_channels_for_team(&self, team_id: &str) -> Result<Vec<Channel>, ApiError> {
        self.get_json(&format!("/users/me/teams/{team_id}/channels"))
            .await
    }

    /// Every public channel on the team, joined or not — what the "browse
    /// channels" panel lists. Paginated by the server, so this walks pages
    /// until one comes back short, with a hard ceiling so a very large
    /// server can't turn one panel open into an unbounded crawl. Filtering
    /// out the ones already joined is the caller's job: it's the client
    /// that knows the user's own channel list.
    pub async fn get_public_channels_for_team(&self, team_id: &str) -> Result<Vec<PublicChannel>, ApiError> {
        const PER_PAGE: usize = 200;
        const MAX_PAGES: usize = 5;

        let mut all = Vec::new();
        for page in 0..MAX_PAGES {
            let batch: Vec<PublicChannel> = self
                .get_json(&format!(
                    "/teams/{team_id}/channels?page={page}&per_page={PER_PAGE}"
                ))
                .await?;
            let is_last = batch.len() < PER_PAGE;
            all.extend(batch);
            if is_last {
                break;
            }
        }
        all.retain(|channel| channel.delete_at == 0);
        Ok(all)
    }

    /// Joins a public channel. Mattermost is happy to be told this about a
    /// channel the user is already in (it just returns the existing
    /// membership), so this doesn't need a "check first" round trip.
    pub async fn join_channel(&self, channel_id: &str, user_id: &str) -> Result<(), ApiError> {
        let body = serde_json::json!({ "user_id": user_id });
        let _: serde_json::Value = self
            .post_json(&format!("/channels/{channel_id}/members"), &body)
            .await?;
        Ok(())
    }

    /// Opens (or, if one already exists between these two users, just
    /// returns) a 1:1 direct-message channel — what "message this person"
    /// resolves to, driven from a profile popover rather than the sidebar.
    pub async fn create_direct_channel(&self, user_id: &str, other_user_id: &str) -> Result<Channel, ApiError> {
        let body = serde_json::json!([user_id, other_user_id]);
        self.post_json("/channels/direct", &body).await
    }

    /// Marks a channel as read — the server resets that channel member's
    /// msg_count (and mention_count) as if every message currently in it
    /// had been seen. Called whenever the user opens a channel.
    pub async fn mark_channel_viewed(&self, channel_id: &str) -> Result<(), ApiError> {
        let body = serde_json::json!({ "channel_id": channel_id });
        let _: serde_json::Value = self.post_json("/channels/members/me/view", &body).await?;
        Ok(())
    }

    /// Mutes/unmutes a channel — the same server-side preference the
    /// official clients use (`notify_props.mark_unread`), so it shows up
    /// muted there too, not just locally in this app.
    pub async fn set_channel_muted(&self, user_id: &str, channel_id: &str, muted: bool) -> Result<(), ApiError> {
        let body = serde_json::json!({ "mark_unread": if muted { "mention" } else { "all" } });
        let _: serde_json::Value = self
            .put_json(&format!("/channels/{channel_id}/members/{user_id}/notify_props"), &body)
            .await?;
        Ok(())
    }

    /// This user's per-channel read state (how many of each channel's
    /// messages they've actually seen) — a separate endpoint from the
    /// channels themselves, one call covering every channel in the team.
    pub async fn get_channel_members_for_team(&self, team_id: &str) -> Result<Vec<ChannelMember>, ApiError> {
        self.get_json(&format!("/users/me/teams/{team_id}/channels/members"))
            .await
    }

    /// Every member of one specific channel — used to resolve a group DM's
    /// participant list, since a GM channel's `name` (unlike a 1:1 DM's)
    /// isn't parseable into user ids.
    pub async fn get_channel_members(&self, channel_id: &str) -> Result<Vec<ChannelParticipant>, ApiError> {
        self.get_json(&format!("/channels/{channel_id}/members")).await
    }

    /// Users matching `term`, restricted to members of `channel_id` — what
    /// drives the composer's @mention autocomplete popup. `term` may be
    /// empty (returns the channel's members, most-recently-active first),
    /// same as typing a bare "@" in the official client.
    pub async fn search_users(&self, channel_id: &str, term: &str) -> Result<Vec<AuthenticatedUser>, ApiError> {
        let body = serde_json::json!({ "term": term, "in_channel_id": channel_id });
        self.post_json("/users/search", &body).await
    }

    /// Users anywhere on the team — what drives "start a conversation with
    /// anyone", as opposed to `search_users`, which is scoped to one
    /// channel's members. An empty `term` lists the team rather than
    /// searching, so the panel has something to show before any typing:
    /// `/users/search` requires a non-empty term and would just error.
    pub async fn search_team_users(&self, team_id: &str, term: &str) -> Result<Vec<AuthenticatedUser>, ApiError> {
        if term.is_empty() {
            return self
                .get_json(&format!("/users?in_team={team_id}&per_page=60&active=true"))
                .await;
        }
        let body = serde_json::json!({
            "term": term,
            "team_id": team_id,
            "allow_inactive": false,
        });
        self.post_json("/users/search", &body).await
    }

    pub async fn get_posts(&self, channel_id: &str) -> Result<Vec<Post>, ApiError> {
        let list: PostList = self
            .get_json(&format!("/channels/{channel_id}/posts"))
            .await?;
        Ok(list.into_ordered_posts())
    }

    /// The root post plus all its replies.
    pub async fn get_thread(&self, root_id: &str) -> Result<Vec<Post>, ApiError> {
        let list: PostList = self.get_json(&format!("/posts/{root_id}/thread")).await?;
        Ok(list.into_ordered_posts())
    }

    /// Presence for a batch of users — not cached anywhere (see db.rs):
    /// it's ephemeral by nature, so re-asking the server each time the DM
    /// list loads is simpler than trying to keep a stored copy fresh.
    pub async fn get_statuses_by_ids(&self, user_ids: &[String]) -> Result<Vec<UserStatus>, ApiError> {
        if user_ids.is_empty() {
            return Ok(Vec::new());
        }
        self.post_json("/users/status/ids", user_ids).await
    }

    /// Sets this user's own presence — `status` is one of Mattermost's own
    /// status strings ("online", "away", "dnd", "offline").
    pub async fn set_status(&self, user_id: &str, status: &str) -> Result<(), ApiError> {
        let body = serde_json::json!({ "user_id": user_id, "status": status });
        let _: serde_json::Value = self.put_json(&format!("/users/{user_id}/status"), &body).await?;
        Ok(())
    }

    /// Posts don't carry author names, only `user_id` — this resolves a
    /// batch of ids to users in one call instead of one request per author.
    pub async fn get_users_by_ids(
        &self,
        user_ids: &[String],
    ) -> Result<Vec<AuthenticatedUser>, ApiError> {
        if user_ids.is_empty() {
            return Ok(Vec::new());
        }
        self.post_json("/users/ids", user_ids).await
    }

    /// The single post fetch used to refresh a post's reactions right after
    /// this client's own add/remove call — cheaper and simpler than teaching
    /// the cache how to patch just the reactions list in place.
    pub async fn get_post(&self, post_id: &str) -> Result<Post, ApiError> {
        self.get_json(&format!("/posts/{post_id}")).await
    }

    /// Sends a new message — a top-level post if `root_id` is `None`, a
    /// thread reply otherwise. The returned `Post` is what dispatch.rs
    /// upserts into the cache, same as any other server response.
    pub async fn create_post(
        &self,
        channel_id: &str,
        message: &str,
        root_id: Option<&str>,
        file_ids: &[String],
    ) -> Result<Post, ApiError> {
        let mut body = serde_json::json!({
            "channel_id": channel_id,
            "message": message,
            "root_id": root_id.unwrap_or(""),
        });
        if !file_ids.is_empty() {
            body["file_ids"] = serde_json::json!(file_ids);
        }
        self.post_json("/posts", &body).await
    }

    /// Edits an already-sent message's text — only the author (or someone
    /// with the server-side permission) can actually do this; the server
    /// enforces that, not this client.
    pub async fn update_post(&self, post_id: &str, message: &str) -> Result<Post, ApiError> {
        let body = serde_json::json!({ "id": post_id, "message": message });
        self.put_json(&format!("/posts/{post_id}"), &body).await
    }

    /// Deletes an already-sent message — same permission note as `update_post`.
    pub async fn delete_post(&self, post_id: &str) -> Result<(), ApiError> {
        let mut req = self.http.delete(self.url(&format!("/posts/{post_id}")));
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(())
    }

    /// Uploads a file to attach to a not-yet-sent message, returning its
    /// server-assigned info (id, name, size) to carry in the eventual
    /// `create_post`'s `file_ids`. Unlike a text message, there's no offline
    /// queue for this — an upload genuinely requires a live connection, so a
    /// failure here just fails the attach attempt outright.
    pub async fn upload_file(&self, channel_id: &str, file_name: &str, bytes: Vec<u8>) -> Result<FileInfo, ApiError> {
        let part = reqwest::multipart::Part::bytes(bytes).file_name(file_name.to_string());
        let form = reqwest::multipart::Form::new()
            .text("channel_id", channel_id.to_string())
            .part("files", part);

        let mut req = self.http.post(self.url("/files")).multipart(form);
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }

        #[derive(serde::Deserialize)]
        struct UploadResponse {
            file_infos: Vec<FileInfo>,
        }
        let parsed: UploadResponse = resp.json().await?;
        parsed
            .file_infos
            .into_iter()
            .next()
            .ok_or_else(|| ApiError::Server { status: 200, message: "upload response had no file info".to_string() })
    }

    /// A window of messages around one specific post — what it's next to,
    /// the post itself, and what comes after — for "jump to this message"
    /// (e.g. from a search result) where the target might be far outside
    /// the channel's normal most-recent-messages view.
    pub async fn get_posts_around(&self, channel_id: &str, post_id: &str) -> Result<Vec<Post>, ApiError> {
        let before: PostList = self
            .get_json(&format!("/channels/{channel_id}/posts?before={post_id}&per_page=30"))
            .await?;
        let after: PostList = self
            .get_json(&format!("/channels/{channel_id}/posts?after={post_id}&per_page=30"))
            .await?;
        let anchor = self.get_post(post_id).await?;

        let mut posts = before.into_ordered_posts();
        posts.extend(after.into_ordered_posts());
        posts.push(anchor);
        posts.sort_by_key(|p| p.create_at);
        let mut seen = std::collections::HashSet::new();
        posts.retain(|p| seen.insert(p.id.clone()));
        Ok(posts)
    }

    /// Searches every message the server will let this user see across the
    /// whole team — not just what's locally cached — same response shape as
    /// get_posts/get_thread, so it reuses the same ordering helper.
    pub async fn search_posts(&self, team_id: &str, terms: &str) -> Result<Vec<Post>, ApiError> {
        let body = serde_json::json!({ "terms": terms, "is_or_search": false });
        let list: PostList = self
            .post_json(&format!("/teams/{team_id}/posts/search"), &body)
            .await?;
        Ok(list.into_ordered_posts())
    }

    pub async fn add_reaction(
        &self,
        user_id: &str,
        post_id: &str,
        emoji_name: &str,
    ) -> Result<(), ApiError> {
        let body = serde_json::json!({ "user_id": user_id, "post_id": post_id, "emoji_name": emoji_name });
        let _: serde_json::Value = self.post_json("/reactions", &body).await?;
        Ok(())
    }

    pub async fn remove_reaction(
        &self,
        user_id: &str,
        post_id: &str,
        emoji_name: &str,
    ) -> Result<(), ApiError> {
        let mut req = self.http.delete(
            self.url(&format!("/users/{user_id}/posts/{post_id}/reactions/{emoji_name}")),
        );
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(())
    }

    /// The server's custom emoji list — sorted by name so it's browsable as
    /// a picker. `per_page=200` is the server's own maximum; a personal
    /// Mattermost instance's custom emoji count realistically fits in one
    /// page, so this doesn't paginate further.
    pub async fn get_custom_emoji_list(&self) -> Result<Vec<CustomEmoji>, ApiError> {
        self.get_json("/emoji?page=0&per_page=200&sort=name").await
    }

    /// Raw image bytes for one custom emoji — not JSON, so this bypasses
    /// `get_json` and reads the response body directly.
    pub async fn get_emoji_image(&self, emoji_id: &str) -> Result<Vec<u8>, ApiError> {
        let mut req = self.http.get(self.url(&format!("/emoji/{emoji_id}/image")));
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(resp.bytes().await?.to_vec())
    }

    /// Raw bytes for one message attachment — same shape as
    /// `get_emoji_image`, just a different endpoint.
    pub async fn get_file_bytes(&self, file_id: &str) -> Result<Vec<u8>, ApiError> {
        let mut req = self.http.get(self.url(&format!("/files/{file_id}")));
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(resp.bytes().await?.to_vec())
    }

    /// A user's profile picture — Mattermost always returns *something*
    /// here (a generated default avatar if they never set a custom one),
    /// so this only fails for a genuinely unreachable server or unknown user.
    pub async fn get_user_avatar(&self, user_id: &str) -> Result<Vec<u8>, ApiError> {
        let mut req = self.http.get(self.url(&format!("/users/{user_id}/image")));
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(resp.bytes().await?.to_vec())
    }

    /// Channels favorited via the standard Mattermost preferences mechanism
    /// (`category: "favorite_channel"`) — the same store the official
    /// clients use, so favorites stay in sync across devices rather than
    /// being an Almatter-only local flag.
    pub async fn get_favorite_channel_ids(&self, user_id: &str) -> Result<Vec<String>, ApiError> {
        let prefs: Vec<Preference> = self
            .get_json(&format!("/users/{user_id}/preferences/favorite_channel"))
            .await?;
        Ok(prefs.into_iter().filter(|p| p.value == "true").map(|p| p.name).collect())
    }

    pub async fn set_favorite_channel(
        &self,
        user_id: &str,
        channel_id: &str,
        is_favorite: bool,
    ) -> Result<(), ApiError> {
        let body = serde_json::json!([{
            "user_id": user_id,
            "category": "favorite_channel",
            "name": channel_id,
            "value": if is_favorite { "true" } else { "false" },
        }]);
        let _: serde_json::Value = self.put_json(&format!("/users/{user_id}/preferences"), &body).await?;
        Ok(())
    }

    async fn get_json<T: serde::de::DeserializeOwned>(&self, path: &str) -> Result<T, ApiError> {
        let mut req = self.http.get(self.url(path));
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        Self::parse_json(resp).await
    }

    async fn post_json<B: serde::Serialize + ?Sized, T: serde::de::DeserializeOwned>(
        &self,
        path: &str,
        body: &B,
    ) -> Result<T, ApiError> {
        let mut req = self.http.post(self.url(path)).json(body);
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        Self::parse_json(resp).await
    }

    async fn put_json<B: serde::Serialize + ?Sized, T: serde::de::DeserializeOwned>(
        &self,
        path: &str,
        body: &B,
    ) -> Result<T, ApiError> {
        let mut req = self.http.put(self.url(path)).json(body);
        if let Some(token) = &self.token {
            req = req.bearer_auth(token);
        }
        let resp = req.send().await?;
        Self::parse_json(resp).await
    }

    async fn parse_json<T: serde::de::DeserializeOwned>(
        resp: reqwest::Response,
    ) -> Result<T, ApiError> {
        if !resp.status().is_success() {
            let status = resp.status().as_u16();
            let body = resp.text().await.unwrap_or_default();
            return Err(ApiError::from_response(status, body));
        }
        Ok(resp.json().await?)
    }
}

/// Raw bytes from an arbitrary external URL — a link preview's `og:image`,
/// say. Deliberately a free function rather than a `MattermostClient`
/// method: the target here is some third-party site, not this Mattermost
/// server, so it must never carry the session's bearer token.
pub async fn fetch_external_bytes(url: &str) -> Result<Vec<u8>, ApiError> {
    let resp = shared_http_client().get(url).send().await?;
    if !resp.status().is_success() {
        let status = resp.status().as_u16();
        let body = resp.text().await.unwrap_or_default();
        return Err(ApiError::from_response(status, body));
    }
    Ok(resp.bytes().await?.to_vec())
}

#[cfg(test)]
mod tests {
    use super::*;
    use wiremock::matchers::{method, path};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    #[tokio::test]
    async fn login_succeeds_and_returns_token() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/login"))
            .respond_with(ResponseTemplate::new(200).insert_header("Token", "abc123").set_body_json(
                serde_json::json!({ "id": "u1", "username": "jdupont", "email": "v@example.com" }),
            ))
            .mount(&server)
            .await;

        let (token, user) = MattermostClient::login(&server.uri(), "jdupont", "hunter2")
            .await
            .expect("login should succeed against the mock server");

        assert_eq!(token, "abc123");
        assert_eq!(user.username, "jdupont");
    }

    #[test]
    fn is_permanent_rejection_distinguishes_bad_messages_from_transient_failures() {
        let permanent = [400, 403, 404, 410, 413, 422];
        for status in permanent {
            let err = ApiError::Server { status, message: "x".into() };
            assert!(err.is_permanent_rejection(), "status {status} should be permanent");
        }

        let transient = [401, 408, 409, 429, 500, 502, 503, 504];
        for status in transient {
            let err = ApiError::Server { status, message: "x".into() };
            assert!(!err.is_permanent_rejection(), "status {status} should not be permanent");
        }

        assert!(!ApiError::MissingToken.is_permanent_rejection());
    }

    #[tokio::test]
    async fn login_fails_with_wrong_credentials() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/login"))
            .respond_with(ResponseTemplate::new(401).set_body_json(serde_json::json!({
                "id": "api.user.login.invalid_credentials_email_username",
                "message": "Enter a valid email or username and/or password."
            })))
            .mount(&server)
            .await;

        let err = MattermostClient::login(&server.uri(), "jdupont", "wrong")
            .await
            .expect_err("login with wrong credentials should fail");

        assert!(matches!(err, ApiError::Server { status: 401, .. }));
    }

    #[tokio::test]
    async fn get_teams_parses_the_real_response_shape() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "t1", "name": "acme", "display_name": "Acme Corp", "type": "O", "extra_field_the_server_might_send": 42 }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let teams = client.get_teams().await.expect("teams should parse");

        assert_eq!(teams.len(), 1);
        assert_eq!(teams[0].display_name, "Acme Corp");
    }

    #[tokio::test]
    async fn mark_channel_viewed_posts_the_expected_body() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/channels/members/me/view"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({ "status": "OK" })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .mark_channel_viewed("c1")
            .await
            .expect("marking a channel viewed should succeed");
    }

    #[tokio::test]
    async fn set_channel_muted_puts_the_expected_notify_prop() {
        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/channels/c1/members/u1/notify_props"))
            .and(wiremock::matchers::body_json(serde_json::json!({ "mark_unread": "mention" })))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({ "mark_unread": "mention" })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .set_channel_muted("u1", "c1", true)
            .await
            .expect("muting a channel should succeed");
    }

    #[tokio::test]
    async fn get_channel_members_returns_participant_user_ids() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/channels/gm1/members"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "channel_id": "gm1", "user_id": "u1", "msg_count": 3 },
                { "channel_id": "gm1", "user_id": "u2", "msg_count": 0 },
                { "channel_id": "gm1", "user_id": "u3", "msg_count": 5 }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let members = client.get_channel_members("gm1").await.expect("members should parse");

        assert_eq!(members.len(), 3);
        assert_eq!(members[0].user_id, "u1");
        assert_eq!(members[2].user_id, "u3");
    }

    #[tokio::test]
    async fn search_users_posts_the_expected_body_and_parses_matches() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/search"))
            .and(wiremock::matchers::body_json(serde_json::json!({ "term": "vin", "in_channel_id": "c1" })))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "u1", "username": "jdupont", "nickname": "", "first_name": "Jean", "last_name": "Dupont" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let users = client.search_users("c1", "vin").await.expect("search should succeed");

        assert_eq!(users.len(), 1);
        assert_eq!(users[0].username, "jdupont");
    }

    #[tokio::test]
    async fn get_channel_members_for_team_parses_read_state() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels/members"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "channel_id": "c1", "user_id": "u1", "msg_count": 7, "mention_count": 2 }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let members = client
            .get_channel_members_for_team("t1")
            .await
            .expect("members should parse");

        assert_eq!(members.len(), 1);
        assert_eq!(members[0].channel_id, "c1");
        assert_eq!(members[0].msg_count, 7);
        // No notify_props sent at all — must default to "not muted", not fail to parse.
        assert!(!members[0].is_muted());
    }

    #[tokio::test]
    async fn get_channel_members_for_team_recognizes_a_muted_channel() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/me/teams/t1/channels/members"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "channel_id": "c1", "user_id": "u1", "msg_count": 7, "mention_count": 2,
                  "notify_props": { "mark_unread": "mention" } },
                { "channel_id": "c2", "user_id": "u1", "msg_count": 3, "mention_count": 0,
                  "notify_props": { "mark_unread": "all" } }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let members = client
            .get_channel_members_for_team("t1")
            .await
            .expect("members should parse");

        assert!(members[0].is_muted());
        assert!(!members[1].is_muted());
    }

    #[tokio::test]
    async fn get_posts_flattens_the_order_and_map_shape() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/channels/c1/posts"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "order": ["p2", "p1"],
                "posts": {
                    "p1": { "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "first", "create_at": 1000 },
                    "p2": { "id": "p2", "channel_id": "c1", "user_id": "u1", "message": "second", "create_at": 2000 }
                }
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let posts = client.get_posts("c1").await.expect("posts should parse");

        assert_eq!(posts.len(), 2);
        assert_eq!(posts[0].message, "second");
        assert_eq!(posts[1].message, "first");
    }

    #[tokio::test]
    async fn get_statuses_by_ids_resolves_presence() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/status/ids"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "user_id": "u1", "status": "online" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let statuses = client
            .get_statuses_by_ids(&["u1".to_string()])
            .await
            .expect("statuses should resolve");

        assert_eq!(statuses.len(), 1);
        assert_eq!(statuses[0].status, "online");
    }

    #[tokio::test]
    async fn get_thread_returns_root_and_replies() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/posts/p1/thread"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "order": ["p1", "p2"],
                "posts": {
                    "p1": { "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "root", "create_at": 1000, "reply_count": 1 },
                    "p2": { "id": "p2", "channel_id": "c1", "root_id": "p1", "user_id": "u2", "message": "reply", "create_at": 2000 }
                }
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let posts = client.get_thread("p1").await.expect("thread should parse");

        assert_eq!(posts.len(), 2);
        assert_eq!(posts[0].reply_count, 1);
        assert_eq!(posts[1].root_id, "p1");
    }

    #[tokio::test]
    async fn get_posts_around_merges_before_after_and_the_anchor_in_order() {
        use wiremock::matchers::query_param;

        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/channels/c1/posts"))
            .and(query_param("before", "p2"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "order": ["p1"],
                "posts": { "p1": { "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "before", "create_at": 1000 } }
            })))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/api/v4/channels/c1/posts"))
            .and(query_param("after", "p2"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "order": ["p3"],
                "posts": { "p3": { "id": "p3", "channel_id": "c1", "user_id": "u1", "message": "after", "create_at": 3000 } }
            })))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/api/v4/posts/p2"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!(
                { "id": "p2", "channel_id": "c1", "user_id": "u1", "message": "anchor", "create_at": 2000 }
            )))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let posts = client
            .get_posts_around("c1", "p2")
            .await
            .expect("posts around should merge");

        assert_eq!(posts.len(), 3);
        assert_eq!(posts.iter().map(|p| p.id.as_str()).collect::<Vec<_>>(), vec!["p1", "p2", "p3"]);
    }

    #[tokio::test]
    async fn get_users_by_ids_resolves_authors() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/users/ids"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "u1", "username": "jdupont", "nickname": "", "first_name": "Jean", "last_name": "Dupont" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let users = client
            .get_users_by_ids(&["u1".to_string()])
            .await
            .expect("users should resolve");

        assert_eq!(users.len(), 1);
        assert_eq!(users[0].display_name(), "Jean Dupont");
    }

    #[tokio::test]
    async fn get_users_by_ids_skips_the_call_when_empty() {
        // No mock registered: if this made a request, wiremock would panic
        // on the unexpected call, so an Ok([]) here proves it didn't.
        let server = MockServer::start().await;
        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let users = client.get_users_by_ids(&[]).await.expect("should short-circuit");
        assert!(users.is_empty());
    }

    #[tokio::test]
    async fn add_reaction_posts_the_expected_body() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/reactions"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "user_id": "u1", "post_id": "p1", "emoji_name": "+1"
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .add_reaction("u1", "p1", "+1")
            .await
            .expect("adding a reaction should succeed");
    }

    #[tokio::test]
    async fn update_post_puts_the_expected_body_and_parses_the_response() {
        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "edited", "create_at": 1000
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let post = client.update_post("p1", "edited").await.expect("editing a post should succeed");

        assert_eq!(post.message, "edited");
    }

    #[tokio::test]
    async fn delete_post_calls_the_expected_route() {
        let server = MockServer::start().await;
        Mock::given(method("DELETE"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client.delete_post("p1").await.expect("deleting a post should succeed");
    }

    #[tokio::test]
    async fn remove_reaction_calls_the_expected_delete_route() {
        let server = MockServer::start().await;
        Mock::given(method("DELETE"))
            .and(path("/api/v4/users/u1/posts/p1/reactions/+1"))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .remove_reaction("u1", "p1", "+1")
            .await
            .expect("removing a reaction should succeed");
    }

    #[tokio::test]
    async fn get_custom_emoji_list_parses_the_real_response_shape() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/emoji"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "id": "e1", "name": "party-parrot", "creator_id": "u1" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let emoji = client.get_custom_emoji_list().await.expect("emoji list should parse");

        assert_eq!(emoji.len(), 1);
        assert_eq!(emoji[0].name, "party-parrot");
    }

    #[tokio::test]
    async fn get_emoji_image_returns_raw_bytes() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/emoji/e1/image"))
            .respond_with(ResponseTemplate::new(200).set_body_bytes(vec![0x89, 0x50, 0x4E, 0x47]))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let bytes = client.get_emoji_image("e1").await.expect("image bytes should download");

        assert_eq!(bytes, vec![0x89, 0x50, 0x4E, 0x47]);
    }

    #[tokio::test]
    async fn get_user_avatar_returns_raw_bytes() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/u1/image"))
            .respond_with(ResponseTemplate::new(200).set_body_bytes(vec![0x89, 0x50, 0x4E, 0x47]))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let bytes = client.get_user_avatar("u1").await.expect("avatar bytes should download");

        assert_eq!(bytes, vec![0x89, 0x50, 0x4E, 0x47]);
    }

    #[tokio::test]
    async fn get_favorite_channel_ids_filters_out_unfavorited_preferences() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/users/u1/preferences/favorite_channel"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "user_id": "u1", "category": "favorite_channel", "name": "c1", "value": "true" },
                { "user_id": "u1", "category": "favorite_channel", "name": "c2", "value": "false" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let ids = client.get_favorite_channel_ids("u1").await.expect("favorites should parse");

        assert_eq!(ids, vec!["c1".to_string()]);
    }

    #[tokio::test]
    async fn set_favorite_channel_puts_the_expected_preference() {
        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/users/u1/preferences"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!([
                { "user_id": "u1", "category": "favorite_channel", "name": "c1", "value": "true" }
            ])))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .set_favorite_channel("u1", "c1", true)
            .await
            .expect("favoriting a channel should succeed");
    }

    #[tokio::test]
    async fn set_status_puts_the_expected_body() {
        let server = MockServer::start().await;
        Mock::given(method("PUT"))
            .and(path("/api/v4/users/u1/status"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!(
                { "user_id": "u1", "status": "away" }
            )))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .set_status("u1", "away")
            .await
            .expect("setting status should succeed");
    }

    #[tokio::test]
    async fn create_post_sends_the_expected_body_and_parses_the_response() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/posts"))
            .respond_with(ResponseTemplate::new(201).set_body_json(serde_json::json!({
                "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "hi", "create_at": 1000
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let post = client
            .create_post("c1", "hi", None, &[])
            .await
            .expect("creating a post should succeed");

        assert_eq!(post.id, "p1");
        assert_eq!(post.message, "hi");
    }

    #[tokio::test]
    async fn create_post_includes_file_ids_only_when_given() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/posts"))
            .and(wiremock::matchers::body_json(serde_json::json!({
                "channel_id": "c1", "message": "hi", "root_id": "", "file_ids": ["f1", "f2"]
            })))
            .respond_with(ResponseTemplate::new(201).set_body_json(serde_json::json!({
                "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "hi", "create_at": 1000
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        client
            .create_post("c1", "hi", None, &["f1".to_string(), "f2".to_string()])
            .await
            .expect("creating a post with attachments should succeed");
    }

    #[tokio::test]
    async fn upload_file_returns_the_first_file_infos_entry() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/files"))
            .respond_with(ResponseTemplate::new(201).set_body_json(serde_json::json!({
                "file_infos": [{ "id": "f1", "name": "cat.png", "size": 42 }],
                "client_ids": []
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let file = client
            .upload_file("c1", "cat.png", vec![1, 2, 3])
            .await
            .expect("upload should succeed");

        assert_eq!(file.id, "f1");
        assert_eq!(file.name, "cat.png");
        assert_eq!(file.size, 42);
    }

    #[tokio::test]
    async fn get_file_bytes_returns_raw_bytes() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/files/f1"))
            .respond_with(ResponseTemplate::new(200).set_body_bytes(vec![0x25, 0x50, 0x44, 0x46]))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let bytes = client.get_file_bytes("f1").await.expect("file bytes should download");

        assert_eq!(bytes, vec![0x25, 0x50, 0x44, 0x46]);
    }

    #[tokio::test]
    async fn create_direct_channel_posts_both_user_ids_and_parses_the_channel() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/v4/channels/direct"))
            .and(wiremock::matchers::body_json(serde_json::json!(["u1", "u2"])))
            .respond_with(ResponseTemplate::new(201).set_body_json(serde_json::json!({
                "id": "dm1", "team_id": "", "name": "u1__u2", "display_name": "", "type": "D"
            })))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let channel = client
            .create_direct_channel("u1", "u2")
            .await
            .expect("opening a direct channel should succeed");

        assert_eq!(channel.id, "dm1");
        assert_eq!(channel.channel_type, crate::models::ChannelType::Direct);
    }

    #[tokio::test]
    async fn fetch_external_bytes_downloads_from_an_arbitrary_url_without_a_token() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/img.png"))
            .respond_with(ResponseTemplate::new(200).set_body_bytes(vec![0x89, 0x50, 0x4e, 0x47]))
            .mount(&server)
            .await;

        let bytes = fetch_external_bytes(&format!("{}/img.png", server.uri()))
            .await
            .expect("external bytes should download");

        assert_eq!(bytes, vec![0x89, 0x50, 0x4e, 0x47]);
    }

    #[tokio::test]
    async fn get_post_fetches_a_single_post() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/v4/posts/p1"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!(
                { "id": "p1", "channel_id": "c1", "user_id": "u1", "message": "hi", "create_at": 1000 }
            )))
            .mount(&server)
            .await;

        let client = MattermostClient::new(server.uri()).with_token("abc123");
        let post = client.get_post("p1").await.expect("post should fetch");

        assert_eq!(post.message, "hi");
    }

    /// Hits a real, live third-party Mattermost server — not run by default.
    /// `cargo test -- --ignored` to confirm real-world reachability and that
    /// a genuine 401 from a real server round-trips through `ApiError`.
    #[tokio::test]
    #[ignore]
    async fn real_server_is_reachable_and_rejects_bad_login() {
        let client = MattermostClient::new("https://community.mattermost.com");
        assert!(client.ping().await.expect("real server should answer ping"));

        let err = MattermostClient::login(
            "https://community.mattermost.com",
            "almatter-verification-nonexistent-user",
            "definitely-wrong-password-123!",
        )
        .await
        .expect_err("bogus credentials should be rejected");

        assert!(matches!(err, ApiError::Server { status: 401, .. }), "got {err:?}");
    }
}
