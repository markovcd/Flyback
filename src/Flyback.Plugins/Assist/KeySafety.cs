namespace Flyback.Plugins.Assist;

/// <summary>What a key may not be, and where it may not go.</summary>
internal static class KeySafety
{
    /// <summary>
    /// The shortest key worth looking for in text. A local runtime takes any value as a
    /// key, and a one-letter one is in every sentence.
    /// </summary>
    public const int Findable = 20;

    /// <summary>What a stolen admin key could do that no assistant needs, or null for an ordinary key.</summary>
    public static string? Refused(string? secret) =>
        secret?.Trim() is { } key
        && (key.StartsWith("sk-admin-", StringComparison.Ordinal) || key.StartsWith("sk-ant-admin", StringComparison.Ordinal))
            ? "it is an admin key, which can make keys and run the organization. Make an ordinary one for Flyback."
            : null;

    /// <summary>Whether a key sent to <paramref name="origin"/> crosses the network readable: plain http to another machine.</summary>
    public static bool Cleartext(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttp
        && !address.IsLoopback;
}
