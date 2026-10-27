using System.Net;

namespace Sela.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 1, 4);

    private static string Release(string? tag = "v1.2.0", string? html = "https://github.com/agusumri/sela/releases/tag/v1.2.0",
        bool draft = false, bool prerelease = false)
    {
        var tagJson = tag is null ? "" : $"\"tag_name\":\"{tag}\",";
        var htmlJson = html is null ? "" : $"\"html_url\":\"{html}\",";
        return $"{{{tagJson}{htmlJson}\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(prerelease ? "true" : "false")}}}";
    }

    // ---- ParseTag ----

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData(" v1.2.3 ", 1, 2, 3)]
    public void ParseTag_three_parts(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateChecker.ParseTag(tag));

    [Fact]
    public void ParseTag_two_parts() => Assert.Equal(new Version(1, 2), UpdateChecker.ParseTag("v1.2"));

    [Fact]
    public void ParseTag_four_parts() => Assert.Equal(new Version(1, 2, 3, 4), UpdateChecker.ParseTag("1.2.3.4"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("v1.2.3-beta")]
    [InlineData("1.2.3+build")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1..2")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("release")]
    [InlineData("99999999999.0.0")]
    public void ParseTag_rejects(string? tag) => Assert.Null(UpdateChecker.ParseTag(tag));

    // ---- Parse ----

    [Fact]
    public void Parse_newer_returns_release()
    {
        var info = UpdateChecker.Parse(Release(), Current);
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 2, 0), info!.Version);
        Assert.Equal("https://github.com/agusumri/sela/releases/tag/v1.2.0", info.ReleaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_equal_is_null() => Assert.Null(UpdateChecker.Parse(Release("v1.1.4"), Current));

    [Fact]
    public void Parse_equal_with_revision_part_is_null() =>
        Assert.Null(UpdateChecker.Parse(Release("v1.1.4"), new Version(1, 1, 4, 0)));

    [Fact]
    public void Parse_two_part_current_compares_as_zero_build() =>
        Assert.NotNull(UpdateChecker.Parse(Release("v1.1.1"), new Version(1, 1)));

    [Fact]
    public void Parse_older_is_null() => Assert.Null(UpdateChecker.Parse(Release("v1.0.9"), Current));

    [Fact]
    public void Parse_draft_is_null() => Assert.Null(UpdateChecker.Parse(Release(draft: true), Current));

    [Fact]
    public void Parse_prerelease_is_null() => Assert.Null(UpdateChecker.Parse(Release(prerelease: true), Current));

    [Fact]
    public void Parse_missing_tag_is_null() => Assert.Null(UpdateChecker.Parse(Release(tag: null), Current));

    [Fact]
    public void Parse_suffixed_tag_is_null() => Assert.Null(UpdateChecker.Parse(Release("v1.2.0-rc1"), Current));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"tag_name\":123}")]
    public void Parse_bad_json_is_null(string json) => Assert.Null(UpdateChecker.Parse(json, Current));

    [Theory]
    [InlineData("https://evil.example/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("https://github.com.evil.example/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("https://github.com/attacker/sela/releases/tag/v1.2.0")]
    [InlineData("https://github.com/agusumri/sela/archive/v1.2.0.zip")]
    [InlineData("https://github.com/agusumri/selaX/releases/tag/v1.2.0")]
    [InlineData("https://user@github.com/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("https://github.com:8443/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("http://github.com/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/agusumri/sela/releases/tag/v1.2.0")]
    [InlineData("")]
    public void Parse_untrusted_html_url_falls_back(string html)
    {
        var info = UpdateChecker.Parse(Release(html: html), Current);
        Assert.Equal("https://github.com/agusumri/sela/releases/tag/v1.2.0", info!.ReleaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_missing_html_url_falls_back()
    {
        var info = UpdateChecker.Parse(Release("1.3.0", html: null), Current);
        Assert.Equal("https://github.com/agusumri/sela/releases/tag/v1.3.0", info!.ReleaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_trusted_url_is_case_insensitive()
    {
        var info = UpdateChecker.Parse(Release(html: "https://GitHub.com/AgusUmri/Sela/Releases/tag/v1.2.0"), Current);
        Assert.Equal("github.com", info!.ReleaseUrl.Host);
        Assert.StartsWith("/AgusUmri/Sela/Releases/", info.ReleaseUrl.AbsolutePath);
    }

    // ---- CheckAsync ----

    [Fact]
    public async Task CheckAsync_newer_returns_release_and_sends_github_headers()
    {
        HttpRequestMessage? seen = null;
        string? ua = null, accept = null;
        var handler = new StubHandler(r =>
        {
            seen = r;
            ua = string.Join(" ", r.Headers.UserAgent);
            accept = string.Join(",", r.Headers.Accept);
            return StubHandler.Json(Release());
        });
        using var http = new HttpClient(handler);

        var info = await new UpdateChecker(http).CheckAsync(Current, CancellationToken.None);

        Assert.Equal(new Version(1, 2, 0), info!.Version);
        Assert.Equal(UpdateChecker.LatestReleaseApi, seen!.RequestUri);
        Assert.False(string.IsNullOrWhiteSpace(ua));
        Assert.Contains("application/vnd.github+json", accept);
        Assert.Empty(http.DefaultRequestHeaders.UserAgent); // shared client untouched
    }

    [Fact]
    public async Task CheckAsync_http_500_is_null_and_logged()
    {
        var logs = new List<string>();
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json("{\"message\":\"secret body\"}", HttpStatusCode.InternalServerError)));

        var info = await new UpdateChecker(http, logs.Add).CheckAsync(Current, CancellationToken.None);

        Assert.Null(info);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, l => l.Contains("secret body"));
    }

    [Fact]
    public async Task CheckAsync_timeout_is_null()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new TaskCanceledException("timed out")));
        Assert.Null(await new UpdateChecker(http).CheckAsync(Current, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_network_exception_is_null()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("no route")));
        Assert.Null(await new UpdateChecker(http).CheckAsync(Current, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_bad_json_is_null()
    {
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json("<html>")));
        Assert.Null(await new UpdateChecker(http).CheckAsync(Current, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_cancelled_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json(Release())));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new UpdateChecker(http).CheckAsync(Current, cts.Token));
    }

    [Fact]
    public async Task CheckAsync_cancelled_mid_request_throws()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new StubHandler(_ =>
        {
            cts.Cancel();
            throw new HttpRequestException("aborted");
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new UpdateChecker(http).CheckAsync(Current, cts.Token));
    }
}
