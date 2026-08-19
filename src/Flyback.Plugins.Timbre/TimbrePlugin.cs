using Flyback.Core.Graph;

namespace Flyback.Plugins.Timbre;

/// <summary>
/// The two halves of shaping a waveform. A filter takes harmonics away and a
/// folder puts them there, and between them they are most of what makes one
/// instrument sound different from another — which is why they ship together
/// rather than as two plugins that would each be half an idea.
/// </summary>
/// <remarks>
/// The catalogue was strong on where a signal goes and had nothing at all for
/// what it sounds like when it gets there: five oscillators, no filter, and no
/// way to make a spectrum out of a sine. This is that gap.
/// <para>
/// Unlike the delay and reverb of <c>Flyback.Plugins.Space</c>, none of this
/// needed the engine changed. The filter's memory is a pair of one-evaluation
/// cells taken from the emitter, and the two shaping modules have no memory at
/// all — see ADR-0041.
/// </para>
/// </remarks>
public sealed class TimbrePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.timbre", "Filter and fold");

    public PluginInfo Info { get; } = new(
        "flyback.timbre",
        "Filter and fold",
        "Waveshaping: a resonant filter, a wavefolder and a saturator.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [FilterModule.Definition, FoldModule.Definition, DriveModule.Definition]);

        registry.AddPresets([new PatchPreset(TimbrePreset.Name, TimbrePreset.Build)]);
    }
}
