using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A picture composed back to front, the way an image editor stacks one: a sky, a
/// sun laid over it, a sea multiplied into both, a horizon inked on top and a
/// vignette round the lot.
/// </summary>
/// <remarks>
/// Every other preset here ends in one color; this one is about what happens
/// after that. A Fill is a mask as well as a picture, and a Layer's 'amount' is
/// where a mask goes — so each form decides where its layer shows, and the mode on
/// the node decides what showing means. Screen for the sun, because light adds;
/// multiply for the sea, because water darkens what is behind it and lets it
/// through, which is why the sun can be seen going under.
/// <para>
/// The sun is the one thing that moves, and slowly: what is being shown is an
/// order, and an order is easier to read in a picture that holds still.
/// </para>
/// <para>
/// There is no sound in it. A sunset has none of its own, and one chosen to go
/// with it would be a second idea.
/// </para>
/// </remarks>
internal static class LayersPreset
{
    public const string Name = "Layers";

    /// <summary>Where the sea meets the sky.</summary>
    private const float Horizon = -0.2f;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Here for 'y': the sky is a gradient up the frame and the swell is bands
        // across it, and both are the pixel's own height read as a number.
        var coord = b.Add("coord");

        // The back: a palette read up the frame from the horizon, warm at the
        // bottom and dark at the top.
        var height = b.Add("math.remap", (1, Horizon), (2, 1f), (3, 0f), (4, 0.45f));
        var sky = b.Add(PaletteModule.TypeId, (2, 0.1f), (3, 0.55f), (4, 0.45f));

        // The sun, from a third of the way up the sky to half under the water.
        var bob = b.Add("osc.sine", (1, 0.05f), (3, 0.25f), (4, 0.05f));
        var rise = b.Add("space.translate");
        var disc = b.Add(CircleModule.TypeId, (2, 0.32f));
        var sun = b.Add(FillModule.TypeId, (1, 0.01f));
        var glow = b.Add("color.hsv", (0, 0.11f), (1, 0.55f), (2, 1f));
        var lit = Mode(b.Add(LayerModule.TypeId), "screen");

        // The sea: everything under the horizon, in a blue whose brightness is
        // a slow swell. Wide enough to cover any frame and deep enough to reach
        // the bottom of it.
        var below = b.Add("space.translate", (3, Horizon - 0.8f));
        var slab = b.Add(BoxModule.TypeId, (2, 3f), (3, 0.8f));
        var sea = b.Add(FillModule.TypeId, (1, 0.004f));
        var clock = b.Add("time");
        var roll = b.Add("math.mul", (1, 0.2f));
        var bands = b.Add("osc.sine", (1, 6f));
        var swell = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.55f), (4, 0.95f));
        var water = b.Add("color.hsv", (0, 0.6f), (1, 0.7f));
        var wet = Mode(b.Add(LayerModule.TypeId), "multiply");

        // The front: one stroke along the horizon, laid on as light.
        var edge = b.Add(LineModule.TypeId, (2, -2f), (3, Horizon), (4, 2f), (5, Horizon), (6, 0.004f));
        var shore = b.Add(FillModule.TypeId, (1, 0.004f));
        var inked = b.Add("color.ink", (2, 1f), (3, 0.85f), (4, 0.6f));

        var framed = b.Add("color.vignette", (3, 0.7f), (4, 1.9f), (5, 0.75f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, 1, height, 0)
         .Wire(height, 0, sky, 0)

         .Wire(bob, 0, rise, 3)
         .Wire(rise, 0, disc, 0)
         .Wire(rise, 1, disc, 1)
         .Wire(disc, 0, sun, 0)
         .Wire(sky, 0, lit, 0)
         .Wire(glow, 0, lit, 1)
         .Wire(sun, 0, lit, 2)

         .Wire(below, 0, slab, 0)
         .Wire(below, 1, slab, 1)
         .Wire(slab, 0, sea, 0)
         .Wire(clock, 0, roll, 0)
         .Wire(coord, 1, bands, 0)
         .Wire(roll, 0, bands, 2)
         .Wire(bands, 0, swell, 0)
         .Wire(swell, 0, water, 2)
         .Wire(lit, 0, wet, 0)
         .Wire(water, 0, wet, 1)
         .Wire(sea, 0, wet, 2)

         .Wire(edge, 0, shore, 0)
         .Wire(wet, 0, inked, 0)
         .Wire(shore, 0, inked, 1)
         .Wire(inked, 0, framed, 0)
         .Wire(framed, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Sky", coord, height, sky)
         .Group("Sun", bob, rise, disc, sun, glow, lit)
         .Group("Sea", below, slab, sea, clock, roll, bands, swell, water, wet)
         .Group("Horizon", edge, shore, inked, framed);

        return b.Build();
    }

    /// <summary>A Layer with its blend mode chosen, which is a setting and not a knob.</summary>
    private static NodeInstance Mode(NodeInstance layer, string mode)
    {
        layer.SetState("layer", new JsonObject { ["mode"] = mode });
        return layer;
    }
}
