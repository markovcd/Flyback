using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// One c going round a circle, seen as its Julia set and heard as its orbit.
/// </summary>
/// <remarks>
/// The circle is |c| = 0.7885, which grazes the Mandelbrot set's edge all the
/// way round, so the Julia set is always one of the lacy ones and the orbit keeps
/// crossing between settling and not: a tone at a fraction of the rate where it
/// settles, a hiss where it never does. Two sines a quarter turn apart are the
/// circle, and the same two wires go to both modules.
/// </remarks>
internal static class JuliaWalkPreset
{
    public const string Name = "Julia walk";

    private const float Radius = 0.7885f;

    /// <summary>Once round in two minutes.</summary>
    private const float Round = 1f / 120f;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var re = b.Add(NodeCatalog.SineTypeId, (1, Round), (2, 0.25f), (3, Radius));
        var im = b.Add(NodeCatalog.SineTypeId, (1, Round), (3, Radius));

        var julia = Escape.WithIterations(b.Add(JuliaModule.TypeId), 64);
        var orbit = b.Add(OrbitModule.TypeId, (OrbitModule.RatePort, 440f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.3f));

        b.Wire(re, 0, julia, JuliaModule.RePort)
         .Wire(im, 0, julia, JuliaModule.ImPort)
         .Wire(re, 0, orbit, OrbitModule.RePort)
         .Wire(im, 0, orbit, OrbitModule.ImPort)

         .Wire(julia, 0, output, NodeCatalog.OutputColorPort)
         .Wire(orbit, OrbitModule.LeftPort, output, NodeCatalog.OutputLeftPort)
         .Wire(orbit, OrbitModule.RightPort, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }
}
