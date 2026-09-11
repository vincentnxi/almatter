namespace Almatter.App.Models;

/// <summary>A file already uploaded and waiting to go out with the next message sent from this composer.</summary>
public sealed class PendingAttachmentItem
{
    public required string FileId { get; init; }
    public required string FileName { get; init; }
}
