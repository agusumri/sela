using Sela.Core;
using Sela.Localization;

namespace Sela.Services;

/// <summary>
/// Fixed, privacy-safe data for <c>--mock</c> (README screenshots): no network, disk, registry or audio.
/// </summary>
public static class MockData
{
    private static readonly (Prayer Prayer, TimeSpan At)[] BaseTimes =
    {
        (Prayer.Imsak, new(4, 2, 0)), (Prayer.Subuh, new(4, 12, 0)), (Prayer.Terbit, new(5, 25, 0)),
        (Prayer.Dhuha, new(5, 53, 0)), (Prayer.Dzuhur, new(11, 40, 0)), (Prayer.Ashar, new(14, 51, 0)),
        (Prayer.Maghrib, new(17, 49, 0)), (Prayer.Isya, new(18, 59, 0)),
    };

    /// <summary>
    /// Jakarta schedule shifted so Ashar is 25 minutes after <paramref name="start"/>, or, with
    /// <paramref name="justStarted"/> (--just-started), began 12 minutes before it.
    /// Times are clamped to stay on the start date (order stays non-decreasing).
    /// </summary>
    public static (DaySchedule Today, DaySchedule Tomorrow) Schedule(DateTime start, City? city = null, bool justStarted = false)
    {
        city ??= DefaultCity.Jakarta;
        var day = start.Date;
        var offset = start.AddMinutes(justStarted ? -12 : 25) - day.Add(new TimeSpan(14, 51, 0));
        var lastMinute = day.AddDays(1).AddMinutes(-1);
        var today = BaseTimes
            .Select(t => new PrayerTime(t.Prayer, Clamp(day.Add(t.At) + offset, day, lastMinute)))
            .ToList();
        var tomorrow = today.Select(t => t with { Time = t.Time.AddDays(1) }).ToList();
        var date = DateOnly.FromDateTime(day);
        return (new DaySchedule(date, city, today),
                new DaySchedule(date.AddDays(1), city, tomorrow));
    }

    /// <summary>The unshifted Jakarta times on <paramref name="date"/> (and the next day), for <c>--prayer-tour</c>.</summary>
    public static (DaySchedule Today, DaySchedule Tomorrow) FixedSchedule(DateOnly date, City? city = null)
    {
        city ??= DefaultCity.Jakarta;
        var day = date.ToDateTime(TimeOnly.MinValue);
        var today = BaseTimes.Select(t => new PrayerTime(t.Prayer, day.Add(t.At))).ToList();
        var tomorrow = today.Select(t => t with { Time = t.Time.AddDays(1) }).ToList();
        return (new DaySchedule(date, city, today), new DaySchedule(date.AddDays(1), city, tomorrow));
    }

    /// <summary>
    /// <c>--prayer-tour</c> stops on the <see cref="FixedSchedule"/> day: for each period, 12 min after it starts,
    /// mid-period and 5 min before the next prayer, plus after midnight, Imsak, Terbit and the Dhuha time.
    /// </summary>
    public static IReadOnlyList<(TimeSpan At, string Label)> PrayerTourStops { get; } = new (TimeSpan, string)[]
    {
        (new(1, 30, 0), "after midnight"),
        (new(4, 2, 0), "Imsak time"),
        (new(4, 7, 0), "5 min before Subuh"),
        (new(4, 24, 0), "Subuh +12 min"),
        (new(4, 48, 0), "Subuh mid"),
        (new(5, 20, 0), "5 min before Terbit"),
        (new(5, 25, 0), "Terbit"),
        (new(5, 53, 0), "Dhuha time"),
        (new(8, 40, 0), "Dhuha mid"),
        (new(11, 35, 0), "5 min before Dzuhur"),
        (new(11, 52, 0), "Dzuhur +12 min"),
        (new(13, 15, 0), "Dzuhur mid"),
        (new(14, 46, 0), "5 min before Ashar"),
        (new(15, 3, 0), "Ashar +12 min"),
        (new(16, 20, 0), "Ashar mid"),
        (new(17, 44, 0), "5 min before Maghrib"),
        (new(18, 1, 0), "Maghrib +12 min"),
        (new(18, 24, 0), "Maghrib mid"),
        (new(18, 54, 0), "5 min before Isya"),
        (new(19, 11, 0), "Isya +12 min"),
        (new(21, 30, 0), "Isya mid"),
        (new(23, 55, 0), "before midnight"),
    };

    /// <summary>Fixed world city for <c>--mock-world</c> screenshots.</summary>
    public static City London { get; } = new("geo:2643743", "London", "England", 51.50853, -0.12574,
        "Europe/London", "United Kingdom");

    /// <summary>Three fake events today, relative to <paramref name="start"/>.</summary>
    public static IReadOnlyList<CalendarEvent> Agenda(DateTime start)
    {
        var standup = start.AddMinutes(40);
        var review = start.AddMinutes(100);
        var focus = start.AddHours(3);
        return new[]
        {
            new CalendarEvent($"mock-standup|{standup:O}", "Daily standup", standup, standup.AddMinutes(15),
                null, "https://meet.google.com/abc-defg-hij", 10, CalendarSource.Google),
            new CalendarEvent($"mock-review|{review:O}", "Review desain", review, review.AddMinutes(45),
                "Ruang Rapat 2", "https://teams.microsoft.com/l/meetup-join/demo", 10, CalendarSource.Outlook),
            new CalendarEvent($"mock-focus|{focus:O}", "Sesi fokus", focus, focus.AddMinutes(90),
                null, null, 10, CalendarSource.Google),
        };
    }

    /// <summary>Arbain no. 1 as the fixed hadith of the day, in the current UI language.</summary>
    public static Hadith Hadith(DateOnly date) => Loc.Instance.Language == AppLanguage.English
        ? new(
            "Forty Hadith of an-Nawawi", 1, "Hadith 1",
            "إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ، وَإِنَّمَا لِكُلِّ امْرِئٍ مَا نَوَى",
            "Actions are judged by intentions, and each person will have what they intended. [Bukhari & Muslim]",
            AppLanguage.English,
            date)
        : new(
        "Arbain Nawawi", 1, "Amalan Bergantung pada Niat",
        "إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ، وَإِنَّمَا لِكُلِّ امْرِئٍ مَا نَوَى",
        "Sesungguhnya setiap amalan tergantung pada niatnya, dan setiap orang akan mendapatkan sesuai dengan apa yang ia niatkan. (HR. Bukhari dan Muslim)",
        AppLanguage.Indonesian,
        date);

    /// <summary>Morning dzikir done, the rest still open.</summary>
    public static IReadOnlyDictionary<Habit, bool> HabitsDone() => new Dictionary<Habit, bool>
    {
        [Habit.DzikirPagi] = true, [Habit.Sholawat] = false, [Habit.DzikirPetang] = false,
    };

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) =>
        value < min ? min : value > max ? max : value;
}

/// <summary>Adzan player that never makes a sound but reports a "playing" state so the stop control shows.</summary>
public sealed class SilentAdhanPlayer : IAdhanPlayer
{
    private bool _isPlaying;
    public bool IsPlaying => _isPlaying;
    public event Action? PlayingChanged;

    public void PlayFor(Prayer prayer, SelaSettings settings)
    {
        if (settings.ShouldPlayAdhan(prayer)) SetPlaying(true);
    }

    public void Preview(string? file, bool subuh, int volume) => SetPlaying(true);
    public void Stop() => SetPlaying(false);

    private void SetPlaying(bool value)
    {
        if (_isPlaying == value) return;
        _isPlaying = value;
        PlayingChanged?.Invoke();
    }
}
