namespace Sela.Core;

/// <summary>Fills in the events of calendars that couldn't be read from the last saved agenda.</summary>
public static class AgendaMerge
{
    /// <summary>
    /// For each failed source, uses cached events of that source that haven't ended and start before the end of
    /// tomorrow; when the cache has such events they replace that source's (stale) fresh events.
    /// Result is de-duplicated and sorted by start. UsedCache = at least one cached event was added.
    /// </summary>
    public static (IReadOnlyList<CalendarEvent> Events, bool UsedCache) Merge(
        IReadOnlyList<CalendarEvent> fresh, IReadOnlySet<CalendarSource> failed, IReadOnlyList<CalendarEvent>? cached, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(failed);
        var endOfTomorrow = now.Date.AddDays(2);
        var fromCache = (cached ?? Array.Empty<CalendarEvent>())
            .Where(e => failed.Contains(e.Source) && e.End > now && e.Start < endOfTomorrow)
            .ToList();
        if (fromCache.Count == 0) return (AgendaService.Dedup(fresh), false);

        var replaced = fromCache.Select(e => e.Source).ToHashSet();
        var merged = AgendaService.Dedup(fresh.Where(e => !replaced.Contains(e.Source)).Concat(fromCache));
        return (merged, merged.Any(fromCache.Contains));
    }
}
