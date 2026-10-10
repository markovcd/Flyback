using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Dpapi;

/// <summary>
/// Hands secrets to Windows to look after.
/// </summary>
/// <remarks>
/// Loading this assembly must not touch the data-protection package — the type
/// that does is only reached once a secret is actually kept or recalled, which
/// is the same rule <c>WasapiPlugin</c> follows for NAudio. That is what lets
/// the plugin load harmlessly on a machine it cannot work on and say so
/// politely, instead of throwing at start-up.
/// </remarks>
public sealed class DpapiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.dpapi",
        "Windows credential store",
        "Keeps API keys encrypted with the signed-in Windows account.");

    public void Register(IPluginRegistry registry) => registry.AddSecretStore(new DpapiSecretStore());
}
