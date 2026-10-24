using System.Net;
using System.Text;

namespace Sela.Core.Tests;

public class IcsCalendarProviderTests
{
    private static readonly TimeZoneInfo Wib = TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
    private static readonly DateTime From = new(2026, 10, 26, 0, 0, 0);
    private static readonly DateTime To = From.AddDays(7);

    private const string Ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//test//EN
        BEGIN:VEVENT
        UID:standup@test
        DTSTART:20261026T020000Z
        DTEND:20261026T021500Z
        RRULE:FREQ=DAILY;COUNT=3
        SUMMARY:Daily standup
        LOCATION:Ruang 3
        DESCRIPTION:Join https://meet.google.com/abc-defg-hij please
        BEGIN:VALARM
        ACTION:DISPLAY
        DESCRIPTION:Reminder
        TRIGGER:-PT5M
        END:VALARM
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled@test
        DTSTART:20261027T030000Z
        DTEND:20261027T040000Z
        STATUS:CANCELLED
        SUMMARY:Cancelled meeting
        END:VEVENT
        BEGIN:VEVENT
        UID:allday@test
        DTSTART;VALUE=DATE:20261028
        DTEND;VALUE=DATE:20261029
        SUMMARY:Holiday
        END:VEVENT
        BEGIN:VEVENT
        UID:old@test
        DTSTART:20261001T030000Z
        DTEND:20261001T040000Z
        SUMMARY:Old meeting
        END:VEVENT
        BEGIN:VEVENT
        UID:tz@test
        DTSTART;TZID=Asia/Makassar:20261030T090000
        DTEND;TZID=Asia/Makassar:20261030T100000
        SUMMARY:Makassar sync
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Parse_expands_recurrences_and_filters()
    {
        var events = IcsCalendarProvider.Parse(Ics, From, To, 10, Wib);

        Assert.Equal(new[] { "Daily standup", "Daily standup", "Daily standup", "Makassar sync" }, events.Select(e => e.Subject));
        var first = events[0];
        Assert.Equal(new DateTime(2026, 10, 26, 9, 0, 0), first.Start);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 15, 0), first.End);
        Assert.Equal(DateTimeKind.Unspecified, first.Start.Kind);
        Assert.Equal(5, first.ReminderMinutes);
        Assert.Equal("Ruang 3", first.Location);
        Assert.Equal("https://meet.google.com/abc-defg-hij", first.JoinUrl);
        Assert.Equal(CalendarSource.Google, first.Source);
        Assert.Equal(3, events.Take(3).Select(e => e.Key).Distinct().Count());

        var tz = events[3];
        Assert.Equal(new DateTime(2026, 10, 30, 8, 0, 0), tz.Start); // 09:00 WITA = 08:00 WIB
        Assert.Equal(10, tz.ReminderMinutes);                         // default
        Assert.Null(tz.JoinUrl);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        public Uri? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            var r = respond(request);
            r.RequestMessage ??= request;
            return Task.FromResult(r);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };

    [Fact]
    public async Task Webcal_is_rewritten_and_results_are_cached_for_five_minutes()
    {
        var handler = new FakeHandler(_ => Ok(Ics));
        var clock = new ManualTime();
        var url = "webcal://calendar.google.com/calendar/ical/x/private-secret/basic.ics";
        var p = new IcsCalendarProvider(new HttpClient(handler), () => url, () => 10, null, clock, Wib);

        Assert.Equal(4, (await p.GetEventsAsync(From, To, default)).Count);
        Assert.Equal("https", handler.LastUri!.Scheme);
        await p.GetEventsAsync(From, To, default);
        Assert.Equal(1, handler.Calls);

        clock.Now += TimeSpan.FromMinutes(5);
        await p.GetEventsAsync(From, To, default);
        Assert.Equal(2, handler.Calls);

        url = "https://calendar.google.com/calendar/ical/y/private-other/basic.ics"; // URL change invalidates cache
        await p.GetEventsAsync(From, To, default);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Network_errors_return_cached_data_and_log_without_url()
    {
        bool fail = false;
        var handler = new FakeHandler(_ => fail ? throw new HttpRequestException("down") : Ok(Ics));
        var clock = new ManualTime();
        var logs = new List<string>();
        var p = new IcsCalendarProvider(new HttpClient(handler), () => "https://calendar.google.com/private-secret/basic.ics", null, logs.Add, clock, Wib);

        Assert.Equal(4, (await p.GetEventsAsync(From, To, default)).Count);
        fail = true;
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Equal(4, (await p.GetEventsAsync(From, To, default)).Count);
        var log = Assert.Single(logs);
        Assert.DoesNotContain("private-secret", log);
    }

    [Fact]
    public async Task Server_error_with_no_cache_returns_empty()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var p = new IcsCalendarProvider(new HttpClient(handler), () => "https://example.com/a.ics", localZone: Wib);
        Assert.Empty(await p.GetEventsAsync(From, To, default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://calendar.google.com/a.ics")]
    [InlineData("file:///c:/a.ics")]
    [InlineData("not a url")]
    public async Task Non_https_urls_are_never_fetched(string? url)
    {
        var handler = new FakeHandler(_ => Ok(Ics));
        var p = new IcsCalendarProvider(new HttpClient(handler), () => url, localZone: Wib);
        Assert.Empty(await p.GetEventsAsync(From, To, default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Oversized_download_is_rejected()
    {
        var big = new string('x', (int)IcsCalendarProvider.MaxBytes + 10);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.ASCII.GetBytes(big))) });
        var logs = new List<string>();
        var p = new IcsCalendarProvider(new HttpClient(handler), () => "https://example.com/a.ics", log: logs.Add, localZone: Wib);
        Assert.Empty(await p.GetEventsAsync(From, To, default));
        Assert.Contains("10 MB", Assert.Single(logs));
    }

    [Fact]
    public async Task Invalid_ics_returns_empty_and_logs()
    {
        var logs = new List<string>();
        var p = new IcsCalendarProvider(new HttpClient(new FakeHandler(_ => Ok("garbage"))), () => "https://example.com/a.ics", log: logs.Add, localZone: Wib);
        Assert.Empty(await p.GetEventsAsync(From, To, default));
    }
}
