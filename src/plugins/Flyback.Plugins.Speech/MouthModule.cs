using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Speech;

/// <summary>
/// A pair of lips, with teeth and a tongue behind them, drawn as open and as
/// round as a Speak says, and a caption under them.
/// </summary>
/// <remarks>
/// Every edge is a height above or below the mouth's middle line, worked out
/// across it: the inside opens as a half-ellipse, each lip is a band tapering to
/// the corners, and the upper one dips at the middle for its bow. Rounding pulls
/// the corners in and pushes the lips out.
/// </remarks>
internal static class MouthModule
{
    public const string TypeId = "flyback.speech.mouth";

    /// <summary>How soft each edge is, in the mouth's own units.</summary>
    private const float Edge = 0.012f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Mouth", ModuleCategories.Forms,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across) { Standard = true },
            new PortSpec("y", NormalledTo: NodeCatalog.Down) { Standard = true },
            new PortSpec("size", PortKind.Scalar, 0.6f, 0.05f, 2f) { Help = "Half the mouth's width when the lips are spread." },
            new PortSpec("open", PortKind.Scalar, 0.3f, 0f, 1f) { Help = "How far the jaw drops. A Speak's 'open'." },
            new PortSpec("round", PortKind.Scalar, 0f, 0f, 1f) { Help = "How far the lips pucker. A Speak's 'round'." },
            new PortSpec("caption", PortKind.Scalar, 2f, -2f, 2f)
            {
                Help = "A shape drawn in white where it is below nought: a Text's 'distance', typed out by a Speak.",
            },
        ],
        [
            new PortSpec("color", PortKind.Color) { Help = "The mouth and its caption, on black." },
            new PortSpec("mask") { Help = "One where the mouth or the caption is drawn, to lay them over another picture." },
        ],
        Emit,
        "Lips, teeth and a tongue that move as a Speak talks: patch its 'open' and 'round' in, and a "
        + "Text it types out into 'caption'. Picture only.")
    {
        Words = "lips, face, lip sync, talking head",
        Sinks = ModuleSinks.Video,
        Skin = Art.Skin("mouth"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var size = em.Binary(OpCode.Max, node[2], em.Constant(0.05f));
        var u = em.Binary(OpCode.Div, node[0], size);
        var v = em.Binary(OpCode.Div, node[1], size);
        var open = em.Ternary(OpCode.Clamp, node[3], zero, one);
        var round = em.Ternary(OpCode.Clamp, node[4], zero, one);

        // Across the mouth: one at the middle, nought at the corners and beyond. The
        // opening narrows to the corners faster than the lips do, so they meet there.
        var width = em.Sub(one, em.Mul(round, 0.4f));
        var across = em.Binary(OpCode.Div, u, width);
        var k = em.Binary(OpCode.Max, em.Sub(one, em.Mul(across, across)), zero);
        var arch = em.Binary(OpCode.Pow, k, em.Constant(0.9f));
        var taper = em.Binary(OpCode.Pow, k, em.Constant(0.45f));

        // The gap between the lips, the jaw dropping further than the lip lifts.
        var gap = em.Add(em.Mul(em.Mul(open, em.Sub(one, em.Mul(round, 0.25f))), 0.6f), 0.01f);
        var top = em.Mul(gap, arch);
        var bottom = Negate(em.Mul(em.Mul(gap, 1.25f), arch));

        var pout = em.Add(em.Mul(round, 0.06f), 1f);
        var bow = em.Mul(em.Unary(OpCode.Exp, Negate(em.Mul(em.Mul(across, across), 40f))), 0.05f);
        var upper = em.Sub(em.Add(top, em.Mul(em.Mul(taper, 0.17f), pout)), em.Mul(bow, taper));
        var lower = em.Sub(bottom, em.Mul(em.Mul(taper, 0.21f), pout));

        var aside = em.Unary(OpCode.Abs, across);
        var within = Below(aside, one);
        var inside = em.Mul(em.Mul(Below(v, top), Above(v, bottom)), within);
        var outline = em.Mul(em.Mul(Below(v, upper), Above(v, lower)), within);
        var lips = em.Sub(outline, inside);

        // Teeth hang from the top of the gap and stand on its bottom; the tongue lies low.
        var upperTeeth = em.Mul(em.Mul(inside, Above(v, em.Sub(top, em.Constant(0.1f)))), Below(aside, em.Constant(0.75f)));
        var lowerTeeth = em.Mul(em.Mul(inside, Below(v, em.Add(bottom, em.Constant(0.05f)))), Below(aside, em.Constant(0.55f)));
        var teeth = em.Binary(OpCode.Max, upperTeeth, lowerTeeth);
        var tongueX = em.Mul(across, 1.6f);
        var tongueY = em.Binary(OpCode.Div, em.Sub(v, bottom), em.Binary(OpCode.Max, em.Mul(gap, 0.9f), em.Constant(0.01f)));
        var tongue = em.Mul(em.Mul(inside, Below(em.Add(em.Mul(tongueX, tongueX), em.Mul(tongueY, tongueY)), one)), em.Sub(one, teeth));

        // The lower lip catches the light along its middle.
        var shine = em.Mul(em.Unary(OpCode.Exp, Negate(em.Mul(Square(em.Binary(OpCode.Div, em.Sub(v, em.Mul(em.Add(bottom, lower), 0.5f)), em.Constant(0.05f))), 1f))), 0.35f);
        var lipColor = em.Ternary(OpCode.Mix, Color(0.72f, 0.12f, 0.24f), Color(1f, 0.55f, 0.6f), em.Mul(shine, k));

        var color = em.Mul(lipColor, lips);
        color = em.Ternary(OpCode.Mix, color, Color(0.16f, 0.02f, 0.05f), inside);
        color = em.Ternary(OpCode.Mix, color, Color(0.8f, 0.3f, 0.36f), tongue);
        color = em.Ternary(OpCode.Mix, color, Color(0.95f, 0.93f, 0.88f), teeth);

        var caption = em.Ternary(OpCode.Smoothstep, em.Constant(0.004f), em.Constant(-0.004f), node[5]);
        color = em.Ternary(OpCode.Mix, color, Color(1f, 0.97f, 0.9f), caption);

        return [color, em.Binary(OpCode.Max, outline, caption)];

        Slot Below(Slot value, Slot edge) =>
            em.Ternary(OpCode.Smoothstep, em.Constant(-Edge), em.Constant(Edge), em.Sub(edge, value));

        Slot Above(Slot value, Slot edge) => Below(edge, value);

        Slot Square(Slot value) => em.Mul(value, value);

        Slot Negate(Slot value) => em.Unary(OpCode.Neg, value);

        Slot Color(float r, float g, float b) => em.Combine(em.Constant(r), em.Constant(g), em.Constant(b));
    }
}
