using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// A brickwall limiter: nothing that comes out is louder than the ceiling, and
/// the gain that makes it so is already down by the time the peak arrives.
/// </summary>
/// <remarks>
/// The sound is delayed by the lookahead while the gain is computed from the
/// undelayed input. Three cells:
/// <list type="bullet">
/// <item>The aim: the lowest gain any sample in the delay needs, held for one
/// lookahead.</item>
/// <item>The next-lowest, which becomes the aim when the hold runs out.</item>
/// <item>The gain, falling linearly from unity to the aim in one lookahead and
/// recovering as a one-pole release.</item>
/// </list>
/// The final clamp only catches a swept lookahead and rounding. Oversampling
/// makes the ceiling close to true-peak. Cells but the hold store one minus their
/// value, so they start at unity with nothing pending.
/// </remarks>
internal static class LimiterModule
{
    public const string TypeId = "flyback.mastering.limiter";

    /// <summary>
    /// The longest lookahead the delay holds, in seconds. It sizes the buffer:
    /// ten milliseconds at the oversampled rate is under two thousand samples.
    /// </summary>
    private const float Longest = 0.01f;

    /// <summary>
    /// The shortest lookahead, in seconds. A delay of nothing reads a line
    /// before it has been written.
    /// </summary>
    private const float Least = 1e-4f;

    private const int Left = 0;
    private const int Right = 1;
    private const int Ceiling = 2;
    private const int Release = 3;
    private const int Lookahead = 4;

    public static NodeDef Definition { get; } = new(
        TypeId, "Limiter", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true) { Help = Dsp.LeftIn },
            new PortSpec("right", NormalledFrom: Left, PatchOnly: true) { Standard = true },
            new PortSpec("ceiling", PortKind.Scalar, -1f, -24f, 0f) { Help = "In dB. Nothing leaves louder." },
            new PortSpec("release", PortKind.Scalar, -1f, -3f, 0.5f, Display: PortDisplay.Duration)
            {
                Help = CompressorModule.ReleaseHelp,
            },
            new PortSpec("lookahead", PortKind.Scalar, 1.5f, 0.1f, Longest * 1000f)
            {
                Help = "In milliseconds: how early it sees a peak. The sound is delayed by as much.",
            },
        ],
        [
            new PortSpec("left") { Help = "The left side, held under 'ceiling'." },
            new PortSpec("right") { Help = "The right side, held under 'ceiling'." },
            new PortSpec("gain") { Help = "The gain applied." },
        ],
        Emit,
        "A brickwall limiter: nothing leaves louder than 'ceiling', and the gain is already "
        + "down when a peak arrives.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M4,7 L20,7 M2,18 C4,10 5,7 7,7 L17,7 C19,7 20,10 22,18",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var lookahead = em.Mul(node[Lookahead], 1e-3f);

        var (left, right, gain) = Limit(
            em, node[Left], node[Right], Dsp.Linear(em, node[Ceiling]), node[Release], lookahead);

        return
        [
            Dsp.Pick(em, node[Left], left, live),
            Dsp.Pick(em, node[Right], right, live),
            Dsp.Pick(em, em.Constant(1f), gain, live),
        ];
    }

    /// <summary>
    /// A pair limited to <paramref name="ceiling"/>, a lookahead late, and the gain
    /// that did it, for a module with a limiter inside it — see
    /// <see cref="MaximizerModule"/>. What the picture sees is the caller's to decide.
    /// </summary>
    /// <param name="ceiling">As a gain, not in dB.</param>
    /// <param name="release">A power of ten of seconds, as the socket has it.</param>
    /// <param name="lookahead">In seconds, held to what the delay can hold.</param>
    public static (Slot Left, Slot Right, Slot Gain) Limit(
        Emitter em, Slot left, Slot right, Slot ceiling, Slot release, Slot lookahead)
    {
        var step = em.Interval();
        var one = em.Constant(1f);

        lookahead = em.Ternary(OpCode.Clamp, lookahead, em.Constant(Least), em.Constant(Longest));

        // The gain this sample will need when it is heard.
        var peak = em.Binary(OpCode.Max, em.Unary(OpCode.Abs, left), em.Unary(OpCode.Abs, right));
        var needed = em.Binary(
            OpCode.Min, one, em.Binary(OpCode.Div, ceiling, em.Binary(OpCode.Max, peak, em.Constant(1e-9f))));

        var aimCell = em.AllocateUnitSlot();
        var nextCell = em.AllocateUnitSlot();
        var holdCell = em.AllocateUnitSlot();
        var gainCell = em.AllocateUnitSlot();

        var aim = em.Sub(one, em.UnitRead(aimCell));
        var next = em.Sub(one, em.UnitRead(nextCell));
        var hold = em.UnitRead(holdCell);
        var held = em.Sub(one, em.UnitRead(gainCell));

        // One sample over the lookahead, so rounding in the count cannot let a
        // hold run out a sample before its peak is heard.
        var full = em.Add(lookahead, step);

        // A hold run out hands the aim to whatever was waiting behind it.
        var expired = Dsp.AtLeast(em, em.Constant(0f), hold);
        var aimNow = Dsp.Pick(em, aim, next, expired);
        var holdNow = Dsp.Pick(em, hold, full, expired);
        var nextNow = Dsp.Pick(em, next, one, expired);

        // A sample needing less gain than the aim becomes the aim. Anything
        // waiting is then covered by it and is dropped.
        var lower = Dsp.AtLeast(em, aimNow, needed);
        var aimAfter = Dsp.Pick(em, aimNow, needed, lower);
        var holdAfter = Dsp.Pick(em, em.Sub(holdNow, step), full, lower);
        var nextAfter = Dsp.Pick(em, em.Binary(OpCode.Min, nextNow, needed), one, lower);

        // Down in a straight line fast enough to cover unity to the aim in one
        // lookahead, and never past the aim. Up on the release.
        var fall = em.Binary(OpCode.Div, em.Mul(em.Sub(one, aimAfter), step), lookahead);
        var falling = em.Binary(OpCode.Max, aimAfter, em.Sub(held, fall));
        var rising = em.Ternary(
            OpCode.Mix, held, aimAfter, Dsp.Closing(em, Dsp.Seconds(em, release)));
        var gain = Dsp.Pick(em, rising, falling, Dsp.AtLeast(em, held, aimAfter));

        em.UnitWrite(aimCell, em.Sub(one, aimAfter));
        em.UnitWrite(nextCell, em.Sub(one, nextAfter));
        em.UnitWrite(holdCell, holdAfter);
        em.UnitWrite(gainCell, em.Sub(one, gain));

        var zero = em.Constant(0f);
        var negative = em.Unary(OpCode.Neg, ceiling);

        return (Heard(left), Heard(right), gain);

        Slot Heard(Slot dry)
        {
            // A line reads before it writes, so it is always an evaluation later
            // than it is asked for. One is taken off, and the latency is exactly
            // the lookahead.
            var late = em.DelayLine(OpCode.Delay, dry, zero, em.Sub(lookahead, step), Longest);

            return em.Ternary(OpCode.Clamp, em.Mul(late, gain), negative, ceiling);
        }
    }
}
