using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>A message's link preview card — the server's opengraph data for the first link it recognized, plus the (asynchronously resolved) og:image bitmap.</summary>
public sealed partial class LinkPreviewItem : ObservableObject
{
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SiteName { get; init; }
    public string ImageUrl { get; init; } = "";
    public bool HasImage => ImageUrl.Length > 0;
    public bool HasTitle => Title.Length > 0;
    public bool HasDescription => Description.Length > 0;
    public bool HasSiteName => SiteName.Length > 0;

    /// <summary>Null until the image has downloaded (or forever, on a fetch failure) — the card reserves the same fixed-height area for it either way, so this arriving late never changes the row's height.</summary>
    [ObservableProperty]
    public partial Bitmap? ImageSource { get; set; }
}
