using Flyback.Core.Graph;

namespace Flyback.Plugins.Modulation;

/// <summary>
/// The three effects that move on their own. Each is a copy of the signal put
/// slightly out of step with itself and mixed back in, and the whole difference
/// between them is how far out of step and by what means — twenty milliseconds
/// for a chorus, two for a flanger, and none at all for a phaser, which shifts
/// phase without delaying anything.
/// </summary>
/// <remarks>
/// They ship together because they are one idea at three scales, and separately
/// from <c>Flyback.Plugins.Space</c> because that plugin is about a signal
/// arriving late enough to be heard as a second event. Nothing here is: the
/// point of all three is that the copy is *not* heard separately.
/// <para>
/// Each carries its own sweep rather than taking one on a socket, and hands it
/// back out on <c>lfo</c> — the one output of the three that works on the video
/// path, since a phase accumulator falls back to the multiply it replaced where
/// there is no state. So a patch can show the movement it is playing.
/// </para>
/// </remarks>
public sealed class ModulationPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.mod", "Modulation");

    public PluginInfo Info { get; } = new(
        "flyback.mod",
        "Modulation",
        "The effects that move: chorus, flanger and phaser.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [ChorusModule.Definition, FlangerModule.Definition, PhaserModule.Definition]);

        // Two: one for what this plugin does on its own, and one for what it
        // does at the end of everything the Timbre plugin added — which is where
        // all six of the new modules are in one chain.
        registry.AddPresets(
        [
            new PatchPreset(ModulationPreset.Name, ModulationPreset.Build),
            new PatchPreset(WholeRackPreset.Name, WholeRackPreset.Build),
        ]);
    }
}
