using System;
using System.Threading.Tasks;
using Almatter.App.Localization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>
/// A picture shown in a message, below its text: an attached image
/// (AttachmentItem) or a picture the text links to (LinkedPictureItem —
/// a GIF from Giphy, say). Both are laid out, folded away, and — for an
/// animated GIF — played on a click the same way; this holds what they share.
/// </summary>
public abstract partial class PictureItem : ObservableObject
{
    /// <summary>What the picture is called: the file's name, or a linked picture's alt text. Heads the line that folds a message's pictures away.</summary>
    public abstract string Caption { get; }

    /// <summary>True while a click is fetching the file — lets the picture show it's working.</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>
    /// The thumbnail's box, worked out from the picture's own dimensions when
    /// the row is built (see MainViewModel.BuildAttachments) and never
    /// changed afterwards: the box is there before the picture is, so the
    /// picture arriving late doesn't change the row's height — which, with
    /// the message list virtualized, would make the conversation jump.
    /// </summary>
    public double ThumbnailWidth { get; init; }
    public double ThumbnailHeight { get; init; }

    /// <summary>What the picture says when pointed at. A GIF adds what a click does, since a click no longer opens it.</summary>
    public string ThumbnailTip => IsGif
        ? $"{TipText}\n{(IsGifPlaying ? Loc.S.StopGif : Loc.S.PlayGif)}"
        : TipText;

    protected abstract string TipText { get; }

    /// <summary>
    /// An animated GIF: shown still with a "GIF" badge, and played in place on
    /// a click (see MainViewModel.ClickPicture). Turned off for good if the
    /// file turns out to hold a single frame, or one too large to play — that
    /// one then opens like any other picture.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailTip))]
    [NotifyPropertyChangedFor(nameof(ShowsGifBadge))]
    public partial bool IsGif { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailTip))]
    [NotifyPropertyChangedFor(nameof(ShowsGifBadge))]
    public partial bool IsGifPlaying { get; set; }

    /// <summary>The badge is there while the GIF is still, telling it can move; it goes away while it plays.</summary>
    public bool ShowsGifBadge => IsGif && !IsGifPlaying;

    /// <summary>Only an attachment has a download button on its corner; a linked picture opens its address instead.</summary>
    public virtual bool CanSave => false;

    /// <summary>Whether a thumbnail is to be fetched at all — a file that is not a picture never is.</summary>
    protected virtual bool HasThumbnail => true;

    private Bitmap? _playingFrame;

    /// <summary>The GIF frame currently on screen in place of the still thumbnail; null puts the still back.</summary>
    public void ShowFrame(Bitmap? frame)
    {
        _playingFrame = frame;
        OnPropertyChanged(nameof(Thumbnail));
    }

    /// <summary>Fetches and decodes the thumbnail. Supplied by the ViewModel, which owns the image cache.</summary>
    public Func<PictureItem, Task>? ThumbnailResolver { get; init; }

    private bool _thumbnailRequested;
    private Bitmap? _thumbnail;

    /// <summary>
    /// Null until the picture has been decoded (or forever, on a failure).
    /// Only asked for the first time something reads this — the first time
    /// the message's row is built on screen — for the same reason as
    /// LinkPreviewItem.ImageSource: a picture in a message nobody has
    /// scrolled to is never downloaded.
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested && HasThumbnail && ThumbnailResolver is not null)
            {
                _thumbnailRequested = true;
                _ = ThumbnailResolver(this);
            }
            return _playingFrame ?? _thumbnail;
        }
        set
        {
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public virtual void RefreshLanguage() => OnPropertyChanged(nameof(ThumbnailTip));
}
