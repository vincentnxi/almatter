namespace Almatter.App.Localization;

/// <summary>
/// The languages the interface can be shown in. Adding one means a value
/// here, a Strings.&lt;Language&gt;.cs file next to the others, and a line in
/// Loc.StringsFor and Loc.CultureFor — the compiler points at anything missed.
/// </summary>
public enum AppLanguage
{
    French,
    English,
}
