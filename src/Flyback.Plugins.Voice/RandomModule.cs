using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// White and pink noise, and a random value that steps or drifts at a rate.
/// </summary>
/// <remarks>
/// Stateless: every output reads the engine's value noise at whole lattice points,
/// which returns the hash itself, so it is identical on both sinks and on the GPU.
/// Pink is Voss–McCartney — one held row per octave, summed.
/// </remarks>
internal static class RandomModule
{
    public const string TypeId = "flyback.voice.random";

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

    public static NodeDef Definition { get; } = new(
        TypeId, "Random", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("rate", PortKind.Scalar, 4f, 0f, 64f),
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer),
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f),
            new PortSpec("bias", PortKind.Scalar, 0f, -2f, 2f),
        ],
        [new PortSpec("white"), new PortSpec("pink"), new PortSpec("random"), new PortSpec("drift")],
        Emit,
        "Noise and chance, each -1 to 1 before 'amp' and 'bias'. 'white' is bright hiss for "
        + "hats and snares; 'pink' is darker, like rain. 'random' jumps to a new value 'rate' "
        + "times a second and holds it; 'drift' glides between the same values. Modules with "
        + "the same 'seed' produce the same noise, so give each its own. On the picture it "
        + "runs across its domain: on Time the frame flickers, from a coordinate it is grain.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var domain = node[0];
        var seed = node[2];

        // Split into whole seconds and the fraction, because domain * Grain would
        // overflow the hash's int after about nine minutes.
        var lanes = em.Mul(em.Unary(OpCode.Floor, domain), Lanes);
        var within = em.Unary(OpCode.Fract, domain);

        var white = Hash(lanes, em.Unary(OpCode.Floor, em.Mul(within, Grain)));

        var pink = em.Constant(0f);
        for (var row = 0; row < Rows; row++)
        {
            var index = em.Unary(OpCode.Floor, em.Mul(within, MathF.Pow(2f, Fastest - row)));
            pink = em.Add(pink, Hash(em.Add(lanes, row + 1f), index));
        }

        pink = em.Ternary(OpCode.Clamp, em.Mul(pink, PinkScale), em.Constant(-1f), em.Constant(1f));

        var along = em.Mul(domain, node[1]);
        var random = Hash(em.Unary(OpCode.Floor, along), em.Constant(ChanceRow));
        var drift = Hash(along, em.Constant(ChanceRow));

        return [Scaled(white), Scaled(pink), Scaled(random), Scaled(drift)];

        // The seed is the third axis, so a fractional seed crossfades two.
        Slot Hash(Slot x, Slot y) =>
            em.Add(em.Mul(em.Ternary(OpCode.Noise3, x, y, seed), 2f), -1f);

        Slot Scaled(Slot signal) => em.Add(em.Mul(signal, node[3]), node[4]);
    }
}
