using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sela.Core;

/// <summary>
/// Client for the free Aladhan calendar API (worldwide prayer times by coordinates).
/// Times arrive as wall-clock in the location's time zone and are converted to this PC's local time.
/// </summary>
public sealed class AladhanClient
{
    public static readonly Uri BaseAddress = new("https://api.aladhan.com/v1/");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const long MaxBytes = 2 * 1024 * 1024;
    /// <summary>Aladhan has no Dhuha; it is shown this long after sunrise.</summary>
    public static readonly TimeSpan DhuhaAfterSunrise = TimeSpan.FromMinutes(20);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private readonly TimeZoneInfo? _localZone;
    private readonly HashSet<string> _unknownZonesLogged = new(StringComparer.OrdinalIgnoreCase);

    public AladhanClient(HttpClient http, Action<string>? log = null) : this(http, log, null) { }

    /// <param name="localZone">Zone to convert times into; null = <see cref="TimeZoneInfo.Local"/> (tests inject a fixed zone).</param>
    public AladhanClient(HttpClient http, Action<string>? log, TimeZoneInfo? localZone)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
        _localZone = localZone;
    }

    /// <summary>All days of the month for a world city, in PC-local time. Throws <see cref="PrayerApiException"/> on any failure.</summary>
    public async Task<IReadOnlyList<DaySchedule>> GetMonthAsync(City city, int year, int month, PrayerCalc calc, CancellationToken ct)
    {
        var raw = await GetRawMonthAsync(city, year, month, calc, ct).ConfigureAwait(false);
        return ToLocal(raw, city);
    }

    /// <summary>Throws ArgumentException unless the city is a world city with sane coordinates.</summary>
    internal static void Validate(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        if (!city.IsWorld) throw new ArgumentException("City has no coordinates.", nameof(city));
        var lat = city.Latitude!.Value;
        var lng = city.Longitude!.Value;
        if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
            throw new ArgumentException("City coordinates are out of range.", nameof(city));
    }

    /// <summary>Location wall-clock times (HH:mm) plus the IANA zone they are in; this is what the disk cache stores.</summary>
    internal sealed record RawMonth(string? TimeZone, IReadOnlyList<MyQuranClient.DayDto> Days);

    internal async Task<RawMonth> GetRawMonthAsync(City city, int year, int month, PrayerCalc calc, CancellationToken ct)
    {
        Validate(city);
        ArgumentNullException.ThrowIfNull(calc);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);

        var inv = CultureInfo.InvariantCulture;
        var query = string.Create(inv,
            $"calendar/{year:D4}/{month}?latitude={Coord(city.Latitude!.Value)}&longitude={Coord(city.Longitude!.Value)}");
        if (calc.Method is { } m) query += string.Create(inv, $"&method={m}");
        query += calc.AsrHanafi ? "&school=1" : "&school=0";

        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, new Uri(BaseAddress, query), Timeout, MaxBytes, "application/json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"aladhan: {query} failed ({ex.GetType().Name}: {ex.Message})");
            throw new PrayerApiException("Cannot reach api.aladhan.com.", ex);
        }

        CalendarDto? dto;
        try { dto = JsonSerializer.Deserialize<CalendarDto>(text, Json); }
        catch (JsonException ex)
        {
            _log?.Invoke($"aladhan: {query} invalid JSON ({ex.Message})");
            throw new PrayerApiException("Invalid response from Aladhan.", ex);
        }

        if (dto is null || dto.Code != 200) throw new PrayerApiException($"Aladhan rejected the request: {dto?.Status ?? "empty"}.");
        if (dto.Data is not { Count: > 0 } items) throw new PrayerApiException("Aladhan returned no days.");

        string? zone = null;
        var days = new List<MyQuranClient.DayDto>();
        foreach (var item in items)
        {
            if (item?.Timings is not { } t) throw new PrayerApiException("Aladhan day without timings.");
            var dateText = item.Date?.Gregorian?.Date;
            if (!DateOnly.TryParseExact(dateText, "dd-MM-yyyy", inv, DateTimeStyles.None, out var date))
                throw new PrayerApiException($"Invalid Aladhan date: '{dateText}'.");
            if (date.Year != year || date.Month != month) continue;
            zone ??= item.Meta?.Timezone;

            var sunrise = Hm(t, "Sunrise", date);
            days.Add(new MyQuranClient.DayDto
            {
                Date = date.ToString("yyyy-MM-dd", inv),
                Imsak = Hm(t, "Imsak", date),
                Subuh = Hm(t, "Fajr", date),
                Terbit = sunrise,
                Dhuha = TimeOnly.ParseExact(sunrise, "HH:mm", inv).Add(DhuhaAfterSunrise).ToString("HH:mm", inv),
                Dzuhur = Hm(t, "Dhuhr", date),
                Ashar = Hm(t, "Asr", date),
                Maghrib = Hm(t, "Maghrib", date),
                Isya = Hm(t, "Isha", date),
            });
        }
        if (days.Count == 0) throw new PrayerApiException("Aladhan days do not match the requested month.");
        return new RawMonth(string.IsNullOrWhiteSpace(zone) ? city.TimeZoneId : zone.Trim(), days.OrderBy(d => d.Date, StringComparer.Ordinal).ToList());
    }

    /// <summary>Turns location wall-clock times into PC-local times. Unknown zone: keeps wall-clock and logs once.</summary>
    internal IReadOnlyList<DaySchedule> ToLocal(RawMonth raw, City city)
    {
        var source = FindZone(raw.TimeZone);
        var local = _localZone ?? TimeZoneInfo.Local;
        var result = new List<DaySchedule>(raw.Days.Count);
        foreach (var d in raw.Days)
        {
            var wall = MyQuranClient.ToSchedule(d, city);
            if (source is null || source.Id == local.Id) { result.Add(wall); continue; }
            result.Add(wall with { Times = wall.Times.Select(p => p with { Time = Convert(p.Time, source, local) }).ToList() });
        }
        return result;
    }

    private static DateTime Convert(DateTime wall, TimeZoneInfo source, TimeZoneInfo local)
    {
        // A wall-clock time inside a DST gap does not exist; step past the gap (gaps can exceed an hour
        // in historical zone shifts) instead of failing the whole month. Bounded to one day.
        for (int i = 0; i < 96 && source.IsInvalidTime(wall); i++) wall = wall.AddMinutes(15);
        try
        {
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(wall, source, local), DateTimeKind.Unspecified);
        }
        catch (ArgumentException ex)
        {
            throw new PrayerApiException($"Cannot convert a prayer time from {source.Id}.", ex);
        }
    }

    private TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        // The id comes from the API: any lookup failure just means "unknown zone" (wall-clock times).
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lock (_unknownZonesLogged)
                if (_unknownZonesLogged.Add(id)) _log?.Invoke($"aladhan: unknown time zone '{id}', showing location wall-clock times.");
            return null;
        }
    }

    private static string Coord(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>"05:09 (BST)" → "05:09".</summary>
    private static string Hm(Dictionary<string, string?> timings, string key, DateOnly date)
    {
        timings.TryGetValue(key, out var value);
        var s = value?.Trim() ?? "";
        var space = s.IndexOf(' ');
        if (space >= 0) s = s[..space];
        if (!TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new PrayerApiException($"Invalid {key} time on {date:yyyy-MM-dd}: '{value}'.");
        return s;
    }

    // ---- DTOs ----

    private sealed class CalendarDto
    {
        public int Code { get; set; }
        public string? Status { get; set; }
        public List<DayItemDto?>? Data { get; set; }
    }

    private sealed class DayItemDto
    {
        public Dictionary<string, string?>? Timings { get; set; }
        public DateDto? Date { get; set; }
        public MetaDto? Meta { get; set; }
    }

    private sealed class DateDto { public GregorianDto? Gregorian { get; set; } }
    private sealed class GregorianDto { public string? Date { get; set; } }
    private sealed class MetaDto { public string? Timezone { get; set; } }
}
