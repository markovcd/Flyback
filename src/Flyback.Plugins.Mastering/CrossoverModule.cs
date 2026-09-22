using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// Splits a signal into three bands that add back up, so each can be treated on
/// its own: a Compressor on each band is a multiband compressor.
/// </summary>
/// <remarks>
/// Linkwitz-Riley at four poles: two Butterworth sections in a row at each
/// corner. A low and a high band made that way sum to an allpass: the same
/// level at every frequency, with the phase turned. The low band is split off
/// first and then sent through the allpass the upper corner makes of the other
/// two. That way all three have been through the same two allpasses, and they
/// add back to the input with its phase turned and nothing else.
/// </remarks>
internal static class CrossoverModule
{
    public const string TypeId = "flyback.mastering.crossover";

    private static readonly float Butterworth = MathF.Sqrt(2f);

    private const int In = 0;
    private const int Low = 1;
    private const int High = 2;

    public static NodeDef Definition { get; } = new(
        TypeId, "Crossover", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("low", PortKind.Scalar, 200f, 20f, 2_000f) { Knee = 20f },
            new PortSpec("high", PortKind.Scalar, 2_000f, 200f, 16_000f) { Knee = 200f },
        ],
        [new PortSpec("low"), new PortSpec("mid"), new PortSpec("high")],
        Emit,
        "Splits 'in' at 'low' and 'high' (Hz) into three bands that add back up to it. Into "
        + "Compressors, a multiband compressor.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M2,12 L7.7,12 M10.3,12 L18,5 M10.3,12 L18,19 "
                + "M7.7,12 A1.3,1.3 0 1 1 10.3,12 A1.3,1.3 0 1 1 7.7,12",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var dry = node[In];

        var (low, mid, high) = Split(em, dry, node[Low], node[High]);

        return
        [
            Dsp.Pick(em, dry, low, live),
            em.Mul(mid, live),
            em.Mul(high, live),
        ];
    }

    /// <summary>
    /// The three bands of <paramref name="dry"/>, for a module with a crossover
    /// inside it — see <see cref="MaximizerModule"/>.
    /// </summary>
    public static (Slot Low, Slot Mid, Slot High) Split(Emitter em, Slot dry, Slot lowHertz, Slot highHertz)
    {
        var k = em.Constant(Butterworth);

        var lowG = Dsp.Warp(em, lowHertz);

        // A 'high' below 'low' is read as 'low', which leaves the middle empty.
        var highG = Dsp.Warp(em, em.Binary(OpCode.Max, highHertz, lowHertz));

        var under = Lowpass(Lowpass(dry, lowG), lowG);
        var over = Highpass(Highpass(dry, lowG), lowG);

        var mid = Lowpass(Lowpass(over, highG), highG);
        var high = Highpass(Highpass(over, highG), highG);

        // The allpass of the upper corner: low minus k band plus high.
        var turned = Dsp.Filter(em, under, highG, k);
        var low = em.Sub(em.Add(turned.Low, turned.High), em.Mul(k, turned.Band));

        return (low, mid, high);

        Slot Lowpass(Slot x, Slot g) => Dsp.Filter(em, x, g, k).Low;

        Slot Highpass(Slot x, Slot g) => Dsp.Filter(em, x, g, k).High;
    }
}
