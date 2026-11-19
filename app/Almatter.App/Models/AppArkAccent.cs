namespace Almatter.App.Models;

/// <summary>
/// The accent of the Ark theme, picked from its own row of pastel swatches.
/// Kept apart from <see cref="AppAccent"/> so that trying Ark and going back
/// to Almatter's own palette finds the user's usual accent untouched.
///
/// The numbers are what settings.json stores, so they never change meaning:
/// a new color takes the next free one. A number this version does not know
/// falls back to Citron.
/// </summary>
public enum AppArkAccent
{
    Citron = 0,
    Sauge = 1,
    Ciel = 2,
    Lavande = 3,
    Peche = 4,
    Rose = 5,
}

/// <summary>
/// The colors behind each <see cref="AppArkAccent"/>.
///
/// A pastel is the same on both Ark palettes: light enough to carry dark
/// text on a badge or a button, and light enough to read as text on the dark
/// palette. What a pastel cannot do is be read as text on the light palette
/// (the citron is 1,2:1 on white), so each one also has a deep shade of the
/// same hue for links, mentions and labels there. Every deep shade clears
/// 4,5:1 on white, on the sidebar and on its own selected-row tint.
/// </summary>
public static class ArkAccentPalette
{
    /// <summary>Text and icons drawn on a pastel: one near-black for all six.</summary>
    public const string OnPastelHex = "#1E2024";

    public static string PastelHex(AppArkAccent accent) => accent switch
    {
        AppArkAccent.Sauge => "#B9DDB1",
        AppArkAccent.Ciel => "#B4D5F3",
        AppArkAccent.Lavande => "#CEC3F4",
        AppArkAccent.Peche => "#F8C9A6",
        AppArkAccent.Rose => "#F4BFD2",
        _ => "#D6F35C",
    };

    /// <summary>The pastel's hue dark enough to read as text on the light palette.</summary>
    public static string DeepHex(AppArkAccent accent) => accent switch
    {
        AppArkAccent.Sauge => "#2E6A33",
        AppArkAccent.Ciel => "#285E8E",
        AppArkAccent.Lavande => "#5944A6",
        AppArkAccent.Peche => "#924A18",
        AppArkAccent.Rose => "#9C3560",
        _ => "#4E5E0B",
    };
}
