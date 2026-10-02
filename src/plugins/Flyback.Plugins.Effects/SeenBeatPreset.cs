using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The patch the beat tutorial builds: a kick, a hat and a bass line on one Tempo, and
/// rings the kick lights up.
/// </summary>
/// <remarks>
/// The tutorial's text, module for module, so the preset and the page are one instrument
/// and a reader can open the finished patch to compare with theirs. Every sum is the
/// Expression the text's arithmetic builds.
/// </remarks>
internal sealed class SeenBeatPreset(ModuleCatalog modules) : PresetBench(modules)
{
    public const string Name = "Beat you can see";

    public const string Description =
        "A kick, a hat and a bass line on one tempo, and rings the kick lights up.";

    private const string StrokeType = "flyback.voice.stroke";

    private const string EuclidType = "flyback.voice.euclid";

    private const string DrumType = "flyback.voice.drum";

    private const string HissType = "flyback.voice.hiss";

    /// <summary>The bass line: A1 A1 C2 A1 G1 A1 E2 D2.</summary>
    private static readonly float[] Line = [33f, 33f, 36f, 33f, 31f, 33f, 40f, 38f];

    public static Patch Build(ModuleCatalog modules) => new SeenBeatPreset(modules).Patch(modules);

    private Patch Patch(ModuleCatalog catalog)
    {
        var tone = Panel("Bass filter", 0.4f);
        tone.Word = "tone";

        var tempo = b.Add("seq.tempo", (0, 118f));

        // The drums: a kick on every beat, a hat on five sixteenths in eight.
        var kick = b.Add(StrokeType, (3, 4f));
        var hats = b.Add(EuclidType, (3, 5f));
        var drum = b.Add(DrumType, (2, 48f), (3, 160f));
        var hat = b.Add(HissType, (2, 9000f), (5, 3f));

        b.Wire(tempo, 1, kick, 0)
         .Wire(tempo, 1, hats, 0)
         .Wire(kick, 0, drum, 1)
         .Wire(hats, EuclidStroke, hat, 1);

        // The bass: eight eighths through a filter the knob opens.
        var bass = b.Add("seq.notes", (1, 2f), (2, 0.4f));
        StepsExtra.Set(bass, [.. Line.Select(note => new Step(note))]);

        var pitch = b.Add("audio.note");
        var pluck = b.Add(NodeCatalog.AdsrTypeId, (1, Ms(2)), (2, Ms(180)), (3, 0.2f), (4, Ms(80)));
        var saw = b.Add("osc.saw");
        var voiced = Formula("a * b", saw, pluck);
        var low = b.Add("audio.filter", (2, 0.35f));
        Follows(catalog, low, 1, tone, 150f, 3000f);

        b.Wire(tempo, 1, bass, 0)
         .Wire(bass, 0, pitch, 0)
         .Wire(pitch, 0, saw, 1)
         .Wire(bass, 1, pluck, 0)
         .Wire(voiced, 0, low, 0);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));
        var mix = Formula("a * 0.8 + b * 0.2 + c * 0.45", drum, hat, low);

        b.Wire(mix, 0, output, NodeCatalog.OutputLeftPort);

        // The picture: rings the kick lights, a hue the bar turns, white where the hat hits.
        var time = b.Add(NodeCatalog.TimeTypeId);
        var push = Formula("a * 0.25 - b * 0.2", kick, time);
        var rings = b.Add("pattern.rings", (2, 6f));
        var glow = b.Add("math.remap");
        var lit = Formula("a * (0.12 + b * 0.88)", glow, kick);
        var hue = Formula("fract(a / 16)", new Read(tempo, 1));
        var pale = Formula("0.85 - a * 0.5", new Read(hats, EuclidStroke));
        var color = b.Add("color.hsv");

        b.Wire(push, 0, rings, 3)
         .Wire(rings, 0, glow, 0)
         .Wire(hue, 0, color, 0)
         .Wire(pale, 0, color, 1)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        var patch = b.Build();
        patch.Description = Description;
        return patch;
    }
}
