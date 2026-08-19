using Flyback.Core.Graph;

namespace Flyback.Plugins.Modulation;

/// <summary>
/// All three in the order a pedalboard would have them — flanger, phaser,
/// chorus — with the chorus last because its two outputs are what make the
/// result stereo, and nothing after it could stay that way.
/// </summary>
/// <remarks>
/// The picture is the three sweeps and nothing else. Each module hands its own
/// LFO back out, and they run at three different rates, so what the eye sees is
/// three unrelated motions over one image and what the ear hears is three
/// unrelated motions over one note — the same three. Nothing is duplicated
/// between the sinks: every difference between them is a different output of the
/// same module, which is the arrangement the Sequence preset uses too.
/// </remarks>
internal static class ModulationPreset
{
    public const string Name = "Moving parts";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 120);
        var time = b.Add("time", 40, 700, (0, 1f));

        // Ear: one saw, and then nothing but movement.
        var pitch = b.Add("audio.frequency", 250, 720, (0, 165f));
        var saw = b.Add("osc.saw", 470, 680, (3, 0.9f));

        var flanger = b.Add(FlangerModule.TypeId, 700, 680, (1, 0.18f), (2, 0.8f), (3, 0.45f), (4, 0.35f));
        var phaser = b.Add(PhaserModule.TypeId, 940, 700, (1, 0.4f), (2, 0.75f), (3, 0.5f), (4, 0.5f));
        var chorus = b.Add(ChorusModule.TypeId, 1180, 720, (1, 0.6f), (2, 0.6f), (3, 0.5f));

        // Eye: the chorus slides the plane, the phaser picks the hue and the
        // flanger drains the colour out and lets it back in. Three knobs, none of
        // them a hand's, and the rings underneath them are the only thing on
        // screen that is not one of the three.
        var slide = b.Add("space.translate", 250, 120);
        var rings = b.Add("pattern.rings", 470, 120, (2, 2.5f));
        var lit = b.Add("math.remap", 700, 120, (3, 0.05f), (4, 1f));
        var hue = b.Add("math.remap", 700, 300, (3, 0.5f), (4, 0.95f));
        var sat = b.Add("math.remap", 700, 480, (3, 0.35f), (4, 1f));
        var colour = b.Add("colour.hsv", 940, 220);

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1420, 440, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(time, 0, saw, 0)
         .Wire(pitch, 0, saw, 1)
         .Wire(saw, 0, flanger, 0)
         .Wire(flanger, 0, phaser, 0)
         .Wire(phaser, 0, chorus, 0)
         .Wire(chorus, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(chorus, 1, output, NodeCatalog.OutputRightPort)

         .Wire(coord, 0, slide, 0)
         .Wire(coord, 1, slide, 1)
         .Wire(chorus, 2, slide, 2)
         .Wire(slide, 0, rings, 0)
         .Wire(slide, 1, rings, 1)
         .Wire(rings, 0, lit, 0)
         .Wire(phaser, 1, hue, 0)
         .Wire(flanger, 1, sat, 0)
         .Wire(hue, 0, colour, 0)
         .Wire(sat, 0, colour, 1)
         .Wire(lit, 0, colour, 2)
         .Wire(colour, 0, output, NodeCatalog.OutputColourPort);

        return b.Patch;
    }
}
