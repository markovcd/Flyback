using Flyback.Core.Graph;

namespace Flyback.Plugins.Easy;

/// <summary>
/// The fewest wires an Easy Synth needs to play a tune: a Note Sequencer's note
/// and gate in, both ears out.
/// </summary>
/// <remarks>
/// LFO 1 sways the filter slowly and LFO 2 is a light vibrato. The picture is
/// rings colored round the wheel by the synth's LFO and lit by its envelope, so
/// they flash with each note.
/// </remarks>
internal static class FirstNotesPreset
{
    public const string Name = "First notes";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var riff = b.Add("seq.notes");

        var synth = b.Add(
            SynthModule.TypeId,
            (SynthModule.GlidePort, 0.04f),
            (SynthModule.Lfo1RatePort, 0.2f),
            (SynthModule.Lfo1DepthPort, 0.4f),
            (SynthModule.Lfo2RatePort, 5f),
            (SynthModule.Lfo2DepthPort, 0.15f),
            (SynthModule.DrivePort, 0.3f));
        SynthModule.Configure(
            synth,
            (SynthModule.Lfo1TargetKey, SynthModule.Filter),
            (SynthModule.Lfo2ShapeKey, Lfo.Sine),
            (SynthModule.Lfo2TargetKey, SynthModule.Pitch));

        var rings = b.Add("pattern.rings", (2, 5f));
        var color = b.Add("color.hsv", (1, 0.7f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.3f));

        b.Wire(riff, 0, synth, SynthModule.PitchPort)
         .Wire(riff, 1, synth, SynthModule.GatePort)

         .Wire(synth, SynthModule.LfoPort, rings, 3)
         .Wire(rings, 0, color, 0)
         .Wire(synth, SynthModule.EnvPort, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort)

         .Wire(synth, SynthModule.LeftPort, output, NodeCatalog.OutputLeftPort)
         .Wire(synth, SynthModule.RightPort, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }
}
