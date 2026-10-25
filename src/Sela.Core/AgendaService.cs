namespace Sela.Core;

/// <summary>Merged events plus which configured sources could not be read (their events are missing or stale).</summary>
public sealed record AgendaResult(IReadOnlyList<CalendarEvent> Events, IReadOnlySet<CalendarSource> Failed, IReadOnlySet<CalendarSource> Configured);

/// <summary>Merges events from several calendar providers; a failing or slow provider never blocks the others.</summary>
public sealed class AgendaService
{
    public static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(20);

    private readonly IReadOnlyList<ICalendarProvider> _providers;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _log;

    public AgendaService(IEnumerable<ICalendarProvider> providers, TimeSpan? providerTimeout = null, Action<string>? log = null)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
        _timeout = providerTimeout ?? DefaultProviderTimeout;
        _log = log;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct) =>
        (await GetEventsWithStatusAsync(from, to, ct).ConfigureAwait(false)).Events;

    /// <summary>
    /// Like <see cref="GetEventsAsync"/>, also reporting failed sources. <paramref name="force"/> asks throttling
    /// providers (<see cref="IRefreshableCalendarProvider"/>) to read again now.
    /// </summary>
    public async Task<AgendaResult> GetEventsWithStatusAsync(DateTime from, DateTime to, CancellationToken ct, bool force = false)
    {
        if (force)
            foreach (var p in _providers.OfType<IRefreshableCalendarProvider>()) p.RequestFreshFetch();

        var results = await Task.WhenAll(_providers.Select(p => FetchAsync(p, from, to, ct))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var failed = new HashSet<CalendarSource>();
        for (int i = 0; i < _providers.Count; i++)
            if (!results[i].Ok || _providers[i] is IRefreshableCalendarProvider { LastFetchFailed: true })
                failed.Add(_providers[i].Source);

        var configured = _providers.Select(p => p.Source).ToHashSet();
        return new AgendaResult(Dedup(results.SelectMany(r => r.Events)), failed, configured);
    }

    /// <summary>Sorted by start (then source); drops events with the same trimmed, case-insensitive subject and start.</summary>
    internal static List<CalendarEvent> Dedup(IEnumerable<CalendarEvent> events)
    {
        var seen = new HashSet<(string, DateTime)>();
        var merged = new List<CalendarEvent>();
        foreach (var e in events.OrderBy(e => e.Start).ThenBy(e => e.Source))
        {
            if (seen.Add((e.Subject.Trim().ToUpperInvariant(), e.Start))) merged.Add(e);
        }
        return merged;
    }

    private async Task<(IReadOnlyList<CalendarEvent> Events, bool Ok)> FetchAsync(ICalendarProvider provider, DateTime from, DateTime to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            // WaitAsync also bounds providers that ignore the token.
            var events = await provider.GetEventsAsync(from, to, cts.Token).WaitAsync(_timeout, ct).ConfigureAwait(false);
            return (events ?? Array.Empty<CalendarEvent>(), true);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"{provider.Source}: {(ex is TimeoutException or OperationCanceledException ? "timeout" : ex.Message)}");
            return (Array.Empty<CalendarEvent>(), false);
        }
    }
}
