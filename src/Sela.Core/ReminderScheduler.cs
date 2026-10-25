using System.Globalization;

namespace Sela.Core;

/// <summary>
/// Pure reminder logic. Call <see cref="Due"/> periodically (e.g. every 15–30 s) with the current time;
/// each reminder is returned once. Windows are bounded so waking from sleep does not replay old reminders.
/// Not thread-safe: call from a single thread (e.g. the UI timer).
/// </summary>
public sealed class ReminderScheduler
{
    public static readonly TimeSpan PrayerNowWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MeetingLateWindow = TimeSpan.FromMinutes(1);

    /// <summary>Longest meeting reminder lead honoured (one week). Calendar data is untrusted.</summary>
    public const int MaxReminderMinutes = 7 * 24 * 60;

    /// <summary>Prayers that drive the countdown.</summary>
    public static readonly IReadOnlyList<Prayer> Wajib = new[] { Prayer.Subuh, Prayer.Dzuhur, Prayer.Ashar, Prayer.Maghrib, Prayer.Isya };

    private readonly HashSet<string> _fired = new();
    private DateOnly _firedDay;

    public IReadOnlyList<Reminder> Due(DateTime now, DaySchedule today, IReadOnlyList<CalendarEvent> events, SelaSettings s)
    {
        ArgumentNullException.ThrowIfNull(today);
        ArgumentNullException.ThrowIfNull(s);
        events ??= Array.Empty<CalendarEvent>();

        var day = DateOnly.FromDateTime(now);
        if (day != _firedDay)
        {
            _fired.Clear();
            _firedDay = day;
        }

        var due = new List<Reminder>();
        var lang = s.ResolveLanguage();
        AddPrayerReminders(due, now, today, s, lang);
        AddMeetingReminders(due, now, events, lang);

        due.RemoveAll(r => !_fired.Add(r.Key));
        due.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        return due;
    }

    private static void AddPrayerReminders(List<Reminder> due, DateTime now, DaySchedule today, SelaSettings s, AppLanguage lang)
    {
        var lead = TimeSpan.FromMinutes(Math.Max(0, s.PrayerSoonMinutes));
        foreach (var pt in today.Times)
        {
            if (s.PrayerEnabled is null || !s.PrayerEnabled.TryGetValue(pt.Prayer, out var on) || !on) continue;
            var t = pt.Time;
            var name = Names.Prayer(pt.Prayer, lang);
            var en = lang == AppLanguage.English;
            var place = today.City.DisplayName;
            var badge = Hm(t);
            var dateKey = today.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            if (lead > TimeSpan.Zero && now >= t - lead && now < t)
            {
                int n = (int)Math.Ceiling((t - now).TotalMinutes);
                var (title, body) = en ? pt.Prayer switch
                {
                    Prayer.Imsak => ($"Imsak in {n} min", "Imsak time"),
                    Prayer.Terbit => ($"Sunrise in {n} min", "Sunrise"),
                    _ => ($"{name} in {n} min", $"Get ready for {name}"),
                } : pt.Prayer switch
                {
                    Prayer.Imsak => ($"Imsak {n} menit lagi", "Waktu imsak"),
                    Prayer.Terbit => ($"{n} menit lagi {name}", "Matahari terbit"),
                    _ => ($"{n} menit lagi {name}", $"Bersiap untuk sholat {name}"),
                };
                var dhikr = s.PrayerDhikr && Wajib.Contains(pt.Prayer)
                    ? DhikrLibrary.For(DhikrLibrary.Pick(today.Date, pt.Prayer), lang)
                    : null;
                due.Add(new Reminder($"soon|{dateKey}|{pt.Prayer}", ReminderKind.PrayerSoon, title, body, badge, null, t - lead, pt.Prayer,
                    Dhikr: dhikr));
            }

            if (now >= t && now < t + PrayerNowWindow)
            {
                var (title, body) = en ? pt.Prayer switch
                {
                    Prayer.Imsak => ("Imsak", $"Imsak has begun in {place}"),
                    Prayer.Terbit => ("Sunrise", $"The sun has risen in {place}"),
                    _ => ($"Time for {name}", $"It's {name} time in {place}"),
                } : ($"Waktu {name}", pt.Prayer switch
                {
                    Prayer.Imsak => $"Sudah masuk waktu imsak untuk {today.City.DisplayName}",
                    Prayer.Terbit => $"Matahari terbit di {today.City.DisplayName}",
                    _ => $"Sudah masuk waktu {name} untuk {today.City.DisplayName}",
                });
                due.Add(new Reminder($"now|{dateKey}|{pt.Prayer}", ReminderKind.PrayerNow, title, body, badge, null, t, pt.Prayer));
            }
        }
    }

    private static void AddMeetingReminders(List<Reminder> due, DateTime now, IReadOnlyList<CalendarEvent> events, AppLanguage lang)
    {
        foreach (var e in events)
        {
            // Clamp so a hostile invite cannot push remindAt below DateTime.MinValue and abort the whole tick.
            var lead = TimeSpan.FromMinutes(Math.Clamp(e.ReminderMinutes, 0, MaxReminderMinutes));
            if (e.Start - DateTime.MinValue < lead) continue;
            var remindAt = e.Start - lead;
            if (now < remindAt || now >= e.Start + MeetingLateWindow) continue;

            int n = (int)Math.Ceiling((e.Start - now).TotalMinutes);
            var when = lang == AppLanguage.English
                ? (n > 0 ? $"starts in {n} min" : "starting now")
                : (n > 0 ? $"mulai {n} menit lagi" : "mulai sekarang");
            var body = $"{Hm(e.Start)}–{Hm(e.End)} · {when}";
            if (!string.IsNullOrWhiteSpace(e.Location)) body += $" · {e.Location}";
            due.Add(new Reminder($"meeting|{e.Key}", ReminderKind.Meeting, e.Subject, body, Hm(e.Start), e.JoinUrl, remindAt,
                Source: e.Source));
        }
    }

    /// <summary>
    /// Next wajib prayer strictly after <paramref name="now"/>, looking into tomorrow after Isya.
    /// Null when nothing is left today and tomorrow's schedule is not available yet.
    /// </summary>
    public static (Prayer Prayer, DateTime Time)? NextPrayer(DateTime now, DaySchedule? today, DaySchedule? tomorrow)
    {
        foreach (var schedule in new[] { today, tomorrow })
        {
            if (schedule is null) continue;
            foreach (var p in Wajib)
            {
                var t = schedule[p];
                if (t > now) return (p, t);
            }
        }
        return null;
    }

    public static string Name(Prayer p) => p.ToString();

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
