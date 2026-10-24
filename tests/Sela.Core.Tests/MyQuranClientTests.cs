using System.Net;

namespace Sela.Core.Tests;

public class MyQuranClientTests
{
    private static MyQuranClient Client(StubHandler h) => new(new HttpClient(h));

    [Fact]
    public async Task GetMonth_maps_all_fields()
    {
        var h = new StubHandler(MyQuranSamples.Route);
        var month = await Client(h).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default);

        Assert.Equal("https://api.myquran.com/v2/sholat/jadwal/1301/2026/10", h.Requests.Single().AbsoluteUri);
        Assert.Equal(2, month.Count);
        var d = month[0];
        Assert.Equal(new DateOnly(2026, 10, 24), d.Date);
        Assert.Equal(new City("1301", "KOTA JAKARTA", "DKI JAKARTA"), d.City);
        Assert.Equal("Kota Jakarta", d.City.DisplayName);
        Assert.Equal(new DateTime(2026, 10, 24, 4, 2, 0), d[Prayer.Imsak]);
        Assert.Equal(new DateTime(2026, 10, 24, 4, 12, 0), d[Prayer.Subuh]);
        Assert.Equal(new DateTime(2026, 10, 24, 5, 25, 0), d[Prayer.Terbit]);
        Assert.Equal(new DateTime(2026, 10, 24, 5, 53, 0), d[Prayer.Dhuha]);
        Assert.Equal(new DateTime(2026, 10, 24, 11, 40, 0), d[Prayer.Dzuhur]);
        Assert.Equal(new DateTime(2026, 10, 24, 14, 51, 0), d[Prayer.Ashar]);
        Assert.Equal(new DateTime(2026, 10, 24, 17, 49, 0), d[Prayer.Maghrib]);
        Assert.Equal(new DateTime(2026, 10, 24, 18, 59, 0), d[Prayer.Isya]);
        Assert.Equal(8, d.Times.Count);
        Assert.Equal(DateTimeKind.Unspecified, d[Prayer.Isya].Kind);
    }

    [Fact]
    public async Task Search_parses_string_ids_and_encodes_query()
    {
        var h = new StubHandler(_ => StubHandler.Json(MyQuranSamples.Search));
        var r = await Client(h).SearchCitiesAsync("  kota jak ", default);
        Assert.Equal(new[] { new City("1301", "KOTA JAKARTA"), new City("1302", "KAB. KEPULAUAN SERIBU") }, r);
        Assert.Equal("Kab. Kepulauan Seribu", r[1].DisplayName);
        Assert.EndsWith("/sholat/kota/cari/kota%20jak", h.Requests.Single().AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task Search_short_query_makes_no_request(string q)
    {
        var h = new StubHandler(_ => StubHandler.Json(MyQuranSamples.Search));
        Assert.Empty(await Client(h).SearchCitiesAsync(q, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Search_status_false_returns_empty()
    {
        var h = new StubHandler(_ => StubHandler.Json(MyQuranSamples.NotFound));
        Assert.Empty(await Client(h).SearchCitiesAsync("xyzqq", default));
    }

    [Fact]
    public async Task Month_status_false_throws()
    {
        var h = new StubHandler(_ => StubHandler.Json("""{"status":false,"message":"data not found"}"""));
        var ex = await Assert.ThrowsAsync<PrayerApiException>(() => Client(h).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default));
        Assert.Contains("data not found", ex.Message);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{"status":true,"data":{"id":1301,"jadwal":[]}}""")]
    [InlineData("""{"status":true,"data":{"id":1301,"lokasi":"X","jadwal":[{"date":"2026-10-24","imsak":"4:2x","subuh":"04:12","terbit":"05:25","dhuha":"05:53","dzuhur":"11:40","ashar":"14:51","maghrib":"17:49","isya":"18:59"}]}}""")]
    [InlineData("""{"status":true,"data":{"id":1301,"lokasi":"X","jadwal":[{"date":"24-10-2026","imsak":"04:02","subuh":"04:12","terbit":"05:25","dhuha":"05:53","dzuhur":"11:40","ashar":"14:51","maghrib":"17:49","isya":"18:59"}]}}""")]
    public async Task Malformed_responses_throw_typed_exception(string body)
    {
        var h = new StubHandler(_ => StubHandler.Json(body));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(h).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default));
    }

    [Fact]
    public async Task Http_error_and_network_failure_throw_typed_exception()
    {
        var bad = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(bad).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default));
        var down = new StubHandler(_ => throw new HttpRequestException("offline"));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(down).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default));
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        var big = new string(' ', (int)MyQuranClient.MaxBytes + 1);
        var h = new StubHandler(_ => StubHandler.Json(big));
        await Assert.ThrowsAsync<PrayerApiException>(() => Client(h).GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default));
    }

    [Fact]
    public async Task Non_numeric_city_id_is_rejected_before_request()
    {
        var h = new StubHandler(MyQuranSamples.Route);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(h).GetMonthAsync(new City("../x", "X"), 2026, 10, default));
        Assert.Empty(h.Requests);
    }

    /// <summary>Hits the real API. Runs only when SELA_LIVE_TESTS=1 (otherwise passes without network).</summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_smoke()
    {
        if (Environment.GetEnvironmentVariable("SELA_LIVE_TESTS") != "1") return;
        var client = new MyQuranClient(new HttpClient());
        var found = await client.SearchCitiesAsync("jakarta", default);
        Assert.Contains(found, c => c.Id == "1301");
        var month = await client.GetMonthAsync(DefaultCity.Jakarta, 2026, 10, default);
        Assert.Equal(31, month.Count);
        Assert.Equal(new DateTime(2026, 10, 24, 4, 12, 0), month.Single(d => d.Date == new DateOnly(2026, 10, 24))[Prayer.Subuh]);
    }
}
