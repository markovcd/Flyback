using System.Text.Json.Nodes;

namespace Flyback.Plugins.Testing;

/// <summary>
/// An endpoint that answers each request with the next canned answer, and keeps what
/// it was sent. The last answer repeats once they run out, so a test that wants a
/// refusal to keep happening only has to say it once.
/// </summary>
public sealed class Canned(params CannedAnswer[] answers) : HttpMessageHandler
{
    private int next;

    /// <summary>Every request, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Each request's body, empty where it had none.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Each request's body read as JSON.</summary>
    public List<JsonNode> Sent => [.. Bodies.Select(body => JsonNode.Parse(body)!)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var answer = answers[Math.Min(next++, answers.Length - 1)];
        var response = new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body) };

        foreach (var (name, value) in answer.Headers ?? [])
            response.Headers.TryAddWithoutValidation(name, value);

        return response;
    }
}
