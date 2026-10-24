using System.Globalization;
using System.Text.Json;

namespace Sela.Core;

/// <summary>
/// Supplies daily prayer schedules from myquran.com with a memory + per-month disk cache, so the app keeps
/// working offline once a month has been fetched. Lookup order: memory → disk → API (then saved to disk).
/// Disk files are refetched when older than 30 days; a stale file is still used if the API is unreachable.
/// </summary>
public sealed class PrayerScheduleService
{
    public static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(30);
    /// <summary>After a failed API call the same month is not retried for this long (avoids hammering from UI timers).</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly MyQuranClient _client;
    private readonly string _cacheDir;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<DaySchedule>> _memory = new();
    private readonly Dictionary<string, DateTimeOffset> _lastFailure = new();

    public PrayerScheduleService(MyQuranClient client, string? cacheDir = null, Action<string>? log = null, TimeProvider? time = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cacheDir = cacheDir ?? DefaultCacheDir;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public static string DefaultCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "cache");

    /// <summary>Schedule for the date, or null when it is neither cached nor fetchable (show "Jadwal belum tersedia, cek koneksi").</summary>
    public async Task<DaySchedule?> GetDayAsync(City city, DateOnly date, CancellationToken ct)
    {
        var month = await GetMonthAsync(city, date.Year, date.Month, ct).ConfigureAwait(false);
        return month?.FirstOrDefault(d => d.Date == date);
    }

    /// <summary>Ensures the current and next month are cached so month rollover works offline. Never throws except on cancellation.</summary>
    public async Task PrefetchAsync(City city, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var next = today.AddMonths(1);
        await GetMonthAsync(city, today.Year, today.Month, ct).ConfigureAwait(false);
        await GetMonthAsync(city, next.Year, next.Month, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DaySchedule>?> GetMonthAsync(City city, int year, int month, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(city);
        var cityId = city.Id?.Trim() ?? "";
        // Id becomes part of a file name: only digits are accepted (prevents path traversal).
        if (cityId.Length == 0 || !cityId.All(char.IsAsciiDigit)) throw new ArgumentException("City id must be numeric.", nameof(city));

        var key = string.Create(CultureInfo.InvariantCulture, $"{cityId}-{year:D4}-{month:D2}");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_memory.TryGetValue(key, out var cached)) return cached;

            var now = _time.GetUtcNow();
            var path = Path.Combine(_cacheDir, $"jadwal-{key}.json");
            var disk = ReadCache(path);
            if (disk is not null && now - disk.FetchedAt < MaxCacheAge)
                return _memory[key] = disk.Schedules;

            if (_lastFailure.TryGetValue(key, out var failedAt) && now - failedAt < RetryBackoff)
                return disk?.Schedules;

            try
            {
                var fetched = await _client.GetMonthAsync(city, year, month, ct).ConfigureAwait(false);
                _lastFailure.Remove(key);
                WriteCache(path, fetched, now);
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

    private sealed class CacheFile
    {
        public DateTimeOffset FetchedAt { get; set; }
        public string? CityId { get; set; }
        public string? CityName { get; set; }
        public string? Province { get; set; }
        public List<MyQuranClient.DayDto>? Days { get; set; }
    }

    private sealed record CachedMonth(DateTimeOffset FetchedAt, IReadOnlyList<DaySchedule> Schedules);

    private CachedMonth? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            if (file?.Days is not { Count: > 0 } days || string.IsNullOrWhiteSpace(file.CityId)) return null;
            var city = new City(file.CityId, file.CityName ?? file.CityId, file.Province);
            return new CachedMonth(file.FetchedAt, days.Select(d => MyQuranClient.ToSchedule(d, city)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or PrayerApiException or IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Cache jadwal rusak, diabaikan: {Path.GetFileName(path)} ({ex.Message})");
            return null;
        }
    }

    private void WriteCache(string path, IReadOnlyList<DaySchedule> schedules, DateTimeOffset fetchedAt)
    {
        var city = schedules[0].City;
        var file = new CacheFile
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
