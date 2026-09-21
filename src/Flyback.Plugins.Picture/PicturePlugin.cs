using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Everything the eye needs that the engine does not ship: shapes with edges,
/// colors chosen rather than swept, and the two famous noises.
/// </summary>
/// <remarks>
/// Every module here is arithmetic over ops the engine already has — none
/// reaches for a table, a cell or a delay line, and the picture Text reads is a
/// texture there — so all cost the same at either sink and all survive to the
/// shader. That last is the gate that matters for a
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
/// fifteen modules is a worse palette than three holding eight, five and two.
/// </para>
/// </remarks>
public sealed class PicturePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.picture", "Picture");

    public PluginInfo Info { get; } = new(
        "flyback.picture",
        "Picture",
        "Shapes, lines and text to fill and combine, palettes, grading and layers, and the two "
        + "fractal noises.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                CircleModule.Definition,
                BoxModule.Definition,
                PolygonModule.Definition,
                StarModule.Definition,
                LineModule.Definition,
                TextModule.Definition,
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
                FourFormsPreset.Name,
                FourFormsPreset.Build,
                "Four forms on a turning ring, with the seam between them opening and closing."),
            new PatchPreset(
                ShapesPreset.Name,
                ShapesPreset.Build,
                "A star's points heard as bumps in a waveform, by sweeping a loop through its field.",
                PresetKind.Interplay),
            new PatchPreset(
                CaptionsPreset.Name,
                CaptionsPreset.Build,
                "Lines of text, chosen by the clock and typed out as each one arrives."),
            new PatchPreset(
                SpectrumPreset.Name,
                SpectrumPreset.Build,
                "Plasma's field colored out of a palette instead of off the hue wheel, then graded."),
            new PatchPreset(
                MarblePreset.Name,
                MarblePreset.Build,
                "A fractal bent by a fractal, which is stone."),
            new PatchPreset(
                StainedGlassPreset.Name,
                StainedGlassPreset.Build,
                "Cells as panes: the cell picks a color, the edge is the lead, and jitter slides grid to scatter."),
            new PatchPreset(
                LayersPreset.Name,
                LayersPreset.Build,
                "A sunset composed back to front: a sky, a sun screened over it, a sea multiplied in, a line inked on top."),
            new PatchPreset(
                DodgePreset.Name,
                DodgePreset.Build,
                "A game to play on the keys: walls fall down seven lanes, Z to M, and the key under "
                + "each gap is where you have to be when it arrives.",
                PresetKind.Showcase),
        ]);
    }
}
