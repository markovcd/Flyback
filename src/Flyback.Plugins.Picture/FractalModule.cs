using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Clouds at several sizes at once, which is what makes it look like something.
/// </summary>
/// <remarks>
/// Each octave is twice the frequency and a fraction of the height of the last.
/// 'smooth' sums them as they are (cloud); 'folded' sums their distance from the
/// middle, creasing where the noise crosses it (smoke, flame, metal). One minus
/// 'folded' gives ridges.
/// <para>
/// The octave count sets the program's length, so it is a node setting
/// (ADR-0051, ADR-0055) rather than a socket. 'z' scales with x and y, so fine
/// detail churns faster than the broad shape.
/// </para>
/// </remarks>
internal static class FractalModule
{
    public const string TypeId = "flyback.picture.fractal";

    /// <summary>
    /// The most octaves this will build. Past here each one is finer than a pixel
    /// at any size a patch is rendered at, so it costs a noise lookup to add
    /// nothing anybody can see.
    /// </summary>
    private const int Most = 8;

    /// <summary>
    /// How much finer each octave is than the one before. Two is the number
    /// everyone uses and the only one that leaves the sum looking like one field
    /// rather than two — a knob for it would be a fourth thing to turn to get
    /// back to where you started.
    /// </summary>
    private const float Finer = 2f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Fractal", ModuleCategories.Patterns,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across) { Standard = true },
            new PortSpec("y", NormalledTo: NodeCatalog.Down) { Standard = true },
            new PortSpec("z") { Help = "Boils it. Scaled with x and y, so fine detail churns faster." },
            new PortSpec("scale", PortKind.Scalar, 2f, 0f, 32f) { Help = "How fine the broadest octave is." },
            new PortSpec("roughness", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "How much each octave keeps of the last: 0 is a single Clouds, 1 is sand.",
            },
        ],
        [
            new PortSpec("smooth", PortKind.Scalar, 0f, 0f, 1f) { Help = "The plain sum, which looks like weather." },
            new PortSpec("folded", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Creased wherever the noise crossed its middle, like smoke or hammered "
                    + "metal. One minus it is ridges.",
            },
        ],
        Emit,
        "Clouds at several sizes at once: cloud, coastline, marble. Both outputs run 0 to 1. "
        + "The octave count is set on the node, and each octave costs a noise lookup.")
    {
        Extras = [new OctaveExtra()],
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Patterns))
        {
            Glyph = "M12,3 L20,19 L4,19 Z M12,10 L16,17.5 L8,17.5 Z",
        },
    };

    /// <summary>
    /// Sets how many octaves an instance builds, for a preset assembling one in
    /// code. What the inspector does when somebody picks from the list, said
    /// once so that a preset and the panel cannot disagree about the shape of
    /// what is stored.
    /// </summary>
    public static NodeInstance WithOctaves(NodeInstance node, int octaves)
    {
        var extra = Definition.Extra<OctaveExtra>() ?? new OctaveExtra();

        var held = extra.Stored(node.StateOf(extra.Key));
        held[OctaveExtra.CountKey] = JsonValue.Create(Math.Clamp(octaves, 1, Most).ToString());

        node.SetState(extra.Key, held);

        return node;
    }

    /// <summary>
    /// How many octaves this instance builds. Carried rather than computed — see
    /// the note on the class.
    /// </summary>
    private sealed record OctaveExtra : NodeExtra
    {
        public const string StateKey = "fractal";
        public const string CountKey = "octaves";

        public override string Key => StateKey;

        /// <summary>
        /// A choice rather than a number, which is the whole of what this
        /// vocabulary had to offer and turns out to be the right one anyway: the
        /// count is discrete, and a knob resting at three and a half would say a
        /// thing the module cannot mean.
        /// </summary>
        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Choice(CountKey, "octaves", Counts, "4") { Help = "How many sizes of cloud it stacks. Each costs a noise lookup." },
        ];

        private static IReadOnlyList<ChoiceOption> Counts { get; } =
        [
            .. Enumerable.Range(1, Most).Select(n =>
                new ChoiceOption(n.ToString(), n == 1 ? "1 octave" : $"{n} octaves")),
        ];

        public override string Report(NodeInstance node) =>
            $"It builds {Octaves(new ExtraState(Fields, node.StateOf(StateKey)))} octaves.";
    }

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var octaves = Octaves(node.Extra<ExtraState>(OctaveExtra.StateKey));

        var one = em.Constant(1f);
        var scale = node[3];

        // Held to what an amplitude can mean, because it is a socket and may be
        // swept past either end. Above one the sum would be dominated by the
        // finest octave and below nought the octaves would alternate in sign,
        // and neither is a fractal.
        var roughness = em.Ternary(OpCode.Clamp, node[4], em.Constant(0f), one);

        var amplitude = one;
        var smooth = em.Constant(0f);
        var folded = em.Constant(0f);
        var weight = em.Constant(0f);

        for (var octave = 0; octave < octaves; octave++)
        {
            // A constant, so the multiply below folds into one op rather than
            // accumulating a frequency the program has to carry.
            var step = em.Mul(scale, MathF.Pow(Finer, octave));

            var field = em.Ternary(
                OpCode.Noise3,
                em.Mul(node[0], step),
                em.Mul(node[1], step),
                em.Mul(node[2], step));

            // Each octave arrives 0 to 1 and is wanted either side of nothing: the
            // plain sum needs a signed field or every octave would pile onto the
            // same side, and the fold needs a middle to be folded about.
            var signed = em.Add(em.Mul(field, 2f), -1f);

            smooth = em.Add(smooth, em.Mul(amplitude, signed));
            folded = em.Add(folded, em.Mul(amplitude, em.Unary(OpCode.Abs, signed)));
            weight = em.Add(weight, amplitude);

            amplitude = em.Mul(amplitude, roughness);
        }

        // The first octave's amplitude is one, so this is never less than one and
        // never needs guarding — which is why roughness is clamped above rather
        // than the division being defended here.
        return
        [
            em.Add(em.Mul(em.Binary(OpCode.Div, smooth, weight), 0.5f), 0.5f),
            em.Binary(OpCode.Div, folded, weight),
        ];
    }

    /// <summary>
    /// The count this instance carries, held to what the module can build. A
    /// stored value that means nothing — a patch edited by hand, or one saved by
    /// a build that offered more — reads as the default rather than as an error.
    /// </summary>
    private static int Octaves(ExtraState? state)
    {
        if (state is null) return 4;

        return int.TryParse(state.Chosen(OctaveExtra.CountKey), out var count)
            ? Math.Clamp(count, 1, Most)
            : 4;
    }
}
