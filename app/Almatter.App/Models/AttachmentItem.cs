using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>A file attached to a message: a thumbnail when it's a picture (see PictureItem), a file card otherwise.</summary>
public sealed partial class AttachmentItem : PictureItem
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }

    public string SizeLabel => FormatSize(SizeBytes);

    public override string Caption => FileName;

    /// <summary>The card it replaces showed the name and size outright.</summary>
    protected override string TipText => $"{FileName} · {SizeLabel}";

    /// <summary>Flips on for a moment after the file has been saved where the user asked, so the download button shows a check mark as confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadTip))]
    public partial bool IsJustSaved { get; set; }

    public string DownloadTip => IsJustSaved ? Loc.S.AttachmentSaved : Loc.S.Download;

    public override bool CanSave => true;

    /// <summary>
    /// A picture the server has a preview of: shown as a thumbnail in the
    /// conversation rather than as a file card. Everything else — and a
    /// picture cached before its dimensions were kept — stays a card.
    /// </summary>
    public bool IsImage { get; init; }

    protected override bool HasThumbnail => IsImage;

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(SizeLabel));
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
