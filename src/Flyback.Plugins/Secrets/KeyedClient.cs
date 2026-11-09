using System.Text;

namespace Flyback.Plugins.Secrets;

/// <summary>The host's <see cref="IKeyedClient"/>: a key bound to one origin, put on requests there and nowhere else.</summary>
internal sealed class KeyedClient : IKeyedClient, IEquatable<KeyedClient>
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

    public static KeyedClient None { get; } = new(null, null, new KeyCredential("", ""));

    private readonly string? secret;
    private readonly KeyCredential credential;
    private readonly HttpMessageHandler network;

    /// <param name="inner">Where requests go once signed; the network unless a test says otherwise.</param>
    /// <remarks>An admin key is never sent, wherever it came from (<see cref="KeySafety.Refused"/>).</remarks>
    public KeyedClient(string? secret, string? origin, KeyCredential credential, HttpMessageHandler? inner = null)
    {
        this.secret = string.IsNullOrWhiteSpace(secret) || origin is null || KeySafety.Refused(secret) is not null ? null : secret.Trim();
        this.credential = credential;
        Origin = this.secret is null ? null : origin;
        network = inner ?? Network;
    }

    public bool HasKey => secret is not null;

    public string? Origin { get; }

    /// <summary>Whether <paramref name="text"/> has this key in it. A key too short to be anybody's secret is never found.</summary>
    public bool Holds(string? text) =>
        secret is { Length: >= KeySafety.Findable } key && text is not null && text.Contains(key, StringComparison.Ordinal);

    /// <summary><paramref name="text"/> with this key taken out of it.</summary>
    public string? Scrubbed(string? text) =>
        Holds(text) ? text!.Replace(secret!, "[key]", StringComparison.Ordinal) : text;

    public async Task<KeyedResponse> Send(Uri address, string? json, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var request = new HttpRequestMessage(json is null ? HttpMethod.Get : HttpMethod.Post, address);

        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (secret is { } key
            && address.IsAbsoluteUri
            && string.Equals(OriginOf(address), Origin, StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation(credential.Header, credential.Scheme is null ? key : $"{credential.Scheme} {key}");
        }

        // A turn at high effort is minutes, and a survey asks a list of models several
        // questions each. Cancellation is what stops either; the timeout is a backstop
        // for a connection that has died without saying so.
        using var client = new HttpClient(network, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        // Copied onto a message of their own: the response leads back to the request the key is on.
        var headers = new HttpResponseMessage().Headers;

        foreach (var (name, values) in response.Headers) headers.TryAddWithoutValidation(name, values);

        return new KeyedResponse((int)response.StatusCode, body, headers);
    }

    /// <summary><c>scheme://host[:port]</c>, the port only where it is not the scheme's own.</summary>
    public static string OriginOf(Uri address) => address.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    /// <summary>
    /// Alike when they carry the same key to the same origin the same way, because the
    /// transport is rebuilt for every message and a conversation carries on only while
    /// its configuration is unchanged.
    /// </summary>
    public bool Equals(KeyedClient? other) =>
        other is not null
        && secret == other.secret
        && Origin == other.Origin
        && credential == other.credential
        && ReferenceEquals(network, other.network);

    public override bool Equals(object? obj) => Equals(obj as KeyedClient);

    public override int GetHashCode() => HashCode.Combine(secret, Origin, credential);

    public override string ToString() => HasKey ? $"a key for {Origin}" : "no key";
}
