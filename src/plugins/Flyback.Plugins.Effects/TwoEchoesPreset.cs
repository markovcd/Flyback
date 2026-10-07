using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The patch the "One knob, two echoes" video builds: a pluck through an Echo, and a ring
/// that a Trails carries down a tunnel, both held by one panel knob.
/// </summary>
/// <remarks>
/// The video's patch, module for module, so a viewer can open the finished one to compare
/// with theirs. The knob is the point: it sets how much comes round again in the sound and
/// in the picture at once, each over its own range.
/// </remarks>
internal sealed class TwoEchoesPreset(ModuleCatalog modules) : PresetBench(modules)
{
    public const string Name = "Two echoes";

    public const string Description =
        "One knob, two loops: a pluck through an echo, and a ring that echoes down a tunnel.";

    private const string StrokeType = "flyback.voice.stroke";

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    /// <summary>The Fill's second output: the shape's edge alone.</summary>
    private const int FillOutline = 1;

    /// <summary>The tune, a note a beat: E4 G4 B4 E5 D5 B4 G4 A4.</summary>
    private static readonly float[] Tune = [64f, 67f, 71f, 76f, 74f, 71f, 67f, 69f];

    public static Patch Build(ModuleCatalog modules) => new TwoEchoesPreset(modules).Patch(modules);

    private Patch Patch(ModuleCatalog catalog)
    {
        var repeats = Panel("Repeats", 0.6f);
        repeats.Word = "repeats";

        var clock = b.Add("seq.tempo", (0, 96f));

        // The sound: a note a beat, and an echo that keeps time with it.
        var tune = b.Add("seq.notes", (1, 1f), (2, 0.3f));
        StepsExtra.Set(tune, [.. Tune.Select(note => new Step(note))]);

        var pitch = b.Add("audio.note");
        var pluck = b.Add(NodeCatalog.AdsrTypeId, (1, Ms(3)), (2, Ms(250)), (3, 0.1f), (4, Ms(200)));
        var triangle = b.Add("osc.triangle");
        var voice = Formula("a * b", triangle, pluck);
        var echo = b.Add(EchoModule.TypeId, (5, 0.45f));
        Follows(catalog, echo, 4, repeats, 0f, 0.9f);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.75f));

        b.Wire(clock, 1, tune, 0)
         .Wire(tune, 0, pitch, 0)
         .Wire(pitch, 0, triangle, 1)
         .Wire(tune, 1, pluck, 0)
         .Wire(voice, 0, echo, 0)
         .Wire(clock, 0, echo, 1)
         .Wire(echo, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(echo, 1, output, NodeCatalog.OutputRightPort);

        // The picture: a ring on every note, and a trail that carries it inward.
        var hit = b.Add(StrokeType);
        var circle = b.Add(CircleType, (2, 0.6f));
        var ring = b.Add(FillType, (2, 0.025f));
        var lit = Formula("a * b", new Read(ring, FillOutline), hit);
        var color = b.Add("color.hsv", (1, 0.7f));
        var trail = b.Add(TrailsType, (TrailsZoom, 1.02f), (TrailsAngle, 0.01f));
        Follows(catalog, trail, TrailsPersist, repeats, 0.85f, 0.985f);

        b.Wire(clock, 1, hit, 0)
         .Wire(circle, 0, ring, 0)
         .Wire(tune, 2, color, 0)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        var patch = b.Build();
        patch.Description = Description;
        return patch;
    }
}
