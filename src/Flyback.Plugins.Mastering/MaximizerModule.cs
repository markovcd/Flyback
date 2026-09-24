using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// One knob that makes a mix louder, denser and more even: a three-band
/// compressor into a limiter, with every setting worked out from 'amount' and a
/// style.
/// </summary>
/// <remarks>
/// The kind of box a mix is put through at the end when there is no time to
/// master it. The two sides are split at 150 Hz and 2.5 kHz. Each band is
/// compressed on its own, both sides by one gain, so a kick pumping the bass does
/// not pull the cymbals down with it. The bands are summed with makeup and a
/// style's tilt, and limited to -1 dB.
/// <para>
/// 'amount' is how far every threshold comes down and how much of the makeup and
/// tilt is applied. At nought the thresholds sit at full scale and the tilt is
/// flat, so what is left is the crossover's allpass, the limiter's latency and its
/// ceiling. It is not a wet/dry mix: the crossover turns the phase and the limiter
/// delays, so mixing the dry signal back in would comb-filter.
/// </para>
/// <para>
/// The style is a socket rather than a setting because it changes only the
/// numbers the ops are given, not which ops there are: ADR-0097's rule. The four
/// are voiced by ear, not copied from anything.
/// </para>
/// </remarks>
internal static class MaximizerModule
{
    public const string TypeId = "flyback.mastering.maximizer";

    private const float LowCorner = 150f;
    private const float HighCorner = 2_500f;
    private const float Knee = 6f;
    private const float Ceiling = -1f;
    private const float LookaheadSeconds = 0.0015f;

    /// <summary>The limiter's release, as a power of ten of seconds: 50 ms.</summary>
    private const float LimiterRelease = -1.3f;

    /// <summary>How much of the compression a band's makeup gives back.</summary>
    private const float GivenBack = 0.5f;

    /// <summary>The Drive module's curve at a drive of two, for the loud style.</summary>
    private const float Drive = 2f;

    private const int Left = 0;
    private const int Right = 1;
    private const int Amount = 2;
    private const int Style = 3;

    /// <summary>
    /// Each style's numbers, glue, punch, bright and loud in that order. Times are
    /// powers of ten of seconds, as a socket has them.
    /// </summary>
    private static readonly float[] Ratio = [3f, 4f, 3f, 6f];
    private static readonly float[] Attack = [-2f, -1.5f, -2f, -2.5f];
    private static readonly float[] Release = [-0.9f, -1.1f, -0.9f, -1.2f];
    private static readonly float[] Depth = [18f, 18f, 18f, 24f];
    private static readonly float[] LowTilt = [0f, 2f, 0f, 1f];
    private static readonly float[] MidTilt = [0f, 0f, -1f, 0f];
    private static readonly float[] HighTilt = [0f, 1f, 3f, 1f];
    private static readonly float[] Saturation = [0f, 0f, 0f, 0.5f];

    public static NodeDef Definition { get; } = new(
        TypeId, "Maximizer", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true) { Help = Dsp.LeftIn },
            new PortSpec("right", NormalledFrom: Left, PatchOnly: true) { Help = SocketHelp.Right },
            new PortSpec("amount", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "How hard it works: the thresholds, the makeup and the style's tilt follow it.",
            },
            new PortSpec("style", PortKind.Scalar, 1f, 1f, Display: PortDisplay.Integer)
            {
                Help = "1 glues, 2 punches, 3 brightens, 4 is loudest.",
            },
        ],
        [new PortSpec("left") { Help = "The left side, louder and held under -1 dB." }, new PortSpec("right") { Help = "The right side, louder and held under -1 dB." }],
        Emit,
        "One knob to make a mix louder and denser: three bands compressed, then limited to -1 "
        + "dB.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M12,20 L12,6 M7,11 L12,6 L17,11 M4,4 L20,4",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var amount = em.Ternary(OpCode.Clamp, node[Amount], em.Constant(0f), em.Constant(1f));
        var style = node[Style];

        var ratio = Choose(Ratio);
        var attack = Choose(Attack);
        var release = Choose(Release);
        var depth = em.Mul(Choose(Depth), amount);

        var threshold = em.Unary(OpCode.Neg, depth);
        var knee = em.Constant(Knee);

        // Half of what the ratio takes off a level at the threshold's depth,
        // given back: louder as the amount goes up, not level-matched.
        var one = em.Constant(1f);
        var taken = em.Mul(depth, em.Sub(one, em.Binary(OpCode.Div, one, ratio)));
        var given = em.Mul(taken, GivenBack);

        var lowCorner = em.Constant(LowCorner);
        var highCorner = em.Constant(HighCorner);

        var (lowL, midL, highL) = CrossoverModule.Split(em, node[Left], lowCorner, highCorner);
        var (lowR, midR, highR) = CrossoverModule.Split(em, node[Right], lowCorner, highCorner);

        var low = Band(lowL, lowR, LowTilt);
        var mid = Band(midL, midR, MidTilt);
        var high = Band(highL, highR, HighTilt);

        var summedL = em.Add(em.Add(low.Left, mid.Left), high.Left);
        var summedR = em.Add(em.Add(low.Right, mid.Right), high.Right);

        var saturation = em.Mul(Choose(Saturation), amount);

        var (left, right, _) = LimiterModule.Limit(
            em,
            Saturated(summedL),
            Saturated(summedR),
            Dsp.Linear(em, em.Constant(Ceiling)),
            em.Constant(LimiterRelease),
            em.Constant(LookaheadSeconds));

        return [Dsp.Pick(em, node[Left], left, live), Dsp.Pick(em, node[Right], right, live)];

        (Slot Left, Slot Right) Band(Slot bandL, Slot bandR, float[] tilt)
        {
            var level = em.Binary(OpCode.Max, em.Unary(OpCode.Abs, bandL), em.Unary(OpCode.Abs, bandR));
            var gain = CompressorModule.Gain(em, level, threshold, ratio, attack, release, knee);

            var makeup = Dsp.Linear(em, em.Add(given, em.Mul(Choose(tilt), amount)));
            var applied = em.Mul(gain, makeup);

            return (em.Mul(bandL, applied), em.Mul(bandR, applied));
        }

        // The Drive module's curve, faded in by 'saturation'.
        Slot Saturated(Slot x)
        {
            var driven = em.Mul(x, Drive);
            var curve = em.Binary(OpCode.Div, driven, em.Add(em.Unary(OpCode.Abs, driven), 1f));
            var normalized = em.Mul(curve, (Drive + 1f) / Drive);

            return em.Ternary(OpCode.Mix, x, normalized, saturation);
        }

        // The style's entry in a table, for a style counted from one.
        Slot Choose(float[] values)
        {
            var chosen = em.Constant(values[0]);

            for (var i = 1; i < values.Length; i++)
                chosen = Dsp.Pick(em, chosen, em.Constant(values[i]), Dsp.AtLeast(em, style, em.Constant(i + 0.5f)));

            return chosen;
        }
    }
}
