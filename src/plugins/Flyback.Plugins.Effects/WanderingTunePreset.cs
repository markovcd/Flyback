using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The patch the "Music that writes itself" video builds: a wandering value snapped
/// to a pentatonic, one note a beat of a wandering length, drawn as a score that
/// scrolls as it plays.
/// </summary>
/// <remarks>
/// Nothing is held: every Wander walks across whole beats, so a note's pitch cannot
/// move while it sounds, and the picture reads the very pitch the speakers play.
/// </remarks>
internal sealed class WanderingTunePreset(ModuleCatalog modules) : PresetBench(modules)
{
    public const string Name = "Wandering tune";

    public const string Description =
        "A melody nobody wrote: a wandering value snapped to a pentatonic, each note its own length, drawn as it plays.";

    /// <summary>C, D, E, G and A: a pentatonic, so no two notes it picks can clash.</summary>
    private static readonly int[] Pentatonic = [0, 2, 4, 7, 9];

    private const string WanderType = "flyback.voice.wander";

    public static Patch Build(ModuleCatalog modules) => new WanderingTunePreset(modules).Patch();

    /// <summary>Seconds as a duration knob holds them.</summary>
    private static float Decades(double seconds) => (float)Math.Log10(seconds);

    private Patch Patch()
    {
        // One note a beat. The Wanders walk across the beat count rounded down, so each
        // holds one value for a whole beat, the same on the picture as in the speakers.
        var clock = b.Add("seq.tempo", (0, 300f));
        var step = b.Add("math.floor");
        var phase = b.Add("math.fract");

        // Two octaves of drift, D3 to E5, snapped to the scale.
        var drift = b.Add(WanderType, (1, 0.14f), (2, 3f), (3, 50f), (4, 76f));
        var pitch = b.Add("audio.quantiser");
        ScaleExtra.Set(pitch, Pentatonic);

        // How much of its beat each note lasts; below nought, it is a rest.
        var length = b.Add(WanderType, (1, 3.1f), (2, 11f), (3, -0.4f), (4, 0.8f));
        var gate = b.Add("math.step");

        var strike = b.Add(NodeCatalog.AdsrTypeId, (1, Decades(0.004)), (2, Decades(0.15)), (3, 0.6f), (4, Decades(0.03)));
        var note = b.Add("audio.note");
        var voice = b.Add("osc.triangle");
        var echo = b.Add(EchoModule.TypeId, (2, 6f), (3, 4f), (4, 0.4f), (5, 0.35f));

        // The strike itself, read fast enough to be over before the next note.
        var heard = b.Add(NodeCatalog.MeterTypeId, (1, -2f), (2, 0.6f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));

        b.Wire(clock, 1, step, 0)
         .Wire(clock, 1, phase, 0)
         .Wire(step, 0, drift, 0)
         .Wire(step, 0, length, 0)
         .Wire(drift, 0, pitch, 0)
         .Wire(phase, 0, gate, 0)
         .Wire(length, 0, gate, 1)
         .Wire(gate, 0, strike, 0)
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
