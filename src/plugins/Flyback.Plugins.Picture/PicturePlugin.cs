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
[assembly: FlybackModule(RaymarchModule.TypeId, "Raymarch")]

namespace Flyback.Plugins.Picture;

/// <summary>
/// Everything the eye needs that the engine does not ship: shapes with edges,
/// colors chosen rather than swept, the two famous noises, and a scene in depth.
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
        + "and layers, the two fractal noises, and a 3D scene to fly through.");

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
                RaymarchModule.Definition,
            ]);

        registry.AddPresets(
        [
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Four forms", "FourForms.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Shape scan", "ShapeScan.fbks", PresetKind.Interplay),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Captions", "Captions.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Spectrum", "Spectrum.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Marble", "Marble.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Stained glass", "StainedGlass.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Layers", "Layers.fbks"),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Dodge", "Dodge.fbks", PresetKind.Showcase),
            PresetText.Preset(typeof(PicturePlugin).Assembly, "Flythrough", "Flythrough.fbks", PresetKind.Showcase),
        ]);
    }
}
