using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Fractals;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(MandelbrotModule.TypeId, "Mandelbrot")]
[assembly: FlybackModule(JuliaModule.TypeId, "Julia")]
[assembly: FlybackModule(OrbitModule.TypeId, "Orbit")]

namespace Flyback.Plugins.Fractals;

/// <summary>
/// Three modules about one point c: the Mandelbrot set, which maps every c; the
/// Julia set of c, its picture; and the orbit of c, its sound.
/// </summary>
/// <remarks>
/// All three name c by the same 're' and 'im', so one pair of knobs patched into
/// each is one point seen three ways. Mandelbrot and Julia are pure arithmetic
/// and cost the same at either sink; Orbit keeps its steps in cells, which only
/// the speakers have, and draws its path afresh for the screen.
/// </remarks>
public sealed class FractalsPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.fractals", "Fractals");

    /// <summary>The category all three sit under in the palette, and nowhere else.</summary>
    public const string Category = "Fractals";

    public PluginInfo Info { get; } = new(
        "flyback.fractals",
        "Fractals",
        "The Mandelbrot set, which maps every c; the Julia set of c, its picture; and the orbit "
        + "of c, its sound. In the classic colors.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                MandelbrotModule.Definition,
                JuliaModule.Definition,
                OrbitModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                DivePreset.Name,
                DivePreset.Build,
                "A dive into the Mandelbrot set and back out, its colors cycling as it goes."),
            new PatchPreset(
                JuliaWalkPreset.Name,
                JuliaWalkPreset.Build,
                "One c going slowly round a circle: its Julia set on the screen and its orbit in the "
                + "speakers, a tone where the orbit settles and a hiss where it never does.",
                PresetKind.Interplay),
        ]);
    }
}
