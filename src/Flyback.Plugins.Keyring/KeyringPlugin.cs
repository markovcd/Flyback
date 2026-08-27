using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Keyring;

/// <summary>
/// Hands secrets to the desktop keyring to look after.
/// </summary>
/// <remarks>
/// The Linux half of what ADR-0034 described and only built for Windows: the
/// same contract, the same shape, and the same rule that loading the assembly
/// must not touch the operating system. Nothing here runs a program until a
/// secret is actually kept or recalled — or until somebody asks whether this
/// machine has a keyring at all, which is a question about a file rather than a
/// conversation with a keyring.
/// </remarks>
public sealed class KeyringPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.keyring",
        "Linux keyring",
        "Keeps API keys in the desktop keyring, through the Secret Service — GNOME Keyring, KWallet, or whatever else answers.");

    public void Register(IPluginRegistry registry) => registry.AddSecretStore(new KeyringSecretStore());
}

/// <summary>
/// A secret store backed by the Secret Service — the D-Bus interface GNOME
/// Keyring and KWallet both implement, reached through <c>secret-tool</c>.
/// </summary>
/// <remarks>
/// Nothing here invents any cryptography, which is the whole point of
/// delegating rather than encrypting something with a key we would also have to
/// ship. What goes in is what comes out; the keyring decides everything about
/// how it is held, and unlocks with the login the person has already done.
/// </remarks>
public sealed class KeyringSecretStore : ISecretStore
{
    public string Id => "secret-service";

    public string Name => "Linux keyring";

    /// <summary>
    /// The same 100 the other two native stores claim, and for the same reason
    /// the sound backends all claim it: where it works it is the native path,
    /// and none of the three ever competes with another — each is supported only
    /// where the others are not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// Three questions, because on Linux the right operating system is not
    /// nearly enough. ADR-0034 said a program that appears to save something and
    /// does not is worse than one that never offered, and a server, a container
    /// or a bare window manager is exactly where that would happen — so this is
    /// the last moment the answer can be "no" instead of a key that quietly
    /// failed to outlive the window.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsLinux() && SecretTool.IsUsable;

    public void Keep(string account, string secret)
    {
        OnLinuxOnly();

        SecretTool.Keep(account, secret);
    }

    public string? Recall(string account)
    {
        OnLinuxOnly();

        return SecretTool.Recall(account);
    }

    public void Forget(string account)
    {
        OnLinuxOnly();

        SecretTool.Forget(account);
    }

    /// <summary>
    /// Written once rather than at all three call sites, unlike the Windows
    /// store. There the platform analyser demanded the guard be visible inside
    /// each method because it cannot see through a helper; here nothing is gated
    /// at compile time at all — starting a program is not a Linux API — so the
    /// repetition would buy nothing.
    /// </summary>
    private static void OnLinuxOnly()
    {
        if (OperatingSystem.IsLinux()) return;

        throw new PlatformNotSupportedException("The Secret Service is only available on Linux.");
    }
}
