using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Supersaw;

/// <summary>
/// Seven saws around one pitch, spread apart and summed — the sound a JP-8000
/// made famous, and the same thing seen as a slow moiré when it drives a
/// picture instead of a speaker.
/// </summary>
/// <remarks>
/// The voice count is fixed at seven because an emit function cannot see knob
/// values: it runs once at compile time and writes straight-line ops, so a
/// variable voice count would have to be a variable number of instructions.
/// Seven is what the original had, and it is what the classic sound is.
/// <para>
/// That unrolling is also why this is affordable — roughly seventy ops, no
/// branches and no state, evaluated the same way one Saw is. There is no
/// band-limiting here beyond the oversampling ADR-0023 already applies to the
/// audio path; seven naive saws alias seven times as interestingly as one.
/// </para>
/// </remarks>
internal static class SupersawModule
{
    /// <summary>
    /// Where each voice sits relative to the centre, from a full step flat to a
    /// full step sharp. Symmetric, so the perceived pitch is the centre voice's
    /// whatever the detune is set to.
    /// </summary>
    private static readonly float[] Spread = [-1f, -0.62f, -0.24f, 0f, 0.24f, 0.62f, 1f];

    /// <summary>
    /// Fixed starting phases, so the voices do not all begin aligned and snap
    /// out of it. The centre voice starts at zero, which is what lets the whole
    /// module collapse to exactly one <c>Saw</c> when mix is 0.
    /// </summary>
    private static readonly float[] Offsets = [0.13f, 0.27f, 0.41f, 0f, 0.58f, 0.72f, 0.89f];

    /// <summary>
    /// Which side voices are loud in <c>out</c>. <c>wide</c> uses the other
    /// three, so the two outputs hear the same voices at different weights and
    /// drift apart as the detune spreads them — that decorrelation is the width.
    /// Both sets total the same, so neither output is louder than the other.
    /// </summary>
    private static readonly int[] Loud = [0, 2, 5];

    private static readonly int[] Quiet = [1, 4, 6];

    private const int Centre = 3;

    private const float QuietGain = 0.55f;

    /// <summary>What the six side voices sum to at full weight. Keeps the peak at 1.</summary>
    private const float SideTotal = 3f * (1f + QuietGain);

    /// <summary>Detune at full knob, as a fraction of the frequency — about ±1.4 semitones.</summary>
    private const float MaxDetune = 0.08f;

    public const string TypeId = "flyback.supersaw.osc";

    public static NodeDef Definition { get; } = new(
        TypeId, "Supersaw", "Oscillator",
        [
            // The domain the seven voices are read across, and normalled to Time
            // like every other oscillator's — a plugin's socket may name a
            // module the engine ships, and this is the one worth naming. Left on
            // a knob it was seven saws holding still, which is the same nothing
            // one saw holds.
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("freq", PortKind.Scalar, 1f, 0f, 16f),
            new PortSpec("detune", PortKind.Scalar, 0.3f, 0f, 1f),
            new PortSpec("mix", PortKind.Scalar, 0.75f, 0f, 1f),
            new PortSpec("phase", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f),
            new PortSpec("bias", PortKind.Scalar, 0f, -2f, 2f),
        ],
        [new PortSpec("out"), new PortSpec("wide")],
        Emit,
        "Seven detuned saws in one module. 'detune' spreads them apart, 'mix' fades the "
        + "six outer voices in against the centre — at 0 it is exactly a plain Saw. "
        + "Patch 'out' and 'wide' to the two channels for stereo, or use 'out' alone.");

    /// <summary>
    /// Everything is peak-normalised as it is mixed, so the output stays inside
    /// -1..1 at every setting and 'amp' means the same here as on every other
    /// oscillator. Turning mix up makes it wider, never louder.
    /// </summary>
    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var drive = inputs[0];
        var freq = inputs[1];
        var detune = inputs[2];

        // Guarded rather than trusted: the port range only constrains the knob,
        // and anything at all can be patched into it. Out of range this would
        // divide by whatever the weights happened to cancel to.
        var mix = em.Ternary(OpCode.Clamp, inputs[3], em.Constant(0f), em.Constant(1f));

        var centre = em.Add(em.Mul(mix, -0.5f), 1f);
        var sides = mix;
        var norm = em.Binary(OpCode.Div, em.Constant(1f), em.Add(centre, em.Mul(sides, SideTotal)));

        var voices = new Slot[Spread.Length];

        for (var v = 0; v < voices.Length; v++)
        {
            var frequency = Spread[v] == 0f
                ? freq
                : em.Mul(freq, em.Add(em.Mul(detune, Spread[v] * MaxDetune), 1f));

            var phase = em.Add(em.Mul(drive, frequency), inputs[4]);
            if (Offsets[v] != 0f) phase = em.Add(phase, Offsets[v]);

            voices[v] = em.Add(em.Mul(em.Unary(OpCode.Fract, phase), 2f), -1f);
        }

        return [Channel(Loud, Quiet), Channel(Quiet, Loud)];

        Slot Channel(int[] loud, int[] quiet)
        {
            var near = em.Add(em.Add(voices[loud[0]], voices[loud[1]]), voices[loud[2]]);
            var far = em.Add(em.Add(voices[quiet[0]], voices[quiet[1]]), voices[quiet[2]]);

            var mixed = em.Add(
                em.Mul(voices[Centre], centre),
                em.Mul(em.Add(near, em.Mul(far, QuietGain)), sides));

            return em.Add(em.Mul(em.Mul(mixed, norm), inputs[5]), inputs[6]);
        }
    }
}
