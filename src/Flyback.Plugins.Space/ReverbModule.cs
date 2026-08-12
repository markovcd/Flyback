using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Space;

/// <summary>
/// A room. Four feedback combs in parallel give the echo density, two allpasses
/// in series smear what comes out of them until the individual repeats stop
/// being audible as repeats — Schroeder's arrangement, and still the cheapest
/// thing that sounds like a space rather than a machine.
/// </summary>
/// <remarks>
/// On the video path, where there is no delay state, this does not become a wire
/// the way <see cref="DelayModule"/> does — it passes its input through scaled by
/// one minus the feedback, so the picture dims.
/// <para>
/// That is not an oversight and it cannot be fixed from here. A comb's gain at DC
/// is one over one minus its feedback, so the wet path has to be scaled by
/// exactly that to come out at unity; with no state the combs are wires and the
/// scaling is all that is left. Any normalisation that depends on the feedback
/// breaks the identity, and one that does not would make the reverb's level
/// depend on its decay. Correct levels where the module is actually used win.
/// </para>
/// </remarks>
internal static class ReverbModule
{
    public const string TypeId = "flyback.space.reverb";

    /// <summary>
    /// Comb delays in seconds, mutually prime so their repeats do not line up.
    /// Lining up is exactly what makes a reverb ring on one note.
    /// </summary>
    private static readonly float[] Combs = [0.0253f, 0.0269f, 0.0290f, 0.0308f];

    /// <summary>Shorter than any comb, or the smearing becomes another echo.</summary>
    private static readonly float[] Allpasses = [0.0126f, 0.0100f];

    /// <summary>What 'size' at 1 multiplies every delay by. Also what sizes the buffers.</summary>
    private const float Widest = 2f;

    private const float AllpassGain = 0.5f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Reverb", "Space",
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("size", PortKind.Scalar, 0.5f, 0f, 1f),
            new PortSpec("decay", PortKind.Scalar, 0.6f, 0f, 1f),
            new PortSpec("mix", PortKind.Scalar, 0.3f, 0f, 1f),
        ],
        [new PortSpec("out")],
        Emit,
        "A room. 'size' stretches every delay together, so it moves from a tiled bathroom to a "
        + "hall; 'decay' is how long the tail takes to die. Audio only — on the picture it has "
        + "nothing to remember, and dims rather than passing through.");

    private static Slot[] Emit(Emitter em, Slot[] inputs)
    {
        var dry = inputs[0];
        var size = em.Ternary(OpCode.Clamp, inputs[1], em.Constant(0f), em.Constant(1f));
        var decay = em.Ternary(OpCode.Clamp, inputs[2], em.Constant(0f), em.Constant(1f));

        // Every delay stretches by the same factor, which is what keeps the
        // ratios between them — and so the character — while the room changes size.
        var stretch = em.Add(em.Mul(size, 1.6f), 0.4f);

        // A comb's gain at DC is 1/(1-feedback), so a long tail is also an
        // enormous one unless the input is scaled down by exactly that much
        // first. Four of them in parallel then need a quarter each.
        var feedback = em.Add(em.Mul(decay, 0.38f), 0.6f);
        var scaled = em.Mul(dry, em.Mul(em.Sub(em.Constant(1f), feedback), 0.25f));

        Slot? sum = null;

        foreach (var length in Combs)
        {
            var comb = em.DelayLine(
                OpCode.Delay, scaled, feedback, em.Mul(stretch, length), length * Widest);

            sum = sum is null ? comb : em.Add(sum.Value, comb);
        }

        var wet = sum!.Value;

        foreach (var length in Allpasses)
            wet = em.DelayLine(
                OpCode.Allpass, wet, em.Constant(AllpassGain), em.Mul(stretch, length), length * Widest);

        return [em.Ternary(OpCode.Mix, dry, wet, inputs[3])];
    }
}
