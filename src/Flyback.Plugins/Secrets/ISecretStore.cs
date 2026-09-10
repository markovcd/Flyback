namespace Flyback.Plugins.Secrets;

/// <summary>
/// Somewhere the operating system will hold a secret for us.
/// </summary>
/// <remarks>
/// Deliberately not "somewhere Flyback encrypts a secret": a key encrypted with a key
/// we also ship is obfuscated rather than protected. So this delegates to whatever the
/// platform already has, unlocked by the login the person has already done — DPAPI,
/// the Keychain, the Secret Service. There is no cryptography anywhere in Flyback,
/// which is the point. Platform I/O, and therefore a plugin (ADR-0025).
/// </remarks>
public interface ISecretStore
{
    /// <summary>Stable identifier, e.g. <c>dpapi</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Windows credential store</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several are installed. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this can hold anything here. Must answer without throwing and
    /// without touching whatever package does the work — a store for another
    /// operating system says no rather than failing when it is first used.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Puts a secret away under a name, replacing whatever was there.</summary>
    void Keep(string account, string secret);

    /// <summary>The secret kept under a name, or null if there is none.</summary>
    string? Recall(string account);

    /// <summary>Removes a secret. Doing this to a name with nothing under it is not an error.</summary>
    void Forget(string account);
}
