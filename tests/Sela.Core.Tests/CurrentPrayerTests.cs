namespace Sela.Core.Tests;

public class CurrentPrayerTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static DateTime At(int h, int m) => Day.ToDateTime(new TimeOnly(h, m));

    private static DaySchedule Schedule() => new(Day, DefaultCity.Jakarta, new[]
    {
        new PrayerTime(Prayer.Imsak, At(4, 1)), new PrayerTime(Prayer.Subuh, At(4, 11)),
        new PrayerTime(Prayer.Terbit, At(5, 30)), new PrayerTime(Prayer.Dhuha, At(6, 30)),
        new PrayerTime(Prayer.Dzuhur, At(11, 39)), new PrayerTime(Prayer.Ashar, At(14, 51)),
        new PrayerTime(Prayer.Maghrib, At(17, 48)), new PrayerTime(Prayer.Isya, At(18, 59)),
    });

    [Theory]
    // midnight to Subuh: last night's Isya, start unknown
    [InlineData(0, 0, Prayer.Isya)]
    [InlineData(4, 1, Prayer.Isya)]   // Imsak is not a period
    [InlineData(4, 10, Prayer.Isya)]
    [InlineData(4, 11, Prayer.Subuh)]
    [InlineData(4, 12, Prayer.Subuh)]
    [InlineData(5, 29, Prayer.Subuh)]
    [InlineData(5, 30, Prayer.Dhuha)]
    [InlineData(5, 31, Prayer.Dhuha)]
    [InlineData(6, 30, Prayer.Dhuha)]  // the Dhuha time itself changes nothing
    [InlineData(11, 38, Prayer.Dhuha)]
    [InlineData(11, 39, Prayer.Dzuhur)]
    [InlineData(11, 40, Prayer.Dzuhur)]
    [InlineData(14, 50, Prayer.Dzuhur)]
    [InlineData(14, 51, Prayer.Ashar)]
    [InlineData(14, 52, Prayer.Ashar)]
    [InlineData(17, 47, Prayer.Ashar)]
    [InlineData(17, 48, Prayer.Maghrib)]
    [InlineData(17, 49, Prayer.Maghrib)]
    [InlineData(18, 58, Prayer.Maghrib)]
    [InlineData(18, 59, Prayer.Isya)]
    [InlineData(19, 0, Prayer.Isya)]
    [InlineData(23, 59, Prayer.Isya)]
    public void Follows_the_schedule(int h, int m, Prayer expected) =>
        Assert.Equal(expected, CurrentPrayer.For(At(h, m), Schedule())?.Prayer);

    [Theory]
    [InlineData(4, 30, 4, 11)]   // Subuh since Subuh
    [InlineData(9, 0, 5, 30)]    // Dhuha since Terbit
    [InlineData(12, 0, 11, 39)]
    [InlineData(15, 0, 14, 51)]
    [InlineData(18, 0, 17, 48)]
    [InlineData(22, 0, 18, 59)]
    public void Since_is_the_period_start(int h, int m, int sh, int sm) =>
        Assert.Equal(At(sh, sm), CurrentPrayer.For(At(h, m), Schedule())?.Since);

    [Fact]
    public void Before_subuh_has_no_known_start()
    {
        var c = CurrentPrayer.For(At(2, 0), Schedule());
        Assert.Equal(Prayer.Isya, c?.Prayer);
        Assert.Null(c?.Since);
    }

    [Theory]
    [InlineData(11, 39, 0.0)]   // at the start
    [InlineData(13, 15, 0.5)]   // halfway Dzuhur 11:39 → Ashar 14:51
    [InlineData(14, 51, 1.0)]   // at the end
    [InlineData(10, 0, 0.0)]    // before: clamped
    [InlineData(16, 0, 1.0)]    // after: clamped
    public void Progress_runs_from_start_to_end(int h, int m, double expected) =>
        Assert.Equal(expected, CurrentPrayer.Progress(At(h, m), At(11, 39), At(14, 51)), 6);

    [Fact]
    public void Progress_of_an_empty_span_is_zero()
    {
        Assert.Equal(0, CurrentPrayer.Progress(At(12, 0), At(12, 0), At(12, 0)));
        Assert.Equal(0, CurrentPrayer.Progress(At(12, 0), At(13, 0), At(12, 0)));
    }

    [Fact]
    public void No_schedule_is_null() => Assert.Null(CurrentPrayer.For(At(12, 0), null));

    [Fact]
    public void Stale_schedule_is_null() =>
        Assert.Null(CurrentPrayer.For(Day.AddDays(1).ToDateTime(new TimeOnly(12, 0)), Schedule()));
}
