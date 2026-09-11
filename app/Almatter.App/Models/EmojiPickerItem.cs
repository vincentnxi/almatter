using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One clickable entry in the reaction picker — either a standard glyph emoji or a server custom emoji (image loaded lazily).</summary>
public sealed partial class EmojiPickerItem : ObservableObject
{
    /// <summary>The emoji_name sent to the reactions API — e.g. "+1" or a custom emoji's name.</summary>
    public required string Name { get; init; }

    /// <summary>Set for a standard emoji; null for a custom one (which renders via Image instead).</summary>
    public string? Glyph { get; init; }

    /// <summary>Set for a custom emoji — the server id used to fetch/cache its image.</summary>
    public string? EmojiId { get; init; }

    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    public bool IsCustom => Glyph is null;
}
