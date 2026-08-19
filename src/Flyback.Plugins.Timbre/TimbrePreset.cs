using Flyback.Core.Graph;

namespace Flyback.Plugins.Timbre;

/// <summary>
/// A saw folded and then filtered, which is the order both modules are usually
/// wanted in: make the harmonics first, take them away second. Two slow sines
/// drive it, and the same two drive the picture — so the bands on screen are the
/// folding the ear hears, and the brightness is the cutoff sweep.
/// </summary>
/// <remarks>
/// The Fold appears twice and the Filter once, which is the honest arrangement
/// rather than an oversight. Folding is pure and does the same arithmetic at
/// both sinks, so it can be shown; the filter has a memory and is a wire on the
/// video path, so there is nothing of it to show and it is not pretended
/// otherwise.
/// </remarks>
internal static class TimbrePreset
{
    public const string Name = "Filter sweep";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 120);
        var time = b.Add("time", 40, 620, (0, 1f));

        // The two hands on the instrument, both far below audio rate: one opens
        // the filter, the other drives the fold. Every knob in the patch that
        // moves is moved by one of these.
        var sweep = b.Add("osc.sine", 250, 860, (1, 0.12f));
        var wobble = b.Add("osc.sine", 250, 1080, (1, 0.07f));

        var cutoff = b.Add("math.remap", 470, 860, (3, 180f), (4, 5000f));
        var drive = b.Add("math.remap", 470, 1080, (3, 1f), (4, 4f));

        // Ear: 110 Hz, folded into a spectrum, then filtered back down out of it.
        var pitch = b.Add("audio.frequency", 250, 640, (0, 110f));
        var saw = b.Add("osc.saw", 470, 600, (3, 0.9f));
        var fold = b.Add(FoldModule.TypeId, 700, 600);
        var filter = b.Add(FilterModule.TypeId, 930, 640, (2, 0.75f));

        // Eye: a ring field through the same folder at the same drive, so the
        // bands appear and disappear exactly as the tone brightens and dulls.
        var rings = b.Add("pattern.rings", 250, 120, (2, 1.2f));
        var bands = b.Add(FoldModule.TypeId, 470, 120);
        var hue = b.Add("math.remap", 700, 120, (3, 0.45f), (4, 0.95f));
        var lit = b.Add("math.remap", 700, 320, (3, 0.35f), (4, 1f));
        var colour = b.Add("colour.hsv", 930, 180, (1, 0.8f));

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1250, 400, (NodeCatalog.OutputGainPort, 0.45f));

        b.Wire(time, 0, sweep, 0)
         .Wire(time, 0, wobble, 0)
         .Wire(sweep, 0, cutoff, 0)
         .Wire(wobble, 0, drive, 0)

         .Wire(time, 0, saw, 0)
         .Wire(pitch, 0, saw, 1)
         .Wire(saw, 0, fold, 0)
         .Wire(drive, 0, fold, 1)
         .Wire(fold, 0, filter, 0)
         .Wire(cutoff, 0, filter, 1)
         .Wire(filter, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(rings, 0, bands, 0)
         .Wire(drive, 0, bands, 1)
         .Wire(bands, 0, hue, 0)
         .Wire(sweep, 0, lit, 0)
         .Wire(hue, 0, colour, 0)
         .Wire(lit, 0, colour, 2)
         .Wire(colour, 0, output, NodeCatalog.OutputColourPort);

        return b.Patch;
    }
}
