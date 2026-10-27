namespace Sela.Core;

internal static class SafeIo
{
    /// <summary>GET <paramref name="uri"/> over https with a timeout and a hard size cap. Throws on any failure.</summary>
    /// <param name="headers">Optional extra per-request headers (never touches the shared client's defaults).</param>
    public static async Task<string> GetStringAsync(HttpClient http, Uri uri, TimeSpan timeout, long maxBytes, string? accept, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("Only https addresses are allowed.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        if (headers is not null)
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
            throw new HttpRequestException("Redirected to a non-https address.");
        var tooLarge = $"Response exceeds {maxBytes / (1024 * 1024)} MB.";
        if (response.Content.Headers.ContentLength > maxBytes) throw new HttpRequestException(tooLarge);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new HttpRequestException(tooLarge);
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes text to a temp file then moves it over the target, so readers never see a partial file.</summary>
    public static void WriteAllTextAtomic(string path, string text)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
