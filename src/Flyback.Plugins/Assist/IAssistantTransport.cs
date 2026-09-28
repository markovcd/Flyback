namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant sends its requests over. The host holds the key and puts it on
/// each request to the one origin it is bound to; the assistant never sees it.
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
    /// Sends over the network, adding the key to each request to <see cref="Origin"/> and
    /// to no other. Owned by the host: build an <see cref="HttpClient"/> over it with
    /// <c>disposeHandler: false</c>.
    /// </summary>
    HttpMessageHandler Handler { get; }
}
