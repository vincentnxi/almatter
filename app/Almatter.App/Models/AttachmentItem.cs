using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public sealed partial class AttachmentItem : ObservableObject
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string SizeLabel { get; init; }

    /// <summary>True while a click is downloading this attachment before opening it — lets the row show it's working.</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; set; }
}
