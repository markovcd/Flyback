using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// What makes a tone and what is done to it before it leaves the instrument: the
/// stacked oscillator, and the three ways of changing a waveform's shape.
/// </summary>
/// <remarks>
/// Everything here is part of one voice, in the order a voice is built. The Filter is
/// the one module that is not pure: it carries its integrators in one-evaluation cells
/// (ADR-0041), which only the speakers' program has, so it is declared audio-only and
/// is a wire on the screen.
/// </remarks>
public sealed class VoicePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.voice", "Voice");

    public PluginInfo Info { get; } = new(
        "flyback.voice",
        "Voice",
        "A seven-oscillator supersaw, and the fold, drive and filter that shape it.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                SupersawModule.Definition,
                FoldModule.Definition,
                DriveModule.Definition,
                FilterModule.Definition,
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
        ]);
    }
}
