using Almatter.App.Localization;
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

    public void RefreshLanguage() => OnPropertyChanged(nameof(SizeLabel));

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
