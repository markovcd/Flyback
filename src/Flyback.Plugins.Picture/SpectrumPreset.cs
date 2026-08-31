using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The Plasma preset's own field, colored out of a palette instead of off the
/// hue wheel — and then graded, posterised, and read back apart again.
/// </summary>
/// <remarks>
/// Deliberately the same two sines the engine's first preset is built on, so
/// that what is being demonstrated is the color and nothing else. Plasma sends
/// that field into HSV's hue and comes out a rainbow, because that is the only
/// thing the catalogue could do with a number that wanted to be a color. Here
/// the same number goes into a Palette, and a slow sweep walks 'spread' from
/// nothing to a third — from tints of one color, through the sunsets and teals
/// in between, to the rainbow Plasma is stuck at. The whole plugin is in that
/// one knob moving.
/// <para>
/// After it: a Grade leaning on the contrast, and a Posterise whose level count
/// is swept, so the picture resolves from flat bands into a gradient and back.
/// Both are on the finished color rather than on the signal behind it, which is
/// the point of their being color modules — a patch can be colored first and
/// corrected afterwards, the way a picture is.
/// </para>
/// <para>
/// There is no sound in it, and there was: the same sweep opened a pulse from one
/// partial to a stack, so the tone widened as the palette widened. It was the
/// house style rather than the patch — the ear cannot read the field at all,
/// because x and y are the pixel's own position and the speakers have no pixel,
/// so what the sound was actually made of was the one knob and nothing else. A
/// tone that shares a knob with a picture is not the picture being heard.
/// </para>
/// </remarks>
internal static class SpectrumPreset
{
    public const string Name = "Spectrum";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 60, 200);
        var clock = b.Add("time", 60, 420);

        // Plasma's own arrangement, unchanged: a sine along x, a second along y
        // whose phase drifts, and the two summed.
        var slowly = b.Add("math.mul", 220, 420, (1, 0.2f));
        var across = b.Add("osc.sine", 380, 120, (1, 1.5f));
        var down = b.Add("osc.sine", 380, 300, (1, 1.1f));
        var field = b.Add("math.add", 600, 200);
        var along = b.Add("math.remap", 780, 200, (1, -2f), (2, 2f), (3, 0f), (4, 1f));

        // The one sweep. Slow enough that a whole pass takes half a minute, since
        // what it is showing is a family of palettes rather than a movement.
        var sweep = b.Add("osc.sine", 60, 700, (1, 0.035f), (3, 0.5f), (4, 0.5f));
        var spread = b.Add("math.remap", 320, 700, (1, 0f), (2, 1f), (3, 0.02f), (4, 0.34f));

        var palette = b.Add(PaletteModule.TypeId, 1000, 200);
        var graded = b.Add(GradeModule.TypeId, 1220, 200, (1, 1.15f), (2, 1.25f), (3, 1.1f));

        // Swept the other way from the palette, so the picture is at its flattest
        // where the colors are at their calmest.
        var levels = b.Add("math.remap", 320, 900, (1, 0f), (2, 1f), (3, 3f), (4, 24f));
        var banded = b.Add(PosteriseModule.TypeId, 1440, 200);

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1700, 400, (NodeCatalog.OutputGainPort, 0.45f));

        b.Wire(coord, 0, across, 0)
         .Wire(coord, 1, down, 0)
         .Wire(clock, 0, slowly, 0)
         .Wire(slowly, 0, down, 2)
         .Wire(across, 0, field, 0)
         .Wire(down, 0, field, 1)
         .Wire(field, 0, along, 0)

         .Wire(along, 0, palette, 0)
         .Wire(sweep, 0, spread, 0)
         .Wire(spread, 0, palette, 2)
         .Wire(palette, 0, graded, 0)
         .Wire(sweep, 0, levels, 0)
         .Wire(levels, 0, banded, 1)
         .Wire(graded, 0, banded, 0)
         .Wire(banded, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Field", coord, clock, slowly, across, down, field, along)
         .Group("Palette Sweep", sweep, spread, palette, graded, levels, banded);

        return b.Patch;
    }
}
