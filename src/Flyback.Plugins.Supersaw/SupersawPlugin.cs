using Flyback.Core.Graph;

namespace Flyback.Plugins.Supersaw;

/// <summary>
/// Adds the Supersaw oscillator. Nothing platform-specific and no dependencies:
/// a module plugin is the same data <c>NodeCatalog</c> holds, written in another
/// assembly.
/// </summary>
public sealed class SupersawPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.supersaw", "Supersaw");

    public PluginInfo Info { get; } = new(
        "flyback.supersaw",
        "Supersaw",
        "A seven-voice detuned saw oscillator.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [SupersawModule.Definition]);
        registry.AddPresets([new PatchPreset(SupersawPreset.Name, SupersawPreset.Build)]);
    }
}
