using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A kick, a hat and a bell, each one module with one envelope into it, and the
/// envelopes all counted off the same beats.
/// </summary>
/// <remarks>
/// The short way round to what the Euclid kit builds by hand. There a drum is a
/// trigger, a Decay, a noise, a filter and a multiply; here it is a Stroke into a
/// Drum. A Stroke has no trigger in it: it reads a count of beats and is the
/// envelope of whichever stroke that count is in, so three of them on one Tempo
/// cannot drift apart, and 'rate' and 'offset' are the whole of the rhythm —
/// every beat, the half between, and every other beat a little late.
/// <para>
/// The bell's pitch is the one thing not written down. A Wander drifts through
/// two octaves, a Sample &amp; Hold catches it as each stroke begins so a note
/// stays one note while it rings, and a Tune puts what was caught into a scale.
/// </para>
/// <para>
/// Nothing is drawn. A Stroke is the same number on the screen as in the
/// speakers, but the Hiss is a Filter and the hold is a cell, and neither has a
/// picture side to show.
/// </para>
/// </remarks>
internal static class StruckPreset
{
    public const string Name = "Struck";

    /// <summary>The Tempo's second output: the count of beats so far.</summary>
    private const int Beats = 1;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 104f));

        // Every beat, falling fast: the one envelope is the kick's loudness and
        // how far its pitch drops.
        var thump = b.Add(StrokeModule.TypeId, (1, 1f), (3, 4f));
        var kick = b.Add(DrumModule.TypeId, (2, 46f), (3, 140f));

        // The half beats between, and a curve steep enough to be a tick. A level
        // is the envelope times how loud the part is, which is a Multiply.
        var tick = b.Add(StrokeModule.TypeId, (1, 2f), (2, 0.5f), (3, 9f));
        var quiet = b.Add("math.mul", (1, 0.35f));
        var hat = b.Add(HissModule.TypeId, (2, 9000f));

        // Every other beat and a quarter of a stroke late, with a slow fall so
        // the bell rings into the next one.
        var ring = b.Add(StrokeModule.TypeId, (1, 0.5f), (2, 0.25f), (3, 2.5f));
        var soft = b.Add("math.mul", (1, 0.5f));

        // A3 to A5, drifting, and caught on the stroke's leading edge.
        var drift = b.Add(WanderModule.TypeId, (1, 0.6f), (2, 3f), (3, 57f), (4, 81f));
        var caught = b.Add(NodeCatalog.HoldTypeId);

        // A minor pentatonic, so whatever is caught is in key.
        var key = b.Add("audio.tune");
        ScaleExtra.Set(key, [0, 2, 4, 7, 9]);

        var chime = b.Add(BellModule.TypeId, (3, 2.76f), (4, 0.5f));

        var desk = b.Add("math.mixer", (1, 0.8f), (3, 0.5f), (5, 0.7f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(tempo, Beats, thump, 0)
         .Wire(thump, 0, kick, 1)

         .Wire(tempo, Beats, tick, 0)
         .Wire(tick, 0, quiet, 0)
         .Wire(quiet, 0, hat, 1)

         .Wire(tempo, Beats, ring, 0)
         .Wire(ring, 0, soft, 0)
         .Wire(drift, 0, caught, 0)
         .Wire(ring, 0, caught, 1)
         .Wire(caught, 0, key, 0)
         .Wire(key, 0, chime, 1)
         .Wire(soft, 0, chime, 2)

         .Wire(kick, 0, desk, 0)
         .Wire(hat, 0, desk, 2)
         .Wire(chime, 0, desk, 4)
         .Wire(desk, 0, output, NodeCatalog.OutputLeftPort);

        b.Group("Kick", thump, kick)
         .Group("Hat", tick, quiet, hat)
         .Group("Bell", ring, soft, drift, caught, key, chime);

        return b.Build();
    }
}
