namespace Almatter.App.Models;

/// <summary>
/// A file waiting to go out with the next message sent from this composer:
/// either already uploaded (picked or pasted — <see cref="FileId"/> set), or
/// only noted (dropped on the window — <see cref="LocalPath"/> set, uploaded
/// when the message is sent). A drop can land anywhere, by mistake too, so
/// nothing of it leaves the machine before the user presses Send.
/// </summary>
public sealed class PendingAttachmentItem
{
    /// <summary>The server's id once uploaded; null while the file is only noted.</summary>
    public string? FileId { get; set; }

    /// <summary>The file on disk, for one that isn't uploaded yet.</summary>
    public string? LocalPath { get; init; }

    public required string FileName { get; init; }
}
