using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Noise;

/// <summary>
/// Noise at several sizes at once, which is what makes it look like something.
/// </summary>
/// <remarks>
/// One octave of value noise is a field of smooth blobs all the same size, and
/// nothing in the world looks like that. What everything does look like is a
/// large shape with a smaller one on it and a smaller one on that: a cloud, a
/// coastline, marble, rust, a mountain. Adding octaves — each twice the
/// frequency and a fraction of the height of the one before — is the whole of
/// how that is made, and it is the oldest trick in the subject.
/// <para>
/// Two readings of the same sum, off one pass, because they are the two halves
/// of the subject and the difference between them is what a patch is choosing
/// between. 'smooth' adds the octaves as they come and is cloud; 'folded' adds
/// their distance from the middle and is smoke, flame and beaten metal, because
/// folding a smooth field at its midline puts a crease everywhere the noise
/// crossed it and every octave adds more of them. Subtracting 'folded' from one
/// turns those creases into ridges, which is a mountain — one Subtract, so it is
/// not a third output.
/// </para>
/// <para>
/// The octave count is not a socket, and that is the one thing about this module
/// worth reading twice. Every other number here is a value the program computes
/// with; this one decides how long the program <em>is</em> — eight octaves is
/// eight times the noise of one, and noise is far and away the dearest op in the
/// machine. A socket could not say it: the shape of the program would have to
/// cover eight however few were asked for, and every patch would pay for the
/// most anybody might want. So it is carried on the node, the way a Quantiser
/// carries its scale and for exactly the same reason
/// ([0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)), and declared
/// rather than drawn ([0055](0055-a-plugins-extra-declares-its-editor.md)) so
/// that no plugin ships a control.
/// </para>
/// <para>
/// 'z' is scaled with x and y rather than left alone, which is where this parts
/// company with the Noise it is built from. Driven by Time it is what makes the
/// field boil, and scaling it means the fine detail churns faster than the broad
/// shape — which is what a cloud does, and what a single-octave field cannot do
/// however it is driven.
/// </para>
/// </remarks>
internal static class FractalModule
{
    public const string TypeId = "flyback.noise.fractal";

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
        TypeId, "Fractal", "Pattern",
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
            new PortSpec("z"),
            new PortSpec("scale", PortKind.Scalar, 2f, 0f, 32f),
            new PortSpec("roughness", PortKind.Scalar, 0.5f, 0f, 1f),
        ],
        [
            new PortSpec("smooth", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("folded", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "Noise at several sizes at once, which is what a cloud, a coastline or a slab of "
        + "marble is made of. 'smooth' is the plain sum and looks like weather; 'folded' takes "
        + "each octave's distance from the middle instead, which creases the field everywhere "
        + "the noise crossed it and looks like smoke or hammered metal — one minus that is "
        + "ridges, and a mountain. 'roughness' is how much each octave keeps of the one before "
        + "it: at 0 this is a single Noise, and at 1 the fine detail is as loud as the broad "
        + "shape and the field is sand. Both outputs run 0 to 1. 'z' boils it as Noise's does "
        + "and is scaled with the picture, so the detail churns faster than the shape. How many "
        + "octaves is on the node rather than on a socket, because it decides how much work the "
        + "patch does rather than what the answer is — one noise lookup each, and noise is the "
        + "dearest thing here.")
    {
        Extras = [new OctaveExtra()],
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
            new ExtraField.Choice(CountKey, "octaves", Counts, "4"),
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

            // Noise arrives 0 to 1 and is wanted either side of nothing: the
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
