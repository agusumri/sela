using System.Globalization;
using System.Text.Json;

namespace Sela.Core;

/// <summary>
/// Supplies daily prayer schedules with a memory + per-month disk cache, so the app keeps working offline once a
/// month has been fetched. Indonesian (Kemenag) cities come from myquran.com, worldwide places from Aladhan.
/// Lookup order: memory → disk → API (then saved to disk).
/// Disk files are refetched when older than 30 days; a stale file is still used if the API is unreachable.
/// </summary>
public sealed class PrayerScheduleService
{
    public static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(30);
    /// <summary>After a failed API call the same month is not retried for this long (avoids hammering from UI timers).</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(1);

    private const string GeoPrefix = "geo:";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly MyQuranClient _client;
    private readonly AladhanClient? _world;
    private readonly string _cacheDir;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<DaySchedule>> _memory = new();
    private readonly Dictionary<string, DateTimeOffset> _lastFailure = new();

    public PrayerScheduleService(MyQuranClient client, string? cacheDir = null, Action<string>? log = null, TimeProvider? time = null,
        AladhanClient? world = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _world = world;
        _cacheDir = cacheDir ?? DefaultCacheDir;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public static string DefaultCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "cache");

    /// <summary>
    /// Schedule for the date, or null when it is neither cached nor fetchable (show "schedule not available, check connection").
    /// <paramref name="calc"/> applies to world cities only; null = defaults.
    /// </summary>
    public async Task<DaySchedule?> GetDayAsync(City city, DateOnly date, CancellationToken ct, PrayerCalc? calc = null, bool force = false)
    {
        var month = await GetMonthAsync(city, date.Year, date.Month, calc ?? new PrayerCalc(), ct, force).ConfigureAwait(false);
        return month?.FirstOrDefault(d => d.Date == date);
    }

    /// <summary>Ensures the current and next month are cached so month rollover works offline. Never throws except on cancellation or a bad city.</summary>
    public async Task PrefetchAsync(City city, CancellationToken ct, PrayerCalc? calc = null, bool force = false)
    {
        calc ??= new PrayerCalc();
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var next = today.AddMonths(1);
        await GetMonthAsync(city, today.Year, today.Month, calc, ct, force).ConfigureAwait(false);
        await GetMonthAsync(city, next.Year, next.Month, calc, ct, force).ConfigureAwait(false);
    }

    /// <summary>Cache key (also the file name stem). Ids become part of a file name, so only digits are accepted.</summary>
    private static string KeyFor(City city, PrayerCalc calc, int year, int month)
    {
        var id = city.Id?.Trim() ?? "";
        if (city.IsWorld)
        {
            var digits = id.StartsWith(GeoPrefix, StringComparison.Ordinal) ? id[GeoPrefix.Length..] : "";
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
                throw new ArgumentException("World city id must be \"geo:\" followed by digits.", nameof(city));
            AladhanClient.Validate(city);
            var method = calc.Method is { } m ? m.ToString(CultureInfo.InvariantCulture) : "auto";
            return string.Create(CultureInfo.InvariantCulture, $"geo-{digits}-m{method}-s{(calc.AsrHanafi ? 1 : 0)}-{year:D4}-{month:D2}");
        }
        if (id.Length == 0 || !id.All(char.IsAsciiDigit)) throw new ArgumentException("City id must be numeric.", nameof(city));
        return string.Create(CultureInfo.InvariantCulture, $"{id}-{year:D4}-{month:D2}");
    }

    private async Task<IReadOnlyList<DaySchedule>?> GetMonthAsync(City city, int year, int month, PrayerCalc calc, CancellationToken ct, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(city);
        var key = KeyFor(city, calc, year, month);
        if (city.IsWorld && _world is null) throw new InvalidOperationException("No Aladhan client configured for world locations.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_memory.TryGetValue(key, out var cached)) return cached;

            var now = _time.GetUtcNow();
            var path = Path.Combine(_cacheDir, $"jadwal-{key}.json");
            var disk = ReadCache(path, city);
            if (disk is not null && now - disk.FetchedAt < MaxCacheAge)
                return _memory[key] = disk.Schedules;

            if (!force && _lastFailure.TryGetValue(key, out var failedAt) && now - failedAt < RetryBackoff)
                return disk?.Schedules;

            try
            {
                IReadOnlyList<DaySchedule> fetched;
                if (city.IsWorld)
                {
                    var raw = await _world!.GetRawMonthAsync(city, year, month, calc, ct).ConfigureAwait(false);
                    fetched = _world.ToLocal(raw, city);
                    WriteCache(path, new CacheFile
                    {
                        FetchedAt = now, CityId = city.Id, CityName = city.Name, Province = city.Province,
                        TimeZone = raw.TimeZone, Days = raw.Days.ToList(),
                    });
                }
                else
                {
                    fetched = await _client.GetMonthAsync(city, year, month, ct).ConfigureAwait(false);
                    WriteCache(path, KemenagFile(fetched, now));
                }
                _lastFailure.Remove(key);
                return _memory[key] = fetched;
            }
            catch (PrayerApiException ex)
            {
                _lastFailure[key] = now;
                _log?.Invoke($"Jadwal {key}: {ex.Message}{(disk is null ? "" : " (memakai cache lama)")}");
                if (disk is null) return null;
                // Stale but usable; keep it in memory so we don't re-read the file every tick.
                return _memory[key] = disk.Schedules;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- disk cache ----

    /// <summary>
    /// Kemenag: PC-local times (Indonesia only). World: the location's wall-clock times plus <see cref="TimeZone"/>,
    /// converted to PC-local on every read so a changed PC zone stays correct.
    /// </summary>
    private sealed class CacheFile
    {
        public DateTimeOffset FetchedAt { get; set; }
        public string? CityId { get; set; }
        public string? CityName { get; set; }
        public string? Province { get; set; }
        public string? TimeZone { get; set; }
        public List<MyQuranClient.DayDto>? Days { get; set; }
    }

    private sealed record CachedMonth(DateTimeOffset FetchedAt, IReadOnlyList<DaySchedule> Schedules);

    private CachedMonth? ReadCache(string path, City requested)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            if (file?.Days is not { Count: > 0 } days || string.IsNullOrWhiteSpace(file.CityId)) return null;
            if (requested.IsWorld)
            {
                if (file.CityId != requested.Id) return null;
                var raw = new AladhanClient.RawMonth(file.TimeZone, days);
                return new CachedMonth(file.FetchedAt, _world!.ToLocal(raw, requested));
            }
            var city = new City(file.CityId, file.CityName ?? file.CityId, file.Province);
            return new CachedMonth(file.FetchedAt, days.Select(d => MyQuranClient.ToSchedule(d, city)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or PrayerApiException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke($"Cache jadwal rusak, diabaikan: {Path.GetFileName(path)} ({ex.Message})");
            return null;
        }
    }

    private static CacheFile KemenagFile(IReadOnlyList<DaySchedule> schedules, DateTimeOffset fetchedAt)
    {
        var city = schedules[0].City;
        return new CacheFile
        {
            FetchedAt = fetchedAt,
            CityId = city.Id,
            CityName = city.Name,
            Province = city.Province,
            Days = schedules.Select(s => new MyQuranClient.DayDto
            {
                Date = s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Imsak = Hm(s[Prayer.Imsak]), Subuh = Hm(s[Prayer.Subuh]), Terbit = Hm(s[Prayer.Terbit]),
                Dhuha = Hm(s[Prayer.Dhuha]), Dzuhur = Hm(s[Prayer.Dzuhur]), Ashar = Hm(s[Prayer.Ashar]),
                Maghrib = Hm(s[Prayer.Maghrib]), Isya = Hm(s[Prayer.Isya]),
            }).ToList(),
        };
    }

    private void WriteCache(string path, CacheFile file)
    {
        try
        {
            SafeIo.WriteAllTextAtomic(path, JsonSerializer.Serialize(file, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Gagal menyimpan cache jadwal: {ex.Message}");
        }
    }

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
