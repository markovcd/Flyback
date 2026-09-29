namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant sends its requests over. The host builds each request, puts the
/// key on it where it goes to the one origin the key is bound to, and hands back only
/// what came back; the assistant never holds the key or a request that carries it.
/// </summary>
/// <remarks>
/// A key is bound to where the assistant said it would send it (<see cref="IPatchAssistant.Endpoint"/>)
/// when it was entered, so pointing an endpoint elsewhere afterwards sends it nowhere new.
/// </remarks>
public interface IAssistantTransport
{
    /// <summary>Whether there is a key to send.</summary>
    bool HasKey { get; }

    /// <summary>Where the key is sent, as <c>scheme://host[:port]</c>, or null without one.</summary>
    string? Origin { get; }

    /// <summary>
    /// Sends one request, once: a POST of <paramref name="json"/>, or a GET where it is
    /// null. The key goes on it where <paramref name="address"/> is on <see cref="Origin"/>.
    /// </summary>
    /// <returns>What came back, whatever its status.</returns>
    /// <exception cref="HttpRequestException">Nothing came back.</exception>
    Task<AssistantResponse> Send(Uri address, string? json, CancellationToken cancel);
}
