namespace Flyback.Editor.Tests;

/// <summary>A network with nothing on the other end.</summary>
internal sealed class Unreachable : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("No connection could be made.");
}
