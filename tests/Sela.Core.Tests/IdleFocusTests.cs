namespace Sela.Core.Tests;

public class IdleFocusTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static DateTime At(int h, int m, int s = 0) => Day.ToDateTime(new TimeOnly(h, m, s));

    private static DaySchedule Schedule() => new(Day, DefaultCity.Jakarta, new[]
    {
        new PrayerTime(Prayer.Imsak, At(4, 1)), new PrayerTime(Prayer.Subuh, At(4, 11)),
        new PrayerTime(Prayer.Terbit, At(5, 30)), new PrayerTime(Prayer.Dhuha, At(6, 30)),
        new PrayerTime(Prayer.Dzuhur, At(11, 39)), new PrayerTime(Prayer.Ashar, At(14, 51)),
        new PrayerTime(Prayer.Maghrib, At(17, 48)), new PrayerTime(Prayer.Isya, At(18, 59)),
    });

    // Same rule as the app: next wajib prayer strictly after now (tomorrow = today + 1 day).
    private static IdleFocus? Focus(DateTime now)
    {
        var today = Schedule();
        var tomorrow = today with
        {
            Date = Day.AddDays(1),
            Times = today.Times.Select(t => t with { Time = t.Time.AddDays(1) }).ToList(),
        };
        return IdleFocus.For(now, today, ReminderScheduler.NextPrayer(now, today, tomorrow));
    }

    [Theory]
    [InlineData(4, 11, 0, Prayer.Subuh)]
    [InlineData(11, 39, 0, Prayer.Dzuhur)]   // exactly at the start
    [InlineData(12, 8, 59, Prayer.Dzuhur)]   // 29:59 after
    [InlineData(14, 51, 0, Prayer.Ashar)]
    [InlineData(17, 48, 0, Prayer.Maghrib)]
    [InlineData(18, 59, 0, Prayer.Isya)]
    [InlineData(19, 28, 59, Prayer.Isya)]
    public void Shows_a_wajib_prayer_that_just_began(int h, int m, int s, Prayer expected)
    {
        var f = Assert.IsType<IdleFocus.JustStarted>(Focus(At(h, m, s)));
        Assert.Equal(expected, f.Prayer);
        Assert.Equal(Schedule()[expected], f.Since);
    }

    [Theory]
    [InlineData(12, 9, 0, Prayer.Ashar)]     // 30:00 after Dzuhur: back to the next prayer
    [InlineData(11, 38, 0, Prayer.Dzuhur)]   // just before Dzuhur
    [InlineData(4, 41, 0, Prayer.Dzuhur)]    // 30:00 after Subuh
    [InlineData(19, 29, 0, Prayer.Subuh)]    // 30:00 after Isya → tomorrow's Subuh
    public void Otherwise_shows_the_next_prayer(int h, int m, int s, Prayer expected)
    {
        var f = Assert.IsType<IdleFocus.Next>(Focus(At(h, m, s)));
        Assert.Equal(expected, f.Prayer);
    }

    [Theory]
    [InlineData(5, 30)]   // Terbit begins the Dhuha period
    [InlineData(5, 45)]
    [InlineData(6, 30)]   // the Dhuha time itself
    [InlineData(6, 45)]
    public void Dhuha_is_never_just_started(int h, int m) =>
        Assert.Equal(new IdleFocus.Next(Prayer.Dzuhur, At(11, 39)), Focus(At(h, m)));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 10)]
    [InlineData(4, 10)]
    public void After_midnight_isya_is_never_just_started(int h, int m) =>
        Assert.Equal(new IdleFocus.Next(Prayer.Subuh, At(4, 11)), Focus(At(h, m)));

    [Fact]
    public void Next_prayer_inside_the_window_wins()
    {
        // Ashar only 20 minutes after Dzuhur: the window would overlap it, so show Ashar.
        var times = Schedule().Times.Select(t => t.Prayer == Prayer.Ashar ? t with { Time = At(11, 59) } : t).ToList();
        var today = Schedule() with { Times = times };
        Assert.Equal(new IdleFocus.Next(Prayer.Ashar, At(11, 59)),
            IdleFocus.For(At(11, 45), today, (Prayer.Ashar, At(11, 59))));
    }

    [Fact]
    public void No_schedule_shows_next_or_nothing()
    {
        Assert.Null(IdleFocus.For(At(12, 0), null, null));
        Assert.Equal(new IdleFocus.Next(Prayer.Ashar, At(14, 51)), IdleFocus.For(At(12, 0), null, (Prayer.Ashar, At(14, 51))));
    }

    [Fact]
    public void Window_is_thirty_minutes() => Assert.Equal(TimeSpan.FromMinutes(30), IdleFocus.JustStartedWindow);
}
