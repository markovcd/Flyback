using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// A mastering equalizer: a low cut, a low shelf, one bell and a high shelf, the
/// same on both sides.
/// </summary>
/// <remarks>
/// Every stage is one <see cref="Dsp.Filter"/> with its outputs mixed, using the
/// shelf and bell mixes from Andrew Simper's paper on the topology. They are the
/// same responses as the familiar RBJ biquads. With every gain at zero each mix
/// is the input alone, and a low cut at nought hertz is a filter that never
/// moves, so the module at its defaults is exactly a wire.
/// </remarks>
internal static class EqModule
{
    public const string TypeId = "flyback.mastering.eq";

    /// <summary>
    /// One over Q for the cut and both shelves: Butterworth, which is the flattest
    /// a two-pole corner can be.
    /// </summary>
    private static readonly float Flattest = MathF.Sqrt(2f);

    private const int Left = 0;
    private const int Right = 1;
    private const int LowCut = 2;
    private const int LowFreq = 3;
    private const int LowGain = 4;
    private const int MidFreq = 5;
    private const int MidGain = 6;
    private const int MidQ = 7;
    private const int HighFreq = 8;
    private const int HighGain = 9;

    public static NodeDef Definition { get; } = new(
        TypeId, "EQ", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true) { Help = Dsp.LeftIn },
            new PortSpec("right", NormalledFrom: Left, PatchOnly: true) { Standard = true },
            new PortSpec("low cut", PortKind.Scalar, 0f, 0f, 300f)
            {
                Knee = 20f,
                Help = "In hertz, and 0 is off. Removes rumble under it.",
            },
            new PortSpec("low freq", PortKind.Scalar, 100f, 20f, 1_000f)
            {
                Knee = 20f,
                Help = "In hertz: the low shelf is under it.",
            },
            new PortSpec("low gain", PortKind.Scalar, 0f, -12f, 12f) { Help = "The low shelf, in dB." },
            new PortSpec("mid freq", PortKind.Scalar, 1_000f, 100f, 10_000f)
            {
                Knee = 100f,
                Help = "In hertz: the middle of the bell.",
            },
            new PortSpec("mid gain", PortKind.Scalar, 0f, -12f, 12f) { Help = "The bell, in dB." },
            new PortSpec("mid q", PortKind.Scalar, 0.7f, 0.3f, 3f) { Help = "How narrow the bell is." },
            new PortSpec("high freq", PortKind.Scalar, 8_000f, 1_000f, 16_000f)
            {
                Knee = 1_000f,
                Help = "In hertz: the high shelf is over it.",
            },
            new PortSpec("high gain", PortKind.Scalar, 0f, -12f, 12f) { Help = "The high shelf, in dB." },
        ],
        [new PortSpec("left") { Help = "The left side, equalized." }, new PortSpec("right") { Help = "The right side, equalized." }],
        Emit,
        "Tone shaping for a stereo pair: a low cut, then a low shelf, a bell and a high shelf.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M2,20 C3.5,20 3.5,13 6,13 L8,13 C10,13 10.5,6 12,6 C13.5,6 14,13 16,13 "
                + "C18,13 18,16 20,16 L22,16 "
                + "M5.5,13 A1.5,1.5 0 1 1 8.5,13 A1.5,1.5 0 1 1 5.5,13 "
                + "M10.5,6 A1.5,1.5 0 1 1 13.5,6 A1.5,1.5 0 1 1 10.5,6 "
                + "M17.5,16 A1.5,1.5 0 1 1 20.5,16 A1.5,1.5 0 1 1 17.5,16",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var one = em.Constant(1f);
        var flattest = em.Constant(Flattest);

        // A is the square root of the gain, as the formulas write it.
        var lowA = Root(node[LowGain], 2f);
        var midA = Root(node[MidGain], 2f);
        var highA = Root(node[HighGain], 2f);

        var cutG = Dsp.Warp(em, node[LowCut]);
        var lowG = em.Binary(OpCode.Div, Dsp.Warp(em, node[LowFreq]), Root(node[LowGain], 4f));
        var highG = em.Mul(Dsp.Warp(em, node[HighFreq]), Root(node[HighGain], 4f));
        var midG = Dsp.Warp(em, node[MidFreq]);

        var midK = em.Binary(
            OpCode.Div,
            one,
            em.Mul(em.Binary(OpCode.Max, node[MidQ], em.Constant(0.05f)), midA));

        var lowBand = em.Mul(flattest, em.Sub(lowA, one));
        var lowLow = em.Sub(em.Mul(lowA, lowA), one);
        var midBand = em.Mul(midK, em.Sub(em.Mul(midA, midA), one));
        var highDry = em.Mul(highA, highA);
        var highBand = em.Mul(em.Mul(flattest, em.Sub(one, highA)), highA);
        var highLow = em.Sub(one, highDry);

        return [Equalized(node[Left]), Equalized(node[Right])];

        Slot Equalized(Slot dry)
        {
            var cut = Dsp.Filter(em, dry, cutG, flattest).High;

            var low = Dsp.Filter(em, cut, lowG, flattest);
            var shelved = em.Add(em.Add(cut, em.Mul(lowBand, low.Band)), em.Mul(lowLow, low.Low));

            var mid = Dsp.Filter(em, shelved, midG, midK);
            var belled = em.Add(shelved, em.Mul(midBand, mid.Band));

            var high = Dsp.Filter(em, belled, highG, flattest);
            var topped = em.Add(
                em.Add(em.Mul(highDry, belled), em.Mul(highBand, high.Band)),
                em.Mul(highLow, high.Low));

            return Dsp.Pick(em, dry, topped, live);
        }

        // The 'n'th root of a gain in decibels, as a factor.
        Slot Root(Slot decibels, float n) => em.Unary(OpCode.Exp, em.Mul(decibels, Dsp.Nepers / n));
    }
}
