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
        "A seven-oscillator supersaw, a four-operator FM synth, a drum, a bell, a hiss and a "
        + "wandering value, a fold and a crush to shape them, struck and counted envelopes with a "
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
            new PatchPreset(
                SupersawPreset.Name,
                SupersawPreset.Build,
                "Seven detuned saws, wired the way the module is meant to be driven."),
            new PatchPreset(
                TimbrePreset.Name,
                TimbrePreset.Build,
                "A saw folded and then filtered: make the harmonics first, take them away second."),
            new PatchPreset(
                StruckPreset.Name,
                StruckPreset.Build,
                "A kick, a hat and a wandering bell: each a Stroke into one module, all counted off one Tempo."),
            new PatchPreset(
                EuclidKitPreset.Name,
                EuclidKitPreset.Build,
                "Four Euclidean rhythms playing a noise kit and a gliding bass, drawn as a clock "
                + "hand, a ring the kick pushes and a flash on the snare.",
                PresetKind.Interplay),
        ]);
    }
}
