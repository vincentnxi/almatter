using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Almatter.App.Interop;

/// <summary>
/// The JSON reading code for every reply of the Rust core, written at compile
/// time instead of discovered by reflection while the app runs. Reflection
/// breaks once the app is trimmed (the trimmer cannot see which properties it
/// needs and removes them), and this is also faster to start.
///
/// Each response type passed to MattermostService.CallAsync must be listed
/// here as Envelope&lt;T&gt;; a missing one fails on its first call with an
/// error naming it, trimmed or not.
/// </summary>
[JsonSerializable(typeof(Envelope<JsonObject>))]
[JsonSerializable(typeof(Envelope<AvatarImageData>))]
[JsonSerializable(typeof(Envelope<BookmarksData>))]
[JsonSerializable(typeof(Envelope<ChannelFilesData>))]
[JsonSerializable(typeof(Envelope<ChannelAliasesData>))]
[JsonSerializable(typeof(Envelope<ChannelData>))]
[JsonSerializable(typeof(Envelope<ChannelsData>))]
[JsonSerializable(typeof(Envelope<CustomEmojiData>))]
[JsonSerializable(typeof(Envelope<EmojiImageData>))]
[JsonSerializable(typeof(Envelope<FavoritesData>))]
[JsonSerializable(typeof(Envelope<FileData>))]
[JsonSerializable(typeof(Envelope<FlushOutboxData>))]
[JsonSerializable(typeof(Envelope<LinkPreviewImageData>))]
[JsonSerializable(typeof(Envelope<LoginData>))]
[JsonSerializable(typeof(Envelope<MentionEventsData>))]
[JsonSerializable(typeof(Envelope<OutboxData>))]
[JsonSerializable(typeof(Envelope<PostData>))]
[JsonSerializable(typeof(Envelope<PostsData>))]
[JsonSerializable(typeof(Envelope<PublicChannelsData>))]
[JsonSerializable(typeof(Envelope<RevisionData>))]
[JsonSerializable(typeof(Envelope<SearchData>))]
[JsonSerializable(typeof(Envelope<SendMessageData>))]
[JsonSerializable(typeof(Envelope<StatusesData>))]
[JsonSerializable(typeof(Envelope<TeamsData>))]
[JsonSerializable(typeof(Envelope<UpdateCheckData>))]
[JsonSerializable(typeof(Envelope<UploadFileData>))]
[JsonSerializable(typeof(Envelope<UserIdsData>))]
[JsonSerializable(typeof(Envelope<UsersData>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext
{
}
