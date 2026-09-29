using System.Text;

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
    private readonly AssistantCredential credential;
    private readonly HttpMessageHandler network;

    /// <param name="inner">Where requests go once signed; the network unless a test says otherwise.</param>
    /// <remarks>An admin key is never sent, wherever it came from (<see cref="KeySafety.Refused"/>).</remarks>
    public KeyedTransport(string? secret, string? origin, AssistantCredential credential, HttpMessageHandler? inner = null)
    {
        this.secret = string.IsNullOrWhiteSpace(secret) || origin is null || KeySafety.Refused(secret) is not null ? null : secret;
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

    public async Task<AssistantResponse> Send(Uri address, string? json, CancellationToken cancel)
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

        return new AssistantResponse((int)response.StatusCode, body, headers);
    }

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
}
