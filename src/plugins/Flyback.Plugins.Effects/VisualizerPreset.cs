using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The patch the music visualizer video builds: whatever the Line In hears, drawn as a
/// turning kaleidoscope of clouds that the kick flashes and the loudness colors.
/// </summary>
/// <remarks>
/// Nothing reaches the speakers: the Meters are enough for the sound to run, so a
/// monitor of the speakers can be the input without the patch hearing itself.
/// </remarks>
internal sealed class VisualizerPreset(ModuleCatalog modules) : PresetBench(modules)
{
    public const string Name = "Visualizer";

    public const string Description =
        "Any music through the Line In, folded into a turning kaleidoscope: the kick flashes it outward and the loudness spreads its colors.";

    private const string PaletteType = "flyback.picture.palette";

    public static Patch Build(ModuleCatalog modules) => new VisualizerPreset(modules).Patch();

    private Patch Patch()
    {
        // The kick is what is left under 150 Hz, read fast; the loudness is the whole
        // sound, read over two seconds. Windows in decades: 20 ms and 2 s.
        var music = b.Add("audio.in");
        var loud = b.Add(NodeCatalog.MeterTypeId, (1, 0.30103f), (2, 0.25f));
        var low = b.Add("audio.filter", (1, 150f));
        var kick = b.Add(NodeCatalog.MeterTypeId, (1, -1.69897f), (2, 0.3f));

        b.Wire(music, 0, low, 0)
         .Wire(low, 0, kick, 0)
         .Wire(music, 0, loud, 0);

        // A slow saw of half a turn either way is one whole turn every 33 seconds.
        var spin = b.Add("osc.saw", (1, 0.03f), (3, 3.1416f));
        var turn = b.Add("space.rotate");
        var fold = b.Add("space.kaleidoscope", (2, 8f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var clouds = b.Add("pattern.clouds", (3, 2.5f));
        var colors = b.Add(PaletteType, (1, 2f), (3, 0.45f), (4, 0.55f));
        var flash = b.Add("color.gain");
        var trail = b.Add(TrailsType, (TrailsZoom, 0.97f), (TrailsPersist, 0.82f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(spin, 0, turn, 2)
         .Wire(turn, 0, fold, 0)
         .Wire(turn, 1, fold, 1)
         .Wire(fold, 0, clouds, 0)
         .Wire(fold, 1, clouds, 1)
         .Wire(clock, 0, clouds, 2)
         .Wire(clouds, 0, colors, 0)
         .Wire(loud, 0, colors, 2)
         .Wire(colors, 0, flash, 0)
         .Wire(kick, 1, flash, 1)
         .Wire(flash, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        var patch = b.Build();
        patch.Description = Description;
        return patch;
    }
}
