namespace Sela.Core.Tests;

/// <summary>Forced refreshes (the panel's refresh button) skip retry backoffs but never run fetches in parallel.</summary>
public sealed class ForceRefreshTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-force-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Counts requests and the peak number running at once; each takes ~50 ms.</summary>
    private sealed class SlowHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _running;
        public int Calls, MaxConcurrent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var now = Interlocked.Increment(ref _running);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(50, ct);
                var r = respond(request);
                r.RequestMessage ??= request;
                return r;
            }
            finally { Interlocked.Decrement(ref _running); }
        }
    }

    private static StubHandler Offline() => new(_ => throw new HttpRequestException("offline"));

    [Fact]
    public async Task Prayer_force_bypasses_backoff()
    {
        var h = Offline();
        var svc = new PrayerScheduleService(new MyQuranClient(new HttpClient(h)), _dir, null, new ManualTime());
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default));
        Assert.Single(h.Requests); // backoff
        Assert.Null(await svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default, force: true));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Prayer_prefetch_force_bypasses_backoff()
    {
        var h = Offline();
        var svc = new PrayerScheduleService(new MyQuranClient(new HttpClient(h)), _dir, null, new ManualTime());
        await svc.PrefetchAsync(DefaultCity.Jakarta, default);
        Assert.Equal(2, h.Requests.Count); // this month + next
        await svc.PrefetchAsync(DefaultCity.Jakarta, default);
        Assert.Equal(2, h.Requests.Count);
        await svc.PrefetchAsync(DefaultCity.Jakarta, default, force: true);
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact]
    public async Task Prayer_forced_calls_run_one_at_a_time()
    {
        var h = new SlowHandler(_ => throw new HttpRequestException("offline"));
        var svc = new PrayerScheduleService(new MyQuranClient(new HttpClient(h)), _dir, null, new ManualTime());
        await Task.WhenAll(
            svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default, force: true),
            svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default, force: true));
        Assert.Equal(1, h.MaxConcurrent);
    }

    [Fact]
    public async Task Prayer_forced_call_after_success_serves_memory()
    {
        var h = new SlowHandler(MyQuranSamples.Route);
        var svc = new PrayerScheduleService(new MyQuranClient(new HttpClient(h)), _dir, null, new ManualTime());
        var results = await Task.WhenAll(
            svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default, force: true),
            svc.GetDayAsync(DefaultCity.Jakarta, Oct24, default, force: true));
        Assert.All(results, Assert.NotNull);
        Assert.Equal(1, h.Calls);
    }

    private HadithService Hadith(HttpMessageHandler h) => new(new HadithClient(new HttpClient(h)), _dir, null, new ManualTime());

    [Fact]
    public async Task Hadith_force_bypasses_backoff()
    {
        var h = Offline();
        var svc = Hadith(h);
        Assert.Null(await svc.GetTodayAsync(default));
        Assert.Null(await svc.GetTodayAsync(default));
        Assert.Single(h.Requests);
        Assert.Null(await svc.GetTodayAsync(default, force: true));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Hadith_forced_calls_run_one_at_a_time()
    {
        var h = new SlowHandler(_ => throw new HttpRequestException("offline"));
        var svc = Hadith(h);
        await Task.WhenAll(svc.GetTodayAsync(default, force: true), svc.GetTodayAsync(default, force: true));
        Assert.Equal(2, h.Calls);
        Assert.Equal(1, h.MaxConcurrent);
    }

    // ---- iCal ----

    private sealed class IcsTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:a@t\r\n" +
                               "DTSTART:20261026T020000Z\r\nDTEND:20261026T030000Z\r\nSUMMARY:Sync\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static readonly TimeZoneInfo Wib = TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
    private static readonly DateTime From = new(2026, 10, 26, 0, 0, 0);

    private static HttpResponseMessage IcsOk() => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(Ics, System.Text.Encoding.UTF8, "text/calendar"),
    };

    [Fact]
    public async Task Ics_fresh_fetch_downloads_inside_refresh_interval_and_reports_failure()
    {
        bool fail = false;
        var h = new StubHandler(_ => fail ? throw new HttpRequestException("down") : IcsOk());
        var p = new IcsCalendarProvider(new HttpClient(h), () => "https://example.com/a.ics", null, null, new IcsTime(), Wib);

        Assert.Single(await p.GetEventsAsync(From, From.AddDays(1), default));
        Assert.False(p.LastFetchFailed);
        await p.GetEventsAsync(From, From.AddDays(1), default);
        Assert.Single(h.Requests); // throttled

        fail = true;
        p.RequestFreshFetch();
        Assert.Single(await p.GetEventsAsync(From, From.AddDays(1), default)); // cached text still served
        Assert.Equal(2, h.Requests.Count);
        Assert.True(p.LastFetchFailed);

        await p.GetEventsAsync(From, From.AddDays(1), default);
        Assert.Equal(2, h.Requests.Count); // the request is one-shot

        fail = false;
        p.RequestFreshFetch();
        await p.GetEventsAsync(From, From.AddDays(1), default);
        Assert.Equal(3, h.Requests.Count);
        Assert.False(p.LastFetchFailed);
    }

    [Fact]
    public async Task Ics_forced_reads_run_one_at_a_time()
    {
        var h = new SlowHandler(_ => IcsOk());
        var p = new IcsCalendarProvider(new HttpClient(h), () => "https://example.com/a.ics", null, null, new IcsTime(), Wib);
        p.RequestFreshFetch();
        await Task.WhenAll(p.GetEventsAsync(From, From.AddDays(1), default), p.GetEventsAsync(From, From.AddDays(1), default));
        Assert.Equal(1, h.Calls); // the second read waits, then sees the fresh text
        Assert.Equal(1, h.MaxConcurrent);
    }
}
