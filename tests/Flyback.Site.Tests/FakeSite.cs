using System.Net;
using System.Text;

namespace Flyback.Site.Tests;

/// <summary>The preset site's admin API as a test scripts it, keeping every request it was sent.</summary>
internal sealed class FakeSite(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Body)> Asked { get; } = [];

    public HttpClient Client() => new(this) { BaseAddress = new Uri("https://presets.example.org/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.PathAndQuery;

        lock (Asked) Asked.Add((request.Method, path, body));

        return answer(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);
}
