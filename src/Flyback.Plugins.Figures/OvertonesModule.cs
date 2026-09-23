using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// Any picture is a timbre: what feeds 'spectrum' is read along one row of the
/// screen, a point per partial, and each reading is that partial's height in a
/// tone. The same readings are drawn back as bars and as one cycle of the wave.
/// </summary>
/// <remarks>
/// The reading is a Probe's sweep the other way about. A Probe pushes a time
/// that varies across the picture; this pushes a place that varies along the
/// partials, so whatever upstream of 'spectrum' reads the place is lowered once
/// per partial, reading that place instead of the pixel's own (<see cref="PortSpec.Swept"/>).
/// That is also the cost, so the count is set on the node.
/// <para>
/// The tone is divided by the sum of its partials only where that sum passes
/// one, so a bright row is held at full scale and a dark one is heard as dark.
/// </para>
/// </remarks>
internal static class OvertonesModule
{
    public const string TypeId = "flyback.figures.overtones";

    public const int SpectrumPort = 0;
    public const int InPort = 1;
    public const int FreqPort = 2;
    public const int RowPort = 3;
    public const int TiltPort = 4;
    public const int AmpPort = 5;

    public const int OutPort = 0;
    public const int BarsPort = 1;
    public const int WavePort = 2;

    private const float Tau = 6.283185307179586f;

    /// <summary>Decibels an octave to a power of the partial number: 20·log10(2).</summary>
    private const float DecibelsAnOctave = 6.0206f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Overtones", FiguresPlugin.Category,
        [
            new PortSpec("spectrum", PortKind.Any, Swept: true),
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("freq", PortKind.Scalar, 110f, 20f, 4000f) { Knee = 20f },
            new PortSpec("row", PortKind.Scalar, 0.5f, 0f, 1f),
            new PortSpec("tilt", PortKind.Scalar, 0f, -12f, 12f),
            new PortSpec("amp", PortKind.Scalar, 1f, 0f, 2f),
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
        ],
        [
            new PortSpec("out", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("bars", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("wave", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "Hears a picture as a tone. Whatever is patched into 'spectrum' is read across the "
        + "screen along 'row' (0 the top, 1 the bottom), one point per partial, and each reading "
        + "is that overtone's height over 'freq'. Sweep 'row' to scan the picture as a wavetable. "
        + "'tilt' is decibels an octave on top. 'bars' draws the readings, 'wave' one cycle of the "
        + "result. The partial count is set on the node, and each partial is another copy of whatever "
        + "feeding 'spectrum' reads the place.")
    {
        Extras = [new PartialsExtra()],
        Skin = Art.Skin("overtones"),
    };

    /// <summary>How many partials: 8, 16 or 32.</summary>
    private sealed record PartialsExtra : NodeExtra
    {
        public const string StateKey = "overtones";
        public const string CountKey = "partials";

        public override string Key => StateKey;

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Choice(CountKey, "partials", Counts, "8"),
        ];

        private static IReadOnlyList<ChoiceOption> Counts { get; } =
        [
            new("8", "8 partials"),
            new("16", "16 partials"),
            new("32", "32 partials"),
        ];

        public override string Report(NodeInstance node) =>
            $"It reads {Partials(new ExtraState(Fields, node.StateOf(StateKey)))} partials.";
    }

    /// <summary>The count this instance carries, held to what the module can build.</summary>
    private static int Partials(ExtraState? state) =>
        state is not null && int.TryParse(state.Chosen(PartialsExtra.CountKey), out var count) && count is 8 or 16 or 32
            ? count
            : 8;

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var partials = Partials(node.Extra<ExtraState>(PartialsExtra.StateKey));

        var nought = em.Constant(0f);
        var one = em.Constant(1f);

        // Read before any push, so it is the renderer's clock and this pixel's
        // place that the sweep is measured against, not something a push replaced.
        var now = em.Load(OpCode.LoadT);
        var wide = em.Load(OpCode.LoadAspect);
        var here = node[6];
        var down = node[7];

        var across = em.Add(em.Binary(OpCode.Div, here, em.Mul(wide, 2f)), em.Constant(0.5f));
        // The screen's y is one at the top: 'row' runs down it the way a page does, and a bar stands on the floor.
        var fromFloor = em.Add(em.Mul(down, 0.5f), em.Constant(0.5f));
        var rowY = em.Sub(one, em.Mul(node[RowPort], 2f));
        var slope = em.Mul(node[TiltPort], 1f / DecibelsAnOctave);

        var sum = nought;
        var tone = nought;
        var wave = nought;
        var bars = nought;

        var width = 0.4f / partials;

        for (var k = 1; k <= partials; k++)
        {
            var center = (k - 0.5f) / partials;

            em.PushDomain(em.Mul(wide, center * 2f - 1f), rowY, now);
            var reading = em.Ternary(OpCode.Clamp, em.Coerce(node.Resolve(SpectrumPort), 1), nought, one);
            em.PopDomain();

            var height = em.Mul(reading, em.Binary(OpCode.Pow, em.Constant(k), slope));
            sum = em.Add(sum, height);

            var turns = em.Phase(node[InPort], em.Mul(node[FreqPort], k), nought);
            tone = em.Add(tone, em.Mul(height, em.Unary(OpCode.Sin, em.Mul(turns, Tau))));

            wave = em.Add(wave, em.Mul(height, em.Unary(OpCode.Sin, em.Mul(across, Tau * k))));

            var inBin = em.Sub(one, em.Binary(OpCode.Step, em.Constant(width), em.Unary(OpCode.Abs, em.Sub(across, em.Constant(center)))));
            var under = em.Sub(one, em.Binary(OpCode.Step, height, fromFloor));
            bars = em.Add(bars, em.Mul(inBin, under));
        }

        var scale = em.Binary(OpCode.Div, one, em.Binary(OpCode.Max, sum, one));

        var line = em.Sub(one, em.Ternary(
            OpCode.Smoothstep,
            em.Constant(0.01f),
            em.Constant(0.03f),
            em.Unary(OpCode.Abs, em.Sub(down, em.Mul(em.Mul(wave, scale), 0.5f)))));

        return
        [
            em.Mul(em.Mul(tone, scale), node[AmpPort]),
            em.Binary(OpCode.Min, bars, one),
            line,
        ];
    }
}
