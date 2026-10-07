using System.Net;

namespace Flyback.Plugins.SystemOne.Tests;

/// <summary>Answers each request with the next status and body, and keeps what was sent.</summary>
internal sealed class Canned(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
{
    private int next;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));

        var (status, body) = answers[Math.Min(next++, answers.Length - 1)];

        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}
