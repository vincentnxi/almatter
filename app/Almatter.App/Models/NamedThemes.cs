using System.Collections.Generic;
using System.Linq;

namespace Almatter.App.Models;

/// <summary>
/// One named theme's colours, in the vocabulary of the palette files they
/// come from rather than in Almatter's own: ThemeDefinition.FromPalette maps
/// them onto the app's tokens and derives the few the files leave out.
/// </summary>
public sealed record NamedPalette
{
    public required AppNamedTheme Id { get; init; }

    /// <summary>A proper name, the same in every language.</summary>
    public required string Name { get; init; }

    public required bool IsDark { get; init; }
    public required string Window { get; init; }
    public required string Panel { get; init; }
    public required string Surface { get; init; }
    public required string SurfaceHover { get; init; }
    public required string SurfaceActive { get; init; }
    public required string Outline { get; init; }
    public required string Text { get; init; }
    public required string Secondary { get; init; }
    public required string Dim { get; init; }
    public required string Accent { get; init; }
    public required string OnAccent { get; init; }
    public required string Danger { get; init; }
    public required string Warning { get; init; }

    /// <summary>The "online" presence dot. Most palette files have no green; those fall back to Almatter's own.</summary>
    public string? Online { get; init; }
}

/// <summary>
/// The named themes on offer, in the order the settings panel lists them.
///
/// The first seven are the shared palettes of fastframe-theme
/// (https://github.com/crmne/fastframe, MIT), which adapts Catppuccin, Nord,
/// Rosé Pine and Tokyo Night, all MIT as well: see licenses/README.md.
/// The values are copied by hand rather than read from the JSON files at
/// startup, so there is no file to parse and nothing a user's disk can feed in.
///
/// fastframe's eighth palette, Ristretto, is deliberately missing: its colours
/// are Monokai Pro's "Ristretto" filter, whose licence forbids redistribution.
/// The last three (Expresso, Cardamome, Crème) were designed for Almatter
/// instead, from scratch, and are under the app's own licence.
/// </summary>
public static class NamedThemes
{
    public static readonly IReadOnlyList<NamedPalette> All =
    [
        new()
        {
            Id = AppNamedTheme.Catppuccin, Name = "Catppuccin", IsDark = true,
            Window = "#1e1e2e", Panel = "#232434", Surface = "#2c2d3e", SurfaceHover = "#333446", SurfaceActive = "#3e3f52",
            Outline = "#414356", Text = "#cdd6f4", Secondary = "#999fb9", Dim = "#767a91",
            Accent = "#89b4fa", OnAccent = "#1e1e2e", Danger = "#f38ba8", Warning = "#f9e2af",
        },
        new()
        {
            Id = AppNamedTheme.CatppuccinLatte, Name = "Catppuccin Latte", IsDark = false,
            Window = "#eff1f5", Panel = "#eaecf1", Surface = "#e2e4ea", SurfaceHover = "#dbdee4", SurfaceActive = "#d2d4dc",
            // Secondary and accent darkened from fastframe's #7d8093 and
            // #1e66f5 (blended toward the palette's own text) to clear 4,5:1:
            // they measured 3,5:1 and 4,3:1. See the note on RosePineDawn.
            Outline = "#ced1d9", Text = "#4c4f69", Secondary = "#60637a", Dim = "#9ea0af",
            Accent = "#2762da", OnAccent = "#eff1f5", Danger = "#d20f39", Warning = "#df8e1d",
        },
        new()
        {
            Id = AppNamedTheme.Nord, Name = "Nord", IsDark = true,
            Window = "#2e3440", Panel = "#333945", Surface = "#3c424e", SurfaceHover = "#424854", SurfaceActive = "#4d535e",
            Outline = "#505662", Text = "#d8dee9", Secondary = "#a5abb6", Dim = "#838995",
            Accent = "#81a1c1", OnAccent = "#2e3440", Danger = "#bf616a", Warning = "#ebcb8b",
        },
        new()
        {
            Id = AppNamedTheme.RosePine, Name = "Rosé Pine", IsDark = true,
            Window = "#191724", Panel = "#1f1d2e", Surface = "#26233a", SurfaceHover = "#403d52", SurfaceActive = "#524f67",
            Outline = "#403d52", Text = "#e0def4", Secondary = "#908caa", Dim = "#6e6a86",
            Accent = "#c4a7e7", OnAccent = "#191724", Danger = "#eb6f92", Warning = "#f6c177",
        },
        new()
        {
            Id = AppNamedTheme.RosePineMoon, Name = "Rosé Pine Moon", IsDark = true,
            Window = "#232136", Panel = "#2a273f", Surface = "#393552", SurfaceHover = "#44415a", SurfaceActive = "#56526e",
            // Secondary lifted from #908caa (4,46:1 on the sidebar), as for Tokyo Night.
            Outline = "#44415a", Text = "#e0def4", Secondary = "#a3a0bc", Dim = "#6e6a86",
            Accent = "#c4a7e7", OnAccent = "#232136", Danger = "#eb6f92", Warning = "#f6c177",
        },
        new()
        {
            Id = AppNamedTheme.RosePineDawn, Name = "Rosé Pine Dawn", IsDark = false,
            Window = "#faf4ed", Panel = "#fffaf3", Surface = "#f2e9e1", SurfaceHover = "#dfdad9", SurfaceActive = "#cecacd",
            // Secondary and accent darkened from fastframe's #797593 and
            // #907aa9, which measured 4,0:1 and 3,5:1. Each is blended toward
            // the palette's own text by the least amount that reaches 4,6:1
            // on every ground it is drawn on, so the hue stays the authors'.
            // Only these two, and the secondary text of Tokyo Night and Rosé
            // Pine Moon, were under 4,5:1; everything else is as shipped.
            Outline = "#dfdad9", Text = "#575279", Secondary = "#696487", Dim = "#9893a5",
            Accent = "#766893", OnAccent = "#faf4ed", Danger = "#b4637a", Warning = "#ea9d34",
        },
        new()
        {
            Id = AppNamedTheme.TokyoNight, Name = "Tokyo Night", IsDark = true,
            Window = "#1a1b26", Panel = "#1e202b", Surface = "#252734", SurfaceHover = "#2b2d3b", SurfaceActive = "#343646",
            // Secondary lifted from fastframe's #7e84a1 (4,4:1 on the
            // sidebar) the same way as the light palettes' below.
            Outline = "#373949", Text = "#a9b1d6", Secondary = "#888fae", Dim = "#62667e",
            Accent = "#7aa2f7", OnAccent = "#1a1b26", Danger = "#f7768e", Warning = "#e0af68",
        },

        // Almatter's own. Contrast on the window, measured: text 13,7:1,
        // secondary 7,3:1, dim 4,1:1, accent 7,5:1.
        new()
        {
            Id = AppNamedTheme.Expresso, Name = "Expresso", IsDark = true,
            Window = "#1f1a17", Panel = "#262019", Surface = "#2e2722", SurfaceHover = "#352d27", SurfaceActive = "#40362f",
            Outline = "#3d342e", Text = "#ede4dc", Secondary = "#b3a79c", Dim = "#857a70",
            Accent = "#d9a066", OnAccent = "#1f1a17", Danger = "#e8685a", Warning = "#e6b450", Online = "#8fbf6a",
        },
        // Text 13,5:1, secondary 7,4:1, dim 4,3:1, accent 8,6:1.
        new()
        {
            Id = AppNamedTheme.Cardamome, Name = "Cardamome", IsDark = true,
            Window = "#1b1d19", Panel = "#20231e", Surface = "#282b25", SurfaceHover = "#2f332c", SurfaceActive = "#393e35",
            Outline = "#363a32", Text = "#e4e6dc", Secondary = "#a9ad9f", Dim = "#7d8174",
            Accent = "#a3c27f", OnAccent = "#1b1d19", Danger = "#e5735f", Warning = "#d9b55a", Online = "#7fc29b",
        },
        // Text 13,4:1, secondary 5,7:1, dim 3,5:1, accent 5,1:1.
        new()
        {
            Id = AppNamedTheme.Creme, Name = "Crème", IsDark = false,
            Window = "#f7f1ea", Panel = "#efe6dc", Surface = "#e8ddd1", SurfaceHover = "#e2d6c9", SurfaceActive = "#d3c5b6",
            Outline = "#ddd0c2", Text = "#2e2520", Secondary = "#6b5d53", Dim = "#8c7d72",
            Accent = "#9a5428", OnAccent = "#fffaf4", Danger = "#b8392b", Warning = "#a86a00", Online = "#3d8a4f",
        },
    ];

    /// <summary>The palette for a saved choice, or null for None and for any number this version does not know.</summary>
    public static NamedPalette? Find(AppNamedTheme id) =>
        id == AppNamedTheme.None ? null : All.FirstOrDefault(p => p.Id == id);
}
