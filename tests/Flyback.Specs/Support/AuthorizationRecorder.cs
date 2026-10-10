using System.Net;

namespace Flyback.Specs.Support;

/// <summary>The network, as far as the key is concerned: what each request carried in its Authorization header.</summary>
internal sealed class AuthorizationRecorder(List<string?> carried) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        carried.Add(request.Headers.Authorization?.ToString());

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
