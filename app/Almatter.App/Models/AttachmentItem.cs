using System;
using Almatter.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>A file attached to a message: a thumbnail when it's a picture (see PictureItem), a file card otherwise.</summary>
public sealed partial class AttachmentItem : PictureItem, IServerFile
{
    public required string Id { get; init; }

    string IServerFile.FileId => Id;

    /// <summary>
    /// The name as sent, minus the invisible characters that reverse the
    /// direction of the text. "facture" + U+202E + "fdp.exe", with a right-to-left
    /// override before "fdp", is shown as "factureexe.pdf": the trick that
    /// passes a program off as a document. Everything built from this name —
    /// the card, its tooltip, the name proposed when saving — gets it without
    /// them, so the extension on screen is the real one. The core strips the
    /// same characters from the file written to disk.
    /// </summary>
    public required string FileName
    {
        get => _fileName;
        init => _fileName = WithoutDirectionControls(value);
    }

    private readonly string _fileName = "";

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
    /// A picture the server has a preview of, or a GIF of known size: shown
    /// as a thumbnail in the conversation rather than as a file card.
    /// Everything else stays a card.
    /// </summary>
    public bool IsImage { get; init; }

    /// <summary>
    /// The thumbnail is decoded from the file itself (a GIF's first frame)
    /// because the server made no preview of it — as for an animated GIF.
    /// </summary>
    public bool ThumbnailFromOriginal { get; init; }

    protected override bool HasThumbnail => IsImage;

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(DownloadTip));
    }

    /// <summary>
    /// Drops the Unicode marks and embeddings that change the direction of
    /// text (U+061C, U+200E-200F, U+202A-202E, U+2066-2069). Kept in step
    /// with <c>is_direction_control</c> in the core's dispatch.rs.
    /// </summary>
    public static string WithoutDirectionControls(string name)
    {
        if (!name.AsSpan().ContainsAny(DirectionControls))
        {
            return name;
        }
        var kept = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!DirectionControls.Contains(c))
            {
                kept.Append(c);
            }
        }
        return kept.ToString();
    }

    private static readonly System.Buffers.SearchValues<char> DirectionControls = System.Buffers.SearchValues.Create(
        [(char)0x061C, (char)0x200E, (char)0x200F, (char)0x202A, (char)0x202B, (char)0x202C, (char)0x202D, (char)0x202E, (char)0x2066, (char)0x2067, (char)0x2068, (char)0x2069]);

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
