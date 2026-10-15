using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A pitch function drawn as a glowing curve across beats, laid over the picture.
/// </summary>
/// <remarks>
/// 'in' is swept like a Probe's (ADR-0040): each column re-evaluates it at that
/// column's moment, so the curve shows what is coming as readily as what played.
/// It is evaluated a second time a step to the left so a jump draws as a vertical
/// stroke rather than a gap, and a third time at now for the dot (ADR-0203).
/// </remarks>
internal static class PlotModule
{
    public const string TypeId = "flyback.picture.plot";

    /// <summary>The second evaluation's offset, as a fraction of the span.</summary>
    private const float Join = 0.0025f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Plot", ModuleCategories.Forms,
        [
            ..Field.Position(),
            new PortSpec("under", PortKind.Color) { Help = "The picture to draw over: another Plot's 'out' to stack curves." },
            new PortSpec("in", PortKind.Any, Swept: true) { Help = "The pitch to draw, a function of time or beats. 1 is an octave." },
            new PortSpec("bpm", PortKind.Scalar, 120f, 20f, 300f) { Help = "The tempo the beats count at, from time 0." },
            new PortSpec("span", PortKind.Scalar, 8f, 0.25f, 64f) { Help = "How many beats fit across." },
            new PortSpec("page", PortKind.Scalar, 0f, 0f, 1f, Display: PortDisplay.Integer)
            {
                Help = "0 scrolls past a fixed now; 1 turns a page every 'span' beats with now moving across.",
            },
            new PortSpec("ahead", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "When scrolling, how much of the span is still to come." },
            new PortSpec("future", PortKind.Scalar, 0.35f, 0f, 1f) { Help = "How bright what is still to come is drawn." },
            new PortSpec("low", PortKind.Scalar, 0f, -8f, 16f) { Help = "The pitch at 'bottom'." },
            new PortSpec("high", PortKind.Scalar, 4f, -8f, 16f) { Help = "The pitch at 'top'." },
            Field.Distance("left") with { Default = -1.2f, Min = -4f, Max = 4f, Help = "Where the chart starts across." },
            Field.Distance("right") with { Default = 1.2f, Min = -4f, Max = 4f, Help = "Where it ends across." },
            Field.Distance("bottom") with { Default = -0.8f, Help = "Where 'low' is drawn." },
            Field.Distance("top") with { Default = 0.6f, Help = "Where 'high' is drawn." },
            new PortSpec("color", PortKind.Color, 1f) { Help = "The curve's color. White without a wire." },
            Field.Size("width", 0.008f, 0.1f) with { Help = "How thick the curve is." },
            new PortSpec("glow", PortKind.Scalar, 0.5f, 0f, 2f) { Help = "How much light spills round the curve and the dot." },
            Field.Size("dot", 0.025f, 0.2f) with { Help = "How big the dot riding the curve at now is. 0 for none." },
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f) { Help = "Dims the whole curve: patch an Arrangement part in to light it with its part." },
            new PortSpec("grid", PortKind.Scalar, 0f, 0f, 1f) { Help = "How bright the octave lines and the now rule are." },
        ],
        [
            new PortSpec("out", PortKind.Color) { Help = "'under' with the curve drawn over it." },
            new PortSpec("ink", PortKind.Scalar, 0f, 0f, 1f) { Help = "The curve and its dot alone, for a Layer or a Mixer." },
        ],
        Emit,
        "Draws a pitch function as a curve across beats, with a dot at now: one part of a song "
        + "made of functions. Chain Plots through 'under'. 'in' is drawn ahead of time, so a "
        + "delay or an oscillator's phase does not show.")
    {
        Words = "chart pitch over beats, piano roll",
        Sinks = ModuleSinks.Video,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M3,18 L8,18 L8,12 L13,12 L13,15 L18,15 L18,7 L21,7 "
                + "M16.6,15 A1.4,1.4 0 1 1 19.4,15 A1.4,1.4 0 1 1 16.6,15",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var x = node[0];
        var y = node[1];
        var (left, right, bottom, top) = (node[11], node[12], node[13], node[14]);
        var span = em.Binary(OpCode.Max, node[5], em.Constant(0.25f));
        var level = node[19];

        var t = em.Load(OpCode.LoadT);
        var perBeat = em.Binary(OpCode.Div, em.Constant(60f), node[4]);
        var now = em.Binary(OpCode.Div, t, perBeat);

        // The beat at the chart's left edge: a page's first beat, or a fixed
        // stretch behind now.
        var paged = em.Mul(em.Unary(OpCode.Floor, em.Binary(OpCode.Div, now, span)), span);
        var scrolled = em.Sub(now, em.Mul(span, em.Sub(one, node[7])));
        var start = em.Ternary(OpCode.Mix, scrolled, paged, node[6]);

        var across = em.Sub(right, left);
        var beat = em.Add(start, em.Mul(em.Binary(OpCode.Div, em.Sub(x, left), across), span));
        var step = em.Mul(span, Join);

        var here = Value(beat);
        var before = Value(em.Binary(OpCode.Max, em.Sub(beat, step), start));
        var current = Value(now);

        var rise = em.Binary(OpCode.Div, em.Sub(top, bottom), em.Sub(node[10], node[9]));
        Slot Height(Slot pitch) => em.Add(bottom, em.Mul(em.Sub(pitch, node[9]), rise));

        var a = Height(here);
        var b = Height(before);
        var gap = em.Binary(
            OpCode.Max,
            zero,
            em.Binary(OpCode.Max, em.Sub(em.Binary(OpCode.Min, a, b), y), em.Sub(y, em.Binary(OpCode.Max, a, b))));

        var width = em.Binary(OpCode.Max, node[16], em.Constant(0.001f));
        var glow = node[17];

        // Played is full, still to come is 'future', with a short ramp between.
        var played = em.Ternary(
            OpCode.Clamp,
            em.Binary(OpCode.Div, em.Sub(now, beat), em.Mul(span, 0.005f)),
            zero,
            one);
        var shown = em.Ternary(OpCode.Mix, node[8], one, played);

        var inside = em.Mul(em.Binary(OpCode.Step, left, x), em.Binary(OpCode.Step, x, right));
        var curve = em.Mul(em.Mul(Lit(gap, width), shown), inside);

        var nowX = em.Add(left, em.Mul(em.Binary(OpCode.Div, em.Sub(now, start), span), across));
        var dotSize = node[18];
        var reach = em.Binary(OpCode.Hypot, em.Sub(x, nowX), em.Sub(y, Height(current)));
        var dot = em.Mul(
            Lit(reach, em.Binary(OpCode.Max, dotSize, em.Constant(0.001f))),
            em.Binary(OpCode.Step, em.Constant(0.0005f), dotSize));

        var ink = em.Mul(em.Add(curve, dot), level);

        var color = em.Coerce(node[15], 3);
        var lit = em.Add(node[2], em.Mul(color, ink));

        return [em.Add(lit, em.Mul(Grid(), em.Mul(node[20], 0.3f))), ink];

        // The function at a beat, by sweeping 'in' to that beat's moment.
        Slot Value(Slot at)
        {
            em.PushDomain(zero, zero, em.Mul(at, perBeat));
            var value = em.Coerce(node.Resolve(3), 1);
            em.PopDomain();
            return value;
        }

        // A solid core 'size' across with a falloff of light round it.
        Slot Lit(Slot distance, Slot size)
        {
            var core = em.Sub(one, em.Ternary(OpCode.Smoothstep, em.Mul(size, 0.5f), size, distance));
            var spread = em.Binary(OpCode.Div, distance, em.Mul(size, 3f));
            var halo = em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Mul(spread, spread)));
            return em.Add(core, em.Mul(halo, em.Mul(glow, 0.5f)));
        }

        // A line at every whole pitch, an octave apart, and a rule at now, inside the chart.
        Slot Grid()
        {
            var pitch = em.Add(node[9], em.Binary(OpCode.Div, em.Sub(y, bottom), rise));
            var off = em.Mul(
                em.Unary(OpCode.Abs, em.Add(em.Unary(OpCode.Fract, em.Add(pitch, 0.5f)), -0.5f)),
                em.Unary(OpCode.Abs, rise));
            var octave = Hairline(off);
            var rule = Hairline(em.Unary(OpCode.Abs, em.Sub(x, nowX)));

            var low = em.Binary(OpCode.Min, bottom, top);
            var high = em.Binary(OpCode.Max, bottom, top);
            var tall = em.Mul(em.Binary(OpCode.Step, low, y), em.Binary(OpCode.Step, y, high));

            return em.Mul(em.Add(em.Mul(octave, inside), rule), tall);
        }

        Slot Hairline(Slot distance) =>
            em.Sub(one, em.Ternary(OpCode.Smoothstep, em.Constant(0.0015f), em.Constant(0.004f), distance));
    }
}
