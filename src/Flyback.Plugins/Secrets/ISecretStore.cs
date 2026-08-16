namespace Flyback.Plugins.Secrets;

/// <summary>
/// Somewhere the operating system will hold a secret for us.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not "somewhere Flyback encrypts a secret". A key encrypted with
/// a key we also ship is obfuscated, not protected — anything that can start the
/// application can undo it. So this delegates to whatever the platform already
/// has, which is unlocked by the login the person has already done: DPAPI on
/// Windows, the Keychain on macOS, the Secret Service on Linux. There is no
/// cryptography anywhere in Flyback, which is the point.
/// </para>
/// <para>
/// Platform I/O, and therefore a plugin — the case ADR-0025 drew the boundary
/// for, and the same shape as the sound backends: one small plugin per system,
/// filtered by the <c>Platform</c> attribute so a macOS build carries no Windows
/// credential code at all.
/// </para>
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
