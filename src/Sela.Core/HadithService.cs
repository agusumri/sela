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

        return new Hadith(ArbainCollection, number, d.Judul?.Trim() ?? "", arabic, indonesian, AppLanguage.Indonesian, default);
    }

    public const string EnglishCollection = "Forty Hadith of an-Nawawi";
    /// <summary>fawazahmed0/hadith-api (Unlicense / public domain), served by jsDelivr.</summary>
    public static readonly Uri EditionsAddress = new("https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions/");

    /// <summary>Gets an-Nawawi hadith 1..42 in English with its Arabic text. Throws PrayerApiException on a bad response.</summary>
    public async Task<Hadith> GetNawawiEnglishAsync(int number, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, ArbainCount);

        var english = GetEditionTextAsync("eng-nawawi", number, ct);
        var arabic = GetEditionTextAsync("ara-nawawi", number, ct);
        await Task.WhenAll(english, arabic).ConfigureAwait(false);
        return new Hadith(EnglishCollection, number, string.Create(CultureInfo.InvariantCulture, $"Hadith {number}"),
            arabic.Result, english.Result, AppLanguage.English, default);
    }

    private async Task<string> GetEditionTextAsync(string edition, int number, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"{edition}/{number}.json");
        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, new Uri(EditionsAddress, path), Timeout, MaxBytes, "application/json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"hadith-api: {path} failed ({ex.GetType().Name}: {ex.Message})");
            throw new PrayerApiException("Cannot reach cdn.jsdelivr.net.", ex);
        }

        EditionDto? dto;
        try { dto = JsonSerializer.Deserialize<EditionDto>(text, Json); }
        catch (JsonException ex)
        {
            _log?.Invoke($"hadith-api: {path} invalid JSON ({ex.Message})");
            throw new PrayerApiException("Invalid hadith response.", ex);
        }

        var h = dto?.Hadiths?.FirstOrDefault(x => x is not null && x.Hadithnumber == number)
            ?? throw new PrayerApiException($"Hadith {number} missing from {edition}.");
        var clean = Whitespace().Replace(h.Text ?? "", " ").Trim();
        if (clean.Length == 0) throw new PrayerApiException($"Empty hadith text from {edition}.");
        return clean;
    }

    internal sealed class EditionDto { public List<EditionHadithDto?>? Hadiths { get; set; } }

    internal sealed class EditionHadithDto
    {
        public decimal Hadithnumber { get; set; }
        public string? Text { get; set; }
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly HadithClient _client;
    private readonly string _cacheDir;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<AppLanguage, Hadith> _today = new();
    private readonly Dictionary<AppLanguage, DateTimeOffset> _lastFailure = new();

    /// <summary>"hadith-" for Indonesian (name kept for existing caches), "hadith-en-" for English.</summary>
    private static string PrefixFor(AppLanguage language) => language == AppLanguage.English ? "hadith-en-" : "hadith-";

    public HadithService(HadithClient client, string? cacheDir = null, Action<string>? log = null, TimeProvider? time = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cacheDir = cacheDir ?? PrayerScheduleService.DefaultCacheDir;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Today's hadith in <paramref name="language"/>; null when offline and nothing in that language is cached.
    /// Both languages show the same hadith number on a given day. Never throws except on cancellation.
    /// </summary>
    public async Task<Hadith?> GetTodayAsync(CancellationToken ct, AppLanguage language = AppLanguage.Indonesian)
    {
        if (!Enum.IsDefined(language)) language = AppLanguage.Indonesian;
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_today.TryGetValue(language, out var mem) && mem.Date == today) return mem;

            var path = PathFor(language, today);
            if (ReadCache(path, language) is { } disk && disk.Date == today) return _today[language] = disk;

            var now = _time.GetUtcNow();
            if (_lastFailure.TryGetValue(language, out var failedAt) && now - failedAt < RetryBackoff) return MostRecentCached(language);

            try
            {
                var number = NumberFor(today);
                var fetched = language == AppLanguage.English
                    ? await _client.GetNawawiEnglishAsync(number, ct).ConfigureAwait(false)
                    : await _client.GetArbainAsync(number, ct).ConfigureAwait(false);
                fetched = fetched with { Date = today };
                _lastFailure.Remove(language);
                WriteCache(path, fetched);
                return _today[language] = fetched;
            }
            catch (PrayerApiException ex)
            {
                _lastFailure[language] = now;
                var fallback = MostRecentCached(language);
                _log?.Invoke($"Hadits {language} {today:yyyy-MM-dd}: {ex.Message}{(fallback is null ? "" : $" (memakai hadits {fallback.Date:yyyy-MM-dd})")}");
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

    private string PathFor(AppLanguage language, DateOnly date) =>
        Path.Combine(_cacheDir, PrefixFor(language) + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    /// <summary>Cache files of one language, newest first (the yyyy-MM-dd file name sorts chronologically).</summary>
    private List<string> CacheFiles(AppLanguage language)
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return new();
            // "hadith-????-??-??.json" cannot match "hadith-en-2026-10-24.json" (length differs), so languages stay apart.
            return Directory.EnumerateFiles(_cacheDir, PrefixFor(language) + "????-??-??.json")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Cache hadits tidak terbaca: {ex.Message}");
            return new();
        }
    }

    private Hadith? MostRecentCached(AppLanguage language)
    {
        foreach (var file in CacheFiles(language))
            if (ReadCache(file, language) is { } h) return h;
        return null;
    }

    private Hadith? ReadCache(string path, AppLanguage language)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            var h = JsonSerializer.Deserialize<Hadith>(text, Json);
            if (h is null) return null;
            // Files written before the Translation rename stored the text as "Indonesian" (and had no Language = Indonesian).
            if (string.IsNullOrWhiteSpace(h.Translation)
                && JsonDocument.Parse(text).RootElement.TryGetProperty("Indonesian", out var legacy) && legacy.ValueKind == JsonValueKind.String)
                h = h with { Translation = legacy.GetString()! };
            if (h.Language != language || h.Number is < 1 or > HadithClient.ArbainCount
                || string.IsNullOrWhiteSpace(h.Arabic) || string.IsNullOrWhiteSpace(h.Translation)) return null;
            var collection = language == AppLanguage.English ? HadithClient.EnglishCollection : HadithClient.ArbainCollection;
            return h with { Collection = h.Collection ?? collection, Title = h.Title ?? "" };
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
            foreach (var old in CacheFiles(hadith.Language).Skip(MaxCachedFiles)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Gagal menyimpan cache hadits: {ex.Message}");
        }
    }
}
