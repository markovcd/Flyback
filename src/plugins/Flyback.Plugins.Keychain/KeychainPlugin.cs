using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Keychain;

/// <summary>
/// Hands secrets to macOS to look after.
/// </summary>
/// <remarks>
/// The macOS half of what ADR-0034 described and only built for Windows: the
/// same contract, the same shape, and the same rule that loading the assembly
/// must not touch the operating system. Nothing here runs a program until a
/// secret is actually kept or recalled, so the plugin loads harmlessly on a
/// machine it cannot work on and says so politely.
/// </remarks>
public sealed class KeychainPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.keychain",
        "macOS Keychain",
        "Keeps API keys in the login keychain, unlocked by the macOS login.");

    public void Register(IPluginRegistry registry) => registry.AddSecretStore(new KeychainSecretStore());
}

/// <summary>
/// A secret store backed by the login keychain, unlocked by the login the
/// person has already done.
/// </summary>
/// <remarks>
/// Nothing here invents any cryptography, which is the whole point of
/// delegating rather than encrypting something with a key we would also have to
/// ship. What goes in is what comes out; the keychain decides everything about
/// how it is held.
/// </remarks>
public sealed class KeychainSecretStore : ISecretStore
{
    public string Id => "keychain";

    public string Name => "macOS Keychain";

    /// <summary>
    /// The same 100 the Windows store claims, and for the same reason the sound
    /// backends all claim it: where it works it is the native path, and the two
    /// never compete — each is supported only where the other is not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// One question, unlike the Linux store: <c>security</c> is part of macOS
    /// rather than something a machine may or may not have installed, so being
    /// on macOS is the whole of the answer.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsMacOS();

    public void Keep(string account, string secret)
    {
        OnMacOsOnly();

        LoginKeychain.Keep(account, secret);
    }

    public string? Recall(string account)
    {
        OnMacOsOnly();

        return LoginKeychain.Recall(account);
    }

    public void Forget(string account)
    {
        OnMacOsOnly();

        LoginKeychain.Forget(account);
    }

    /// <summary>
    /// Written once rather than at all three call sites, unlike the Windows
    /// store. There the platform analyser demanded the guard be visible inside
    /// each method because it cannot see through a helper; here nothing is
    /// gated at compile time at all — starting a program is not a Windows API —
    /// so the repetition would buy nothing.
    /// </summary>
    private static void OnMacOsOnly()
    {
        if (OperatingSystem.IsMacOS()) return;

        throw new PlatformNotSupportedException("The macOS Keychain is only available on macOS.");
    }
}
