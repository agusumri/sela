namespace Sela.Core.Tests;

public class AgendaServiceTests
{
    private sealed class FakeProvider(CalendarSource source, Func<CancellationToken, Task<IReadOnlyList<CalendarEvent>>> fn) : ICalendarProvider
    {
        public CalendarSource Source => source;
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct) => fn(ct);
    }

    private static readonly DateTime T0 = new(2026, 10, 24, 9, 0, 0);

    private static CalendarEvent E(string subject, DateTime start, CalendarSource src) =>
        new($"{src}|{subject}|{start:s}", subject, start, start.AddHours(1), null, null, 10, src);

    [Fact]
    public async Task Merges_dedups_and_sorts()
    {
        var outlook = new FakeProvider(CalendarSource.Outlook, _ => Task.FromResult<IReadOnlyList<CalendarEvent>>(
            new[] { E("Standup", T0.AddHours(2), CalendarSource.Outlook), E("Review", T0, CalendarSource.Outlook) }));
        var google = new FakeProvider(CalendarSource.Google, _ => Task.FromResult<IReadOnlyList<CalendarEvent>>(
            new[] { E("review ", T0, CalendarSource.Google), E("Lunch", T0.AddHours(1), CalendarSource.Google) }));

        var r = await new AgendaService(new ICalendarProvider[] { outlook, google }).GetEventsAsync(T0.Date, T0.Date.AddDays(1), default);

        Assert.Equal(new[] { "Review", "Lunch", "Standup" }, r.Select(e => e.Subject));
        Assert.Equal(CalendarSource.Outlook, r[0].Source);
    }

    [Fact]
    public async Task Failing_and_hanging_providers_are_isolated()
    {
        var logs = new List<string>();
        var ok = new FakeProvider(CalendarSource.Google, _ => Task.FromResult<IReadOnlyList<CalendarEvent>>(new[] { E("A", T0, CalendarSource.Google) }));
        var throws = new FakeProvider(CalendarSource.Outlook, _ => throw new InvalidOperationException("boom"));
        var hangs = new FakeProvider(CalendarSource.Outlook, _ => new TaskCompletionSource<IReadOnlyList<CalendarEvent>>().Task); // ignores token

        var svc = new AgendaService(new ICalendarProvider[] { ok, throws, hangs }, TimeSpan.FromMilliseconds(200), logs.Add);
        var r = await svc.GetEventsAsync(T0.Date, T0.Date.AddDays(1), default);

        Assert.Equal("A", Assert.Single(r).Subject);
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var hangs = new FakeProvider(CalendarSource.Outlook, ct => Task.Delay(Timeout.Infinite, ct).ContinueWith<IReadOnlyList<CalendarEvent>>(_ => [], TaskScheduler.Default));
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AgendaService(new[] { hangs }).GetEventsAsync(T0, T0.AddDays(1), cts.Token));
    }
}
