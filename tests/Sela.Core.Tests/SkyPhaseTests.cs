namespace Sela.Core.Tests;

public class SkyPhaseTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);

    private static DaySchedule Jakarta()
    {
        DateTime At(int h, int m) => Day.ToDateTime(new TimeOnly(h, m));
        return new DaySchedule(Day, DefaultCity.Jakarta, new[]
        {
            new PrayerTime(Prayer.Imsak, At(4, 2)), new PrayerTime(Prayer.Subuh, At(4, 12)),
            new PrayerTime(Prayer.Terbit, At(5, 25)), new PrayerTime(Prayer.Dhuha, At(5, 53)),
            new PrayerTime(Prayer.Dzuhur, At(11, 40)), new PrayerTime(Prayer.Ashar, At(14, 51)),
            new PrayerTime(Prayer.Maghrib, At(17, 49)), new PrayerTime(Prayer.Isya, At(18, 59)),
        });
    }

    [Theory]
    [InlineData(3, 0, SkyPhase.Night)]
    [InlineData(4, 12, SkyPhase.Dawn)]
    [InlineData(5, 54, SkyPhase.Dawn)]
    [InlineData(5, 55, SkyPhase.Day)]
    [InlineData(14, 51, SkyPhase.Dusk)]
    [InlineData(18, 8, SkyPhase.Dusk)]
    [InlineData(18, 9, SkyPhase.Night)]
    [InlineData(23, 30, SkyPhase.Night)]
    public void Follows_prayer_times(int h, int m, SkyPhase expected) =>
        Assert.Equal(expected, SkyPhases.For(Day.ToDateTime(new TimeOnly(h, m)), Jakarta()));

    [Theory]
    [InlineData(5, SkyPhase.Dawn)]
    [InlineData(12, SkyPhase.Day)]
    [InlineData(16, SkyPhase.Dusk)]
    [InlineData(21, SkyPhase.Night)]
    public void Falls_back_to_clock_without_schedule(int h, SkyPhase expected) =>
        Assert.Equal(expected, SkyPhases.For(Day.ToDateTime(new TimeOnly(h, 0)), null));

    [Fact]
    public void Stale_schedule_uses_clock() =>
        Assert.Equal(SkyPhase.Day, SkyPhases.For(Day.AddDays(1).ToDateTime(new TimeOnly(12, 0)), Jakarta()));
}
