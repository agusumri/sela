using System.Net;
using System.Text;

namespace Sela.Core.Tests;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        var r = respond(request);
        r.RequestMessage ??= request;
        return Task.FromResult(r);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal static class MyQuranSamples
{
    public const string JakartaOct2026 = """
        {"status":true,"request":{"path":"/sholat/jadwal/1301/2026/10"},"data":{"id":1301,"lokasi":"KOTA JAKARTA","daerah":"DKI JAKARTA","jadwal":[
        {"tanggal":"Sabtu, 24/10/2026","imsak":"04:02","subuh":"04:12","terbit":"05:25","dhuha":"05:53","dzuhur":"11:40","ashar":"14:51","maghrib":"17:49","isya":"18:59","date":"2026-10-24"},
        {"tanggal":"Minggu, 25/10/2026","imsak":"04:02","subuh":"04:12","terbit":"05:25","dhuha":"05:53","dzuhur":"11:40","ashar":"14:52","maghrib":"17:49","isya":"18:59","date":"2026-10-25"}]}}
        """;

    public const string JakartaNov2026 = """
        {"status":true,"data":{"id":1301,"lokasi":"KOTA JAKARTA","daerah":"DKI JAKARTA","jadwal":[
        {"tanggal":"Minggu, 01/11/2026","imsak":"03:58","subuh":"04:08","terbit":"05:22","dhuha":"05:50","dzuhur":"11:39","ashar":"14:58","maghrib":"17:51","isya":"19:03","date":"2026-11-01"}]}}
        """;

    public const string Search = """{"status":true,"request":{"path":"/sholat/kota/cari/jakarta"},"data":[{"id":"1301","lokasi":"KOTA JAKARTA"},{"id":"1302","lokasi":"KAB. KEPULAUAN SERIBU"}]}""";

    public const string NotFound = """{"status":false,"message":"data not found","request":{"path":"/sholat/kota/cari/xyzqq","keyword":"xyzqq"}}""";

    /// <summary>Routes /jadwal/.../2026/10 and /2026/11 to the samples; anything else 400.</summary>
    public static HttpResponseMessage Route(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.EndsWith("/2026/10")) return StubHandler.Json(JakartaOct2026);
        if (path.EndsWith("/2026/11")) return StubHandler.Json(JakartaNov2026);
        return StubHandler.Json("""{"status":false,"message":"bad request"}""", HttpStatusCode.BadRequest);
    }
}
