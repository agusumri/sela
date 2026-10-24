using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sela.Core;

/// <summary>Thrown when the myquran.com API is unreachable or returns an unusable response.</summary>
public sealed class PrayerApiException : Exception
{
    public PrayerApiException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Client for the free myquran.com v2 prayer-time API (Kemenag/Bimas Islam data, ihtiyat included).</summary>
public sealed class MyQuranClient
{
    public static readonly Uri BaseAddress = new("https://api.myquran.com/v2/");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public MyQuranClient(HttpClient http, Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
    }

    /// <summary>Case-insensitive city search; queries shorter than 2 characters or with no match return empty.</summary>
    public async Task<IReadOnlyList<City>> SearchCitiesAsync(string query, CancellationToken ct)
    {
        var q = query?.Trim() ?? "";
        if (q.Length < 2) return Array.Empty<City>();

        var envelope = await GetAsync<List<CityDto>>($"sholat/kota/cari/{Uri.EscapeDataString(q)}", allowNotFound: true, ct).ConfigureAwait(false);
        if (envelope?.Data is not { } data) return Array.Empty<City>();
        return data
            .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Lokasi))
            .Select(c => new City(c.Id!.Trim(), c.Lokasi!.Trim()))
            .ToList();
    }

    /// <summary>All days of the month for the city. Throws <see cref="PrayerApiException"/> on any failure.</summary>
    public async Task<IReadOnlyList<DaySchedule>> GetMonthAsync(City city, int year, int month, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(city);
        if (string.IsNullOrWhiteSpace(city.Id) || !city.Id.Trim().All(char.IsAsciiDigit))
            throw new ArgumentException("City id must be numeric.", nameof(city));
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        var path = string.Create(CultureInfo.InvariantCulture, $"sholat/jadwal/{city.Id.Trim()}/{year:D4}/{month:D2}");
        var envelope = await GetAsync<MonthDto>(path, allowNotFound: false, ct).ConfigureAwait(false);
        var data = envelope?.Data ?? throw new PrayerApiException("Jadwal kosong dari API.");
        if (data.Jadwal is not { Count: > 0 } days) throw new PrayerApiException("Jadwal kosong dari API.");

        var resolved = new City(city.Id.Trim(),
            string.IsNullOrWhiteSpace(data.Lokasi) ? city.Name : data.Lokasi.Trim(),
            string.IsNullOrWhiteSpace(data.Daerah) ? city.Province : data.Daerah.Trim());

        var result = days.Select(d => ToSchedule(d, resolved))
            .Where(s => s.Date.Year == year && s.Date.Month == month)
            .OrderBy(s => s.Date)
            .ToList();
        if (result.Count == 0) throw new PrayerApiException("Jadwal tidak cocok dengan bulan yang diminta.");
        return result;
    }

    internal static DaySchedule ToSchedule(DayDto d, City city)
    {
        if (!DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new PrayerApiException($"Tanggal tidak valid: '{d.Date}'.");

        DateTime T(string? hm, string field) =>
            TimeOnly.TryParseExact(hm?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? date.ToDateTime(t, DateTimeKind.Unspecified)
                : throw new PrayerApiException($"Waktu {field} tidak valid pada {d.Date}: '{hm}'.");

        return new DaySchedule(date, city, new[]
        {
            new PrayerTime(Prayer.Imsak, T(d.Imsak, "imsak")),
            new PrayerTime(Prayer.Subuh, T(d.Subuh, "subuh")),
            new PrayerTime(Prayer.Terbit, T(d.Terbit, "terbit")),
            new PrayerTime(Prayer.Dhuha, T(d.Dhuha, "dhuha")),
            new PrayerTime(Prayer.Dzuhur, T(d.Dzuhur, "dzuhur")),
            new PrayerTime(Prayer.Ashar, T(d.Ashar, "ashar")),
            new PrayerTime(Prayer.Maghrib, T(d.Maghrib, "maghrib")),
            new PrayerTime(Prayer.Isya, T(d.Isya, "isya")),
        });
    }

    /// <summary>Returns null when status=false and <paramref name="allowNotFound"/>; otherwise throws.</summary>
    private async Task<Envelope<T>?> GetAsync<T>(string relativePath, bool allowNotFound, CancellationToken ct)
    {
        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, new Uri(BaseAddress, relativePath), Timeout, MaxBytes, "application/json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"myquran: {relativePath} gagal ({ex.GetType().Name}: {ex.Message})");
            throw new PrayerApiException("Tidak dapat menghubungi api.myquran.com.", ex);
        }

        Envelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<T>>(text, Json);
        }
        catch (JsonException ex)
        {
            _log?.Invoke($"myquran: {relativePath} JSON tidak valid ({ex.Message})");
            throw new PrayerApiException("Respons API tidak valid.", ex);
        }

        if (envelope is null) throw new PrayerApiException("Respons API kosong.");
        if (!envelope.Status)
        {
            if (allowNotFound) return null;
            throw new PrayerApiException($"API menolak permintaan: {envelope.Message ?? "status false"}.");
        }
        return envelope;
    }

    // ---- DTOs (internal; also used by the disk cache) ----

    internal sealed class Envelope<T>
    {
        public bool Status { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    internal sealed class CityDto
    {
        public string? Id { get; set; }
        public string? Lokasi { get; set; }
    }

    internal sealed class MonthDto
    {
        public long Id { get; set; }
        public string? Lokasi { get; set; }
        public string? Daerah { get; set; }
        public List<DayDto>? Jadwal { get; set; }
    }

    internal sealed class DayDto
    {
        public string? Date { get; set; }
        public string? Imsak { get; set; }
        public string? Subuh { get; set; }
        public string? Terbit { get; set; }
        public string? Dhuha { get; set; }
        public string? Dzuhur { get; set; }
        public string? Ashar { get; set; }
        public string? Maghrib { get; set; }
        public string? Isya { get; set; }
    }
}
