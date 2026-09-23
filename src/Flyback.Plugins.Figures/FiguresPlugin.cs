using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Figures;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(PlateModule.TypeId, "Plate")]
[assembly: FlybackModule(HarmonographModule.TypeId, "Harmonograph")]
[assembly: FlybackModule(OvertonesModule.TypeId, "Overtones")]

namespace Flyback.Plugins.Figures;

/// <summary>
/// Three modules that are each one formula heard and seen: a struck plate and
/// the sand figure it settles into, a harmonograph whose drawing and chord are
/// the same pendulums, and a picture read along a row as the overtones of a tone.
/// </summary>
/// <remarks>
/// None keeps a cell. What has to be remembered — when a thing was struck, how
/// hard, where a pen was — is kept in planes, which the screen keeps per pixel
/// and the speakers keep as a cell, so one lowering serves both sinks with the
/// same age in it (ADR-0074).
/// </remarks>
public sealed class FiguresPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.figures", "Figures");

    /// <summary>The category all three sit under in the palette, and nowhere else.</summary>
    public const string Category = "Figures";

    public PluginInfo Info { get; } = new(
        "flyback.figures",
        "Figures",
        "A struck plate and its sand figure, a harmonograph whose drawing is its chord, "
        + "and any picture read as the overtones of a tone: each a picture and a sound "
        + "from one formula.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                PlateModule.Definition,
                HarmonographModule.Definition,
                OvertonesModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                VigilPreset.Name,
                VigilPreset.Build,
                "Dark ambient in D phrygian dominant: a drone of four voices that is the fog heard "
                + "through Overtones, a harmonograph drawing the chords as they swell, and a far bell, rarely.",
                PresetKind.Interplay),
        ]);
    }
}
