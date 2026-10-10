using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Dpapi;

/// <summary>
/// A secret store backed by Windows' own data protection, keyed to the account
/// that is signed in.
/// </summary>
/// <remarks>
/// Nothing here invents any cryptography. What is written out is what
/// <c>ProtectedData</c> hands back, and only the signed-in user on this machine
/// can turn it into a key again — which is the whole reason to delegate rather
/// than encrypt something with a key we would also have to ship.
/// </remarks>
public sealed class DpapiSecretStore : ISecretStore
{
    public string Id => "dpapi";

    public string Name => "Windows credential store";

    public int Priority => 100;

    /// <summary>
    /// Answered without touching the package that does the work, so a load on
    /// the wrong system costs nothing and explains itself.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsWindows();

    // The guard is written out at each call rather than shared, because the
    // platform analyser reads it here and cannot see through a helper.

    public void Keep(string account, string secret)
    {
        if (!OperatingSystem.IsWindows()) throw Elsewhere();

        Vault.Keep(account, secret);
    }

    public string? Recall(string account)
    {
        if (!OperatingSystem.IsWindows()) throw Elsewhere();

        return Vault.Recall(account);
    }

    public void Forget(string account)
    {
        if (!OperatingSystem.IsWindows()) throw Elsewhere();

        Vault.Forget(account);
    }

    private static PlatformNotSupportedException Elsewhere() =>
        new("Windows data protection is only available on Windows.");
}
