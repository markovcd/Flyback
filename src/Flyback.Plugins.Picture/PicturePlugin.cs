using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Everything the eye needs that the engine does not ship: shapes with edges,
/// colors chosen rather than swept, and the two famous noises.
/// </summary>
/// <remarks>
/// Every module here is pure arithmetic over ops the engine already has — none
/// reaches for a table, a cell or a delay line — so all cost the same at either
/// sink and all survive to the shader. That last is the gate that matters for a
/// video plugin: a program the shader cannot draw takes the preview back to the
/// CPU for as long as the patch is loaded.
/// <para>
/// The three gaps are three different kinds of missing. There was nothing to draw
/// — every field in the catalogue is infinite, so a patch could make a texture of
/// any kind and not a circle. There was no way to choose a color well. And there
/// was no noise but the one, when the two everybody reaches for are the fractal
/// sum and the cell field.
/// </para>
/// <para>
/// Three categories rather than one, because a section called "Picture" holding
/// twelve modules is a worse palette than three holding six, four and two.
/// </para>
/// </remarks>
public sealed class PicturePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.picture", "Picture");

    public PluginInfo Info { get; } = new(
        "flyback.picture",
        "Picture",
        "Shapes to fill and combine, palettes and grading, and the two fractal noises.");

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
                PaletteModule.Definition,
                HsvModule.Definition,
                GradeModule.Definition,
                PosteriseModule.Definition,
                FractalModule.Definition,
                CellsModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                FourFormsPreset.Name,
                FourFormsPreset.Build,
                "Four forms on a turning ring, with the seam between them opening and closing.",
                PresetKind.Idea),
            new PatchPreset(
                ShapesPreset.Name,
                ShapesPreset.Build,
                "A star's points heard as bumps in a waveform, by sweeping a loop through its field.",
                PresetKind.Interplay),
            new PatchPreset(
                SpectrumPreset.Name,
                SpectrumPreset.Build,
                "Plasma's field colored out of a palette instead of off the hue wheel, then graded.",
                PresetKind.Idea),
            new PatchPreset(
                MarblePreset.Name,
                MarblePreset.Build,
                "A fractal bent by a fractal, which is stone.",
                PresetKind.Idea),
        ]);
    }
}
