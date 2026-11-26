using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>
/// One row of the channel files panel: a file someone posted in the channel.
/// Clicking it opens it like an attachment; its buttons save it or go to the
/// message it came with.
/// </summary>
public sealed partial class ChannelFileItem : ObservableObject, IServerFile
{
    public required string FileId { get; init; }
    public required string PostId { get; init; }

    /// <summary>Without the characters that reverse text direction, so the extension shown is the real one.</summary>
    public required string FileName { get; init; }

    /// <summary>A short type badge: "PDF", "PNG"… — empty for a file without extension.</summary>
    public required string TypeLabel { get; init; }

    public bool HasTypeLabel => TypeLabel.Length > 0;

    /// <summary>"Marie Dupont · Hier 14:32 · 1,2 Mo".</summary>
    public required string Details { get; init; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadTip))]
    public partial bool IsJustSaved { get; set; }

    public string DownloadTip => IsJustSaved ? Loc.S.AttachmentSaved : Loc.S.Download;
}
