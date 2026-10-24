using Sela.Core;

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
    /// Jakarta schedule shifted so Ashar is 25 minutes after <paramref name="start"/>.
    /// Times are clamped to stay on the start date (order stays non-decreasing).
    /// </summary>
    public static (DaySchedule Today, DaySchedule Tomorrow) Schedule(DateTime start)
    {
        var day = start.Date;
        var offset = start.AddMinutes(25) - day.Add(new TimeSpan(14, 51, 0));
        var lastMinute = day.AddDays(1).AddMinutes(-1);
        var today = BaseTimes
            .Select(t => new PrayerTime(t.Prayer, Clamp(day.Add(t.At) + offset, day, lastMinute)))
            .ToList();
        var tomorrow = today.Select(t => t with { Time = t.Time.AddDays(1) }).ToList();
        var date = DateOnly.FromDateTime(day);
        return (new DaySchedule(date, DefaultCity.Jakarta, today),
                new DaySchedule(date.AddDays(1), DefaultCity.Jakarta, tomorrow));
    }

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

    /// <summary>Arbain no. 1 as the fixed hadith of the day.</summary>
    public static Hadith Hadith(DateOnly date) => new(
        "Arbain Nawawi", 1, "Amalan Bergantung pada Niat",
        "إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ، وَإِنَّمَا لِكُلِّ امْرِئٍ مَا نَوَى",
        "Sesungguhnya setiap amalan tergantung pada niatnya, dan setiap orang akan mendapatkan sesuai dengan apa yang ia niatkan. (HR. Bukhari dan Muslim)",
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
