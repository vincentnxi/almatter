using System;
using System.Threading.Tasks;
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

    /// <summary>Fetches and decodes the og:image. Supplied by the ViewModel, which owns the image cache.</summary>
    public Func<LinkPreviewItem, Task>? ImageResolver { get; init; }

    private bool _imageRequested;
    private Bitmap? _imageSource;

    /// <summary>
    /// Null until the image has been decoded (or forever, on a failure) — the
    /// card reserves the same fixed-height area for it either way, so this
    /// arriving late never changes the row's height.
    ///
    /// The image is only asked for the first time something reads this, which
    /// in practice means the first time the message's row is built on screen.
    /// Asking at construction instead downloaded and decoded the image of
    /// every link in up to a thousand messages each time a channel opened,
    /// scrolled to or not — the bulk of what made the app's memory grow with
    /// every channel switch.
    /// </summary>
    public Bitmap? ImageSource
    {
        get
        {
            if (!_imageRequested && HasImage && ImageResolver is not null)
            {
                _imageRequested = true;
                _ = ImageResolver(this);
            }
            return _imageSource;
        }
        set => SetProperty(ref _imageSource, value);
    }

    /// <summary>Lets a request that was declined (previews switched off) be made again later. Raises nothing — see RequestImageAgain.</summary>
    public void ForgetImageRequest() => _imageRequested = false;

    /// <summary>Re-reads ImageSource on whatever is bound to it, so a card on screen asks for its image — used when previews are switched back on.</summary>
    public void RequestImageAgain()
    {
        if (_imageSource is null)
        {
            _imageRequested = false;
            OnPropertyChanged(nameof(ImageSource));
        }
    }
}
