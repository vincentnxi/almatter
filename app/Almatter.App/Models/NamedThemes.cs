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

    /// <summary>
    /// Top and bottom of a gradient drawn behind the whole window, or null
    /// for a plain one. When set, Window, Panel and Surface are still needed:
    /// they are the solid colours for the places a gradient can't go (code
    /// blocks, cards, rings around presence dots), so pick them to sit on it.
    /// </summary>
    public string? GroundTop { get; init; }
    public string? GroundBottom { get; init; }
}

/// <summary>
/// The named themes on offer, in the order the settings panel lists them.
///
/// Nord comes from the shared palettes of fastframe-theme
/// (https://github.com/crmne/fastframe, MIT), which adapts Nord, MIT as
/// well: see licenses/README.md. The values are copied by hand rather than
/// read from a JSON file at startup, so there is no file to parse and
/// nothing a user's disk can feed in.
///
/// The others (the gradient pair Aube and Crépuscule, Cardamome, Crème)
/// were designed for Almatter, from scratch, and are under the app's own
/// licence. Salon, Almatter's own as well, follows the light/dark mode and
/// so lives in ThemeDefinition rather than here; the settings panel lists
/// it after Crépuscule.
///
/// A palette only changes colours: every theme keeps Almatter's layout,
/// shapes and fonts.
/// </summary>
public static class NamedThemes
{
    public static readonly IReadOnlyList<NamedPalette> All =
    [
        // The gradient pair: the ground fades from a cooler top to a dark
        // bottom across the whole window. Window is the bottom; Panel is
        // roughly what the sidebar's veil gives over the gradient's middle.
        new()
        {
            Id = AppNamedTheme.Aube, Name = "Aube", IsDark = true,
            GroundTop = "#463440", GroundBottom = "#1e1b1f",
            Window = "#1e1b1f", Panel = "#261e24", Surface = "#32282f", SurfaceHover = "#3a3037", SurfaceActive = "#4a3e46",
            Outline = "#3b3138", Text = "#f0e7ea", Secondary = "#c0b2b8", Dim = "#978a90",
            Accent = "#f4ad72", OnAccent = "#1e1b1f", Danger = "#ee6e74", Warning = "#e5c15e", Online = "#8cc483",
        },
        new()
        {
            Id = AppNamedTheme.Crepuscule, Name = "Crépuscule", IsDark = true,
            GroundTop = "#3b3650", GroundBottom = "#1f1a1f",
            Window = "#1f1a1f", Panel = "#221e2a", Surface = "#2e2938", SurfaceHover = "#36303f", SurfaceActive = "#453e50",
            Outline = "#38323f", Text = "#efe7e6", Secondary = "#bdb2b4", Dim = "#958a8e",
            Accent = "#f7a382", OnAccent = "#1f1a1f", Danger = "#ef6f6f", Warning = "#e8bb5c", Online = "#8fc77a",
        },
        new()
        {
            Id = AppNamedTheme.Nord, Name = "Nord", IsDark = true,
            Window = "#2e3440", Panel = "#333945", Surface = "#3c424e", SurfaceHover = "#424854", SurfaceActive = "#4d535e",
            Outline = "#505662", Text = "#d8dee9", Secondary = "#a5abb6", Dim = "#838995",
            Accent = "#81a1c1", OnAccent = "#2e3440", Danger = "#bf616a", Warning = "#ebcb8b",
        },

        // Almatter's own. Text 13,5:1, secondary 7,4:1, dim 4,3:1, accent 8,6:1.
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
