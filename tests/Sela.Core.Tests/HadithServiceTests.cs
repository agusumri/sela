using System.Net;

namespace Sela.Core.Tests;

public sealed class HadithServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-hadith-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Oct24 = new(2026, 10, 24);

    private const string Sample = """
        {"status":true,"request":{"path":"/hadits/arbain/1"},"info":{"min":1,"max":42},"data":{"arab":"  إِنَّمَا الْأَعْمَالُ بِالنِّيَّاتِ  ","indo":"Sesungguhnya   setiap amalan\n\ttergantung pada niatnya. ","judul":"Amalan Bergantung pada Niat","no":"1"}}
        """;

    private static string SampleFor(int n) => Sample.Replace("\"no\":\"1\"", $"\"no\":\"{n}\"");

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

    /// <summary>Answers /hadits/arbain/{n} with the sample, renumbered to n.</summary>
    private static StubHandler Online() => new(r =>
        StubHandler.Json(SampleFor(int.Parse(r.RequestUri!.Segments[^1]))));

    private static StubHandler Offline() => new(_ => throw new HttpRequestException("offline"));

    private HadithService Service(StubHandler h, TimeProvider? time = null) =>
        new(new HadithClient(new HttpClient(h)), _dir, null, time ?? new ManualTime());

    [Fact]
    public async Task Client_parses_fields()
    {
        var h = new StubHandler(_ => StubHandler.Json(Sample));
        var hadith = await new HadithClient(new HttpClient(h)).GetArbainAsync(1, default);
        Assert.Equal("Arbain Nawawi", hadith.Collection);
        Assert.Equal(1, hadith.Number);
        Assert.Equal("Amalan Bergantung pada Niat", hadith.Title);
        Assert.Equal("إِنَّمَا الْأَعْمَالُ بِالنِّيَّاتِ", hadith.Arabic);
        Assert.Equal("Sesungguhnya setiap amalan tergantung pada niatnya.", hadith.Translation);
        Assert.Equal(default, hadith.Date);
        Assert.Equal("https://api.myquran.com/v2/hadits/arbain/1", Assert.Single(h.Requests).ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(43)]
    [InlineData(-1)]
    public async Task Client_rejects_out_of_range_number(int n)
    {
        var h = Online();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new HadithClient(new HttpClient(h)).GetArbainAsync(n, default));
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData("""{"status":false,"message":"not found"}""")]
    [InlineData("""{"status":true}""")]
    [InlineData("""{"status":true,"data":{"arab":"","indo":"x","judul":"t","no":"1"}}""")]
    [InlineData("not json")]
    public async Task Client_throws_on_bad_response(string body)
    {
        var h = new StubHandler(_ => StubHandler.Json(body));
        await Assert.ThrowsAsync<PrayerApiException>(() => new HadithClient(new HttpClient(h)).GetArbainAsync(1, default));
    }

    [Fact]
    public async Task Client_throws_on_http_error()
    {
        var h = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<PrayerApiException>(() => new HadithClient(new HttpClient(h)).GetArbainAsync(1, default));
    }

    [Fact]
    public async Task Same_day_second_call_makes_no_request()
    {
        var h = Online();
        var svc = Service(h);
        var a = await svc.GetTodayAsync(default);
        var b = await svc.GetTodayAsync(default);
        Assert.NotNull(a);
        Assert.Equal(Oct24, a!.Date);
        Assert.Equal(HadithService.NumberFor(Oct24), a.Number);
        Assert.Same(a, b);
        Assert.Single(h.Requests);
        Assert.True(File.Exists(Path.Combine(_dir, "hadith-2026-10-24.json")));
    }

    [Fact]
    public async Task New_session_same_day_reads_disk_cache()
    {
        await Service(Online()).GetTodayAsync(default);
        var h = Offline();
        var hadith = await Service(h).GetTodayAsync(default);
        Assert.Equal(Oct24, hadith!.Date);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Offline_uses_yesterdays_cached_hadith()
    {
        var time = new ManualTime();
        var yesterday = await Service(Online(), time).GetTodayAsync(default);
        time.Now = time.Now.AddDays(1);

        var h = Offline();
        var svc = Service(h, time);
        var hadith = await svc.GetTodayAsync(default);
        Assert.NotNull(hadith);
        Assert.Equal(Oct24, hadith!.Date);
        Assert.Equal(yesterday!.Number, hadith.Number);
        Assert.Single(h.Requests);

        // Within the backoff window the API is not retried.
        await svc.GetTodayAsync(default);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Nothing_cached_and_offline_returns_null()
    {
        Assert.Null(await Service(Offline()).GetTodayAsync(default));
    }

    [Fact]
    public async Task Keeps_at_most_seven_cache_files()
    {
        var time = new ManualTime();
        for (var i = 0; i < 10; i++)
        {
            await Service(Online(), time).GetTodayAsync(default);
            time.Now = time.Now.AddDays(1);
        }
        var files = Directory.GetFiles(_dir, "hadith-*.json");
        Assert.Equal(HadithService.MaxCachedFiles, files.Length);
        Assert.DoesNotContain(files, f => f.EndsWith("hadith-2026-10-24.json"));
        Assert.Contains(files, f => f.EndsWith("hadith-2026-11-02.json"));
    }

    [Fact]
    public async Task Corrupt_cache_file_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "hadith-2026-10-23.json"), "{broken");
        Assert.Null(await Service(Offline()).GetTodayAsync(default));
    }

    [Fact]
    public void NumberFor_is_in_range_and_changes_daily()
    {
        var d = new DateOnly(2026, 1, 1);
        for (var i = 0; i < 400; i++, d = d.AddDays(1))
        {
            var n = HadithService.NumberFor(d);
            Assert.InRange(n, 1, HadithClient.ArbainCount);
            Assert.NotEqual(n, HadithService.NumberFor(d.AddDays(1)));
        }
    }

    /// <summary>Hits the real API. Runs only when SELA_LIVE_TESTS=1 (otherwise passes without network).</summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_smoke()
    {
        if (Environment.GetEnvironmentVariable("SELA_LIVE_TESTS") != "1") return;
        var hadith = await new HadithClient(new HttpClient()).GetArbainAsync(1, default);
        Assert.Equal(1, hadith.Number);
        Assert.False(string.IsNullOrWhiteSpace(hadith.Title));
        Assert.False(string.IsNullOrWhiteSpace(hadith.Arabic));
        Assert.False(string.IsNullOrWhiteSpace(hadith.Translation));
    }
}
