namespace Almatter.App.Models;

/// <summary>
/// A file on the server the user can open or save from a click: a message
/// attachment, a bookmarked file, a row of the channel files panel. One
/// shape so the opening and saving — and their safety rules (mark of the
/// Web, programs shown rather than run) — exist in one place,
/// MainViewModel.OpenServerFileAsync / SaveServerFileAsync.
/// </summary>
public interface IServerFile
{
    string FileId { get; }

    /// <summary>Already stripped of the characters that reverse text direction (see AttachmentItem.WithoutDirectionControls).</summary>
    string FileName { get; }

    bool IsDownloading { get; set; }

    /// <summary>On for a moment after a save, for the check mark on the download button.</summary>
    bool IsJustSaved { get; set; }
}
