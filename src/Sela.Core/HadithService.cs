using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sela.Core;

/// <summary>Reads hadith text from api.myquran.com (Arbain Nawawi, 42 hadith).</summary>
public sealed partial class HadithClient
{
    public const int ArbainCount = 42;
    public const string ArbainCollection = "Arbain Nawawi";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const long MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public HadithClient(HttpClient http, Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
    }

    /// <summary>Gets Arbain hadith number 1..42. Throws PrayerApiException on a bad response.</summary>
    public async Task<Hadith> GetArbainAsync(int number, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, ArbainCount);

        var path = string.Create(CultureInfo.InvariantCulture, $"hadits/arbain/{number}");
        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, new Uri(MyQuranClient.BaseAddress, path), Timeout, MaxBytes, "application/json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"myquran: {path} gagal ({ex.GetType().Name}: {ex.Message})");
            throw new PrayerApiException("Tidak dapat menghubungi api.myquran.com.", ex);
        }

        MyQuranClient.Envelope<HadithDto>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MyQuranClient.Envelope<HadithDto>>(text, Json);
        }
        catch (JsonException ex)
        {
            _log?.Invoke($"myquran: {path} JSON tidak valid ({ex.Message})");
            throw new PrayerApiException("Respons API tidak valid.", ex);
        }

        if (envelope is null) throw new PrayerApiException("Respons API kosong.");
        if (!envelope.Status) throw new PrayerApiException($"API menolak permintaan: {envelope.Message ?? "status false"}.");
        var d = envelope.Data ?? throw new PrayerApiException("Hadits kosong dari API.");

        var arabic = d.Arab?.Trim() ?? "";
        var indonesian = Whitespace().Replace(d.Indo ?? "", " ").Trim();
        if (arabic.Length == 0 || indonesian.Length == 0) throw new PrayerApiException("Teks hadits kosong dari API.");
        if (d.No is { } no && (!int.TryParse(no.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n != number))
            throw new PrayerApiException($"Nomor hadits tidak cocok: '{no}'.");

        return new Hadith(ArbainCollection, number, d.Judul?.Trim() ?? "", arabic, indonesian, default);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    internal sealed class HadithDto
    {
        public string? Arab { get; set; }
        public string? Indo { get; set; }
        public string? Judul { get; set; }
        public string? No { get; set; }
    }
}

/// <summary>Picks one hadith per day (deterministic by date) and caches it on disk for offline use.</summary>
public sealed class HadithService
{
    /// <summary>After a failed API call the API is not retried for this long (avoids hammering from UI timers).</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(1);
    public const int MaxCachedFiles = 7;

    private const string FilePrefix = "hadith-";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly HadithClient _client;
    private readonly string _cacheDir;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Hadith? _today;
    private DateTimeOffset? _lastFailure;

    public HadithService(HadithClient client, string? cacheDir = null, Action<string>? log = null, TimeProvider? time = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cacheDir = cacheDir ?? PrayerScheduleService.DefaultCacheDir;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Today's hadith; null when offline and nothing is cached. Never throws except on cancellation.</summary>
    public async Task<Hadith?> GetTodayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_today is { } mem && mem.Date == today) return mem;

            var path = PathFor(today);
            if (ReadCache(path) is { } disk && disk.Date == today) return _today = disk;

            var now = _time.GetUtcNow();
            if (_lastFailure is { } failedAt && now - failedAt < RetryBackoff) return MostRecentCached();

            try
            {
                var fetched = await _client.GetArbainAsync(NumberFor(today), ct).ConfigureAwait(false) with { Date = today };
                _lastFailure = null;
                WriteCache(path, fetched);
                return _today = fetched;
            }
            catch (PrayerApiException ex)
            {
                _lastFailure = now;
                var fallback = MostRecentCached();
                _log?.Invoke($"Hadits {today:yyyy-MM-dd}: {ex.Message}{(fallback is null ? "" : $" (memakai hadits {fallback.Date:yyyy-MM-dd})")}");
                return fallback;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Invoke($"Hadits gagal dimuat: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Arbain number used for a date (1..42).</summary>
    public static int NumberFor(DateOnly date) => date.DayNumber % HadithClient.ArbainCount + 1;

    // ---- disk cache ----

    private string PathFor(DateOnly date) =>
        Path.Combine(_cacheDir, FilePrefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    /// <summary>Cache files newest first (the yyyy-MM-dd file name sorts chronologically).</summary>
    private List<string> CacheFiles()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return new();
            return Directory.EnumerateFiles(_cacheDir, FilePrefix + "????-??-??.json")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Cache hadits tidak terbaca: {ex.Message}");
            return new();
        }
    }

    private Hadith? MostRecentCached()
    {
        foreach (var file in CacheFiles())
            if (ReadCache(file) is { } h) return h;
        return null;
    }

    private Hadith? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var h = JsonSerializer.Deserialize<Hadith>(File.ReadAllText(path), Json);
            if (h is null || h.Number is < 1 or > HadithClient.ArbainCount
                || string.IsNullOrWhiteSpace(h.Arabic) || string.IsNullOrWhiteSpace(h.Indonesian)) return null;
            return h with { Collection = h.Collection ?? HadithClient.ArbainCollection, Title = h.Title ?? "" };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log?.Invoke($"Cache hadits rusak, diabaikan: {Path.GetFileName(path)} ({ex.Message})");
            return null;
        }
    }

    private void WriteCache(string path, Hadith hadith)
    {
        try
        {
            SafeIo.WriteAllTextAtomic(path, JsonSerializer.Serialize(hadith, Json));
            foreach (var old in CacheFiles().Skip(MaxCachedFiles)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Gagal menyimpan cache hadits: {ex.Message}");
        }
    }
}
