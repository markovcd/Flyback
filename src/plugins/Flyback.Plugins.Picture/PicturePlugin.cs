using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Picture;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(CircleModule.TypeId, "Circle")]
[assembly: FlybackModule(BoxModule.TypeId, "Box")]
[assembly: FlybackModule(PolygonModule.TypeId, "Polygon")]
[assembly: FlybackModule(StarModule.TypeId, "Star")]
[assembly: FlybackModule(ArcModule.TypeId, "Arc")]
[assembly: FlybackModule(LineModule.TypeId, "Line")]
[assembly: FlybackModule(TextModule.TypeId, "Text")]
[assembly: FlybackModule(PlotModule.TypeId, "Plot")]
[assembly: FlybackModule(CombineModule.TypeId, "Combine")]
[assembly: FlybackModule(FillModule.TypeId, "Fill")]
[assembly: FlybackModule(PaletteModule.TypeId, "Palette")]
[assembly: FlybackModule(HsvModule.TypeId, "To HSV")]
[assembly: FlybackModule(GradeModule.TypeId, "Grade")]
[assembly: FlybackModule(PosteriseModule.TypeId, "Posterise")]
[assembly: FlybackModule(LayerModule.TypeId, "Layer")]
[assembly: FlybackModule(FractalModule.TypeId, "Fractal")]
[assembly: FlybackModule(CellsModule.TypeId, "Cells")]

namespace Flyback.Plugins.Picture;

/// <summary>
/// Everything the eye needs that the engine does not ship: shapes with edges,
/// colors chosen rather than swept, and the two famous noises.
/// </summary>
/// <remarks>
/// Every module here is arithmetic over ops the engine already has — none
/// reaches for a table, a cell or a delay line, and the picture Text reads is a
/// texture there — so all cost the same at either sink and all survive to the
/// shader.
/// <para>
/// The three gaps are three different kinds of missing. There was nothing to draw
/// — every field in the catalog is infinite, so a patch could make a texture of
/// any kind and not a circle. There was no way to choose a color well. And there
/// was no noise but the one, when the two everybody reaches for are the fractal
/// sum and the cell field.
/// </para>
/// <para>
/// Three categories rather than one, because a section called "Picture" holding
/// seventeen modules is a worse palette than three holding ten, five and two.
/// </para>
/// </remarks>
public sealed class PicturePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.picture", "Picture");

    public PluginInfo Info { get; } = new(
        "flyback.picture",
        "Picture",
        "Shapes, arcs, lines and text to fill and combine, pitch plotted across beats, palettes, grading "
        + "and layers, and the two fractal noises.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                CircleModule.Definition,
                BoxModule.Definition,
                PolygonModule.Definition,
                StarModule.Definition,
                ArcModule.Definition,
                LineModule.Definition,
                TextModule.Definition,
                PlotModule.Definition,
                CombineModule.Definition,
                FillModule.Definition,
                PaletteModule.Definition,
                HsvModule.Definition,
                GradeModule.Definition,
                PosteriseModule.Definition,
                LayerModule.Definition,
                FractalModule.Definition,
                CellsModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                "Four forms",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "FourForms.fbks"),
                "Four forms on a turning ring round a slowly filling arc, with the seam between them opening and closing.")
            {
                Tags = ["shapes", "color"],
            },
            new PatchPreset(
                "Shape scan",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "ShapeScan.fbks"),
                "A star's points heard as bumps in a waveform, by sweeping a loop through its field.",
                PresetKind.Interplay)
            {
                Tags = ["shapes", "scan", "synthesis"],
            },
            new PatchPreset(
                "Captions",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "Captions.fbks"),
                "Lines of text, chosen by the clock and typed out as each one arrives.")
            {
                Tags = ["text"],
            },
            new PatchPreset(
                "Spectrum",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "Spectrum.fbks"),
                "Two crossed sine fields colored out of a palette, then graded.")
            {
                Tags = ["pattern", "color"],
            },
            new PatchPreset(
                "Marble",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "Marble.fbks"),
                "A fractal bent by a fractal, which is stone.")
            {
                Tags = ["fractal", "pattern"],
            },
            new PatchPreset(
                "Stained glass",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "StainedGlass.fbks"),
                "Cells as panes: the cell picks a color, the edge is the lead, and jitter slides grid to scatter.")
            {
                Tags = ["pattern", "color"],
            },
            new PatchPreset(
                "Layers",
                PresetText.Embedded(typeof(PicturePlugin).Assembly, "Layers.fbks"),
                "A sunset composed back to front: a sky, a sun screened over it, a sea multiplied in, a line inked on top.")
            {
                Tags = ["layers", "shapes", "color"],
            },
            new PatchPreset(
                DodgePreset.Name,
                DodgePreset.Build,
                "A game to play on the keys: walls fall down seven lanes, Z to M, and the key under "
                + "each gap is where you have to be when it arrives.",
                PresetKind.Showcase)
            {
                Tags = ["game", "playable", "shapes"],
            },
        ]);
    }
}
