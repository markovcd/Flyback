using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string ReverbTypeId = "audio.reverb";

    /// <summary>
    /// Comb delays in seconds, mutually prime so their repeats do not line up.
    /// Lining up is exactly what makes a reverb ring on one note.
    /// </summary>
    private static readonly float[] ReverbCombs =
        [0.0253f, 0.0269f, 0.0290f, 0.0308f, 0.0322f, 0.0338f, 0.0353f, 0.0367f];

    /// <summary>
    /// How fast each comb's length wanders, in hertz. Mutually prime like the
    /// lengths themselves and for the same reason: eight combs breathing together
    /// would be a chorus on the tail rather than the absence of a resonance.
    /// </summary>
    private static readonly float[] ReverbWanders =
        [0.51f, 0.63f, 0.77f, 0.89f, 1.03f, 1.17f, 1.29f, 1.41f];

    /// <summary>Shorter than any comb, or the smearing becomes another echo.</summary>
    private static readonly float[] ReverbAllpasses = [0.0126f, 0.0100f, 0.0077f, 0.0051f];

    /// <summary>
    /// The same chain for the other channel, every length moved by half a
    /// millisecond: small enough that both chains smear the same way, different
    /// enough that they do not smear into the same pattern.
    /// </summary>
    private static readonly float[] ReverbWidened = [0.0131f, 0.0105f, 0.0082f, 0.0056f];

    /// <summary>What 'size' at 1 multiplies every delay by. Also what sizes the buffers.</summary>
    private const float ReverbWidest = 2f;

    /// <summary>
    /// A little more buffer than <see cref="ReverbWidest"/> alone would ask for, so
    /// that a comb at full size still has somewhere to wander into rather than
    /// pinning against the end of its own line.
    /// </summary>
    private const float ReverbHeadroom = 1.01f;

    /// <summary>
    /// How far a comb's length wanders, as a fraction of itself. Enough to stop
    /// a resonance settling, far too little to be heard as pitch movement — past
    /// about a percent the tail starts to warble and the room turns into a tape.
    /// </summary>
    private const float ReverbWander = 0.0015f;

    private const float ReverbAllpassGain = 0.5f;

    /// <summary>Shortest and longest gap before the first reflection arrives.</summary>
    private const float ReverbNearestWall = 0.005f;

    private const float ReverbFurthestWall = 0.05f;

    /// <summary>
    /// Where the tail loses its highs, in hertz — the corner of the lowpass inside
    /// every comb's loop. A constant and not a socket: it is a property of the
    /// surfaces a room is made of rather than a gesture anyone performs.
    /// </summary>
    private const float ReverbAbsorption = 4_000f;

    /// <summary>Where the bank stops listening, in hertz. Below hearing and above nothing.</summary>
    private const float ReverbRumble = 20f;

    /// <summary>
    /// What the arithmetic in <see cref="ReverbLevel"/> does not account for, measured
    /// rather than derived: about four decibels.
    /// </summary>
    /// <remarks>
    /// None of these combs is ideal — every line is read at a fractional sample
    /// and interpolated, which is a gentle lowpass applied on every pass; the
    /// allpasses stop being exactly allpass for it; the highpass takes its corner
    /// off the bottom. Each is small and none is worth modeling to recover a
    /// number that can be measured.
    /// </remarks>
    private const float ReverbMakeup = 1.7f;

    /// <summary>
    /// A room. Eight feedback combs in parallel give the echo density, each losing
    /// its highs a little faster than its lows on every trip round; two chains of
    /// four allpasses smear what comes out until the repeats stop being audible as
    /// repeats. Schroeder's arrangement with Moorer's correction.
    /// </summary>
    /// <remarks>
    /// A comb with a plain gain returns every repeat as bright as the one before,
    /// which is the metallic ring that gives a cheap reverb away — no real space does
    /// it, because air and soft surfaces take the top off every reflection. A
    /// one-pole lowpass inside each loop is the whole of that, cornered at
    /// <see cref="ReverbAbsorption"/>.
    /// <para>
    /// The rest is density: eight combs and four allpasses fill the gaps between
    /// early repeats that would otherwise be heard as separate events. The comb
    /// delays wander by a fraction of a percent under mutually prime sines, which
    /// stops a sustained note settling into a standing pattern.
    /// </para>
    /// <para>
    /// <c>out</c> and <c>wide</c> share one comb bank and part company at the
    /// allpasses. Two full banks would decorrelate the tail's envelope as well as its
    /// smear, and would double the seventeen delay lines this reads every sample.
    /// </para>
    /// </remarks>
    private static NodeDef Reverb() => new(
        ReverbTypeId, "Reverb", ModuleCategories.TimeEffects,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("size", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "Stretches every delay and the first reflection, from a bathroom to a hall.",
            },
            new PortSpec("decay", PortKind.Scalar, 0.6f, 0f, 1f) { Help = "How long the tail lasts, darkening as it goes." },
            new PortSpec("mix", PortKind.Scalar, 0.3f, 0f, 1f)
            {
                Help = "A straight crossfade, since the tail comes out at about the level that went in.",
            },
        ],
        [new PortSpec("out"), new PortSpec("wide")],
        ReverbEmit,
        "A room. 'out' and 'wide' are the tail smeared two ways: both for stereo, or 'out' "
        + "alone. Audio only: a wire on the picture.");

    private static Slot[] ReverbEmit(Emitter em, EmitContext inputs)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var dry = inputs[0];
        var size = em.Ternary(OpCode.Clamp, inputs[1], zero, one);
        var decay = em.Ternary(OpCode.Clamp, inputs[2], zero, one);

        // Every delay stretches by the same factor, which is what keeps the
        // ratios between them — and so the character — while the room changes size.
        var stretch = em.Add(em.Mul(size, 1.6f), 0.4f);
        var feedback = em.Add(em.Mul(decay, 0.38f), 0.6f);

        // How far the renderer's clock moves per evaluation, which is the rate
        // said the other way round. Nothing tells a module what rate it runs at,
        // and both filters below are written in hertz — ADR-0042.
        var step = em.Interval();

        // The gap before the first reflection, which is what a listener reads as
        // the distance to the nearest surface. Ahead of the bank rather than
        // inside it, so it moves the whole room back rather than lengthening it:
        // without one the wet starts on top of the dry and smears the source
        // instead of placing it somewhere.
        var entrance = em.DelayLine(
            OpCode.Delay,
            dry,
            zero,
            em.Add(em.Mul(size, ReverbFurthestWall - ReverbNearestWall), ReverbNearestWall),
            ReverbFurthestWall);

        var scaled = em.Mul(ReverbBlocked(em, entrance, step), ReverbLevel(em, feedback, one));

        // One damping coefficient for all eight loops, because it is a function
        // of the corner and of the rate and of nothing that varies between them.
        // The corner is fixed but the rate is not, so this is still worked out
        // rather than folded in: the same room has to mean the same hertz at the
        // audio path's oversampled rate and at any other — ADR-0042.
        var damping = em.Ternary(
            OpCode.Clamp,
            em.Sub(one, em.Unary(OpCode.Exp, em.Mul(step, -MathF.Tau * ReverbAbsorption))),
            zero,
            one);

        var now = em.Load(OpCode.LoadT);

        Slot? sum = null;

        for (var i = 0; i < ReverbCombs.Length; i++)
        {
            var length = ReverbCombs[i];

            // The loop is drawn by hand rather than left to the delay op's own
            // feedback, because what goes back round has to be damped on the way
            // and the op writes its line first. So the line is taken at no
            // feedback and the return trip is a one-evaluation cell (ADR-0041).
            var carried = em.AllocateUnitSlot();
            var stored = em.AllocateUnitSlot();

            var wobble = em.Unary(
                OpCode.Sin,
                em.Mul(em.Phase(now, em.Constant(ReverbWanders[i]), zero), MathF.Tau));

            var heard = em.DelayLine(
                OpCode.Delay,
                em.Add(scaled, em.UnitRead(carried)),
                zero,
                em.Mul(em.Mul(stretch, length), em.Add(em.Mul(wobble, ReverbWander), 1f)),
                length * ReverbWidest * ReverbHeadroom);

            // A one-pole lowpass, running at whatever rate the program runs at.
            // Its gain at DC is one, which leaves the level below untouched:
            // damping changes the color of the tail and never how loud it is.
            var previous = em.UnitRead(stored);
            var damped = em.Add(previous, em.Mul(damping, em.Sub(heard, previous)));

            em.UnitWrite(stored, damped);
            em.UnitWrite(carried, em.Mul(damped, feedback));

            sum = sum is null ? heard : em.Add(sum.Value, heard);
        }

        var wet = sum!.Value;
        var gain = em.Constant(ReverbAllpassGain);

        // What the module means where there is nothing to remember. Every delay
        // line is a wire on the video path, so the bank would hand the picture back
        // some multiple of itself — and which multiple would depend on the decay,
        // making the picture's brightness follow a knob about how long a sound
        // takes to die. Deciding it here makes a Reverb what a Delay already is on
        // the screen: a wire (ADR-0041).
        var live = em.HasMemory();
        var mix = inputs[3];

        Slot Room(float[] lengths)
        {
            var smeared = wet;

            foreach (var length in lengths)
                smeared = em.DelayLine(
                    OpCode.Allpass, smeared, gain, em.Mul(stretch, length), length * ReverbWidest);

            return em.Ternary(OpCode.Mix, dry, em.Ternary(OpCode.Mix, dry, smeared, live), mix);
        }

        return [Room(ReverbAllpasses), Room(ReverbWidened)];
    }

    /// <summary>
    /// What the bank is fed, scaled so the tail comes back out at about the level
    /// that went in.
    /// </summary>
    /// <remarks>
    /// A comb's gain at DC is one over one minus its feedback, but that is the top
    /// of its tallest peak. A tail is broadband, and what a comb does to broadband
    /// is set by its energy: the amplitude gain is the root of one over one minus
    /// the feedback squared. Between the two lies six decibels at the shortest
    /// decay and twenty at the longest. The eight combs' delays are mutually prime
    /// so what comes out does not line up, and uncorrelated signals add as power,
    /// so the bank is the root of eight of one of them rather than eight.
    /// </remarks>
    private static Slot ReverbLevel(Emitter em, Slot feedback, Slot one) =>
        em.Mul(
            em.Unary(OpCode.Sqrt, em.Sub(one, em.Mul(feedback, feedback))),
            ReverbMakeup / MathF.Sqrt(ReverbCombs.Length));

    /// <summary>
    /// A one-pole highpass on the way into the bank, cornered below hearing.
    /// </summary>
    /// <remarks>
    /// DC is the single frequency the combs amplify most — fifty times over at the
    /// longest decay — and the broadband scaling above does nothing to hold it
    /// down. A microphone would rarely hand a reverb any DC; a pluck envelope, an
    /// offset Remap and a slow LFO are all ordinary things to patch in here and all
    /// are mostly DC. Blocking it at the door is what lets the tail be loud and the
    /// bank be safe at once.
    /// </remarks>
    private static Slot ReverbBlocked(Emitter em, Slot signal, Slot step)
    {
        var previousIn = em.AllocateUnitSlot();
        var previousOut = em.AllocateUnitSlot();

        var pole = em.Unary(OpCode.Exp, em.Mul(step, -MathF.Tau * ReverbRumble));

        var lastIn = em.UnitRead(previousIn);
        var lastOut = em.UnitRead(previousOut);

        var blocked = em.Add(em.Sub(signal, lastIn), em.Mul(pole, lastOut));

        em.UnitWrite(previousIn, signal);
        em.UnitWrite(previousOut, blocked);

        return blocked;
    }
}
