using System.Globalization;

namespace Sela.Core;

/// <summary>Languages Sela's interface and texts are available in.</summary>
public enum AppLanguage { Indonesian, English }

public static class AppLanguages
{
    /// <summary>Indonesian for an Indonesian Windows display language, English for everything else.</summary>
    public static AppLanguage FromCulture(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "id", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Indonesian
            : AppLanguage.English;

    /// <summary>Culture used for dates and numbers in the given language.</summary>
    public static CultureInfo Culture(AppLanguage language)
    {
        var name = language == AppLanguage.English ? "en-US" : "id-ID";
        try { return CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}

/// <summary>Localized names shared by Core and the UI.</summary>
public static class Names
{
    public static string Prayer(Prayer p, AppLanguage language) => language == AppLanguage.English
        ? p switch
        {
            Core.Prayer.Imsak => "Imsak",
            Core.Prayer.Subuh => "Fajr",
            Core.Prayer.Terbit => "Sunrise",
            Core.Prayer.Dhuha => "Duha",
            Core.Prayer.Dzuhur => "Dhuhr",
            Core.Prayer.Ashar => "Asr",
            Core.Prayer.Maghrib => "Maghrib",
            _ => "Isha",
        }
        : p.ToString();

    public static string Habit(Habit h, AppLanguage language) => (h, language) switch
    {
        (Core.Habit.DzikirPagi, AppLanguage.English) => "Morning dhikr",
        (Core.Habit.DzikirPetang, AppLanguage.English) => "Evening dhikr",
        (_, AppLanguage.English) => "Salawat",
        (Core.Habit.DzikirPagi, _) => "Dzikir pagi",
        (Core.Habit.DzikirPetang, _) => "Dzikir petang",
        _ => "Sholawat",
    };
}
