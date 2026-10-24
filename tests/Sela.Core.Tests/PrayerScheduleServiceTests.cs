namespace Sela.Core.Tests;

public sealed class PrayerScheduleServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-cache-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Oct24 = new(2026, 10, 24);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private PrayerScheduleService Service(StubHandler h, TimeProvider? time = null) =>
        new(new MyQuranClient(new HttpClient(h)), _dir, null, time ?? new ManualTime());

    private static StubHandler Offline() => new(_ => throw new HttpRequestException("offline"));

    [Fact]
    public async Task Fetches_once_then_serves_from_memory()
    {
        var h = new StubHandler(MyQuranSamples.Route);
        var svc = Service(h);
        var d = await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default);
        Assert.Equal(new DateTime(2026, 10, 24, 4, 12, 0), d![Prayer.Subuh]);
        var d2 = await svc.GetDayAsync(DefaultCity.Jakarta, Oct24.AddDays(1), default);
        Assert.Equal(new DateTime(2026, 10, 25, 14, 52, 0), d2![Prayer.Ashar]);
        Assert.Single(h.Requests);
        Assert.True(File.Exists(Path.Combine(_dir, "jadwal-1301-2026-10.json")));
    }

    [Fact]
    public async Task Offline_uses_disk_cache_from_earlier_session()
    {
        await Service(new StubHandler(MyQuranSamples.Route)).GetDayAsync(DefaultCity.Jakarta, Oct24, default);

        var offline = Offline();
        var d = await Service(offline).GetDayAsync(DefaultCity.Jakarta, Oct24, default);
        Assert.Equal(new DateTime(2026, 10, 24, 11, 40, 0), d![Prayer.Dzuhur]);
        Assert.Equal("KOTA JAKARTA", d.City.Name);
        Assert.Equal("DKI JAKARTA", d.City.Province);
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task Api_failure_without_cache_returns_null()
    {
        Assert.Null(await Service(Offline()).GetDayAsync(DefaultCity.Jakarta, Oct24, default));
    }

    [Fact]
    public async Task Missing_day_in_month_returns_null()
    {
        Assert.Null(await Service(new StubHandler(MyQuranSamples.Route)).GetDayAsync(DefaultCity.Jakarta, new DateOnly(2026, 10, 1), default));
    }

    [Fact]
    public async Task Stale_disk_cache_is_refetched_but_used_if_api_down()
    {
        var clock = new ManualTime();
        await Service(new StubHandler(MyQuranSamples.Route), clock).GetDayAsync(DefaultCity.Jakarta, Oct24, default);
        clock.Now += TimeSpan.FromDays(31);

        var online = new StubHandler(MyQuranSamples.Route);
        Assert.NotNull(await Service(online, clock).GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Single(online.Requests);

        clock.Now += TimeSpan.FromDays(31);
        var offline = Offline();
        var svc = Service(offline, clock);
        Assert.NotNull(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.NotNull(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Single(offline.Requests); // stale result kept in memory, not retried every call
    }

    [Fact]
    public async Task Failure_is_not_retried_within_backoff()
    {
        var clock = new ManualTime();
        var offline = Offline();
        var svc = Service(offline, clock);
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Single(offline.Requests);
        clock.Now += PrayerScheduleService.RetryBackoff;
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Equal(2, offline.Requests.Count);
    }

    [Fact]
    public async Task Corrupt_cache_file_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "jadwal-1301-2026-10.json"), "{ broken");
        var h = new StubHandler(MyQuranSamples.Route);
        Assert.NotNull(await Service(h).GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Prefetch_caches_current_and_next_month_for_offline_rollover()
    {
        var clock = new ManualTime();
        await Service(new StubHandler(MyQuranSamples.Route), clock).PrefetchAsync(DefaultCity.Jakarta, default);
        Assert.True(File.Exists(Path.Combine(_dir, "jadwal-1301-2026-11.json")));

        var d = await Service(Offline(), clock).GetDayAsync(DefaultCity.Jakarta, new DateOnly(2026, 11, 1), default);
        Assert.Equal(new DateTime(2026, 11, 1, 4, 8, 0), d![Prayer.Subuh]);
    }

    [Fact]
    public async Task Concurrent_calls_share_one_request()
    {
        var h = new StubHandler(MyQuranSamples.Route);
        var svc = Service(h);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default)));
        Assert.All(results, Assert.NotNull);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Non_numeric_city_id_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service(Offline()).GetDayAsync(new City("..\\..\\x", "X"), Oct24, default));
    }
}
