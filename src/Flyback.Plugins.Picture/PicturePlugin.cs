using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Everything the eye needs that the engine does not ship: shapes with edges,
/// colors chosen rather than swept, and the two famous noises.
/// </summary>
/// <remarks>
/// One plugin covering what reads as three distinct subjects — Shapes, Color
/// and Noise — on a line the code already kept and never named.
/// Every module here is pure arithmetic over ops the engine already has. None
/// reaches for a table, a cell or a delay line, so all of them cost the same at
/// either sink and all of them survive to the shader. That last is the gate that
/// matters for a video plugin and nothing else here does: a program the shader
/// cannot draw takes the preview back to the CPU for as long as the patch is
/// loaded, and the one thing it cannot draw is a table read.
/// <para>
/// The three gaps they were written to fill still stand, and they are worth
/// keeping distinct because they are three different kinds of missing. There was
/// no thing to draw — Coordinates, the oscillators, Noise, Checker and Rings are
/// all infinite fields, and the eight modules under Geometry bend the plane they
/// go on for ever across, so a patch could make a texture of any kind and could
/// not make a circle. There was no way to choose a color well: every picture the
/// machine made went through HSV's hue and came out a rainbow. And there was no
/// noise but the one, when the two that everybody reaches for are the fractal
/// sum and the cell field.
/// </para>
/// <para>
/// Three categories rather than one, because they are three subjects and a
/// section called "Picture" holding twelve modules would be a worse palette than
/// three holding six, four and two. That one assembly supplies all three is a
/// fact about installing, not about finding.
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
