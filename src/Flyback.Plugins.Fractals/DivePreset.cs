using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// A dive into the Mandelbrot set and back out, with its colors cycling.
/// </summary>
/// <remarks>
/// The whole of the module in three wires: a slow triangle on 'zoom' is the dive,
/// the clock on 'shift' turns the colors, and 'color' goes straight to the
/// screen. The middle is Seahorse Valley, the crease between the main body and
/// the bulb to its left, where the detail never runs out.
/// </remarks>
internal static class DivePreset
{
    public const string Name = "Dive";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add("time");

        // Down to ten halvings and back up in a minute, starting at the top: a
        // steady ramp on zoom is a steady dive.
        var dive = b.Add(NodeCatalog.TriangleTypeId, (1, 1f / 60f), (2, 0.5f), (3, 5f), (4, 5f));

        var turn = b.Add("math.mul", (1, 0.04f));

        var set = Escape.WithIterations(
            b.Add(
                MandelbrotModule.TypeId,
                (MandelbrotModule.RePort, -0.7436439f),
                (MandelbrotModule.ImPort, 0.1318259f)),
            128);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0f));

        b.Wire(dive, 0, set, MandelbrotModule.ZoomPort)
         .Wire(clock, 0, turn, 0)
         .Wire(turn, 0, set, MandelbrotModule.ShiftPort)
         .Wire(set, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }
}
