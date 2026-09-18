using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The Cells module's three outputs, each doing the one job only it can: the cell
/// picks a color, the edge draws the leading, and the distance rounds each pane.
/// </summary>
/// <remarks>
/// Built round the output no other noise has. A smooth field colored out of a
/// palette is a gradient; 'cell' is one number for a whole cell and a different
/// one next door, so the same palette comes out as flat panes with nothing
/// between them — and 'edge' is exactly the nothing between them, which is where
/// the lead goes.
/// <para>
/// One sweep, on 'jitter', from a square grid to a scatter and back. It is the
/// knob that says what the module is: the panes were always a grid of points, and
/// the organic look is how far each has been let wander from its square.
/// </para>
/// <para>
/// There is no sound in it. Nothing here moves in a way a listener could pick out
/// of the picture, so a tone could only share a clock with it.
/// </para>
/// </remarks>
internal static class StainedGlassPreset
{
    public const string Name = "Stained glass";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // For 'z', which drifts the points. It is depth through the field rather
        // than a position in it, so nothing is normalled to it (ADR-0050).
        var clock = b.Add("time");
        var drift = b.Add("math.mul", (1, 0.15f));

        // 0..1 over twenty seconds, from amp and bias at a half.
        var loose = b.Add("osc.sine", (1, 0.05f), (3, 0.5f), (4, 0.5f));

        var glass = b.Add(CellsModule.TypeId, (3, 5f));

        // The leading: dark exactly on the line between two panes, and clear of
        // it a little way in.
        var lead = b.Add("math.smoothstep", (0, 0.02f), (1, 0.12f));

        // Each pane a little darker towards its rim, which is what makes flat
        // color read as glass with light behind it. Backwards, because the
        // distance is nothing at the pane's own point.
        var shade = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1f), (4, 0.45f));
        var light = b.Add("math.mul");

        // A fifth of the way to the rainbow: blues with the odd warm pane, which
        // is a window rather than a test card.
        var pane = b.Add(PaletteModule.TypeId, (2, 0.2f));
        var lit = b.Add("color.gain", (2, 0f));

        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, drift, 0)
         .Wire(drift, 0, glass, 2)
         .Wire(loose, 0, glass, 4)
         .Wire(glass, 1, lead, 2)
         .Wire(glass, 0, shade, 0)
         .Wire(lead, 0, light, 0)
         .Wire(shade, 0, light, 1)
         .Wire(glass, 2, pane, 0)
         .Wire(pane, 0, lit, 0)
         .Wire(light, 0, lit, 1)
         .Wire(lit, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Cells", clock, drift, loose, glass)
         .Group("Lead and Light", lead, shade, light)
         .Group("Color", pane, lit);

        return b.Build();
    }
}
