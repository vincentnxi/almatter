using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>
/// One entry of the bookmark bar under the channel header: a link or a file
/// a member of the channel pinned there. Read-only in Almatter for now —
/// clicking opens it, nothing more.
/// </summary>
public sealed partial class BookmarkItem : ObservableObject, IServerFile
{
    public required string Id { get; init; }

    /// <summary>The name shown in the bar, without the characters that reverse text direction (see <see cref="AttachmentItem.WithoutDirectionControls"/>).</summary>
    public required string Label { get; init; }

    public required bool IsFile { get; init; }

    /// <summary>The link's address, for a link bookmark.</summary>
    public string Url { get; init; } = "";

    public string FileId { get; init; } = "";

    public string FileName { get; init; } = "";

    /// <summary>The emoji its author chose as an icon, as a character; empty for none or a server custom emoji, which then fall back to the link or file icon.</summary>
    public string Glyph { get; init; } = "";

    public bool HasGlyph => Glyph.Length > 0;
    public bool ShowLinkIcon => !HasGlyph && !IsFile;
    public bool ShowFileIcon => !HasGlyph && IsFile;

    /// <summary>
    /// The real destination, not the name again: a bookmark's name is free
    /// text, and "Intranet" can lead anywhere. The tooltip is where the user
    /// sees where a click will take them before clicking.
    /// </summary>
    public required string Tip { get; init; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>Unused — the bar has no save button — but part of <see cref="IServerFile"/>.</summary>
    public bool IsJustSaved { get; set; }
}
