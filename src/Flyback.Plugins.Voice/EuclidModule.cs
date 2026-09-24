using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A Euclidean rhythm: some number of hits spread as evenly as they go over a loop of steps.
/// </summary>
/// <remarks>
/// Stateless, like the step sequencers: step i is a hit when (i · hits) mod steps is
/// below hits, which is the even spread with a hit on the first step. Every setting
/// is a socket, since the pattern is arithmetic rather than a list folded at compile time.
/// </remarks>
internal static class EuclidModule
{
    public const string TypeId = "flyback.voice.euclid";

    /// <summary>The share of a step the gate takes to open and close — the sequencers' default shape.</summary>
    private const float Edge = 0.02f;

    private const int MostSteps = 32;

    public static NodeDef Definition { get; } = new(
        TypeId, "Euclid", ModuleCategories.Timing,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("rate", PortKind.Scalar, 4f, 0f, 32f) { Help = "Steps a second." },
            new PortSpec("steps", PortKind.Scalar, 8f, 1f, MostSteps, Display: PortDisplay.Integer)
            {
                Help = "How long the loop is.",
            },
            new PortSpec("hits", PortKind.Scalar, 3f, 0f, MostSteps, Display: PortDisplay.Integer)
            {
                Help = "How many of the steps are hits.",
            },
            new PortSpec("rotate", PortKind.Scalar, 0f, 0f, MostSteps, Display: PortDisplay.Integer)
            {
                Help = "Slides the pattern by whole steps.",
            },
            new PortSpec("gate length", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "The share of each hit step 'gate' stays open." },
            new PortSpec("curve", PortKind.Scalar, 3f, 0.1f, 16f) { Help = "Bends the fall of 'stroke'." },
        ],
        [
            new PortSpec("gate", PortKind.Scalar, 0f, 0f, 1f) { Help = "Opens on each hit, for a Decay or an ADSR." },
            new PortSpec("hit", PortKind.Scalar, 0f, 0f, 1f) { Help = "At 1 for the whole of each hit step." },
            new PortSpec("index", PortKind.Scalar, 0f, 0f, 1f) { Help = "Where the loop is, 0 to 1." },
            new PortSpec("stroke", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Falls from 1 to 0 across each hit step, for a Drum's 'level'.",
            },
        ],
        Emit,
        "A Euclidean rhythm: 'hits' spread as evenly as possible over 'steps'; 3 in 8 is the "
        + "tresillo. On the picture it runs across its domain like a Sequencer.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Timing))
        {
            Glyph = "M3,12 A9,9 0 1 1 21,12 A9,9 0 1 1 3,12 "
                + "M12,3 L12,6 M18.36,5.64 L16.24,7.76 M21,12 L18,12 M18.36,18.36 L16.24,16.24 "
                + "M12,21 L12,18 M5.64,18.36 L7.76,16.24 M3,12 L6,12 M5.64,5.64 L7.76,7.76",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var steps = em.Ternary(OpCode.Clamp, Whole(node[2]), one, em.Constant(MostSteps));
        var hits = em.Ternary(OpCode.Clamp, Whole(node[3]), zero, steps);

        var traveled = em.Mul(node[0], node[1]);
        var index = em.Binary(OpCode.Mod, em.Add(em.Unary(OpCode.Floor, traveled), Whole(node[4])), steps);

        var spread = em.Binary(OpCode.Mod, em.Mul(index, hits), steps);
        var hit = em.Sub(one, em.Binary(OpCode.Step, hits, spread));

        // Ramped like the sequencers' gate, so patched straight into a level it does not click.
        var within = em.Unary(OpCode.Fract, traveled);
        var length = em.Ternary(OpCode.Clamp, node[5], zero, one);
        var opening = em.Ternary(OpCode.Smoothstep, zero, em.Constant(Edge), within);
        var closing = em.Sub(one, em.Ternary(OpCode.Smoothstep, em.Sub(length, em.Constant(Edge)), length, within));

        // A Stroke at this rate, let through on the steps that are hits.
        var stroke = em.Mul(em.Binary(OpCode.Pow, em.Sub(one, within), node[6]), hit);

        return
        [
            em.Mul(hit, em.Mul(opening, closing)),
            hit,
            em.Binary(OpCode.Div, index, steps),
            stroke,
        ];

        Slot Whole(Slot value) => em.Unary(OpCode.Floor, em.Add(value, 0.5f));
    }
}
