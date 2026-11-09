namespace Flyback.Plugins.Bare;

/// <summary>Registers nothing, so the tests of what loads and what is allowed have a plugin that is only a plugin.</summary>
public sealed class BarePlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("flyback.bare", "Bare", "Registers nothing.");

    public void Register(IPluginRegistry registry)
    {
    }
}
