namespace Sela.Core;

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

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var results = await Task.WhenAll(_providers.Select(p => FetchAsync(p, from, to, ct))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var seen = new HashSet<(string, DateTime)>();
        var merged = new List<CalendarEvent>();
        foreach (var e in results.SelectMany(r => r).OrderBy(e => e.Start).ThenBy(e => e.Source))
        {
            if (seen.Add((e.Subject.Trim().ToUpperInvariant(), e.Start))) merged.Add(e);
        }
        return merged;
    }

    private async Task<IReadOnlyList<CalendarEvent>> FetchAsync(ICalendarProvider provider, DateTime from, DateTime to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            // WaitAsync also bounds providers that ignore the token.
            return await provider.GetEventsAsync(from, to, cts.Token).WaitAsync(_timeout, ct).ConfigureAwait(false)
                   ?? Array.Empty<CalendarEvent>();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"{provider.Source}: {(ex is TimeoutException or OperationCanceledException ? "timeout" : ex.Message)}");
            return Array.Empty<CalendarEvent>();
        }
    }
}
