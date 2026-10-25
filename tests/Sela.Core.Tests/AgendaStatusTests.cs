using System.Text;

namespace Sela.Core.Tests;

public class AgendaStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 9, 0, 0);

    private static CalendarEvent E(string subject, DateTime start, CalendarSource src, double hours = 1) =>
        new($"{src}|{subject}|{start:s}", subject, start, start.AddHours(hours), "Room", "https://meet.example.com/x", 10, src);

    private sealed class Fake(CalendarSource source, Func<Task<IReadOnlyList<CalendarEvent>>> fn) : ICalendarProvider
    {
        public CalendarSource Source => source;
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct) => fn();
    }

    private sealed class Refreshable(CalendarSource source, bool failed) : IRefreshableCalendarProvider
    {
        public int FreshRequests;
        public CalendarSource Source => source;
        public bool LastFetchFailed => failed;
        public void RequestFreshFetch() => FreshRequests++;
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>(new[] { E("Stale", Now, source) });
    }

    [Fact]
    public async Task Reports_throwing_timing_out_and_stale_sources()
    {
        var throws = new Fake(CalendarSource.Outlook, () => throw new InvalidOperationException("boom"));
        var hangs = new Fake(CalendarSource.Outlook, () => new TaskCompletionSource<IReadOnlyList<CalendarEvent>>().Task);
        var stale = new Refreshable(CalendarSource.Google, failed: true);
        var svc = new AgendaService(new ICalendarProvider[] { throws, hangs, stale }, TimeSpan.FromMilliseconds(100));

        var r = await svc.GetEventsWithStatusAsync(Now, Now.AddDays(1), default);
        Assert.Equal(new[] { CalendarSource.Outlook, CalendarSource.Google }.ToHashSet(), r.Failed.ToHashSet());
        Assert.Equal(new[] { CalendarSource.Outlook, CalendarSource.Google }.ToHashSet(), r.Configured.ToHashSet());
        Assert.Equal("Stale", Assert.Single(r.Events).Subject);
    }

    [Fact]
    public async Task Each_failure_kind_is_reported_on_its_own()
    {
        async Task<IReadOnlySet<CalendarSource>> FailedFor(ICalendarProvider p) =>
            (await new AgendaService(new[] { p }, TimeSpan.FromMilliseconds(100)).GetEventsWithStatusAsync(Now, Now.AddDays(1), default)).Failed;

        Assert.Contains(CalendarSource.Outlook, await FailedFor(new Fake(CalendarSource.Outlook, () => throw new InvalidOperationException())));
        Assert.Contains(CalendarSource.Outlook, await FailedFor(new Fake(CalendarSource.Outlook, () => new TaskCompletionSource<IReadOnlyList<CalendarEvent>>().Task)));
        Assert.Contains(CalendarSource.Google, await FailedFor(new Refreshable(CalendarSource.Google, failed: true)));
        Assert.Empty(await FailedFor(new Refreshable(CalendarSource.Google, failed: false)));
    }

    [Fact]
    public async Task Force_requests_fresh_fetch_and_old_method_still_works()
    {
        var p = new Refreshable(CalendarSource.Google, failed: false);
        var svc = new AgendaService(new ICalendarProvider[] { p });
        await svc.GetEventsWithStatusAsync(Now, Now.AddDays(1), default);
        Assert.Equal(0, p.FreshRequests);
        var r = await svc.GetEventsWithStatusAsync(Now, Now.AddDays(1), default, force: true);
        Assert.Equal(1, p.FreshRequests);
        Assert.Empty(r.Failed);
        Assert.Single(await svc.GetEventsAsync(Now, Now.AddDays(1), default));
    }

    // ---- AgendaMerge ----

    private static readonly IReadOnlySet<CalendarSource> GoogleFailed = new HashSet<CalendarSource> { CalendarSource.Google };
    private static readonly IReadOnlySet<CalendarSource> None = new HashSet<CalendarSource>();

    [Fact]
    public void Failed_source_is_filled_from_cache_next_to_fresh_events()
    {
        var fresh = new[] { E("Outlook sync", Now.AddHours(2), CalendarSource.Outlook) };
        var cached = new[] { E("Google review", Now.AddHours(1), CalendarSource.Google), E("Old outlook", Now.AddHours(3), CalendarSource.Outlook) };

        var (events, usedCache) = AgendaMerge.Merge(fresh, GoogleFailed, cached, Now);
        Assert.True(usedCache);
        Assert.Equal(new[] { "Google review", "Outlook sync" }, events.Select(e => e.Subject)); // cached Outlook ignored (Outlook is fresh)
    }

    [Fact]
    public void Ended_and_after_tomorrow_cached_events_are_dropped()
    {
        var cached = new[]
        {
            E("Ended", Now.AddHours(-2), CalendarSource.Google),
            E("Running", Now.AddMinutes(-30), CalendarSource.Google),
            E("Tomorrow late", Now.Date.AddDays(1).AddHours(23), CalendarSource.Google),
            E("Day after", Now.Date.AddDays(2).AddHours(1), CalendarSource.Google),
        };
        var (events, usedCache) = AgendaMerge.Merge(Array.Empty<CalendarEvent>(), GoogleFailed, cached, Now);
        Assert.True(usedCache);
        Assert.Equal(new[] { "Running", "Tomorrow late" }, events.Select(e => e.Subject));
    }

    [Fact]
    public void Stale_fresh_events_of_a_failed_source_are_replaced_without_duplicates()
    {
        var fresh = new[] { E("Standup", Now.AddHours(1), CalendarSource.Google), E("Lunch", Now.AddHours(3), CalendarSource.Outlook) };
        var cached = new[] { E("standup ", Now.AddHours(1), CalendarSource.Google), E("Lunch", Now.AddHours(3), CalendarSource.Google) };

        var (events, _) = AgendaMerge.Merge(fresh, GoogleFailed, cached, Now);
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { "standup ", "Lunch" }, events.Select(e => e.Subject));
        Assert.Equal(CalendarSource.Outlook, events[1].Source); // Outlook wins ties, like AgendaService
    }

    [Fact]
    public void Nothing_usable_in_cache_keeps_fresh_events_and_flag_false()
    {
        var fresh = new[] { E("B", Now.AddHours(2), CalendarSource.Google), E("A", Now.AddHours(1), CalendarSource.Outlook) };
        var (events, usedCache) = AgendaMerge.Merge(fresh, GoogleFailed, new[] { E("Ended", Now.AddHours(-3), CalendarSource.Google) }, Now);
        Assert.False(usedCache);
        Assert.Equal(new[] { "A", "B" }, events.Select(e => e.Subject));

        (events, usedCache) = AgendaMerge.Merge(fresh, None, new[] { E("C", Now.AddHours(1), CalendarSource.Google) }, Now);
        Assert.False(usedCache);
        Assert.Equal(2, events.Count);

        (_, usedCache) = AgendaMerge.Merge(fresh, GoogleFailed, null, Now);
        Assert.False(usedCache);
    }

    // ---- AgendaCacheCodec ----

    [Fact]
    public void Codec_round_trips()
    {
        var saved = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(7));
        var events = new[] { E("Weekly sync", Now, CalendarSource.Outlook), E("Design review", Now.AddHours(1), CalendarSource.Google) with { Location = null, JoinUrl = null } };

        Assert.True(AgendaCacheCodec.TryDeserialize(AgendaCacheCodec.Serialize(events, saved), out var at, out var back));
        Assert.Equal(saved, at);
        Assert.Equal(events, back);
        Assert.All(back, e => Assert.Equal(DateTimeKind.Unspecified, e.Start.Kind));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"V":1}""")]
    [InlineData("""{"V":2,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[]}""")]
    [InlineData("""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[null]}""")]
    [InlineData("""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[{"Key":"k","Subject":"","Start":"2026-10-24T09:00:00","End":"2026-10-24T10:00:00","Source":0}]}""")]
    [InlineData("""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[{"Key":"k","Subject":"s","Start":"2026-10-24T11:00:00","End":"2026-10-24T10:00:00","Source":0}]}""")]
    [InlineData("""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[{"Key":"k","Subject":"s","Start":"2026-10-24T09:00:00","End":"2026-10-24T10:00:00","Source":7}]}""")]
    [InlineData("""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[{"Key":"k","Subject":"s","Start":"2026-10-24T09:00:00","End":"2026-10-24T10:00:00","Source":0,"ReminderMinutes":-5}]}""")]
    public void Codec_rejects_garbage(string text)
    {
        Assert.False(AgendaCacheCodec.TryDeserialize(Encoding.UTF8.GetBytes(text), out _, out var events));
        Assert.Empty(events);
    }

    [Fact]
    public void Codec_rejects_truncated_and_oversized_input()
    {
        var bytes = AgendaCacheCodec.Serialize(new[] { E("Weekly sync", Now, CalendarSource.Outlook) }, DateTimeOffset.Now);
        Assert.False(AgendaCacheCodec.TryDeserialize(bytes[..(bytes.Length / 2)], out _, out _));
        Assert.False(AgendaCacheCodec.TryDeserialize(null!, out _, out _));
        Assert.False(AgendaCacheCodec.TryDeserialize(new byte[AgendaCacheCodec.MaxBytes + 1], out _, out _));

        var many = Enumerable.Range(0, AgendaCacheCodec.MaxEvents + 1).Select(i => $$"""{"Key":"k{{i}}","Subject":"s","Start":"2026-10-24T09:00:00","End":"2026-10-24T10:00:00","Source":0}""");
        var tooMany = Encoding.UTF8.GetBytes($$"""{"V":1,"SavedAt":"2026-10-24T08:00:00+07:00","Events":[{{string.Join(',', many)}}]}""");
        Assert.False(AgendaCacheCodec.TryDeserialize(tooMany, out _, out _));
    }

    [Fact]
    public void Codec_drops_non_https_join_links()
    {
        var bytes = AgendaCacheCodec.Serialize(new[] { E("X", Now, CalendarSource.Google) with { JoinUrl = "javascript:alert(1)" } }, DateTimeOffset.Now);
        Assert.True(AgendaCacheCodec.TryDeserialize(bytes, out _, out var back));
        Assert.Null(Assert.Single(back).JoinUrl);
    }
}
