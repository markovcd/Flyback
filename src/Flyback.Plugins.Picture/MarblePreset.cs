using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A fractal bent by a fractal, which is stone.
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
/// There is no sound in it, and there was: the same two fields used to be read
/// at the point the speakers stand and turned into a drone that wandered. It
/// worked, and it was a second patch sharing a canvas with this one. Nothing was
/// sent to both sinks, so nothing about the drone said anything about the stone
/// — a listener could not have told which picture it belonged to, which is the
/// test a patch that carries both halves has to pass.
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
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }
}
