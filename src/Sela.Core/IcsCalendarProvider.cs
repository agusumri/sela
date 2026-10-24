using Ical.Net;
using Ical.Net.DataTypes;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace Sela.Core;

/// <summary>
/// Reads events from a secret Google Calendar iCal address (https:// or webcal://).
/// Downloads are throttled to one attempt per 5 minutes per URL, limited to 10 MB and 15 s.
/// Never throws for network/parse problems: logs (without the secret URL) and returns cached or empty data.
/// </summary>
public sealed class IcsCalendarProvider : ICalendarProvider
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);
    public const long MaxBytes = 10 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Func<string?> _urlProvider;
    private readonly Func<int> _defaultReminderMinutes;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _localZone;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedUrl;
    private string? _cachedText;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private string? _parsedText;
    private Calendar? _parsedCalendar;

    public IcsCalendarProvider(
        HttpClient http,
        Func<string?> urlProvider,
        Func<int>? defaultReminderMinutes = null,
        Action<string>? log = null,
        TimeProvider? timeProvider = null,
        TimeZoneInfo? localZone = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _urlProvider = urlProvider ?? throw new ArgumentNullException(nameof(urlProvider));
        _defaultReminderMinutes = defaultReminderMinutes ?? (() => 10);
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
        _localZone = localZone ?? TimeZoneInfo.Local;
    }

    public CalendarSource Source => CalendarSource.Google;

    /// <summary>Accepts https:// and webcal:// (rewritten to https://); anything else returns null.</summary>
    public static Uri? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["webcal://".Length..];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var uri = NormalizeUrl(_urlProvider());
        if (uri is null) return Array.Empty<CalendarEvent>();

        var text = await GetTextAsync(uri, ct).ConfigureAwait(false);
        if (text is null) return Array.Empty<CalendarEvent>();

        try
        {
            // Parse once per downloaded text; the feed is refreshed at most every 5 minutes.
            if (!ReferenceEquals(text, _parsedText))
            {
                _parsedCalendar = Load(text);
                _parsedText = text;
            }
            return Expand(_parsedCalendar!, from, to, _defaultReminderMinutes(), _localZone, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Invoke($"Google Calendar: gagal membaca iCal ({ex.GetType().Name}: {ex.Message})");
            return Array.Empty<CalendarEvent>();
        }
    }

    private async Task<string?> GetTextAsync(Uri uri, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = uri.AbsoluteUri;
            var now = _time.GetUtcNow();
            if (key != _cachedUrl)
            {
                _cachedUrl = key;
                _cachedText = null;
                _lastAttempt = DateTimeOffset.MinValue;
            }
            if (now - _lastAttempt < RefreshInterval) return _cachedText;
            _lastAttempt = now;

            try
            {
                _cachedText = await SafeIo.GetStringAsync(_http, uri, FetchTimeout, MaxBytes, "text/calendar", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Message deliberately omits the URL: it contains the private calendar token.
                _log?.Invoke($"Google Calendar: unduhan gagal ({ex.GetType().Name}: {ex.Message})");
            }
            return _cachedText;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Upper bound on expanded occurrences per parse, so a hostile feed cannot exhaust CPU or memory.</summary>
    public const int MaxOccurrences = 2000;

    /// <summary>Parses iCal text and expands occurrences overlapping [from, to) (local times). Throws on invalid iCal.</summary>
    public static IReadOnlyList<CalendarEvent> Parse(string icsText, DateTime from, DateTime to, int defaultReminderMinutes, TimeZoneInfo? localZone = null, CancellationToken ct = default)
    {
        return Expand(Load(icsText), from, to, defaultReminderMinutes, localZone ?? TimeZoneInfo.Local, ct);
    }

    /// <summary>
    /// Loads the feed. If the whole document fails to deserialize (one bad event can do that),
    /// retries each VEVENT on its own with the calendar's VTIMEZONEs, keeping the readable ones.
    /// </summary>
    private static Calendar Load(string icsText)
    {
        try
        {
            return Calendar.Load(icsText) ?? throw new FormatException("Empty iCal document.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var events = Blocks(icsText, "VEVENT");
            if (events.Count == 0) throw;
            var timezones = string.Concat(Blocks(icsText, "VTIMEZONE"));
            var merged = new Calendar();
            foreach (var block in events)
            {
                try
                {
                    var single = Calendar.Load("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + timezones + block + "END:VCALENDAR\r\n");
                    if (single is null) continue;
                    foreach (var tz in single.TimeZones)
                        if (!merged.TimeZones.Any(t => t.TzId == tz.TzId)) merged.TimeZones.Add(tz);
                    foreach (var evt in single.Events) merged.Events.Add(evt);
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    // Skip just this event.
                }
            }
            return merged;
        }
    }

    /// <summary>Raw BEGIN:name … END:name blocks (top level only for VEVENT/VTIMEZONE in practice).</summary>
    private static List<string> Blocks(string text, string name)
    {
        var result = new List<string>();
        string begin = "BEGIN:" + name, end = "END:" + name;
        int pos = 0;
        while ((pos = text.IndexOf(begin, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int stop = text.IndexOf(end, pos, StringComparison.OrdinalIgnoreCase);
            if (stop < 0) break;
            stop += end.Length;
            result.Add(text[pos..stop] + "\r\n");
            pos = stop;
        }
        return result;
    }

    private static IReadOnlyList<CalendarEvent> Expand(Calendar calendar, DateTime from, DateTime to, int defaultReminderMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        DateTime fromUtc = ToUtc(from, zone), toUtc = ToUtc(to, zone);
        // Start a day early so events that began before 'from' but are still running are included.
        var start = new CalDateTime(fromUtc.AddDays(-1), CalDateTime.UtcTzId, true);
        var end = new CalDateTime(toUtc, CalDateTime.UtcTzId, true);

        var result = new List<CalendarEvent>();
        var seen = new HashSet<string>();
        int budget = MaxOccurrences;
        // Expand per event: one malformed or hostile event is skipped instead of failing the whole feed.
        foreach (var evt in calendar.Events)
        {
            if (budget <= 0) break;
            ct.ThrowIfCancellationRequested();
            try
            {
                if (evt.IsAllDay) continue;
                if (string.Equals(evt.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                // Meetings never recur more often than daily; sub-daily rules are only an expansion cost.
                if (evt.RecurrenceRule is { } rule && rule.Frequency < FrequencyType.Daily) continue;

                int reminder = ReminderMinutes(evt) ?? defaultReminderMinutes;
                foreach (var occ in evt.GetOccurrences(start).TakeWhileBefore(end))
                {
                    if (--budget < 0) break;
                    ct.ThrowIfCancellationRequested();
                    if (!occ.Period.StartTime.HasTime) continue;

                    var startLocal = ToLocal(occ.Period.StartTime, zone);
                    var endLocal = ToLocal(occ.Period.EffectiveEndTime ?? occ.Period.StartTime, zone);
                    if (endLocal < startLocal) endLocal = startLocal;
                    // Overlap with [from, to); zero-length events count if they start inside the range.
                    bool overlaps = startLocal < to && (endLocal > from || (endLocal == startLocal && startLocal >= from));
                    if (!overlaps) continue;

                    var key = $"google|{evt.Uid}|{startLocal:yyyyMMddTHHmm}";
                    if (!seen.Add(key)) continue;

                    var conference = evt.Properties["X-GOOGLE-CONFERENCE"]?.Value?.ToString();
                    result.Add(new CalendarEvent(
                        key,
                        string.IsNullOrWhiteSpace(evt.Summary) ? "(Tanpa judul)" : evt.Summary.Trim(),
                        startLocal,
                        endLocal,
                        string.IsNullOrWhiteSpace(evt.Location) ? null : evt.Location.Trim(),
                        JoinLinkExtractor.Find(conference, evt.Location, evt.Description, evt.Url?.ToString()),
                        reminder,
                        CalendarSource.Google));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip just this event.
            }
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>
    /// Minutes before start of the first DISPLAY/AUDIO alarm with a start-relative trigger,
    /// clamped to <see cref="ReminderScheduler.MaxReminderMinutes"/>. Unreadable triggers are ignored.
    /// </summary>
    private static int? ReminderMinutes(IcalEvent evt)
    {
        foreach (var alarm in evt.Alarms)
        {
            try
            {
                if (!string.Equals(alarm.Action, "DISPLAY", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(alarm.Action, "AUDIO", StringComparison.OrdinalIgnoreCase)) continue;
                var trigger = alarm.Trigger;
                if (trigger is null || !trigger.IsRelative || trigger.Duration is not { } duration) continue;
                if (trigger.Related is { } related && !string.Equals(related, "START", StringComparison.OrdinalIgnoreCase)) continue;
                var minutes = -duration.ToTimeSpanUnspecified().TotalMinutes;
                if (double.IsNaN(minutes)) continue;
                return (int)Math.Clamp(Math.Round(minutes), 0, ReminderScheduler.MaxReminderMinutes);
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException or FormatException)
            {
                // Out-of-range duration: fall through to the next alarm / default.
            }
        }
        return null;
    }

    private static DateTime ToUtc(DateTime t, TimeZoneInfo zone) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(t, zone),
    };

    private static DateTime ToLocal(CalDateTime t, TimeZoneInfo zone)
    {
        if (t.IsFloating) return DateTime.SpecifyKind(t.Value, DateTimeKind.Unspecified);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.AsUtc, DateTimeKind.Utc), zone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }
}
