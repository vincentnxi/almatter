using System;
using System.Threading.Tasks;
using Almatter.App.Localization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public sealed partial class AttachmentItem : ObservableObject
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }

    public string SizeLabel => FormatSize(SizeBytes);

    /// <summary>True while a click is downloading this attachment before opening it — lets the row show it's working.</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>Flips on for a moment after the file has been saved where the user asked, so the download button shows a check mark as confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadTip))]
    public partial bool IsJustSaved { get; set; }

    public string DownloadTip => IsJustSaved ? Loc.S.AttachmentSaved : Loc.S.Download;

    /// <summary>
    /// A picture the server has a preview of: shown as a thumbnail in the
    /// conversation rather than as a file card. Everything else — and a
    /// picture cached before its dimensions were kept — stays a card.
    /// </summary>
    public bool IsImage { get; init; }

    /// <summary>
    /// The thumbnail's box, worked out from the picture's own dimensions when
    /// the row is built (see MainViewModel.BuildAttachments) and never
    /// changed afterwards: the box is there before the picture is, so the
    /// picture arriving late doesn't change the row's height — which, with
    /// the message list virtualized, would make the conversation jump.
    /// </summary>
    public double ThumbnailWidth { get; init; }
    public double ThumbnailHeight { get; init; }

    /// <summary>What the thumbnail says when pointed at — the card it replaces showed the name and size outright.</summary>
    public string ThumbnailTip => $"{FileName} · {SizeLabel}";

    /// <summary>Fetches and decodes the thumbnail. Supplied by the ViewModel, which owns the image cache.</summary>
    public Func<AttachmentItem, Task>? ThumbnailResolver { get; init; }

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
            if (!_thumbnailRequested && IsImage && ThumbnailResolver is not null)
            {
                _thumbnailRequested = true;
                _ = ThumbnailResolver(this);
            }
            return _thumbnail;
        }
        set => SetProperty(ref _thumbnail, value);
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(ThumbnailTip));
        OnPropertyChanged(nameof(DownloadTip));
    }

    /// <summary>"340 Ko", "1,2 Mo" in French; "340 KB", "1.2 MB" in English.</summary>
    public static string FormatSize(long bytes)
    {
        double size = bytes;
        var units = Loc.S.FileSizeUnits;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        var number = size.ToString(unitIndex == 0 ? "0" : "0.#", Loc.Culture);
        return $"{number} {units[unitIndex]}";
    }
}
