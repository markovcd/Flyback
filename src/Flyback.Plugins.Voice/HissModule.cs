using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// White noise and nothing else, out of arithmetic: a large multiple of the domain,
/// a sine, a larger multiple, the fraction.
/// </summary>
/// <remarks>
/// <see cref="RandomModule"/> makes white too, beside a pink and two stepped values
/// it builds whether or not anything listens — sixteen noise lookups a sample where
/// this is a sine and a fraction. It is the cheap one, for the hats, snares and
/// risers a track wants several of. What it gives up is Random's guarantee of the
/// same value on every backend: a sine of a number in the millions differs in its
/// last digits between a double and a shader's float, and the multiple makes those
/// the digits that are kept. For hiss that is the same hiss.
/// </remarks>
internal static class HissModule
{
    public const string TypeId = "flyback.voice.hiss";

    /// <summary>Far enough along the domain per second that two audio samples share nothing.</summary>
    private const float Grain = 3571f;

    /// <summary>What turns the sine's last digits into its first ones.</summary>
    private const float Scatter = 4371.3f;

    /// <summary>How far one seed is from the next, in radians — not a multiple of a half turn.</summary>
    private const float Apart = 1.7f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Hiss", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer),
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f),
        ],
        [new PortSpec("out")],
        Emit,
        "White noise, -1 to 1 times 'amp', and as cheap as a noise source gets. Put it "
        + "through a Filter and multiply by a Stroke or a Decay for hats, snares and claps; "
        + "sweep the Filter for a riser. One Hiss can feed every drum in a patch. Two with the "
        + "same 'seed' are the same noise, so give a left and a right their own. Random also "
        + "has a white output, with pink and chance beside it, and costs several times as much.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var along = em.Add(em.Mul(node[0], Grain), em.Mul(node[1], Apart));
        var white = em.Unary(OpCode.Fract, em.Mul(em.Unary(OpCode.Sin, along), Scatter));

        var bipolar = em.Ternary(OpCode.Mix, em.Constant(-1f), em.Constant(1f), white);

        return [em.Mul(bipolar, node[2])];
    }
}
