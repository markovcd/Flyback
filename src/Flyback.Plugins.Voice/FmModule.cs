using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// Four sines leaning on one another's phases, wired by one of five algorithms: an
/// electric piano, a brass stab, a slap bass, a bell, a glassy pad.
/// </summary>
/// <remarks>
/// Operator one is always heard and always at the pitch; the other three sit at a
/// ratio above it and, depending on the algorithm, bend something else or are heard
/// themselves. The algorithm is a setting rather than a socket because it decides
/// which ops are emitted (ADR-0097). How hard a modulator bends is its index times
/// the level times 'tone', as in the <see cref="BellModule"/>, so a struck note is
/// bright and rings pure with one envelope. There is no operator feedback: it would
/// need a cell, which would make the module audio-only, and every other oscillator
/// here can draw a picture. With only operator two bending operator one it is a
/// Bell, to the bit.
/// </remarks>
internal static class FmModule
{
    public const string TypeId = "flyback.voice.fm";

    public const string StateKey = "fm";

    public const string AlgorithmKey = "algorithm";

    private const string Stack = "stack";

    private const string Branch = "branch";

    private const string Fan = "fan";

    private const string Pair = "pair";

    private const string Organ = "organ";

    private const float Tau = 6.283185307179586f;

    private const int In = 0, Freq = 1, Level = 2, Tone = 3, Ratio2 = 4, Index2 = 7;

    public static NodeDef Definition { get; } = new(
        TypeId, "FM", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("freq", PortKind.Scalar, 440f, 20f, 4000f),
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f),
            new PortSpec("tone", PortKind.Scalar, 1f, 0f, 2f),
            new PortSpec("ratio2", PortKind.Scalar, 1f, 0.25f, 16f),
            new PortSpec("ratio3", PortKind.Scalar, 2f, 0.25f, 16f),
            new PortSpec("ratio4", PortKind.Scalar, 3f, 0.25f, 16f),
            new PortSpec("index2", PortKind.Scalar, 0.4f, 0f, 2f),
            new PortSpec("index3", PortKind.Scalar, 0.2f, 0f, 2f),
            new PortSpec("index4", PortKind.Scalar, 0f, 0f, 2f),
        ],
        [new PortSpec("out")],
        Emit,
        "A four-operator FM synth: an electric piano, brass, a slap bass, a bell. Patch an "
        + "envelope into 'level' and a frequency into 'freq'. Operator one sounds at the "
        + "pitch; 'ratio2'..'ratio4' put the other three above it and 'index2'..'index4' say "
        + "how hard each bends what it feeds, fading with the level so a note starts bright. "
        + "'tone' scales every index at once. The algorithm is set on the node: stack "
        + "(4→3→2→1), branch (4→3, 3 and 2 → 1), fan (2, 3 and 4 → 1), pair (2→1 beside 4→3) "
        + "or organ (all four heard).")
    {
        Extras =
        [
            new SettingsExtra(
                StateKey,
                [
                    new ExtraField.Choice(
                        AlgorithmKey,
                        "algorithm",
                        [
                            new ChoiceOption(Stack, "Stack"),
                            new ChoiceOption(Branch, "Branch"),
                            new ChoiceOption(Fan, "Fan"),
                            new ChoiceOption(Pair, "Pair"),
                            new ChoiceOption(Organ, "Organ"),
                        ],
                        Stack),
                ]),
        ],
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M2,9 C4,4 6,4 8,9 C10,14 12,14 14,9 C16,4 18,4 20,9 "
                + "M2,16 C3,13 4,13 5,16 C6,19 7,19 8,16 C9,13 10,13 11,16 "
                + "C12,19 13,19 14,16 C15,13 16,13 17,16 C18,19 19,19 20,16",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var nought = em.Constant(0f);
        var level = node[Level];
        var depth = em.Mul(level, node[Tone]);

        return node.Extra<ExtraState>(StateKey)?.Chosen(AlgorithmKey) switch
        {
            Branch => [Carrier(Sum(Bend(2, Bend(4)), Bend(3)))],
            Fan => [Carrier(Sum(Sum(Bend(2), Bend(3)), Bend(4)))],
            Pair => [Heard(Sum(Carrier(Bend(2)), Operator(3, Bend(4), level)), 0.5f)],
            Organ => [Heard(Sum(Sum(Sum(Carrier(nought), Partial(2)), Partial(3)), Partial(4)), 0.25f)],
            _ => [Carrier(Bend(2, Bend(3, Bend(4))))],
        };

        // Operator 'n' as a modulator: at its ratio, as deep as its index.
        Slot Bend(int n, Slot? by = null) => Operator(n, by ?? nought, em.Mul(depth, node[Index2 + n - 2]));

        // Operator 'n' heard, at its ratio and as loud as operator one.
        Slot Partial(int n) => Operator(n, nought, level);

        Slot Carrier(Slot by) => Sine(node[Freq], by, level);

        Slot Operator(int n, Slot by, Slot amp) => Sine(em.Mul(node[Freq], node[Ratio2 + n - 2]), by, amp);

        Slot Sum(Slot a, Slot b) => em.Add(a, b);

        // More than one carrier is shared out, so the peak is the level whatever the algorithm.
        Slot Heard(Slot sum, float share) => em.Mul(sum, share);

        // An oscillator's arithmetic, less the 'bias' that at rest adds nought.
        Slot Sine(Slot hz, Slot phase, Slot amp) =>
            em.Mul(em.Unary(OpCode.Sin, em.Mul(em.Phase(node[In], hz, phase), Tau)), amp);
    }
}
