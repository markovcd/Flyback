using System.Net.Http.Headers;

namespace Flyback.Plugins.Assist;

/// <summary>What an endpoint answered one request with (<see cref="IAssistantTransport.Send"/>).</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body, as text.</param>
/// <param name="Headers">The response's headers, on a message of their own that leads back to no request.</param>
public sealed record AssistantResponse(int Status, string Body, HttpResponseHeaders Headers)
{
    /// <summary>Whether the status is a 2xx.</summary>
    public bool Succeeded => Status is >= 200 and < 300;
}
