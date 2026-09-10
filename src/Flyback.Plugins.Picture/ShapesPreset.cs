using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A star with a hole cut through it, rocking, and the same field read as a
/// waveform.
/// </summary>
/// <remarks>
/// Built round the one thing a shape can do here that it cannot in a drawing
/// program: be heard. The Scan sweeps a circle through the field, so the star's
/// five points are five bumps in every cycle — a timbre that is the shape rather
/// than a sound chosen to go with it.
/// <para>
/// The field goes to the Scan rather than the fill, because a fill is 1 and 0 and
/// a loop crossing one hears a square wave whatever it crossed; the distance
/// underneath slopes from the tip of a point to the middle of the hole.
/// </para>
/// <para>
/// One sweep drives the sharpness and the hue together — sharper points are
/// steeper sides are more harmonics. The other rocks the star rather than spinning
/// it, because a five-fold shape turned through a revolution passes its own
/// reflection and jumps.
/// </para>
/// </remarks>
internal static class ShapesPreset
{
    public const string Name = "Shape scan";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // The two sweeps. Both slow, and neither is heard directly.
        var rock = b.Add("osc.sine", (1, 0.07f), (3, 0.9f));
        var grow = b.Add("osc.sine", (1, 0.11f), (3, 0.22f), (4, 0.45f));

        // Here because the star is read at a turned position rather than at the
        // pixel's own: 'x' and 'y' are normalled to Coordinates, so overriding
        // them takes a wire (ADR-0050).
        var turn = b.Add("space.rotate");

        var star = b.Add(StarModule.TypeId, (2, 0.55f));
        var hole = b.Add(CircleModule.TypeId, (2, 0.18f));

        // Softly, so the hole's rim meets the star's edges in a fillet rather
        // than in a corner — which is the whole difference between this and the
        // Maximum that was always in the catalogue.
        var cut = b.Add(CombineModule.TypeId, (2, 0.04f));

        var ink = b.Add(FillModule.TypeId, (1, 0.008f), (2, 0.02f));

        // Eye: the fill lit in a color the sweep chooses, with its own outline
        // laid over the top — white, because it is added to a color rather than
        // being one.
        var tint = b.Add("color.hsv", (1, 0.8f));
        var lit = b.Add("math.add");

        // Ear: the field itself, read round a loop that crosses the points.
        var pitch = b.Add("audio.frequency", (0, 110f));
        var scan = b.Add(NodeCatalog.ScanTypeId, (3, 0.42f), (6, 0.5f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(rock, 0, turn, 2)
         .Wire(turn, 0, star, 0)
         .Wire(turn, 1, star, 1)
         .Wire(grow, 0, star, 4)

         // Cut, filled, and colored.
         .Wire(star, 0, cut, 0)
         .Wire(hole, 0, cut, 1)
         .Wire(cut, 2, ink, 0)
         .Wire(grow, 0, tint, 0)
         .Wire(ink, 0, tint, 2)
         .Wire(tint, 0, lit, 0)
         .Wire(ink, 1, lit, 1)
         .Wire(lit, 0, output, NodeCatalog.OutputColorPort)

         // The same field, heard. Nothing between it and the speakers but the
         // loop; 'right' carries 'left' through with no wire.
         .Wire(cut, 2, scan, 0)
         .Wire(pitch, 0, scan, 2)
         .Wire(scan, 0, output, NodeCatalog.OutputLeftPort);

        b.Group("Sweeps", rock, grow)
         .Group("Shape", turn, star, hole, cut, ink)
         .Group("Eye", tint, lit)
         .Group("Ear", pitch, scan);

        return b.Build();
    }
}
