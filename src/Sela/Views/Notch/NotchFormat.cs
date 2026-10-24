using System.Globalization;
using Sela.Core;

namespace Sela.Views.Notch;

/// <summary>Indonesian display formatting for the notch.</summary>
public static class NotchFormat
{
    private static readonly CultureInfo Indonesian = LoadIndonesian();
    private static readonly UmAlQuraCalendar Hijri = new();

    private static readonly string[] HijriMonths =
    {
        "Muharram", "Safar", "Rabiul Awal", "Rabiul Akhir", "Jumadil Awal", "Jumadil Akhir",
        "Rajab", "Syaban", "Ramadhan", "Syawal", "Dzulqaidah", "Dzulhijjah",
    };

    private static CultureInfo LoadIndonesian()
    {
        try { return CultureInfo.GetCultureInfo("id-ID"); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    /// <summary>
    /// Live countdown with seconds so it visibly ticks: "1j 05m 09d", "25m 04d", "09d".
    /// Units after the first are zero-padded, so with tabular digits the width stays put.
    /// </summary>
    public static string Countdown(TimeSpan left)
    {
        var total = (long)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        if (h > 0) return $"{h}j {m:00}m {s:00}d";
        if (m > 0) return $"{m}m {s:00}d";
        return $"{s:00}d";
    }

    public static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Sabtu, 24 Oktober 2026".</summary>
    public static string Gregorian(DateTime d) => d.ToString("dddd, d MMMM yyyy", Indonesian);

    /// <summary>"13 Jumadil Awal 1448 H" (Umm al-Qura; can differ a day from the Kemenag isbat).</summary>
    public static string HijriDate(DateTime d)
    {
        try
        {
            return $"{Hijri.GetDayOfMonth(d)} {HijriMonths[Hijri.GetMonth(d) - 1]} {Hijri.GetYear(d)} H";
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    public static string PrayerName(Prayer p) => ReminderScheduler.Name(p);

    /// <summary>Imsak, Terbit and Dhuha are shown as secondary rows.</summary>
    public static bool IsSecondary(Prayer p) => p is Prayer.Imsak or Prayer.Terbit or Prayer.Dhuha;

    public static string SourceName(CalendarSource s) => s == CalendarSource.Google ? "Google" : "Outlook";

    /// <summary>Tiny Fluent glyph for the calendar source: mail for Outlook, globe for Google (iCal).</summary>
    public static string SourceGlyph(CalendarSource s) => s == CalendarSource.Google ? "\uE774" : "\uE715";

    public static string HabitName(Habit h) => HabitScheduler.Name(h);

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
