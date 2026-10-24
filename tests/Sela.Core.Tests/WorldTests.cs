using System.Net;

namespace Sela.Core.Tests;

internal static class WorldSamples
{
    public const string LondonOct2026 = """
        {"code":200,"status":"OK","data":[
        {"timings":{"Fajr":"05:09 (BST)","Sunrise":"07:01 (BST)","Dhuhr":"12:50 (BST)","Asr":"15:58 (BST)","Sunset":"18:38 (BST)","Maghrib":"18:38 (BST)","Isha":"20:23 (BST)","Imsak":"04:59 (BST)","Midnight":"00:50 (BST)"},
         "date":{"readable":"01 Oct 2026","gregorian":{"date":"01-10-2026"}},"meta":{"latitude":51.5074,"longitude":-0.1278,"timezone":"Europe/London","method":{"id":3},"school":"STANDARD"}},
        {"timings":{"Fajr":"05:11 (BST)","Sunrise":"07:03 (BST)","Dhuhr":"12:50 (BST)","Asr":"15:56 (BST)","Sunset":"18:35 (BST)","Maghrib":"18:35 (BST)","Isha":"20:21 (BST)","Imsak":"05:01 (BST)"},
         "date":{"gregorian":{"date":"02-10-2026"}},"meta":{"timezone":"Europe/London"}}]}
        """;

    public static readonly City London = new("geo:2643743", "London", "England", 51.50853, -0.12574, "Europe/London", "United Kingdom");

    public const string GeoLondon = """
        {"results":[
        {"id":2643743,"name":"London","latitude":51.50853,"longitude":-0.12574,"country_code":"GB","timezone":"Europe/London","country":"United Kingdom","admin1":"England"},
        {"id":6058560,"name":"London","latitude":42.98339,"longitude":-81.23304,"country_code":"CA","timezone":"America/Toronto","country":"Canada","admin1":"Ontario"},
        {"id":1642911,"name":"Jakarta","latitude":-6.21462,"longitude":106.84513,"country_code":"ID","timezone":"Asia/Jakarta","country":"Indonesia","admin1":"Jakarta"}],"generationtime_ms":0.5}
        """;

    public static TimeZoneInfo LondonZone => TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
}

public sealed class AladhanClientTests
{
    private static AladhanClient Client(StubHandler h, TimeZoneInfo? local = null) =>
        new(new HttpClient(h), null, local ?? WorldSamples.LondonZone);

    [Fact]
    public async Task Parses_strips_suffix_and_maps_prayers()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.LondonOct2026));
        var days = await Client(h).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(3), default);

        Assert.Equal(2, days.Count);
        var d = days[0];
        Assert.Equal(new DateOnly(2026, 10, 1), d.Date);
        Assert.Same(WorldSamples.London, d.City);
        DateTime T(int hh, int mm) => new(2026, 10, 1, hh, mm, 0);
        Assert.Equal(T(4, 59), d[Prayer.Imsak]);
        Assert.Equal(T(5, 9), d[Prayer.Subuh]);
        Assert.Equal(T(7, 1), d[Prayer.Terbit]);
        Assert.Equal(T(7, 21), d[Prayer.Dhuha]);
        Assert.Equal(T(12, 50), d[Prayer.Dzuhur]);
        Assert.Equal(T(15, 58), d[Prayer.Ashar]);
        Assert.Equal(T(18, 38), d[Prayer.Maghrib]);
        Assert.Equal(T(20, 23), d[Prayer.Isya]);
        Assert.Equal(new DateOnly(2026, 10, 2), days[1].Date);
    }

    [Fact]
    public async Task Query_uses_invariant_rounded_coords_method_and_school()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.LondonOct2026));
        await Client(h).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(3, AsrHanafi: true), default);
        Assert.Equal("https://api.aladhan.com/v1/calendar/2026/10?latitude=51.5085&longitude=-0.1257&method=3&school=1",
            Assert.Single(h.Requests).ToString());
    }

    [Fact]
    public async Task Method_is_omitted_when_null()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.LondonOct2026));
        await Client(h).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default);
        var q = Assert.Single(h.Requests).Query;
        Assert.DoesNotContain("method", q);
        Assert.EndsWith("&school=0", q);
    }

    [Fact]
    public async Task Converts_location_time_to_pc_zone()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.LondonOct2026));
        var days = await Client(h, TimeZoneInfo.Utc).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default);
        Assert.Equal(new DateTime(2026, 10, 1, 4, 9, 0), days[0][Prayer.Subuh]); // 05:09 BST = 04:09 UTC
        Assert.Equal(DateTimeKind.Unspecified, days[0][Prayer.Subuh].Kind);

        var jakarta = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");
        days = await Client(h, jakarta).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 9, 0), days[0][Prayer.Subuh]); // UTC+7
        Assert.Equal(new DateTime(2026, 10, 2, 2, 23, 0), days[0][Prayer.Isya]);  // crosses midnight
    }

    [Fact]
    public async Task Unknown_zone_keeps_wall_clock_and_logs_once()
    {
        var logs = new List<string>();
        var body = WorldSamples.LondonOct2026.Replace("Europe/London", "Mars/Olympus");
        var client = new AladhanClient(new HttpClient(new StubHandler(_ => StubHandler.Json(body))), logs.Add, TimeZoneInfo.Utc);
        var days = await client.GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default);
        await client.GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default);
        Assert.Equal(new DateTime(2026, 10, 1, 5, 9, 0), days[0][Prayer.Subuh]);
        Assert.Single(logs, l => l.Contains("Mars/Olympus"));
    }

    [Theory]
    [InlineData("""{"code":400,"status":"BAD_REQUEST","data":"Invalid"}""")]
    [InlineData("""{"code":200,"status":"OK","data":[]}""")]
    [InlineData("""{"code":200,"status":"OK","data":[{"timings":{"Fajr":"xx"},"date":{"gregorian":{"date":"01-10-2026"}}}]}""")]
    [InlineData("""{"code":200,"status":"OK","data":[{"timings":{},"date":{"gregorian":{"date":"bad"}}}]}""")]
    [InlineData("not json")]
    public async Task Bad_responses_throw_PrayerApiException(string body)
    {
        var h = new StubHandler(_ => StubHandler.Json(body));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(h).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default));
    }

    [Fact]
    public async Task Http_error_throws_PrayerApiException()
    {
        var h = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(h).GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(), default));
    }

    [Fact]
    public async Task Rejects_kemenag_or_out_of_range_cities()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.LondonOct2026));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(h).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, new PrayerCalc(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(h).GetMonthAsync(WorldSamples.London with { Latitude = 91 }, 2026, 10, new PrayerCalc(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(h).GetMonthAsync(WorldSamples.London with { Longitude = double.NaN }, 2026, 10, new PrayerCalc(), default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_london_october_2026()
    {
        if (Environment.GetEnvironmentVariable("SELA_LIVE_TESTS") != "1") return;
        var days = await new AladhanClient(new HttpClient(), null, WorldSamples.LondonZone)
            .GetMonthAsync(WorldSamples.London, 2026, 10, new PrayerCalc(3), default);
        Assert.Equal(31, days.Count);
        var d = days[0];
        Assert.True(d[Prayer.Imsak] < d[Prayer.Subuh] && d[Prayer.Subuh] < d[Prayer.Terbit] && d[Prayer.Terbit] < d[Prayer.Dzuhur]
            && d[Prayer.Dzuhur] < d[Prayer.Ashar] && d[Prayer.Ashar] < d[Prayer.Maghrib] && d[Prayer.Maghrib] < d[Prayer.Isya]);
        Assert.InRange(d[Prayer.Dzuhur].Hour, 12, 13);
    }
}

public sealed class GeocodingAndLocationSearchTests
{
    [Fact]
    public async Task Geocoding_parses_results()
    {
        var h = new StubHandler(_ => StubHandler.Json(WorldSamples.GeoLondon));
        var cities = await new GeocodingClient(new HttpClient(h)).SearchAsync("  London ", AppLanguage.English, default);
        Assert.Equal(3, cities.Count);
        Assert.Equal(WorldSamples.London, cities[0]);
        Assert.True(cities[0].IsWorld);
        Assert.Equal("London, United Kingdom", cities[0].DisplayName);
        Assert.Equal("https://geocoding-api.open-meteo.com/v1/search?name=London&count=10&language=en&format=json",
            Assert.Single(h.Requests).ToString());
    }

    [Fact]
    public async Task Geocoding_escapes_query_and_uses_indonesian()
    {
        var h = new StubHandler(_ => StubHandler.Json("{}"));
        await new GeocodingClient(new HttpClient(h)).SearchAsync("São Paulo&x=1", AppLanguage.Indonesian, default);
        Assert.Equal("?name=S%C3%A3o%20Paulo%26x%3D1&count=10&language=id&format=json", Assert.Single(h.Requests).Query);
    }

    [Fact]
    public async Task Geocoding_no_results_and_short_query_are_empty()
    {
        var h = new StubHandler(_ => StubHandler.Json("""{"generationtime_ms":0.3}"""));
        var client = new GeocodingClient(new HttpClient(h));
        Assert.Empty(await client.SearchAsync("xyzqq", AppLanguage.English, default));
        Assert.Empty(await client.SearchAsync(" a ", AppLanguage.English, default));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Geocoding_skips_invalid_entries()
    {
        var body = """{"results":[{"id":1,"name":"NoCoords"},{"id":2,"name":"Bad","latitude":95,"longitude":0},{"id":3,"name":"Ok","latitude":1,"longitude":2}]}""";
        var cities = await new GeocodingClient(new HttpClient(new StubHandler(_ => StubHandler.Json(body)))).SearchAsync("ok", AppLanguage.English, default);
        Assert.Equal("geo:3", Assert.Single(cities).Id);
    }

    private static LocationSearch Search(Func<HttpRequestMessage, HttpResponseMessage> kemenag, Func<HttpRequestMessage, HttpResponseMessage> world) =>
        new(new MyQuranClient(new HttpClient(new StubHandler(kemenag))), new GeocodingClient(new HttpClient(new StubHandler(world))));

    private static HttpResponseMessage Fail(HttpRequestMessage _) => throw new HttpRequestException("offline");

    [Fact]
    public async Task Kemenag_first_then_world_without_indonesia()
    {
        var results = await Search(_ => StubHandler.Json(MyQuranSamples.Search), _ => StubHandler.Json(WorldSamples.GeoLondon))
            .SearchAsync("jakarta", AppLanguage.English, default);
        Assert.Equal(new[] { "1301", "1302", "geo:2643743", "geo:6058560" }, results.Select(c => c.Id));
    }

    [Fact]
    public async Task One_source_failing_still_returns_the_other()
    {
        var worldOnly = await Search(Fail, _ => StubHandler.Json(WorldSamples.GeoLondon)).SearchAsync("london", AppLanguage.English, default);
        Assert.Equal(2, worldOnly.Count);
        var kemenagOnly = await Search(_ => StubHandler.Json(MyQuranSamples.Search), Fail).SearchAsync("jakarta", AppLanguage.English, default);
        Assert.Equal(2, kemenagOnly.Count);
    }

    [Fact]
    public async Task Both_failing_throws()
    {
        await Assert.ThrowsAsync<PrayerApiException>(() => Search(Fail, Fail).SearchAsync("london", AppLanguage.English, default));
    }

    [Fact]
    public async Task Caps_results()
    {
        var many = "{\"status\":true,\"data\":[" + string.Join(",", Enumerable.Range(1, 14).Select(i => $"{{\"id\":\"{i}\",\"lokasi\":\"KOTA {i}\"}}")) + "]}";
        var results = await Search(_ => StubHandler.Json(many), _ => StubHandler.Json(WorldSamples.GeoLondon)).SearchAsync("kota", AppLanguage.English, default);
        Assert.Equal(LocationSearch.MaxResults, results.Count);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_open_meteo_london()
    {
        if (Environment.GetEnvironmentVariable("SELA_LIVE_TESTS") != "1") return;
        var cities = await new GeocodingClient(new HttpClient()).SearchAsync("London", AppLanguage.English, default);
        var london = Assert.Single(cities, c => c.Id == "geo:2643743");
        Assert.Equal("Europe/London", london.TimeZoneId);
        Assert.Equal("United Kingdom", london.Country);
    }
}

public sealed class WorldPrayerScheduleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-world-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class ManualTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private PrayerScheduleService Service(StubHandler world, StubHandler? kemenag = null, bool withWorld = true) =>
        new(new MyQuranClient(new HttpClient(kemenag ?? new StubHandler(MyQuranSamples.Route))), _dir, null, new ManualTime(),
            withWorld ? new AladhanClient(new HttpClient(world), null, TimeZoneInfo.Utc) : null);

    private static StubHandler Online() => new(_ => StubHandler.Json(WorldSamples.LondonOct2026));
    private static StubHandler Offline() => new(_ => throw new HttpRequestException("offline"));

    [Fact]
    public async Task World_city_routes_to_aladhan_and_caches_per_calc()
    {
        var world = Online();
        var kemenag = new StubHandler(MyQuranSamples.Route);
        var svc = Service(world, kemenag);
        var d = await svc.GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 1), default, new PrayerCalc(3, true));
        Assert.Equal(new DateTime(2026, 10, 1, 4, 9, 0), d![Prayer.Subuh]);
        Assert.Empty(kemenag.Requests);
        Assert.True(File.Exists(Path.Combine(_dir, "jadwal-geo-2643743-m3-s1-2026-10.json")));

        await svc.GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 2), default, new PrayerCalc(3, true));
        Assert.Single(world.Requests);

        await svc.GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 1), default);
        Assert.Equal(2, world.Requests.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "jadwal-geo-2643743-mauto-s0-2026-10.json")));
    }

    [Fact]
    public async Task World_disk_cache_works_offline()
    {
        await Service(Online()).GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 1), default);
        var offline = Offline();
        var d = await Service(offline).GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 2), default);
        Assert.Equal(new DateTime(2026, 10, 2, 4, 11, 0), d![Prayer.Subuh]);
        Assert.Equal(WorldSamples.London, d.City);
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task Kemenag_city_ignores_calc()
    {
        var world = Online();
        var d = await Service(world).GetDayAsync(DefaultCity.Jakarta, new DateOnly(2026, 10, 24), default, new PrayerCalc(3, true));
        Assert.NotNull(d);
        Assert.Empty(world.Requests);
        Assert.True(File.Exists(Path.Combine(_dir, "jadwal-1301-2026-10.json")));
    }

    [Theory]
    [InlineData("geo:../x")]
    [InlineData("geo:")]
    [InlineData("geo:12a")]
    [InlineData("2643743")]
    [InlineData("..\\geo:1")]
    public async Task Unsafe_world_ids_are_rejected(string id)
    {
        var world = Online();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(world).GetDayAsync(WorldSamples.London with { Id = id }, new DateOnly(2026, 10, 1), default));
        Assert.Empty(world.Requests);
    }

    [Fact]
    public async Task World_city_without_client_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(Online(), withWorld: false).GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 1), default));
    }

    [Fact]
    public async Task World_failure_without_cache_returns_null()
    {
        Assert.Null(await Service(Offline()).GetDayAsync(WorldSamples.London, new DateOnly(2026, 10, 1), default));
    }
}

public sealed class EnglishHadithTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-hadith-en-" + Guid.NewGuid().ToString("N"));
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

    private static string Edition(string text, int n) =>
        $$$"""{"metadata":{"name":"Forty Hadith of an-Nawawi"},"hadiths":[{"hadithnumber":{{{n}}},"text":"{{{text}}}","grades":[],"reference":{"book":1,"hadith":{{{n}}}}}]}""";

    private const string Indo = """{"status":true,"data":{"arab":"إِنَّمَا","indo":"Sesungguhnya","judul":"Niat","no":"NUM"}}""";

    /// <summary>jsdelivr eng/ara editions and myquran Indonesian, renumbered from the URL.</summary>
    private static StubHandler Online() => new(r =>
    {
        var n = int.Parse(Path.GetFileNameWithoutExtension(r.RequestUri!.Segments[^1]));
        var path = r.RequestUri.AbsolutePath;
        if (path.Contains("/eng-nawawi/")) return StubHandler.Json(Edition("It is narrated  on the\\nauthority of Umar ", n));
        if (path.Contains("/ara-nawawi/")) return StubHandler.Json(Edition("إِنَّمَا الْأَعْمَالُ", n));
        return StubHandler.Json(Indo.Replace("NUM", n.ToString()));
    });

    private static StubHandler Offline() => new(_ => throw new HttpRequestException("offline"));

    private HadithService Service(StubHandler h, ManualTime? time = null) => new(new HadithClient(new HttpClient(h)), _dir, null, time ?? new ManualTime());

    [Fact]
    public async Task Client_parses_english_and_arabic()
    {
        var h = Online();
        var hadith = await new HadithClient(new HttpClient(h)).GetNawawiEnglishAsync(12, default);
        Assert.Equal("Forty Hadith of an-Nawawi", hadith.Collection);
        Assert.Equal(12, hadith.Number);
        Assert.Equal("Hadith 12", hadith.Title);
        Assert.Equal("It is narrated on the authority of Umar", hadith.Translation);
        Assert.Equal("إِنَّمَا الْأَعْمَالُ", hadith.Arabic);
        Assert.Equal(AppLanguage.English, hadith.Language);
        Assert.Contains(h.Requests, u => u.ToString() == "https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions/eng-nawawi/12.json");
        Assert.Contains(h.Requests, u => u.ToString() == "https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions/ara-nawawi/12.json");
    }

    [Theory]
    [InlineData("""{"hadiths":[]}""")]
    [InlineData("""{"hadiths":[{"hadithnumber":2,"text":"x"}]}""")]
    [InlineData("""{"hadiths":[{"hadithnumber":1,"text":"  "}]}""")]
    [InlineData("nope")]
    public async Task Client_rejects_bad_editions(string body)
    {
        var h = new StubHandler(_ => StubHandler.Json(body));
        await Assert.ThrowsAsync<PrayerApiException>(() => new HadithClient(new HttpClient(h)).GetNawawiEnglishAsync(1, default));
    }

    [Fact]
    public async Task Languages_are_cached_separately_with_same_number()
    {
        var h = Online();
        var svc = Service(h);
        var id = await svc.GetTodayAsync(default);
        var en = await svc.GetTodayAsync(default, AppLanguage.English);
        Assert.Equal(AppLanguage.Indonesian, id!.Language);
        Assert.Equal(AppLanguage.English, en!.Language);
        Assert.Equal(id.Number, en.Number);
        Assert.Equal(Oct24, en.Date);
        Assert.Same(en, await svc.GetTodayAsync(default, AppLanguage.English));
        Assert.Same(id, await svc.GetTodayAsync(default));
        Assert.Equal(3, h.Requests.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "hadith-2026-10-24.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "hadith-en-2026-10-24.json")));

        var offline = Offline();
        Assert.Equal(en, await Service(offline).GetTodayAsync(default, AppLanguage.English));
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task Offline_fallback_only_returns_requested_language()
    {
        var time = new ManualTime();
        await Service(Online(), time).GetTodayAsync(default); // Indonesian only
        time.Now = time.Now.AddDays(1);
        var svc = Service(Offline(), time);
        Assert.Null(await svc.GetTodayAsync(default, AppLanguage.English));
        Assert.Equal(AppLanguage.Indonesian, (await svc.GetTodayAsync(default))!.Language);
    }

    [Fact]
    public async Task Legacy_cache_without_language_reads_as_indonesian()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "hadith-2026-10-24.json"),
            """{"Collection":"Arbain Nawawi","Number":5,"Title":"T","Arabic":"ع","Indonesian":"Teks lama","Date":"2026-10-24"}""");
        var svc = Service(Offline());
        var h = await svc.GetTodayAsync(default);
        Assert.Equal("Teks lama", h!.Translation);
        Assert.Equal(AppLanguage.Indonesian, h.Language);
        Assert.Null(await svc.GetTodayAsync(default, AppLanguage.English));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_english_smoke()
    {
        if (Environment.GetEnvironmentVariable("SELA_LIVE_TESTS") != "1") return;
        var hadith = await new HadithClient(new HttpClient()).GetNawawiEnglishAsync(1, default);
        Assert.Equal(1, hadith.Number);
        Assert.StartsWith("It is narrated on the authority of", hadith.Translation);
        Assert.False(string.IsNullOrWhiteSpace(hadith.Arabic));
    }
}

public sealed class EnglishReminderCopyTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static DateTime At(int h, int m, int s = 0) => Day.ToDateTime(new TimeOnly(h, m, s));

    private static DaySchedule Schedule(City? city = null) => new(Day, city ?? DefaultCity.Jakarta, new[]
    {
        new PrayerTime(Prayer.Imsak, At(4, 1)), new PrayerTime(Prayer.Subuh, At(4, 11)),
        new PrayerTime(Prayer.Terbit, At(5, 30)), new PrayerTime(Prayer.Dhuha, At(5, 50)),
        new PrayerTime(Prayer.Dzuhur, At(11, 39)), new PrayerTime(Prayer.Ashar, At(14, 51)),
        new PrayerTime(Prayer.Maghrib, At(17, 48)), new PrayerTime(Prayer.Isya, At(18, 59)),
    });

    private static SelaSettings En() => new() { Language = AppLanguage.English };
    private static readonly CalendarEvent[] None = Array.Empty<CalendarEvent>();

    [Fact]
    public void Prayer_soon_and_now()
    {
        var soon = Assert.Single(new ReminderScheduler().Due(At(11, 29), Schedule(), None, En()));
        Assert.Equal("Dhuhr in 10 min", soon.Title);
        Assert.Equal("Get ready for Dhuhr", soon.Body);

        var now = Assert.Single(new ReminderScheduler().Due(At(11, 39, 30), Schedule(WorldSamples.London), None, En()));
        Assert.Equal("Time for Dhuhr", now.Title);
        Assert.Equal("It's Dhuhr time in London, United Kingdom", now.Body);
    }

    [Fact]
    public void Imsak_and_sunrise()
    {
        var s = En();
        s.PrayerEnabled[Prayer.Imsak] = true;
        s.PrayerEnabled[Prayer.Terbit] = true;
        var imsak = Assert.Single(new ReminderScheduler().Due(At(3, 55), Schedule(), None, s));
        Assert.Equal(("Imsak in 6 min", "Imsak time"), (imsak.Title, imsak.Body));
        var imsakNow = Assert.Single(new ReminderScheduler().Due(At(4, 1), Schedule(), None, s), r => r.Prayer == Prayer.Imsak);
        Assert.Equal(("Imsak", "Imsak has begun in Kota Jakarta"), (imsakNow.Title, imsakNow.Body));
        var sunrise = Assert.Single(new ReminderScheduler().Due(At(5, 25), Schedule(), None, s));
        Assert.Equal(("Sunrise in 5 min", "Sunrise"), (sunrise.Title, sunrise.Body));
        var sunriseNow = Assert.Single(new ReminderScheduler().Due(At(5, 30), Schedule(), None, s));
        Assert.Equal(("Sunrise", "The sun has risen in Kota Jakarta"), (sunriseNow.Title, sunriseNow.Body));
    }

    [Fact]
    public void Meeting_body()
    {
        var ev = new CalendarEvent("e1", "Sync", At(9, 0), At(9, 30), "Room 3", null, 10, CalendarSource.Outlook);
        Assert.Equal("09:00–09:30 · starts in 8 min · Room 3", Assert.Single(new ReminderScheduler().Due(At(8, 52), Schedule(), new[] { ev }, En())).Body);
        Assert.Equal("09:00–09:30 · starting now", Assert.Single(new ReminderScheduler().Due(At(9, 0), Schedule(), new[] { ev with { Location = null } }, En())).Body);
    }

    [Fact]
    public void Habit_copy_and_names()
    {
        var t = new HabitTracker(null);
        var sch = new HabitScheduler();
        var pagi = Assert.Single(sch.Due(At(4, 26), Schedule(), En(), t));
        Assert.Equal(("Done your morning dhikr?", "Take a moment for your morning dhikr."), (pagi.Title, pagi.Body));
        var sh = Assert.Single(sch.Due(At(12, 9), Schedule(), En(), t));
        Assert.Equal(("Sent salawat today?", "Allahumma salli 'ala Muhammad."), (sh.Title, sh.Body));
        var pt = Assert.Single(sch.Due(At(15, 6), Schedule(), En(), t));
        Assert.Equal(("Done your evening dhikr?", "Make time for evening dhikr before Maghrib."), (pt.Title, pt.Body));

        Assert.Equal("Morning dhikr", HabitScheduler.Name(Habit.DzikirPagi, AppLanguage.English));
        Assert.Equal("Dzikir pagi", HabitScheduler.Name(Habit.DzikirPagi));
        Assert.Equal("Sholawat", HabitScheduler.Name(Habit.Sholawat));
    }

    [Fact]
    public void Settings_expose_prayer_calc()
    {
        Assert.Equal(new PrayerCalc(), new SelaSettings().GetPrayerCalc());
        Assert.Equal(new PrayerCalc(3, true), new SelaSettings { PrayerMethod = 3, AsrHanafi = true }.GetPrayerCalc());
    }
}
