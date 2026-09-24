using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// Loudness as ITU-R BS.1770 measures it, running: the momentary and short-term
/// readings a loudness meter shows, and a peak.
/// </summary>
/// <remarks>
/// K-weighting is two filters: a shelf that lifts the top by four decibels, the
/// head's own effect, and a highpass that ignores what is too low to count. Both
/// are <see cref="Dsp.Filter"/> mixes, with the corners, gains and Qs
/// libebur128 derives the standard's 48 kHz coefficients from. At 48 kHz they
/// are those coefficients, and at any other rate they are what libebur128 and
/// ffmpeg would use there.
/// <para>
/// A window's mean square is a running sum: each weighted square is added as it
/// comes in and taken away again as it comes out of a delay line the window's
/// length. The sum telescopes, so it does not drift. Integrated loudness, gated
/// over a whole program, is not attempted: it needs the whole program, which
/// is what <c>flyback render --loudness</c> has.
/// </para>
/// </remarks>
internal static class LoudnessModule
{
    public const string TypeId = "flyback.mastering.loudness";

    /// <summary>The standard's momentary window, in seconds.</summary>
    private const float Momentary = 0.4f;

    /// <summary>The standard's short-term window, in seconds.</summary>
    private const float ShortTerm = 3f;

    /// <summary>What a mean square of one reads as: the standard's -0.691.</summary>
    private const float Offset = -0.691f;

    /// <summary>The quietest reading, in LUFS: the standard's absolute gate.</summary>
    private const float Silence = -70f;

    /// <summary>The quietest peak, in dBFS.</summary>
    private const float Quietest = -100f;

    /// <summary>How fast a peak falls back, in seconds per twenty decibels: a program meter's.</summary>
    private const float PeakFall = 1.7f;

    private const float ShelfHertz = 1681.974450955533f;
    private const double ShelfDecibels = 3.999843853973347;
    private const float ShelfQ = 0.7071752369554196f;
    private const float HighpassHertz = 38.13547087602444f;
    private const float HighpassQ = 0.5003270373238773f;

    /// <summary>The shelf's gain at the top, and at its corner.</summary>
    private static readonly float Top = (float)Math.Pow(10d, ShelfDecibels / 20d);

    private static readonly float Corner = (float)Math.Pow(Math.Pow(10d, ShelfDecibels / 20d), 0.4996667741545416);

    public static NodeDef Definition { get; } = new(
        TypeId, "Loudness", ModuleCategories.Measurement,
        [
            new PortSpec("left", PatchOnly: true),
            new PortSpec("right", NormalledFrom: 0, PatchOnly: true),
        ],
        [
            new PortSpec("momentary") { Help = "LUFS over the last 0.4 s, floored at -70." },
            new PortSpec("short") { Help = "LUFS over the last 3 s, floored at -70." },
            new PortSpec("peak") { Help = "In dBFS, falling 20 dB in 1.7 s." },
        ],
        Emit,
        "A running BS.1770 loudness meter for a stereo pair: momentary and short-term "
        + "loudness, and a peak.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Measurement))
        {
            Glyph = "M3,6 L21,6 M3,10 L18,10 M3,14 L15,14 M3,18 L12,18",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var step = em.Interval();
        var zero = em.Constant(0f);

        var shelfG = Dsp.Warp(em, em.Constant(ShelfHertz));
        var shelfK = em.Constant(1f / ShelfQ);
        var highpassG = Dsp.Warp(em, em.Constant(HighpassHertz));
        var highpassK = em.Constant(1f / HighpassQ);

        // libebur128's highpass has an unnormalized numerator of 1, -2, 1; that
        // is the normalized highpass times this.
        var highpassScale = em.Add(
            em.Add(em.Constant(1f), em.Mul(highpassG, highpassK)), em.Mul(highpassG, highpassG));

        var energy = em.Add(Squared(node[0]), Squared(node[1]));

        var peak = Peak();

        return
        [
            Dsp.Pick(em, em.Constant(Silence), Window(Momentary), live),
            Dsp.Pick(em, em.Constant(Silence), Window(ShortTerm), live),
            peak,
        ];

        Slot Squared(Slot dry)
        {
            var shelf = Dsp.Filter(em, dry, shelfG, shelfK);
            var shelved = em.Add(
                em.Add(em.Mul(shelf.High, Top), em.Mul(em.Mul(shelf.Band, shelfK), Corner)),
                shelf.Low);

            var weighted = em.Mul(Dsp.Filter(em, shelved, highpassG, highpassK).High, highpassScale);

            return em.Mul(weighted, weighted);
        }

        Slot Window(float seconds)
        {
            // An evaluation short, because a line reads before it writes: what
            // leaves is what came in exactly one window ago.
            var leaving = em.DelayLine(
                OpCode.Delay, energy, zero, em.Sub(em.Constant(seconds), step), seconds);

            var cell = em.AllocateUnitSlot();
            var mean = em.Binary(
                OpCode.Max,
                em.Add(em.UnitRead(cell), em.Mul(em.Sub(energy, leaving), em.Mul(step, 1f / seconds))),
                zero);

            em.UnitWrite(cell, mean);

            var decibels = em.Add(em.Mul(em.Unary(OpCode.Log, em.Binary(OpCode.Max, mean, em.Constant(1e-12f))), 10f / MathF.Log(10f)), Offset);

            return em.Binary(OpCode.Max, decibels, em.Constant(Silence));
        }

        Slot Peak()
        {
            var level = em.Binary(OpCode.Max, em.Unary(OpCode.Abs, node[0]), em.Unary(OpCode.Abs, node[1]));

            var cell = em.AllocateUnitSlot();
            var falling = em.Mul(
                em.UnitRead(cell),
                em.Unary(OpCode.Exp, em.Mul(step, -MathF.Log(10f) / PeakFall)));
            var held = em.Binary(OpCode.Max, level, falling);

            em.UnitWrite(cell, held);

            return Dsp.Decibels(em, held, MathF.Pow(10f, Quietest / 20f));
        }
    }
}
