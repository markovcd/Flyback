using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A fractal bent by a fractal, which is stone.
/// </summary>
/// <remarks>
/// The reason the plugin ships no module for it: warping one noise field by
/// another is a Fractal into the Warp the catalogue has always had, into a second
/// Fractal. What it buys is flow — the veins stop running where the noise happens
/// to and start running where they were pushed. The folded output is what the
/// picture is drawn from, because a crease is a vein, remapped backwards so the
/// creases are the dark.
/// <para>
/// Three octaves on the warp and five on the veins: the warp is read as a
/// direction, and a direction made of fine detail pushes neighbouring pixels
/// opposite ways and tears the field, where the veins are what is being looked at.
/// </para>
/// <para>
/// There is no sound in it. A patch earns both sinks only if a listener could tell
/// which picture the sound belonged to.
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
        var clock = b.Add("time");

        var drift = FractalModule.WithOctaves(
            b.Add(FractalModule.TypeId, (3, 0.9f), (4, 0.5f)), 3);

        // The warp: the plane is pushed by how the first field reads at each
        // point, so the second one is read somewhere other than where it is
        // being drawn.
        var bend = b.Add("space.warp", (3, 0.7f));

        var veins = FractalModule.WithOctaves(
            b.Add(FractalModule.TypeId, (3, 2.5f), (4, 0.55f)), 5);

        // Backwards, so the creases are the dark veins and the body of the field
        // is the light stone. Not all the way to black: marble has no holes in it.
        var stone = b.Add("math.remap", (1, 0f), (2, 0.55f), (3, 1f), (4, 0.1f));
        var tint = b.Add("color.hsv", (1, 0.25f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(clock, 0, drift, 2)
         .Wire(clock, 0, veins, 2)

         .Wire(drift, 0, bend, 2)
         .Wire(bend, 0, veins, 0)
         .Wire(bend, 1, veins, 1)

         .Wire(veins, 1, stone, 0)
         .Wire(drift, 0, tint, 0)
         .Wire(stone, 0, tint, 2)
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }
}
