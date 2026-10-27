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

    /// <summary>For screen readers: "1 jam 5 menit" / "1 hour 5 minutes", rounded up to the minute.</summary>
    public static string Spoken(TimeSpan left)
    {
        var total = (long)Math.Ceiling(Math.Max(0, left.TotalMinutes));
        long h = total / 60, m = total % 60;
        string Part(long n, string one, string many) => Loc.F(n == 1 ? one : many, n);
        if (h == 0) return Part(m, "Notch_SpokenMinute", "Notch_SpokenMinutes");
        var hours = Part(h, "Notch_SpokenHour", "Notch_SpokenHours");
        return m == 0 ? hours : $"{hours} {Part(m, "Notch_SpokenMinute", "Notch_SpokenMinutes")}";
    }

    /// <summary>
    /// Compact countdown for the idle pill: "5j 41m" / "5h 41m" from an hour out, otherwise the ticking
    /// <see cref="Countdown"/> ("25m 04d"), so the last hour still visibly counts down.
    /// </summary>
    public static string CountdownShort(TimeSpan left)
    {
        var total = (long)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        if (total < 3600) return Countdown(left);
        return $"{total / 3600}{Loc.T("Notch_UnitHour")} {total % 3600 / 60:00}{Loc.T("Notch_UnitMinute")}";
    }

    /// <summary>Whole minutes since a start: "baru saja" / "just now" under a minute, then "12m lalu" / "12m ago".</summary>
    public static string Ago(TimeSpan elapsed)
    {
        var m = (long)Math.Floor(Math.Max(0, elapsed.TotalMinutes));
        return m < 1 ? Loc.T("Notch_JustNow") : Loc.F("Notch_MinutesAgo", m);
    }

    public static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Diperbarui 14:05" / "Updated kemarin 21:30"; older than yesterday shows the short date.</summary>
    public static string Updated(DateTime at, DateTime now)
    {
        var when = at.Date == now.Date ? Hm(at)
            : at.Date == now.Date.AddDays(-1) ? Loc.F("Notch_Yesterday", Hm(at))
            : $"{at.ToString("d MMM", Loc.Instance.Culture)} {Hm(at)}";
        return Loc.F("Notch_UpdatedAt", when);
    }

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
