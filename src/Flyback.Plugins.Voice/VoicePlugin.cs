using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// What makes a tone and what is done to it before it leaves the instrument: the
/// stacked oscillator, the drum and the noises, the three ways of changing a
/// waveform's shape, and the slew, the envelopes, the rhythm and the fade that play it.
/// </summary>
/// <remarks>
/// The Filter, Slew and Decay are not pure: they carry state in one-evaluation cells
/// (ADR-0041), which only the speakers' program has, so they are declared audio-only.
/// </remarks>
public sealed class VoicePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.voice", "Voice");

    public PluginInfo Info { get; } = new(
        "flyback.voice",
        "Voice",
        "A seven-oscillator supersaw, a drum and three kinds of noise, the fold, drive and "
        + "filter that shape them, a slew for glide, struck and counted envelopes with a "
        + "Euclidean rhythm to play them, and a fade to arrange them.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                SupersawModule.Definition,
                FoldModule.Definition,
                DriveModule.Definition,
                FilterModule.Definition,
                RandomModule.Definition,
                SlewModule.Definition,
                DecayModule.Definition,
                EuclidModule.Definition,
                StrokeModule.Definition,
                FadeModule.Definition,
                HissModule.Definition,
                WanderModule.Definition,
                DrumModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                SupersawPreset.Name,
                SupersawPreset.Build,
                "Seven detuned saws, wired the way the module is meant to be driven.",
                PresetKind.Idea),
            new PatchPreset(
                TimbrePreset.Name,
                TimbrePreset.Build,
                "A saw folded and then filtered: make the harmonics first, take them away second.",
                PresetKind.Idea),
            new PatchPreset(
                EuclidKitPreset.Name,
                EuclidKitPreset.Build,
                "Four Euclidean rhythms playing a noise kit and a gliding bass, drawn as a clock "
                + "hand, a ring the kick pushes and a flash on the snare.",
                PresetKind.Interplay),
        ]);
    }
}
