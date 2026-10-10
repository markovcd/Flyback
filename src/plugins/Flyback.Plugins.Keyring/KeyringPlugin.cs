using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Keyring;

/// <summary>
/// Hands secrets to the desktop keyring to look after.
/// </summary>
/// <remarks>
/// The Linux half of what ADR-0034 described: the same contract, and the same rule
/// that loading the assembly must not touch the operating system. Nothing here runs a
/// program until a secret is kept or recalled, or until somebody asks whether this
/// machine has a keyring — which is a question about a file.
/// </remarks>
public sealed class KeyringPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.keyring",
        "Linux keyring",
        "Keeps API keys in the desktop keyring, through the Secret Service — GNOME Keyring, KWallet, or whatever else answers.");

    public void Register(IPluginRegistry registry) => registry.AddSecretStore(new KeyringSecretStore());
}
