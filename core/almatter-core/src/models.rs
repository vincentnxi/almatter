use serde::{Deserialize, Serialize};
use std::collections::HashMap;

// Field names and shapes below mirror the real Mattermost REST API responses
// (https://api.mattermost.com) closely enough to deserialize directly with
// serde — only the subset of fields Almatter currently uses is declared;
// serde ignores any other field the server sends.

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Team {
    pub id: String,
    pub name: String,
    pub display_name: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ChannelType {
    #[serde(rename = "O")]
    Public,
    #[serde(rename = "P")]
    Private,
    #[serde(rename = "D")]
    Direct,
    #[serde(rename = "G")]
    Group,
}

impl ChannelType {
    /// Same single-letter codes Mattermost itself uses — reused as-is for
    /// the SQLite cache column so there's no separate encoding to keep in sync.
    pub fn as_code(self) -> &'static str {
        match self {
            ChannelType::Public => "O",
            ChannelType::Private => "P",
            ChannelType::Direct => "D",
            ChannelType::Group => "G",
        }
    }

    pub fn from_code(code: &str) -> Option<Self> {
        match code {
            "O" => Some(ChannelType::Public),
            "P" => Some(ChannelType::Private),
            "D" => Some(ChannelType::Direct),
            "G" => Some(ChannelType::Group),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Channel {
    pub id: String,
    pub team_id: String,
    pub name: String,
    pub display_name: String,
    #[serde(rename = "type")]
    pub channel_type: ChannelType,
    /// Mattermost creates a DM channel record the moment a conversation is
    /// opened, before any message is actually sent — this is what tells a
    /// never-used DM apart from one with real history.
    #[serde(default)]
    pub total_msg_count: i64,
    /// When the last post landed — what the DM list is sorted by (most
    /// recent activity first), same as every real chat client.
    #[serde(default)]
    pub last_post_at: i64,
    /// How many of `total_msg_count` this user has actually seen — not part
    /// of the channel object itself (it comes from the separate channel
    /// membership endpoint), merged in by dispatch.rs after both are
    /// fetched. Defaults to 0 so `total_msg_count - msg_count` overstates
    /// unread rather than silently hiding it if that merge is ever skipped.
    #[serde(default)]
    pub msg_count: i64,
    /// Unread messages that are actual mentions of this user (including
    /// @channel/@all/@here, which the server already counts as mentions) —
    /// same merge-after-the-fact story as msg_count. The UI distinguishes
    /// "something happened" (bold, from msg_count) from "you were mentioned"
    /// (a number badge, from this), matching the official client.
    #[serde(default)]
    pub mention_count: i64,
    /// Whether this user has muted the channel — same merge-after-the-fact
    /// story as msg_count/mention_count, real state comes from `ChannelMember`.
    #[serde(default)]
    pub is_muted: bool,
}

/// One row of `GET /teams/{id}/channels` — a public channel on the team,
/// whether or not this user has joined it. Deliberately its own type rather
/// than a reuse of `Channel`: this is browse-and-join material, never
/// cached and never carrying per-user read state, and keeping it separate
/// means the browse endpoint's extra fields (purpose, delete_at) don't have
/// to be threaded through the channel cache schema.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PublicChannel {
    pub id: String,
    pub name: String,
    pub display_name: String,
    /// The channel's short description, shown under its name in the browse list. Often empty.
    #[serde(default)]
    pub purpose: String,
    #[serde(default)]
    pub total_msg_count: i64,
    /// Non-zero on an archived channel. The listing endpoint excludes those by default, but a server can still send one.
    #[serde(default)]
    pub delete_at: i64,
}

/// One row of `GET /users/me/teams/{id}/channels/members` — per-user read
/// state for a channel, kept separate from `Channel` (which mirrors
/// `GET .../channels` and knows nothing about who's reading it) and merged
/// onto it by dispatch.rs.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ChannelMember {
    pub channel_id: String,
    #[serde(default)]
    pub msg_count: i64,
    #[serde(default)]
    pub mention_count: i64,
    /// Absent on servers/responses that don't send it — treated as "not
    /// muted" rather than failing the whole member row.
    #[serde(default)]
    pub notify_props: NotifyProps,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NotifyProps {
    /// Mattermost's own values: "all" (default/unmuted) or "mention" (muted
    /// — only actual mentions still mark the channel unread).
    #[serde(default = "default_mark_unread")]
    pub mark_unread: String,
}

impl Default for NotifyProps {
    fn default() -> Self {
        Self { mark_unread: default_mark_unread() }
    }
}

fn default_mark_unread() -> String {
    "all".to_string()
}

impl ChannelMember {
    pub fn is_muted(&self) -> bool {
        self.notify_props.mark_unread == "mention"
    }
}

/// One row of `GET /channels/{id}/members` — every member of a channel, not
/// just this user's own read state (that's `ChannelMember` above, a
/// different endpoint). Used to resolve a group DM's participant list,
/// since unlike a 1:1 DM, a GM channel's `name` isn't parseable into user
/// ids. Only `user_id` is declared; the response carries the same shape as
/// `ChannelMember` (msg_count, notify_props, ...) but this doesn't need any
/// of that here.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ChannelParticipant {
    pub user_id: String,
}

/// A message the WebSocket saw for the current user (any channel they're
/// in — not just an @mention or DM) — queued in `mention_events` for the UI's
/// poll loop to drain and turn into a desktop notification. `is_mention`
/// distinguishes a real @mention/DM (worth calling out specifically) from a
/// plain message in some other channel.
#[derive(Debug, Clone, Serialize)]
pub struct MentionEvent {
    pub post_id: String,
    pub channel_id: String,
    pub author_id: String,
    pub message: String,
    pub created_at: i64,
    pub is_mention: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AuthenticatedUser {
    pub id: String,
    pub username: String,
    #[serde(default)]
    pub email: String,
    #[serde(default)]
    pub nickname: String,
    #[serde(default)]
    pub first_name: String,
    #[serde(default)]
    pub last_name: String,
}

impl AuthenticatedUser {
    /// Mirrors Mattermost's own fallback order for showing a name: nickname,
    /// then full name, then username.
    pub fn display_name(&self) -> String {
        if !self.nickname.is_empty() {
            return self.nickname.clone();
        }
        let full_name = format!("{} {}", self.first_name, self.last_name);
        let full_name = full_name.trim();
        if !full_name.is_empty() {
            return full_name.to_string();
        }
        self.username.clone()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Post {
    pub id: String,
    pub channel_id: String,
    /// Empty string (not null/absent) for a top-level post — that's how
    /// Mattermost's API represents "no thread root", so this stays a plain
    /// `String` rather than `Option<String>`.
    #[serde(default)]
    pub root_id: String,
    pub user_id: String,
    pub message: String,
    pub create_at: i64,
    /// How many replies this post's thread has — 0 for a post that isn't a
    /// thread root (or has no replies yet). Mattermost includes this
    /// directly on the post object.
    #[serde(default)]
    pub reply_count: i64,
    /// When this post was last edited — 0 if it never has been. Mattermost's
    /// own field name and meaning, used to show a subtle "(modifié)" tag.
    #[serde(default)]
    pub edit_at: i64,
    #[serde(default)]
    pub metadata: PostMetadata,
}

impl Post {
    pub fn is_thread_reply(&self) -> bool {
        !self.root_id.is_empty()
    }
}

/// Reactions and attached files ride along on the post object itself under
/// `metadata` — no separate per-post request needed to get them.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct PostMetadata {
    #[serde(default)]
    pub reactions: Vec<Reaction>,
    #[serde(default)]
    pub files: Vec<FileInfo>,
    /// A link preview (or bare image embed) the server already generated
    /// for a URL in this post's text — same free ride as reactions/files,
    /// no separate fetch needed. Only populated when the server has
    /// link-preview generation turned on.
    #[serde(default)]
    pub embeds: Vec<PostEmbed>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Reaction {
    pub user_id: String,
    pub emoji_name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileInfo {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub size: i64,
}

/// One entry of a post's `metadata.embeds` — Mattermost generates one of
/// these per URL it recognizes in the message text. `embed_type` is e.g.
/// "opengraph" (a link with real title/description/image data), "image"
/// (a bare image URL — `data` is null), or "link" (recognized but nothing
/// useful to show). Almatter only ever renders the "opengraph" case.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PostEmbed {
    #[serde(rename = "type")]
    pub embed_type: String,
    #[serde(default)]
    pub url: String,
    /// `null` for every embed type except "opengraph" — `Option` rather
    /// than a bare `OpenGraphData` specifically so those other (far more
    /// common) embed types don't fail this post's whole deserialization.
    #[serde(default)]
    pub data: Option<OpenGraphData>,
}

/// The subset of the real (much larger) Open Graph object this app
/// actually displays — every field defaulted since a real-world page
/// rarely sets all of them.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct OpenGraphData {
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub description: String,
    #[serde(default)]
    pub site_name: String,
    #[serde(default)]
    pub images: Vec<OpenGraphImage>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OpenGraphImage {
    #[serde(default)]
    pub url: String,
    #[serde(default)]
    pub secure_url: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserStatus {
    pub user_id: String,
    /// One of Mattermost's own status strings: "online", "away", "dnd", "offline".
    pub status: String,
}

/// One row of `GET /users/{id}/preferences/favorite_channel` — Mattermost's
/// generic preference shape, narrowed to the two fields this app reads.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Preference {
    pub name: String,
    pub value: String,
}

/// A server-defined custom emoji (`GET /emoji`). Its image isn't included
/// here — that's a separate per-emoji fetch (`GET /emoji/{id}/image`),
/// cached to disk rather than round-tripped through the FFI's JSON channel.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CustomEmoji {
    pub id: String,
    pub name: String,
}

/// The raw shape of `GET /channels/{id}/posts`: an ordering plus a map, not
/// a plain array.
#[derive(Debug, Deserialize)]
pub struct PostList {
    pub order: Vec<String>,
    pub posts: HashMap<String, Post>,
}

impl PostList {
    /// Flattens into the order the server reported, newest-first as
    /// Mattermost sends it. Each post's `reply_count` is recomputed by
    /// counting replies actually present in this batch and merged in with
    /// whatever the server sent — the server's own field turned out not to
    /// be reliably populated on every deployment, so this is the figure
    /// actually trusted for display.
    pub fn into_ordered_posts(mut self) -> Vec<Post> {
        let mut reply_counts: HashMap<String, i64> = HashMap::new();
        for post in self.posts.values() {
            if !post.root_id.is_empty() {
                *reply_counts.entry(post.root_id.clone()).or_insert(0) += 1;
            }
        }

        self.order
            .into_iter()
            .filter_map(|id| self.posts.remove(&id))
            .map(|mut post| {
                if let Some(&counted) = reply_counts.get(&post.id) {
                    post.reply_count = post.reply_count.max(counted);
                }
                post
            })
            .collect()
    }
}
