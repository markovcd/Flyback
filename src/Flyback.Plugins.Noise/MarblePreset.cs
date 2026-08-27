using Flyback.Core.Graph;

namespace Flyback.Plugins.Noise;

/// <summary>
/// A fractal bent by a fractal, which is stone — and the same two fields heard
/// as a drone that wanders.
/// </summary>
/// <remarks>
/// The third famous thing in this subject after the two modules themselves, and
/// the reason the plugin ships no module for it: warping the coordinates of one
/// noise field by another is a Fractal into the Warp the catalogue has always
/// had, into a second Fractal. Three wires. What it buys is the thing that makes
/// marble look like marble rather than like weather — the veins stop running
/// where the noise happens to and start running where they were pushed, so the
/// field acquires flow.
/// <para>
/// The folded output is what the picture is drawn from, because a crease is a
/// vein: folding each octave about its middle puts a sharp line everywhere the
/// noise crossed the middle, and stone is made of those. Remapped backwards, so
/// the creases are the dark and the body is the light.
/// </para>
/// <para>
/// Nothing is sent to both sinks. The two fields are read at (0, 0) by the
/// speakers — x and y are the pixel's own position and the speakers have no
/// pixel — so what the ear gets of each is a slow wander through the same field
/// the eye is looking at a whole sheet of. One drifts the pitch and the other
/// swells the level, which is the same pair the picture uses for its warp and
/// its veins.
/// </para>
/// <para>
/// Three octaves on the warp and five on the veins, set on the nodes rather than
/// left at the default. It is worth saying why: the warp is being read as a
/// direction, and a direction made of fine detail pushes neighbouring pixels
/// opposite ways and tears the field. The veins are being looked at, and detail
/// is the whole of what is being looked at. Two knobs that would have been one
/// number if the count were a socket, and would have cost the patch eight noise
/// lookups instead of five.
/// </para>
/// </remarks>
internal static class MarblePreset
{
    public const string Name = "Marble";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Both fields boil on it, and the picture and the sound take their pace
        // from the same one.
        var clock = b.Add("time", 60, 520);

        var drift = FractalModule.WithOctaves(
            b.Add(FractalModule.TypeId, 300, 300, (3, 0.9f), (4, 0.5f)), 3);

        // The warp: the plane is pushed by how the first field reads at each
        // point, so the second one is read somewhere other than where it is
        // being drawn.
        var bend = b.Add("space.warp", 560, 300, (3, 0.7f));

        var veins = FractalModule.WithOctaves(
            b.Add(FractalModule.TypeId, 820, 300, (3, 2.5f), (4, 0.55f)), 5);

        // Backwards, so the creases are the dark veins and the body of the field
        // is the light stone. Not all the way to black: marble has no holes in it.
        var stone = b.Add("math.remap", 1080, 220, (1, 0f), (2, 0.55f), (3, 1f), (4, 0.1f));
        var tint = b.Add("color.hsv", 1320, 220, (1, 0.25f));

        // Ear. The same two fields, read where the speakers stand.
        var pitch = b.Add("math.remap", 1080, 560, (1, 0f), (2, 1f), (3, 90f), (4, 130f));
        var tone = b.Add("osc.sine", 1320, 560);
        var swell = b.Add("math.remap", 1080, 760, (1, 0f), (2, 1f), (3, 0.3f), (4, 1f));
        var voiced = b.Add("math.mul", 1560, 620);

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1800, 400, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(clock, 0, drift, 2)
         .Wire(clock, 0, veins, 2)

         .Wire(drift, 0, bend, 2)
         .Wire(bend, 0, veins, 0)
         .Wire(bend, 1, veins, 1)

         .Wire(veins, 1, stone, 0)
         .Wire(drift, 0, tint, 0)
         .Wire(stone, 0, tint, 2)
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort)

         .Wire(drift, 0, pitch, 0)
         .Wire(pitch, 0, tone, 1)
         .Wire(veins, 1, swell, 0)
         .Wire(tone, 0, voiced, 0)
         .Wire(swell, 0, voiced, 1)
         .Wire(voiced, 0, output, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }
}
