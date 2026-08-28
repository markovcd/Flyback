using Flyback.Core.Graph;

namespace Flyback.Plugins.Shapes;

/// <summary>
/// A star with a hole cut through it, rocking, and the same field read as a
/// waveform.
/// </summary>
/// <remarks>
/// The plugin's own demonstration, and it is built round the one thing a shape
/// can do here that it cannot do in a drawing program: be heard. The Scan sweeps
/// a circle through the field and hands what it passes over to the speakers, so
/// the star's five points are five bumps in every cycle of the waveform — a
/// timbre that is the shape rather than a sound chosen to go with it.
/// <para>
/// Which is why the field goes to the Scan rather than the fill does. A fill is
/// 1 and 0 with a hair of gradient between, and a loop crossing one hears a
/// square wave whatever it crossed; the distance underneath it slopes all the way
/// from the tip of a point to the middle of the hole, so the waveform has the
/// star's proportions in it.
/// </para>
/// <para>
/// One sweep drives the sharpness, and it is the sweep worth watching: the points
/// grow and shrink, and the tone brightens and dulls exactly with them, because
/// sharper points are steeper sides are more harmonics. It takes the hue as well,
/// so the color says which way the shape is going. The other sweep only rocks the
/// star, and rocks rather than spins because a five-fold shape turned through a
/// whole revolution passes its own reflection and jumps.
/// </para>
/// </remarks>
internal static class ShapesPreset
{
    public const string Name = "Shape scan";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // The two sweeps. Both slow, and neither is heard directly.
        var rock = b.Add("osc.sine", 100, 520, (1, 0.07f), (3, 0.9f));
        var grow = b.Add("osc.sine", 100, 780, (1, 0.11f), (3, 0.22f), (4, 0.45f));

        // Here because the star is read at a turned position rather than at the
        // pixel's own: 'x' and 'y' are normalled to Coordinates, so overriding
        // them takes a wire (ADR-0050).
        var turn = b.Add("space.rotate", 340, 320);

        var star = b.Add(StarModule.TypeId, 580, 240, (2, 0.55f));
        var hole = b.Add(CircleModule.TypeId, 580, 520, (2, 0.18f));

        // Softly, so the hole's rim meets the star's edges in a fillet rather
        // than in a corner — which is the whole difference between this and the
        // Maximum that was always in the catalogue.
        var cut = b.Add(CombineModule.TypeId, 840, 340, (2, 0.04f));

        var ink = b.Add(FillModule.TypeId, 1080, 220, (1, 0.008f), (2, 0.02f));

        // Eye: the fill lit in a color the sweep chooses, with its own outline
        // laid over the top — white, because it is added to a color rather than
        // being one.
        var tint = b.Add("color.hsv", 1320, 180, (1, 0.8f));
        var lit = b.Add("math.add", 1560, 260);

        // Ear: the field itself, read round a loop that crosses the points.
        var pitch = b.Add("audio.frequency", 840, 760, (0, 110f));
        var scan = b.Add(NodeCatalog.ScanTypeId, 1080, 640, (3, 0.42f), (6, 0.5f));

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1820, 400, (NodeCatalog.OutputGainPort, 0.5f));

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

        return b.Patch;
    }
}
