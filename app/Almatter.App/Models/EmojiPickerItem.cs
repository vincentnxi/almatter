using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One clickable entry in the emoji picker — either a standard glyph emoji or a server custom emoji (image loaded lazily).</summary>
public sealed partial class EmojiPickerItem : ObservableObject
{
    /// <summary>The toneless shortcode, e.g. "ok_hand" — the identity used for searching and for counting how often it's picked.</summary>
    public required string BaseName { get; init; }

    /// <summary>Set for a standard emoji; null for a custom one (which renders via Image instead).</summary>
    public string? BaseGlyph { get; init; }

    /// <summary>Set for a custom emoji — the server id used to fetch/cache its image.</summary>
    public string? EmojiId { get; init; }

    /// <summary>The emoji_name sent to the reactions API — BaseName plus the chosen skin-tone suffix, when this emoji takes one.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>What's drawn, at the chosen skin tone. Null for a custom emoji.</summary>
    [ObservableProperty]
    public partial string? Glyph { get; set; }

    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    public bool IsCustom => BaseGlyph is null;

    public static EmojiPickerItem Standard(string shortcode, string glyph, EmojiSkinTone tone)
    {
        var item = new EmojiPickerItem { BaseName = shortcode, BaseGlyph = glyph };
        item.ApplyTone(tone);
        return item;
    }

    public static EmojiPickerItem Custom(string name, string emojiId) =>
        new() { BaseName = name, EmojiId = emojiId, Name = name };

    /// <summary>
    /// Re-skins this entry in place when the tone preference changes, so the
    /// already-loaded picker just repaints instead of rebuilding — which for
    /// custom emoji would mean re-fetching every image.
    /// </summary>
    public void ApplyTone(EmojiSkinTone tone)
    {
        if (BaseGlyph is null)
        {
            return;
        }
        Name = EmojiShortcodes.ApplyTone(BaseName, tone);
        Glyph = EmojiShortcodes.ToGlyph(Name);
    }

    /// <summary>
    /// Whether this emoji answers to <paramref name="query"/>. Underscores
    /// are treated as spaces so "ok hand" finds "ok_hand", and the query's
    /// own colons are ignored so pasting ":tada:" works as well as typing
    /// "tada".
    /// </summary>
    public bool Matches(string query) =>
        BaseName.Replace('_', ' ').Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts a typed query into the same shape Matches compares against.</summary>
    public static string NormalizeQuery(string raw) =>
        raw.Replace(':', ' ').Replace('_', ' ').Trim();
}
