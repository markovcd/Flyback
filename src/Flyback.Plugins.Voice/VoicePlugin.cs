using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// What makes a tone and what is done to it before it leaves the instrument:
/// the stacked oscillator, and the three ways of changing a waveform's shape.
/// </summary>
/// <remarks>
/// Everything here has a single boundary: it is part of one voice, in the
/// order a voice is built. Make the harmonics, then take them away.
/// <para>
/// The Filter is the one module here that is not pure. It carries its
/// integrators in the one-evaluation cells
/// ([0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)) rather than in
/// an opcode of its own, and a cell is something only the speakers' program has —
/// so it is declared audio-only and is a wire on the screen. Fold and Drive are
/// arithmetic and are honest at both sinks, which is why the preset shows the
/// folding and does not pretend to show the filtering.
/// </para>
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
