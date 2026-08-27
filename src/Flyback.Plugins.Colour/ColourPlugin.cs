using Flyback.Core.Graph;

namespace Flyback.Plugins.Colour;

/// <summary>
/// What a picture is coloured with, and what is done to it afterwards.
/// </summary>
/// <remarks>
/// The catalogue had five colour modules, and between them they could build a
/// colour, take one apart into channels, blend two and multiply one. What they
/// could not do was choose a colour well, read one back, or change one after the
/// fact — so every picture the machine made was coloured the same way, by putting
/// a signal into HSV's hue, and every one of them came out a rainbow.
/// <para>
/// Four modules, and each closes a different one of those. <b>Palette</b> is the
/// one that changes what patches look like: a hue sweep walks the whole wheel,
/// and a palette walks a handful of colours that are neighbours. <b>To HSV</b> is
/// the missing inverse — the one asymmetry in the catalogue, since Split is RGB's
/// own inverse and nothing was HSV's. <b>Grade</b> is the three adjustments a
/// finished picture wants, none of which is a multiply. <b>Posterise</b> is three
/// ops nobody finds.
/// </para>
/// <para>
/// All four are pure arithmetic over ops the engine already has, so all four cost
/// the same at either sink and survive to the shader — which for a video plugin
/// is the gate that matters. None reaches for a table, a cell or a delay line.
/// They are in the engine's own <c>Color</c> category rather than one of their
/// own, because they are the same kind of thing as what is already there and a
/// second colour section would be a worse palette than a longer one.
/// </para>
/// </remarks>
public sealed class ColourPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.colour", "Colour");

    public PluginInfo Info { get; } = new(
        "flyback.colour",
        "Colour",
        "Palettes, the missing HSV inverse, grading and posterising.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                PaletteModule.Definition,
                HsvModule.Definition,
                GradeModule.Definition,
                PosteriseModule.Definition,
            ]);

        registry.AddPresets([new PatchPreset(SpectrumPreset.Name, SpectrumPreset.Build)]);
    }
}
