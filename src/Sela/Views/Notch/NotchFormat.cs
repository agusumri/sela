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

    /// <summary>Short countdown: "1j 12m", "2j", "12m", "&lt;1m".</summary>
    public static string Countdown(TimeSpan left)
    {
        var total = (int)Math.Floor(left.TotalMinutes);
        if (total < 1) return "<1m";
        int h = total / 60, m = total % 60;
        if (h == 0) return $"{m}m";
        return m == 0 ? $"{h}j" : $"{h}j {m}m";
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

    public static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}
