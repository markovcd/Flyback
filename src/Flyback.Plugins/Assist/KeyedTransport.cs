namespace Flyback.Plugins.Assist;

/// <summary>The host's <see cref="IAssistantTransport"/>: a key bound to one origin, put on requests there and nowhere else.</summary>
internal sealed class KeyedTransport : IAssistantTransport
{
    /// <summary>
    /// Shared by every transport, so connections are pooled across runs. No redirects: a
    /// header of the provider's own naming would follow one to another host.
    /// </summary>
    private static readonly SocketsHttpHandler Network = new()
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    public static KeyedTransport None { get; } = new(null, null, new AssistantCredential("", ""));

    private readonly string? secret;

    /// <param name="inner">Where requests go once signed; the network unless a test says otherwise.</param>
    public KeyedTransport(string? secret, string? origin, AssistantCredential credential, HttpMessageHandler? inner = null)
    {
        this.secret = string.IsNullOrWhiteSpace(secret) || origin is null ? null : secret;
        Origin = this.secret is null ? null : origin;
        Handler = new Signer(this, credential, inner ?? Network);
    }

    public bool HasKey => secret is not null;

    public string? Origin { get; }

    public HttpMessageHandler Handler { get; }

    /// <summary><c>scheme://host[:port]</c>, the port only where it is not the scheme's own.</summary>
    public static string OriginOf(Uri address) => address.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    /// <summary>The origin of what <paramref name="assistant"/> sends to configured as <paramref name="values"/>, or null where that is no address.</summary>
    public static string? OriginOf(IPatchAssistant assistant, Settings.SettingValues values)
    {
        try
        {
            return assistant.Endpoint(values) is { IsAbsoluteUri: true } address ? OriginOf(address) : null;
        }
        catch
        {
            return null;
        }
    }

    public override string ToString() => HasKey ? $"a key for {Origin}" : "no key";

    /// <summary>Never disposes the network under it: an assistant that disposes its client must not take every other one's with it.</summary>
    private sealed class Signer(KeyedTransport transport, AssistantCredential credential, HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker network = new(inner, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(credential.Header);

            if (transport.secret is { } key
                && request.RequestUri is { IsAbsoluteUri: true } address
                && string.Equals(OriginOf(address), transport.Origin, StringComparison.Ordinal))
            {
                request.Headers.TryAddWithoutValidation(credential.Header, credential.Scheme is null ? key : $"{credential.Scheme} {key}");
            }

            return network.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) network.Dispose();

            base.Dispose(disposing);
        }
    }
}
