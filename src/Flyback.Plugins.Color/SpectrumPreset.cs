using Flyback.Core.Graph;

namespace Flyback.Plugins.Color;

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
/// The ear gets the sweep rather than the field, for the reason it always does
/// here: x and y are the pixel's own position and the speakers have no pixel, so
/// the field is one flat number to them. What the sweep does to the sound is what
/// it does to the picture — the tone opens out from one partial to a stack of
/// them as the palette opens out from one color to all of them, both from the
/// same wire.
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

        // Ear: the same sweep, opening a pulse from one partial to a stack.
        var pitch = b.Add("audio.frequency", 600, 700, (0, 110f));
        var width = b.Add("math.remap", 600, 900, (1, 0f), (2, 1f), (3, 0.5f), (4, 0.06f));
        var voice = b.Add("osc.pulse", 1000, 780);

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
         .Wire(banded, 0, output, NodeCatalog.OutputColorPort)

         .Wire(pitch, 0, voice, 1)
         .Wire(sweep, 0, width, 0)
         .Wire(width, 0, voice, 3)
         .Wire(voice, 0, output, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }
}
