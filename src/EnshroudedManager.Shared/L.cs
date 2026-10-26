using System.Globalization;

namespace EnshroudedManager.Shared;

/// <summary>
/// Two-language text helper: every user-facing string is written as L.T("français", "English").
/// The language is chosen at install time and stored in the service settings; clients follow it.
/// </summary>
public static class L
{
    public static string Language { get; private set; } = Default();

    public static bool En => Language == "en";

    public static void Set(string? language) => Language = language == "en" ? "en" : language == "fr" ? "fr" : Default();

    public static string Default() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

    public static string T(string fr, string en) => En ? en : fr;

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(En ? "en-GB" : "fr-FR");
}
