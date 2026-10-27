using System.Globalization;
using System.Text.Json;

namespace Sela.Core;

public sealed record UpdateInfo(Version Version, Uri ReleaseUrl);

/// <summary>Asks GitHub whether a newer Sela release than the running one is published.</summary>
public sealed class UpdateChecker
{
    public static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/agusumri/sela/releases/latest");
    private const string ReleasesPathPrefix = "/agusumri/sela/releases/";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const long MaxBytes = 512 * 1024;

    // Per-request headers: GitHub rejects requests without a User-Agent.
    private static readonly KeyValuePair<string, string>[] Headers =
    {
        new("User-Agent", "Sela-UpdateChecker"),
        new("X-GitHub-Api-Version", "2022-11-28"),
    };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public UpdateChecker(HttpClient http, Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
    }

    /// <summary>
    /// Returns the latest release if strictly newer than current; null if not newer, or on ANY failure
    /// (never throws except OperationCanceledException when ct is cancelled).
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        string text;
        try
        {
            text = await SafeIo.GetStringAsync(_http, LatestReleaseApi, Timeout, MaxBytes, "application/vnd.github+json", ct, Headers)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log?.Invoke($"update: check failed ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException(ct);
        }

        try
        {
            var info = Parse(text, current);
            if (info is null) _log?.Invoke("update: no newer release");
            return info;
        }
        catch (Exception ex)
        {
            // Parse is designed not to throw; this guards the "never throws" contract.
            _log?.Invoke($"update: parse failed ({ex.GetType().Name})");
            return null;
        }
    }

    /// <summary>
    /// Parses GitHub "releases/latest" JSON; returns null when invalid, draft, prerelease,
    /// unparsable tag, or not newer than <paramref name="current"/>.
    /// </summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        if (string.IsNullOrWhiteSpace(json) || current is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (ParseTag(tag) is not { } version) return null;
            if (Normalize(version) <= Normalize(current)) return null;

            var html = root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            return new UpdateInfo(version, SafeReleaseUrl(html, version));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" / "1.2.3" -> Version(1,2,3); null otherwise (suffixes like "-beta" are rejected).</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4) return null;
        var nums = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            // Digits only: rejects "-beta", "+build", signs, whitespace and empty parts.
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return null;
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return null;
        }
        return nums.Length switch
        {
            2 => new Version(nums[0], nums[1]),
            3 => new Version(nums[0], nums[1], nums[2]),
            _ => new Version(nums[0], nums[1], nums[2], nums[3]),
        };
    }

    /// <summary>Major.Minor.Build with missing parts as 0 (so 1.1.4 == 1.1.4.0).</summary>
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    /// <summary>Only https://github.com/agusumri/sela/releases/... is trusted; anything else falls back to the tag page.</summary>
    private static Uri SafeReleaseUrl(string? html, Version version)
    {
        if (Uri.TryCreate(html, UriKind.Absolute, out var u)
            && u.Scheme == Uri.UriSchemeHttps
            && string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && u.IsDefaultPort
            && string.IsNullOrEmpty(u.UserInfo)
            && u.AbsolutePath.StartsWith(ReleasesPathPrefix, StringComparison.OrdinalIgnoreCase))
            return u;
        return new Uri($"https://github.com/agusumri/sela/releases/tag/v{version}");
    }
}
