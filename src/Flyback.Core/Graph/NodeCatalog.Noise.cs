using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string NoiseTypeId = "audio.noise";

    /// <summary>White's lattice points per second (2²²), well above the oversampled audio rate.</summary>
    private const float Grain = 4_194_304f;

    /// <summary>Pink rows run from 2¹⁷ points a second down to 2⁵ (32 Hz).</summary>
    private const int Fastest = 17;

    private const int Rows = 13;

    /// <summary>Spreads whole seconds along x so white and each pink row get their own lane.</summary>
    private const float Lanes = 16f;

    /// <summary>Keeps pink near white's loudness; the rare peak past ±1 is clamped.</summary>
    private const float PinkScale = 2f / Rows;

    /// <summary>A row of y that the within-second index, which is never negative, never reaches.</summary>
    private const float ChanceRow = -1f;

    /// <summary>
    /// White and pink noise, and a random value that steps or drifts at a rate.
    /// </summary>
    /// <remarks>
    /// Stateless: every output reads the engine's value noise at whole lattice points,
    /// which returns the hash itself, so it is identical on both sinks and on the GPU.
    /// Pink is Voss–McCartney — one held row per octave, summed.
    /// </remarks>
    private static NodeDef Noise() => new(
        NoiseTypeId, "Noise", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: Clock, Domain: true),
            new PortSpec("rate", PortKind.Scalar, 4f, 0f, 64f),
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer),
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f),
            new PortSpec("bias", PortKind.Scalar, 0f, -2f, 2f),
        ],
        [new PortSpec("white", PortKind.Scalar, 0f, -1f, 1f), new PortSpec("pink", PortKind.Scalar, 0f, -1f, 1f), new PortSpec("random", PortKind.Scalar, 0f, -1f, 1f), new PortSpec("drift", PortKind.Scalar, 0f, -1f, 1f)],
        NoiseEmit,
        "Noise and chance, each -1 to 1 before 'amp' and 'bias'. 'white' is bright hiss for "
        + "hats and snares; 'pink' is darker, like rain. 'random' jumps to a new value 'rate' "
        + "times a second and holds it; 'drift' glides between the same values. Give each Noise "
        + "its own 'seed'. On the picture it runs across its domain: on Time the frame "
        + "flickers, on a coordinate it is grain. For a smooth field, use Clouds.");

    /// <summary>
    /// White and pink over a domain, each -1 to 1, for a module that is noise
    /// through something — see the Voice plugin's Hiss.
    /// </summary>
    /// <remarks>
    /// Neither has a memory, so two modules asking with the same domain and seed
    /// are handed the same samples: a module that makes its own noise plays what
    /// one fed from a shared Noise played.
    /// </remarks>
    public static (Slot White, Slot Pink) WhiteAndPink(Emitter em, Slot domain, Slot seed)
    {
        // Split into whole seconds and the fraction, because domain * Grain would
        // overflow the hash's int after about nine minutes.
        var lanes = em.Mul(em.Unary(OpCode.Floor, domain), Lanes);
        var within = em.Unary(OpCode.Fract, domain);

        var white = WhiteAt(em, lanes, within, seed);

        var pink = em.Constant(0f);
        for (var row = 0; row < Rows; row++)
        {
            var index = em.Unary(OpCode.Floor, em.Mul(within, MathF.Pow(2f, Fastest - row)));
            pink = em.Add(pink, RandomHash(em, em.Add(lanes, row + 1f), index, seed));
        }

        return (white, em.Ternary(OpCode.Clamp, em.Mul(pink, PinkScale), em.Constant(-1f), em.Constant(1f)));
    }

    /// <summary>White noise over a domain, -1 to 1: a new value every sample.</summary>
    private static Slot White(Emitter em, Slot domain, Slot seed) =>
        WhiteAt(em, em.Mul(em.Unary(OpCode.Floor, domain), Lanes), em.Unary(OpCode.Fract, domain), seed);

    private static Slot WhiteAt(Emitter em, Slot lanes, Slot within, Slot seed) =>
        RandomHash(em, lanes, em.Unary(OpCode.Floor, em.Mul(within, Grain)), seed);

    /// <summary>The hash at a lattice point, -1 to 1. The seed is the third axis, so a fractional seed crossfades two.</summary>
    private static Slot RandomHash(Emitter em, Slot x, Slot y, Slot seed) =>
        em.Add(em.Mul(em.Ternary(OpCode.Noise3, x, y, seed), 2f), -1f);

    private static Slot[] NoiseEmit(Emitter em, EmitContext node)
    {
        var domain = node[0];
        var seed = node[2];

        var (white, pink) = WhiteAndPink(em, domain, seed);

        var along = em.Mul(domain, node[1]);
        var random = RandomHash(em, em.Unary(OpCode.Floor, along), em.Constant(ChanceRow), seed);
        var drift = RandomHash(em, along, em.Constant(ChanceRow), seed);

        return [Scaled(white), Scaled(pink), Scaled(random), Scaled(drift)];

        Slot Scaled(Slot signal) => em.Add(em.Mul(signal, node[3]), node[4]);
    }
}
