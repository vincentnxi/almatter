using System;
using System.ComponentModel;
using System.Globalization;

namespace Almatter.App.Localization;

/// <summary>
/// The interface's current language, and the one place that knows it.
///
/// Every piece of text on screen comes from <see cref="T"/>, which holds the
/// full set of texts for one language. Switching languages swaps that set
/// and announces it: the windows bind their texts through Loc.Instance.T, so
/// they reword themselves on the spot. What the view models put into words
/// in code (dates, "is typing…", reaction tooltips) is reworded by
/// MainViewModel.ApplyLanguage. No restart involved.
///
/// XAML:  Text="{Binding T.SettingsTitle, Source={x:Static loc:Loc.Instance}}"
/// Code:  Loc.S.SettingsTitle
///
/// Both are checked when the app is built — a misspelt name fails the build
/// rather than leaving a blank on screen.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>Shorthand for code: the texts in the current language.</summary>
    public static Strings S => Instance.T;

    /// <summary>Dates, times and numbers, in the current language's conventions.</summary>
    public static CultureInfo Culture => Instance._culture;

    private CultureInfo _culture = CultureFor(AppLanguage.French);

    private Loc()
    {
    }

    public AppLanguage Language { get; private set; } = AppLanguage.French;

    public Strings T { get; private set; } = Strings.French;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(AppLanguage language)
    {
        if (language == Language)
        {
            return;
        }
        Language = language;
        T = StringsFor(language);
        _culture = CultureFor(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(T)));
    }

    /// <summary>
    /// What someone who never picked a language gets: French on a French
    /// Windows, English everywhere else — English being the language most
    /// likely to be read by someone whose own isn't offered.
    /// </summary>
    public static AppLanguage SystemDefault =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? AppLanguage.French : AppLanguage.English;

    private static Strings StringsFor(AppLanguage language) => language switch
    {
        AppLanguage.English => Strings.English,
        _ => Strings.French,
    };

    private static CultureInfo CultureFor(AppLanguage language) => language switch
    {
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.GetCultureInfo("fr-FR"),
    };
}
