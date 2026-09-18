using Flyback.Core.Graph;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// A small mix and the chain that goes at the end of one — EQ, Compressor,
/// Maximizer — switched out and in every four bars, so what the chain does is the
/// only thing that changes.
/// </summary>
/// <remarks>
/// Mastering is heard by comparison or not at all: a mix that has been through a
/// chain sounds like a mix, and only the same bars without it say what was done.
/// So the patch is its own A/B. Four bars raw, four bars mastered, crossed over a
/// fraction of a beat so the switch is a change rather than a click.
/// <para>
/// The mix is left the way a first mix usually is: the kick well over everything,
/// the pluck too quiet to follow. That is the material a chain has something to
/// say about — tone first, so the compressor is not pumped by rumble it would
/// have cut anyway; then the compressor, which brings the kick down towards the
/// rest; then the Maximizer, which brings all of it up to the ceiling.
/// </para>
/// <para>
/// The three parts are the engine's own modules, so the preset asks for no plugin
/// but this one. Nothing is drawn: every module in the chain keeps its state in
/// cells, which the picture has none of.
/// </para>
/// </remarks>
internal static class BeforeAndAfterPreset
{
    public const string Name = "Before and after";

    /// <summary>The Tempo's second output: the count of beats so far.</summary>
    private const int Beats = 1;

    /// <summary>A sequencer's second output: up while a step sounds.</summary>
    private const int Gate = 1;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 120f));

        // Four on the floor. One envelope for the level and the pitch, so the
        // drum falls from a knock to a thud as it dies away.
        var beat = b.Add("osc.pulse", (3, 0.2f));
        var thump = b.Add(NodeCatalog.AdsrTypeId, (1, -3f), (2, -0.745f), (3, 0f), (4, -1.3f));
        var dive = b.Add("math.remap", (1, 0f), (2, 1f), (3, 48f), (4, 160f));
        var drum = b.Add("osc.sine");
        var kick = b.Add("math.mul");

        // Eighths, an octave apart where the line wants a push.
        var eighths = b.Add("math.mul", (1, 2f));
        var line = Notes(b, 0.6f, 33, 33, 45, 33, 36, 36, 31, 43);
        var low = b.Add("audio.note");
        var growl = b.Add("osc.triangle");
        var bass = b.Add("math.mul");

        // Sixteenths on a string, which is the part a raw mix loses.
        var sixteenths = b.Add("math.mul", (1, 4f));
        var arp = Notes(b, 0.3f, 69, 64, 72, 64, 67, 64, 74, 64);
        var high = b.Add("audio.note");
        var pluck = b.Add(NodeCatalog.StringTypeId, (3, -0.52f), (4, 0.6f));

        var raw = b.Add("math.mixer", (1, 0.5f), (3, 0.22f), (5, 0.12f));

        // Rumble out, a little off the boxy middle, a little air on top. Every
        // 'right' here carries its 'left', so a mono mix is one wire down the chain.
        var tone = b.Add(EqModule.TypeId, (2, 30f), (4, 2f), (5, 400f), (6, -2f), (9, 3f));
        var glue = b.Add(
            CompressorModule.TypeId, (3, -24f), (4, 3f), (5, -2f), (6, -0.82f), (8, 4f));
        var loud = b.Add(MaximizerModule.TypeId, (2, 0.8f), (3, 2f));

        // The switch: one turn of a sine every thirty-two beats, squared off
        // nearly all the way. Half a turn in at the start, so the raw mix is
        // heard first and the chain arrives as the improvement.
        var bars = b.Add("osc.sine", (1, 1f / 32f), (2, 0.5f));
        var chain = b.Add("math.smoothstep", (0, -0.05f), (1, 0.05f));
        var heard = b.Add("math.mix");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));

        b.Wire(tempo, 0, beat, 1)
         .Wire(beat, 0, thump, 0)
         .Wire(thump, 0, dive, 0)
         .Wire(dive, 0, drum, 1)
         .Wire(drum, 0, kick, 0)
         .Wire(thump, 0, kick, 1)

         .Wire(tempo, 0, eighths, 0)
         .Wire(eighths, 0, line, 1)
         .Wire(line, 0, low, 0)
         .Wire(low, 0, growl, 1)
         .Wire(growl, 0, bass, 0)
         .Wire(line, Gate, bass, 1)

         .Wire(tempo, 0, sixteenths, 0)
         .Wire(sixteenths, 0, arp, 1)
         .Wire(arp, 0, high, 0)
         .Wire(arp, Gate, pluck, 1)
         .Wire(high, 0, pluck, 2)

         .Wire(kick, 0, raw, 0)
         .Wire(bass, 0, raw, 2)
         .Wire(pluck, 0, raw, 4)

         .Wire(raw, 0, tone, 0)
         .Wire(tone, 0, glue, 0)
         .Wire(glue, 0, loud, 0)

         .Wire(tempo, Beats, bars, 0)
         .Wire(bars, 0, chain, 2)
         .Wire(raw, 0, heard, 0)
         .Wire(loud, 0, heard, 1)
         .Wire(chain, 0, heard, 2)
         .Wire(heard, 0, output, NodeCatalog.OutputLeftPort);

        b.Group("Kick", beat, thump, dive, drum, kick)
         .Group("Bass", eighths, line, low, growl, bass)
         .Group("Pluck", sixteenths, arp, high, pluck)
         .Group("The Chain", tone, glue, loud)
         .Group("Before and After", bars, chain, heard);

        return b.Build();
    }

    /// <summary>A Note Sequencer carrying <paramref name="notes"/>, a step each.</summary>
    private static NodeInstance Notes(PatchBuilder b, float gateLength, params int[] notes)
    {
        var sequencer = b.Add("seq.notes", (2, gateLength));
        StepsExtra.Set(sequencer, notes.Select(note => new Step(note)));
        return sequencer;
    }
}
