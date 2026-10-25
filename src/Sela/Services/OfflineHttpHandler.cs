using System.Net.Http;

namespace Sela.Services;

/// <summary>Debug (--offline): every HTTP request fails immediately, without touching the network.</summary>
public sealed class OfflineHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Host only: full URLs can carry secrets (the private iCal address).
        Log.Write($"--offline: HTTP ke {request.RequestUri?.Host ?? "?"} diblokir");
        throw new HttpRequestException("Offline (--offline)");
    }
}
