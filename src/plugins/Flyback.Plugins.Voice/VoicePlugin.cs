using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Voice;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(SupersawModule.TypeId, "Supersaw")]
[assembly: FlybackModule(FoldModule.TypeId, "Fold")]
[assembly: FlybackModule(CrushModule.TypeId, "Crush")]
[assembly: FlybackModule(DecayModule.TypeId, "Decay")]
[assembly: FlybackModule(EuclidModule.TypeId, "Euclid")]
[assembly: FlybackModule(StrokeModule.TypeId, "Stroke")]
[assembly: FlybackModule(FadeModule.TypeId, "Fade")]
[assembly: FlybackModule(WanderModule.TypeId, "Wander")]
[assembly: FlybackModule(DrumModule.TypeId, "Drum")]
[assembly: FlybackModule(BellModule.TypeId, "Bell")]
[assembly: FlybackModule(FmModule.TypeId, "FM")]
[assembly: FlybackModule(HissModule.TypeId, "Hiss")]

namespace Flyback.Plugins.Voice;

/// <summary>
/// What makes a tone and what is done to it before it leaves the instrument: the
/// stacked oscillator, the drum and the noises, the fold and the crush that shape a waveform,
/// and the envelopes, the rhythm and the fade that play it.
/// </summary>
/// <remarks>
/// Decay is not pure: it carries state in a one-evaluation cell (ADR-0041), which
/// only the speakers' program has, so it is declared audio-only. Filter, Noise,
/// Slew and Drive are the engine's own for the same reason, nothing about them
/// being particular to a voice (ADR-0128).
/// </remarks>
public sealed class VoicePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.voice", "Voice");

    public PluginInfo Info { get; } = new(
        "flyback.voice",
        "Voice",
        "A seven-oscillator supersaw, a four-operator FM synth, a drum, a bell, a hiss "
        + "and a wandering value, a fold and a crush to shape them, struck and counted envelopes with a "
        + "Euclidean rhythm to play them, and a fade to arrange them.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                SupersawModule.Definition,
                FoldModule.Definition,
                CrushModule.Definition,
                DecayModule.Definition,
                EuclidModule.Definition,
                StrokeModule.Definition,
                FadeModule.Definition,
                WanderModule.Definition,
                DrumModule.Definition,
                BellModule.Definition,
                FmModule.Definition,
                HissModule.Definition,
            ]);

        registry.AddPresets(
        [
            PresetText.Preset(typeof(VoicePlugin).Assembly, "Supersaw", "Supersaw.fbks"),
            PresetText.Preset(typeof(VoicePlugin).Assembly, "Filter sweep", "FilterSweep.fbks"),
            PresetText.Preset(typeof(VoicePlugin).Assembly, "Struck", "Struck.fbks"),
            PresetText.Preset(typeof(VoicePlugin).Assembly, "Euclid kit", "EuclidKit.fbks", PresetKind.Interplay),
        ]);
    }
}
