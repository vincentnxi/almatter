using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

public sealed partial class ReactionItem : ObservableObject
{
    public required string PostId { get; init; }
    /// <summary>The raw emoji_name (e.g. "+1") sent to the reactions API — Emoji below is only the rendered glyph.</summary>
    public required string EmojiName { get; init; }
    /// <summary>The rendered glyph, or a ":name:" fallback for a custom emoji until Image below has finished loading.</summary>
    public required string Emoji { get; init; }
    public required int Count { get; init; }
    public bool ReactedByMe { get; init; }

    /// <summary>Set asynchronously for a server custom emoji once its image has downloaded — null (and Emoji's ":name:" fallback shown instead) until then, or permanently if it fails to resolve.</summary>
    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    public IBrush BorderBrush => ReactedByMe ? ColorTokens.Accent : ColorTokens.Divider;
    public IBrush TextBrush => ReactedByMe ? ColorTokens.AccentInk : ColorTokens.TextSecondary;

    /// <summary>Called after the accent color or theme changes so an already-built reaction pill repaints in place.</summary>
    public void RefreshColors()
    {
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(TextBrush));
    }
}
