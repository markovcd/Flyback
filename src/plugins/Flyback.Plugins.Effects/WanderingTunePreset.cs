using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The patch the "Music that writes itself" video builds: a wandering value snapped
/// to a pentatonic and played when a coin lets a note through, drawn as a score that
/// scrolls as it plays.
/// </summary>
/// <remarks>
/// The picture reads the same Quantiser as the sound, but a hold is the sound's alone,
/// so the dot is lit only at each strike: lit any longer, it would slide to the next
/// note while the last still rang.
/// </remarks>
internal sealed class WanderingTunePreset(ModuleCatalog modules) : PresetBench(modules)
{
    public const string Name = "Wandering tune";

    public const string Description =
        "A melody nobody wrote: a wandering value snapped to a pentatonic, a coin for each note, drawn as it plays.";

    /// <summary>C, D, E, G and A: a pentatonic, so no two notes it picks can clash.</summary>
    private static readonly int[] Pentatonic = [0, 2, 4, 7, 9];

    private const string WanderType = "flyback.voice.wander";

    public static Patch Build(ModuleCatalog modules) => new WanderingTunePreset(modules).Patch();

    /// <summary>Seconds as a duration knob holds them.</summary>
    private static float Decades(double seconds) => (float)Math.Log10(seconds);

    private Patch Patch()
    {
        // An eighth note at a time, and a coin for each: six in ten play.
        var clock = b.Add("seq.tempo", (0, 150f));
        var pulse = b.Add("osc.pulse", (3, 0.3f));
        var coin = b.Add("seq.chance", (1, 0.6f));

        // Two octaves of drift, D3 to E5, held to the scale for each note's length.
        var drift = b.Add(WanderType, (1, 0.7f), (2, 3f), (3, 50f), (4, 76f));
        var pitch = b.Add("audio.quantiser");
        ScaleExtra.Set(pitch, Pentatonic);

        var strike = b.Add(NodeCatalog.AdsrTypeId, (1, Decades(0.002)), (2, Decades(0.7)), (3, 0f), (4, Decades(0.4)));
        var note = b.Add("audio.note");
        var voice = b.Add("osc.triangle");
        var echo = b.Add(EchoModule.TypeId, (4, 0.4f), (5, 0.35f));

        // The strike itself, read fast enough to be over before the next note.
        var heard = b.Add(NodeCatalog.MeterTypeId, (1, -2f), (2, 0.6f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(clock, 0, pulse, 1)
         .Wire(pulse, 0, coin, 0)
         .Wire(drift, 0, pitch, 0)
         .Wire(coin, 0, pitch, 1)
         .Wire(coin, 0, strike, 0)
         .Wire(pitch, 0, note, 0)
         .Wire(note, 0, voice, 1)
         .Wire(strike, 0, voice, 3)
         .Wire(voice, 0, echo, 0)
         .Wire(clock, 0, echo, 1)
         .Wire(voice, 0, heard, 0)
         .Wire(echo, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(echo, 1, output, NodeCatalog.OutputRightPort);

        // A dot near the right edge at the note's height, in the note's color.
        var height = b.Add("math.remap", (1, 50f), (2, 76f), (3, -0.85f), (4, 0.85f));
        var hue = b.Add("math.remap", (1, 50f), (2, 76f));
        var where = b.Add("space.translate", (2, 1.4f));
        var dot = b.Add("flyback.picture.circle", (2, 0.06f));
        var ink = b.Add("flyback.picture.fill", (1, 0.03f));
        var color = b.Add("color.hsv", (1, 0.75f));
        var lit = b.Add("color.gain");
        var score = b.Add(TrailsType, (TrailsDx, -0.02f), (TrailsPersist, 0.99f));

        b.Wire(pitch, 0, height, 0)
         .Wire(pitch, 0, hue, 0)
         .Wire(height, 0, where, 3)
         .Wire(where, 0, dot, 0)
         .Wire(where, 1, dot, 1)
         .Wire(dot, 0, ink, 0)
         .Wire(hue, 0, color, 0)
         .Wire(ink, 0, color, 2)
         .Wire(color, 0, lit, 0)
         .Wire(heard, 1, lit, 1)
         .Wire(lit, 0, score, 0)
         .Wire(score, 0, output, NodeCatalog.OutputColorPort);

        var patch = b.Build();
        patch.Description = Description;
        return patch;
    }
}
