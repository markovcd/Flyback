using Flyback.Core.Graph;

namespace Flyback.Plugins.Shapes;

/// <summary>
/// Forms: the first plugin written for the eye rather than for the ear.
/// </summary>
/// <remarks>
/// Every module the picture had was an infinite field. Coordinates, oscillators,
/// Noise, Checker and Rings all go on for ever in every direction, and the eight
/// modules under Space bend the plane they go on for ever across — so a patch
/// could make a texture of any kind and could not make a <em>thing</em>. There
/// was no circle. The nearest a patch could come was a Length into a Threshold,
/// which is a disc with a hard edge that nobody would find and that stair-steps
/// when they did.
/// <para>
/// That is the gap this fills, and it is the same shape of gap
/// [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) found on the
/// other side of the machine: the catalogue was strong on where a signal goes and
/// had nothing for what it is when it gets there.
/// </para>
/// <para>
/// Six modules and no engine change at all — not even the one-evaluation cells
/// the filter takes. Everything here is arithmetic on x and y, so it costs the
/// same at both sinks and renders identically on both. That matters more for a
/// video plugin than for any audio one: a program the shader cannot draw takes
/// the preview back to the CPU for as long as the patch is loaded, and the one
/// thing it cannot draw is a table read — a clip or a trace. Nothing says so at
/// the time, so the tests say it instead.
/// </para>
/// <para>
/// The category is "Form" rather than "Shape", which is taken: the Timbre
/// plugin's Fold and Drive shape a <em>signal</em>, and these are shapes in the
/// other sense of the word. See <see cref="Field"/> for the one convention the
/// six of them share.
/// </para>
/// </remarks>
public sealed class ShapesPlugin : IFlybackPlugin
{
    /// <summary>Where these appear in the palette. See the note on the class.</summary>
    internal const string Category = "Form";

    internal static ModuleProvider Provider { get; } = new("flyback.shapes", "Shapes");

    public PluginInfo Info { get; } = new(
        "flyback.shapes",
        "Shapes",
        "Circles, boxes, polygons and stars, as distance fields to fill and combine.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                CircleModule.Definition,
                BoxModule.Definition,
                PolygonModule.Definition,
                StarModule.Definition,
                CombineModule.Definition,
                FillModule.Definition,
            ]);

        registry.AddPresets([new PatchPreset(ShapesPreset.Name, ShapesPreset.Build)]);
    }
}
