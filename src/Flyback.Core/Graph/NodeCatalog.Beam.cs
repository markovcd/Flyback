using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// The X-Y oscilloscope: what the speakers played on two inputs, drawn against
    /// each other as the trace a beam leaves on phosphor.
    /// </summary>
    public const string BeamTypeId = "beam";

    /// <summary>The trace's side in texels, which <c>Compile.Beams</c> draws at.</summary>
    internal const int BeamSize = 512;

    /// <summary>The halo's side in texels, laid after the trace in the same buffer.</summary>
    internal const int BeamGlowSize = 128;

    /// <summary>
    /// Oscilloscope music's display: 'x' across and 'y' up, the beam brighter where
    /// it lingers and all but invisible where it jumps, fading behind itself.
    /// </summary>
    /// <remarks>
    /// A Scope in how it gets the past: both inputs are tapped and never lowered into
    /// the picture. What fills the buffer is a picture rather than a stretch of time —
    /// see <see cref="NodeDef.ChartsBeam"/> — so here it is read back as a square of
    /// texels, two table reads a row apart for each of the trace and the halo.
    /// 'persistence' is read at compile time, as a Scope's 'window' is; the rest are
    /// applied after the read and may be swept.
    /// </remarks>
    private static NodeDef Beam() =>
        new NodeDef(
            BeamTypeId, "Beam", ModuleCategories.Measurement,
            [
                Swept("x") with { Help = "Across, as it was played: the left channel of oscilloscope music." },
                Swept("y") with { Help = "Up, as it was played: the right channel of oscilloscope music." },
                Seconds("persistence", -1.7f) with { Help = "How long the phosphor takes to fade to a third behind the beam." },
                Num("intensity", 1f, 0f, 16f) with { Help = "How bright the beam is. Past one, slow strokes burn white." },
                Num("glow", 0.5f, 0f, 4f) with { Help = "How much of the halo around the trace is shown." },
                Num("hue", 0.36f, 0f, 1f) with { Help = "The phosphor's color: green at the default, as P31." },
            ],
            [Col("color") with { Help = "The screen: one picture height is full scale, -1 to 1, black around it." }],
            (em, node) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);

                if (node.Trace is not { } trace) return [em.Coerce(em.Constant(0f), VideoChannels)];

                var energy = em.Add(
                    Phosphor(em, x, y, trace, BeamSize, 0),
                    em.Mul(Phosphor(em, x, y, trace, BeamGlowSize, BeamSize * BeamSize), node[4]));

                // Each channel saturates on its own, so a stroke bright enough
                // saturates green first and then whitens, as a phosphor does.
                var tint = em.Triple(OpCode.HsvToRgb, node[5], em.Constant(0.75f), em.Constant(1f));
                var lit = em.Mul(em.Coerce(em.Mul(energy, em.Mul(node[3], BeamGain)), VideoChannels), tint);

                return [em.Sub(em.Coerce(em.Constant(1f), VideoChannels), em.Unary(OpCode.Exp, em.Mul(lit, -1f)))];
            },
            "An X-Y oscilloscope screen of what the speakers played: 'x' across and 'y' up, "
            + "so oscilloscope music wired left to 'x' and right to 'y' draws as it was made to. "
            + "The beam is bright where it moves slowly and fades behind itself. Like a Scope, it "
            + "shows only what reaches the Output's 'left' or 'right', and nothing while sound is off.")
        {
            Words = "X-Y oscilloscope",
            TapsSignal = true,
            ChartsSignal = true,
            ChartsBeam = true,
            TappedInputs = 2,
            Sinks = ModuleSinks.Video,
        };

    /// <summary>
    /// How bright a trace of a given length reads at an intensity of one: a figure a
    /// few picture heights long, as oscilloscope music draws, lights to about half.
    /// </summary>
    private const float BeamGain = 9f;

    /// <summary>
    /// One square of the beam's buffer read where the pixel is, bilinear: two table
    /// reads a row apart, each interpolated along its row.
    /// </summary>
    private static Slot Phosphor(Emitter em, Slot x, Slot y, LoadedSample trace, int size, int start)
    {
        var zero = em.Constant(0f);
        var last = em.Constant(size - 1f);

        // From the picture's -1 to 1 to texel centers, held on the black edge.
        Slot Texel(Slot u) => em.Ternary(OpCode.Clamp, em.Add(em.Mul(u, size * 0.5f), size * 0.5f - 0.5f), zero, last);

        var column = Texel(x);
        var row = Texel(y);

        var below = em.Unary(OpCode.Floor, row);
        var above = em.Binary(OpCode.Min, em.Add(below, 1f), last);
        var between = em.Sub(row, below);

        Slot Read(Slot line) => em.Table(em.Add(em.Add(em.Mul(line, size), column), start), trace);

        return em.Ternary(OpCode.Mix, Read(below), Read(above), between);
    }
}
