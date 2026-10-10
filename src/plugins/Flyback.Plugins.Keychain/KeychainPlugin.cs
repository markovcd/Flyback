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
