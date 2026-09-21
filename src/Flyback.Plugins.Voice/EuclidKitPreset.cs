using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A drum kit and a bass line played by four Euclidean rhythms, and a clock face drawn by the same rhythm.
/// </summary>
/// <remarks>
/// Built to use every module added alongside it: Euclid and Decay play the kit, Random
/// makes the hats, the snare, the bass notes and the filter's drift, and Slew glides the
/// bass. The picture is a Line swept round by the loop, a ring the kick pushes out, and
/// a difference flash on the snare, stacked with three Layers.
/// </remarks>
internal static class EuclidKitPreset
{
    public const string Name = "Euclid kit";

    private const string Picture = "flyback.picture";
    private const string LineType = "flyback.picture.line";
    private const string CircleType = "flyback.picture.circle";
    private const string FillType = "flyback.picture.fill";
    private const string LayerType = "flyback.picture.layer";

    /// <summary>A minor pentatonic, as pitch classes.</summary>
    private static readonly int[] Scale = [9, 0, 2, 4, 7];

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        var b = new PatchBuilder(modules);

        // --- clock and rhythm ------------------------------------------------------

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 118f));
        var sixteenths = b.Add("math.mul", (1, 4f));

        var kickBeat = b.Add(EuclidModule.TypeId, (2, 16f), (3, 4f), (5, 0.4f));
        var snareBeat = b.Add(EuclidModule.TypeId, (2, 16f), (3, 2f), (4, 4f), (5, 0.4f));
        var hatBeat = b.Add(EuclidModule.TypeId, (2, 16f), (3, 7f), (4, 1f), (5, 0.3f));
        var bassBeat = b.Add(EuclidModule.TypeId, (2, 16f), (3, 5f), (4, 3f), (5, 0.6f));

        b.Wire(tempo, 0, sixteenths, 0);
        foreach (var beat in new[] { kickBeat, snareBeat, hatBeat, bassBeat })
            b.Wire(sixteenths, 0, beat, 1);

        b.Group("Rhythm", tempo, sixteenths, kickBeat, snareBeat, hatBeat, bassBeat);

        // --- kick: a sine with a fast pitch drop -------------------------------------

        var kickLevel = b.Add(DecayModule.TypeId, (1, -3.5f), (2, -0.55f), (3, 0.7f));
        var kickDrop = b.Add(DecayModule.TypeId, (1, -4f), (2, -1.4f), (3, 1f));
        var dropHz = b.Add("math.mul", (1, 160f));
        var kickHz = b.Add("math.add", (1, 48f));
        var kick = b.Add("osc.sine");

        b.Wire(kickBeat, 0, kickLevel, 0)
         .Wire(kickBeat, 0, kickDrop, 0)
         .Wire(kickDrop, 0, dropHz, 0)
         .Wire(dropHz, 0, kickHz, 0)
         .Wire(kickHz, 0, kick, 1)
         .Wire(kickLevel, 0, kick, 3);

        b.Group("Kick", kickLevel, kickDrop, dropHz, kickHz, kick);

        // --- hats and snare: noise through a filter ----------------------------------

        var hatLevel = b.Add(DecayModule.TypeId, (1, -4f), (2, -1.5f), (3, 1f));
        var hiss = b.Add(NodeCatalog.RandomTypeId, (2, 3f));
        var hats = b.Add(NodeCatalog.FilterTypeId, (1, 8_000f), (2, 0.1f));

        var snareLevel = b.Add(DecayModule.TypeId, (1, -3.5f), (2, -0.95f), (3, 0.6f));
        var rattle = b.Add(NodeCatalog.RandomTypeId, (2, 7f));
        var snare = b.Add(NodeCatalog.FilterTypeId, (1, 1_900f), (2, 0.35f));

        b.Wire(hatBeat, 0, hatLevel, 0)
         .Wire(hatLevel, 0, hiss, 3)
         .Wire(hiss, 0, hats, 0)
         .Wire(snareBeat, 0, snareLevel, 0)
         .Wire(snareLevel, 0, rattle, 3)
         .Wire(rattle, 1, snare, 0);

        b.Group("Hats and snare", hatLevel, hiss, hats, snareLevel, rattle, snare);

        // --- bass: random notes on the step, snapped to the scale, glided ------------

        // Same domain and rate as the Euclids, so a new note lands exactly on a step.
        var notes = b.Add(NodeCatalog.RandomTypeId, (2, 11f), (3, 6f), (4, 45f));
        var scale = b.Add(NodeCatalog.QuantiserTypeId);
        ScaleExtra.Set(scale, Scale);

        var pitch = b.Add("audio.note");
        var glide = b.Add(NodeCatalog.SlewTypeId, (1, -1.5f), (2, -1.5f));
        var bassOsc = b.Add("osc.saw", (3, 0.8f));

        var drift = b.Add(NodeCatalog.RandomTypeId, (1, 0.15f), (2, 5f), (3, 700f), (4, 1_100f));
        var tone = b.Add(NodeCatalog.FilterTypeId, (2, 0.5f));
        var bassLevel = b.Add(DecayModule.TypeId, (1, -3f), (2, -0.8f), (3, 0.4f));
        var bass = b.Add("math.mul");

        b.Wire(sixteenths, 0, notes, 1)
         .Wire(notes, 2, scale, 0)
         .Wire(scale, 0, pitch, 0)
         .Wire(pitch, 0, glide, 0)
         .Wire(glide, 0, bassOsc, 1)
         .Wire(bassOsc, 0, tone, 0)
         .Wire(drift, 3, tone, 1)
         .Wire(bassBeat, 0, bassLevel, 0)
         .Wire(tone, 0, bass, 0)
         .Wire(bassLevel, 0, bass, 1);

        b.Group("Bass", notes, scale, pitch, glide, bassOsc, drift, tone, bassLevel, bass);

        // --- desk --------------------------------------------------------------------

        var desk = b.Add("math.mixer", (1, 0.9f), (3, 0.35f), (5, 1.2f), (7, 0.5f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(kick, 0, desk, 0)
         .Wire(hats, 2, desk, 2)
         .Wire(snare, 1, desk, 4)
         .Wire(bass, 0, desk, 6)
         .Wire(desk, 0, output, NodeCatalog.OutputLeftPort);

        // --- picture: a hand swept round by the loop ---------------------------------

        var ground = b.Add("color.rgb", (0, 0.02f), (1, 0.02f), (2, 0.05f));

        var angle = b.Add("math.mul", (1, MathF.Tau));
        var across = b.Add("math.sin");
        var up = b.Add("math.cos");
        var reachX = b.Add("math.mul", (1, 0.8f));
        var reachY = b.Add("math.mul", (1, 0.8f));
        var hand = b.Add(LineType, (2, 0f), (3, 0f), (6, 0.006f));
        var handInk = b.Add(FillType, (1, 0.01f));
        var hue = b.Add("color.hsv", (1, 0.6f), (2, 1f));
        var drawn = Mode(b.Add(LayerType), "add");

        b.Wire(kickBeat, 2, angle, 0)
         .Wire(angle, 0, across, 0)
         .Wire(angle, 0, up, 0)
         .Wire(across, 0, reachX, 0)
         .Wire(up, 0, reachY, 0)
         .Wire(reachX, 0, hand, 4)
         .Wire(reachY, 0, hand, 5)
         .Wire(hand, 0, handInk, 0)
         .Wire(bassBeat, 2, hue, 0)
         .Wire(ground, 0, drawn, 0)
         .Wire(hue, 0, drawn, 1)
         .Wire(handInk, 0, drawn, 2);

        b.Group("Picture: hand", ground, angle, across, up, reachX, reachY, hand, handInk, hue, drawn);

        // --- picture: the kick's ring and the snare's flash --------------------------

        // Meters, because an envelope has no memory on the picture and would draw its trigger.
        var kickHeard = b.Add(NodeCatalog.MeterTypeId, (1, -1.7f));
        var radius = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.35f), (4, 0.6f));
        var ring = b.Add(CircleType);
        var ringInk = b.Add(FillType, (1, 0.01f), (2, 0.015f));
        var warm = b.Add("color.rgb", (0, 1f), (1, 0.55f), (2, 0.2f));
        var ringed = Mode(b.Add(LayerType), "screen");

        var snareHeard = b.Add(NodeCatalog.MeterTypeId, (1, -1.5f));
        var white = b.Add("color.rgb", (0, 1f), (1, 1f), (2, 1f));
        var flashed = Mode(b.Add(LayerType), "difference");

        b.Wire(kickLevel, 0, kickHeard, 0)
         .Wire(kickHeard, 1, radius, 0)
         .Wire(radius, 0, ring, 2)
         .Wire(ring, 0, ringInk, 0)
         .Wire(drawn, 0, ringed, 0)
         .Wire(warm, 0, ringed, 1)
         .Wire(ringInk, 1, ringed, 2)
         .Wire(snareLevel, 0, snareHeard, 0)
         .Wire(ringed, 0, flashed, 0)
         .Wire(white, 0, flashed, 1)
         .Wire(snareHeard, 1, flashed, 2)
         .Wire(flashed, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Picture: hits", kickHeard, radius, ring, ringInk, warm, ringed, snareHeard, white, flashed);

        return b.Build();
    }

    /// <summary>A Layer's blend mode, written as the state the module reads; this assembly cannot call it.</summary>
    private static NodeInstance Mode(NodeInstance layer, string mode)
    {
        layer.SetState("layer", new JsonObject { ["mode"] = mode });
        return layer;
    }
}
