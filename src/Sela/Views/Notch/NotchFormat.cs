using System.Globalization;
using Sela.Core;
using Sela.Localization;

namespace Sela.Views.Notch;

/// <summary>Display formatting for the notch, in the current <see cref="Loc"/> language.</summary>
public static class NotchFormat
{
    private static readonly UmAlQuraCalendar Hijri = new();

    private static readonly string[] HijriMonthsId =
    {
        "Muharram", "Safar", "Rabiul Awal", "Rabiul Akhir", "Jumadil Awal", "Jumadil Akhir",
        "Rajab", "Syaban", "Ramadhan", "Syawal", "Dzulqaidah", "Dzulhijjah",
    };

    private static readonly string[] HijriMonthsEn =
    {
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Awwal", "Jumada al-Thani",
        "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah",
    };

    private static AppLanguage Lang => Loc.Instance.Language;

    /// <summary>
    /// Live countdown with seconds so it visibly ticks: "1j 05m 09d" / "1h 05m 09s", "25m 04d", "09d".
    /// Units after the first are zero-padded, so with tabular digits the width stays put.
    /// </summary>
    public static string Countdown(TimeSpan left)
    {
        var total = (long)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        string uh = Loc.T("Notch_UnitHour"), um = Loc.T("Notch_UnitMinute"), us = Loc.T("Notch_UnitSecond");
        if (h > 0) return $"{h}{uh} {m:00}{um} {s:00}{us}";
        if (m > 0) return $"{m}{um} {s:00}{us}";
        return $"{s:00}{us}";
    }

    public static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Sabtu, 24 Oktober 2026" / "Saturday, October 24, 2026".</summary>
    public static string Gregorian(DateTime d) => d.ToString(Loc.T("Notch_DateFormat"), Loc.Instance.Culture);

    /// <summary>"13 Jumadil Awal 1448 H" / "13 Jumada al-Awwal 1448 AH" (Umm al-Qura; can differ a day from the Kemenag isbat).</summary>
    public static string HijriDate(DateTime d)
    {
        try
        {
            var months = Lang == AppLanguage.English ? HijriMonthsEn : HijriMonthsId;
            return $"{Hijri.GetDayOfMonth(d)} {months[Hijri.GetMonth(d) - 1]} {Hijri.GetYear(d)} {Loc.T("Notch_HijriSuffix")}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    public static string PrayerName(Prayer p) => Names.Prayer(p, Lang);

    /// <summary>Imsak, Terbit and Dhuha are shown as secondary rows.</summary>
    public static bool IsSecondary(Prayer p) => p is Prayer.Imsak or Prayer.Terbit or Prayer.Dhuha;

    public static string SourceName(CalendarSource s) => s == CalendarSource.Google ? "Google" : "Outlook";

    /// <summary>Tiny Fluent glyph for the calendar source: mail for Outlook, globe for Google (iCal).</summary>
    public static string SourceGlyph(CalendarSource s) => s == CalendarSource.Google ? "\uE774" : "\uE715";

    public static string HabitName(Habit h) => Names.Habit(h, Lang);

    /// <summary>First ~<paramref name="max"/> characters on a word boundary, whitespace collapsed, with an ellipsis.</summary>
    public static string Excerpt(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= max) return flat;
        var cut = flat.LastIndexOf(' ', max);
        if (cut < max / 2) cut = max;
        return flat[..cut].TrimEnd(',', '.', ';', ':', ' ') + "…";
    }

    public static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}
