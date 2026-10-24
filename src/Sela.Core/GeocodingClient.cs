using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sela.Core;

/// <summary>Worldwide place search via the free Open-Meteo geocoding API (GeoNames data, no key).</summary>
public sealed class GeocodingClient
{
    public static readonly Uri BaseAddress = new("https://geocoding-api.open-meteo.com/v1/");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const long MaxBytes = 512 * 1024;
    public const int MaxResults = 10;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public GeocodingClient(HttpClient http, Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
    }

    /// <summary>Places matching the query (world cities, Id "geo:{geonamesId}"). Shorter than 2 chars or no match = empty.</summary>
    public async Task<IReadOnlyList<City>> SearchAsync(string query, AppLanguage language, CancellationToken ct) =>
        (await SearchWithCountryAsync(query, language, ct).ConfigureAwait(false)).Select(r => r.City).ToList();

    /// <summary>Same as <see cref="SearchAsync"/> plus the ISO country code (e.g. "GB") of each result.</summary>
    internal async Task<IReadOnlyList<(City City, string? CountryCode)>> SearchWithCountryAsync(string query, AppLanguage language, CancellationToken ct)
    {
        var q = query?.Trim() ?? "";
        if (q.Length < 2) return Array.Empty<(City, string?)>();

        var lang = language == AppLanguage.Indonesian ? "id" : "en";
        var path = string.Create(CultureInfo.InvariantCulture,
            $"search?name={Uri.EscapeDataString(q)}&count={MaxResults}&language={lang}&format=json");

        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, new Uri(BaseAddress, path), Timeout, MaxBytes, "application/json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"geocoding: search failed ({ex.GetType().Name}: {ex.Message})");
            throw new PrayerApiException("Cannot reach geocoding-api.open-meteo.com.", ex);
        }

        SearchDto? dto;
        try { dto = JsonSerializer.Deserialize<SearchDto>(text, Json); }
        catch (JsonException ex)
        {
            _log?.Invoke($"geocoding: invalid JSON ({ex.Message})");
            throw new PrayerApiException("Invalid response from the geocoding API.", ex);
        }

        if (dto?.Results is not { } results) return Array.Empty<(City, string?)>();
        var list = new List<(City, string?)>();
        foreach (var r in results)
        {
            if (r is null || r.Id <= 0 || Clean(r.Name) is not { } name
                || r.Latitude is not { } lat || r.Longitude is not { } lng
                || !double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
                continue;
            var city = new City(
                string.Create(CultureInfo.InvariantCulture, $"geo:{r.Id}"),
                name,
                Clean(r.Admin1),
                lat, lng,
                Clean(r.Timezone),
                Clean(r.Country));
            list.Add((city, Clean(r.Country_code)?.ToUpperInvariant()));
            if (list.Count == MaxResults) break;
        }
        return list;
    }

    /// <summary>Longest place-name part kept from the geocoder (names are shown in the UI and stored in settings).</summary>
    internal const int MaxNameLength = 100;

    /// <summary>
    /// Untrusted geocoder text: drops control and Unicode format characters (e.g. bidi overrides that could
    /// spoof the label), collapses whitespace, caps the length. Null when nothing readable is left.
    /// </summary>
    internal static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, MaxNameLength));
        bool space = false;
        foreach (var c in s)
        {
            // Whitespace first: tab and newline are also control characters, but they separate words.
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
            if (sb.Length >= MaxNameLength) break;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private sealed class SearchDto { public List<ResultDto?>? Results { get; set; } }

    private sealed class ResultDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        [JsonPropertyName("country_code")] public string? Country_code { get; set; }
        public string? Timezone { get; set; }
        public string? Country { get; set; }
        public string? Admin1 { get; set; }
    }
}

/// <summary>
/// One search box for every place: Indonesian Kemenag cities first, then worldwide places outside Indonesia.
/// One source failing does not hide the other; throws only when both fail.
/// </summary>
public sealed class LocationSearch
{
    public const int MaxResults = 15;

    private readonly MyQuranClient _kemenag;
    private readonly GeocodingClient _world;
    private readonly Action<string>? _log;

    public LocationSearch(MyQuranClient kemenag, GeocodingClient world, Action<string>? log = null)
    {
        _kemenag = kemenag ?? throw new ArgumentNullException(nameof(kemenag));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public async Task<IReadOnlyList<City>> SearchAsync(string query, AppLanguage language, CancellationToken ct)
    {
        var q = query?.Trim() ?? "";
        if (q.Length < 2) return Array.Empty<City>();

        var kemenagTask = _kemenag.SearchCitiesAsync(q, ct);
        var worldTask = _world.SearchWithCountryAsync(q, language, ct);
        try { await Task.WhenAll(kemenagTask, worldTask).ConfigureAwait(false); }
        catch { /* inspected per task below */ }
        ct.ThrowIfCancellationRequested();

        var result = new List<City>();
        Exception? kemenagError = null, worldError = null;

        if (kemenagTask.IsCompletedSuccessfully) result.AddRange(kemenagTask.Result);
        else
        {
            kemenagError = kemenagTask.Exception?.GetBaseException() ?? new OperationCanceledException();
            _log?.Invoke($"Location search (Kemenag) failed: {kemenagError.Message}");
        }

        if (worldTask.IsCompletedSuccessfully)
            result.AddRange(worldTask.Result
                .Where(r => !string.Equals(r.CountryCode, "ID", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.City));
        else
        {
            worldError = worldTask.Exception?.GetBaseException() ?? new OperationCanceledException();
            _log?.Invoke($"Location search (world) failed: {worldError.Message}");
        }

        if (kemenagError is not null && worldError is not null)
            throw new PrayerApiException("Location search failed.", new AggregateException(kemenagError, worldError));

        return result.Take(MaxResults).ToList();
    }
}
