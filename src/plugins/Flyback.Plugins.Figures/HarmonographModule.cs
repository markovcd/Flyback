using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// Two damped pendulums each way. The pen they carry draws; the same pendulums
/// at pitch are a chord, and the ratio, the twist and the damping are one set of
/// numbers for both.
/// </summary>
/// <remarks>
/// The drawing is kept in a plane of ink. Each frame every pixel measures its
/// distance to the stroke the pen made since the last frame, a straight segment
/// between two closed-form positions, so the line is continuous at any frame
/// rate rather than a trail of dots; the ink under it fades by 'persist' a
/// second. The last frame's clock is kept in a plane as well, since the
/// emitter's interval is a cell the screen does not have.
/// </remarks>
internal static class HarmonographModule
{
    public const string TypeId = "flyback.figures.harmonograph";

    public const int TriggerPort = 0;
    public const int VelocityPort = 1;
    public const int SpeedPort = 2;
    public const int PitchPort = 3;
    public const int RatioPort = 4;
    public const int TwistPort = 5;
    public const int DampingPort = 6;
    public const int PersistPort = 7;
    public const int SizePort = 8;

    public const int LeftPort = 0;
    public const int RightPort = 1;
    public const int FigurePort = 2;

    private const float Tau = 6.283185307179586f;

    /// <summary>The longest step the pen takes between two frames, in seconds.</summary>
    /// <remarks>A first frame, or one after a stall, would otherwise draw a chord across the whole figure.</remarks>
    private const float LongestStep = 0.1f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Harmonograph", FiguresPlugin.Category,
        [
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f)
            {
                Lenient = true,
                Help = "Each rise sets the pendulums swinging afresh.",
            },
            new PortSpec("velocity", PortKind.Scalar, 1f, 0f, 1f)
            {
                Help = "How hard a strike swings them: the chord's loudness and the line's darkness.",
            },
            new PortSpec("speed", PortKind.Scalar, 0.4f, 0.05f, 4f) { Help = "The pen's turns a second." },
            new PortSpec("pitch", PortKind.Scalar, 220f, 20f, 4000f)
            {
                Knee = 20f,
                Help = "In hertz: the pendulums' rate for the chord.",
            },
            new PortSpec("ratio", PortKind.Scalar, 1.5f, 0.5f, 4f)
            {
                Help = "The second pendulum against the first: a fifth at 1.5, an octave at 2.",
            },
            new PortSpec("twist", PortKind.Scalar, 0f, 0f, 1f) { Help = "The lag between the axes." },
            new PortSpec("damping", PortKind.Scalar, 8f, 0.5f, 30f) { Help = "How long the drawing and the chord last." },
            new PortSpec("persist", PortKind.Scalar, 0.6f, 0f, 1f) { Help = "How much of the line is left after a second." },
            new PortSpec("size", PortKind.Scalar, 0.8f, 0f, 2f) { Help = "How much of the screen the pen reaches." },
            new PortSpec("x", NormalledTo: NodeCatalog.Across) { Standard = true },
            new PortSpec("y", NormalledTo: NodeCatalog.Down) { Standard = true },
        ],
        [
            new PortSpec("left", PortKind.Scalar, 0f, -1f, 1f) { Help = "The pendulums one way, at 'pitch'." },
            new PortSpec("right", PortKind.Scalar, 0f, -1f, 1f) { Help = "The pendulums the other way, at 'pitch'." },
            new PortSpec("figure", PortKind.Scalar, 0f, 0f, 1f) { Help = "The line the pen draws, fading by 'persist'." },
        ],
        Emit,
        "A pen on two pendulums each way, set swinging by 'trigger'. The drawing and the chord "
        + "are the same pendulums, so the chord dies as the drawing does.")
    {
        Skin = Art.Skin("harmonograph"),
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var nought = em.Constant(0f);
        var one = em.Constant(1f);

        var (age, level) = Strike.Of(em, node[TriggerPort], node[VelocityPort]);

        var ratio = node[RatioPort];
        var twist = em.Mul(node[TwistPort], Tau);
        var damping = em.Binary(OpCode.Max, node[DampingPort], em.Constant(0.01f));

        // The chord: the pen's own motion, at pitch.
        var (left, right) = Pen(age, node[PitchPort]);

        // The stroke since the last frame, from where the pen was to where it is.
        var lastSlot = em.AllocatePlaneSlot();
        var inkSlot = em.AllocatePlaneSlot();

        var now = Strike.Now(em);
        var last = em.PlaneRead(lastSlot);
        var ink = em.PlaneRead(inkSlot);

        var step = em.Binary(
            OpCode.Min,
            em.Binary(OpCode.Mod, em.Sub(now, last), em.Constant(Strike.Wrap)),
            em.Constant(LongestStep));

        var before = em.Binary(OpCode.Max, em.Sub(age, step), nought);

        var size = node[SizePort];
        var (fromX, fromY) = Pen(before, node[SpeedPort]);
        var (toX, toY) = Pen(age, node[SpeedPort]);

        fromX = em.Mul(fromX, size);
        fromY = em.Mul(fromY, size);
        toX = em.Mul(toX, size);
        toY = em.Mul(toY, size);

        // Distance from this pixel to the segment: the nearest point along it, clamped to its ends.
        var alongX = em.Sub(toX, fromX);
        var alongY = em.Sub(toY, fromY);
        var awayX = em.Sub(node[9], fromX);
        var awayY = em.Sub(node[10], fromY);

        var length = em.Add(em.Mul(alongX, alongX), em.Mul(alongY, alongY));
        var reach = em.Ternary(
            OpCode.Clamp,
            em.Binary(OpCode.Div, em.Add(em.Mul(awayX, alongX), em.Mul(awayY, alongY)), length),
            nought,
            one);

        var distance = em.Binary(
            OpCode.Hypot,
            em.Sub(awayX, em.Mul(reach, alongX)),
            em.Sub(awayY, em.Mul(reach, alongY)));

        var stroke = em.Sub(one, em.Ternary(OpCode.Smoothstep, em.Constant(0.005f), em.Constant(0.013f), distance));

        // What was there fades by 'persist' a second; the new stroke lies over it, as dark as the strike was hard.
        var persist = em.Ternary(OpCode.Clamp, node[PersistPort], nought, one);
        var faded = em.Mul(ink, em.Binary(OpCode.Pow, persist, step));
        var drawn = em.Binary(OpCode.Max, faded, em.Mul(stroke, em.Binary(OpCode.Min, level, one)));

        em.PlaneWrite(lastSlot, now);
        em.PlaneWrite(inkSlot, drawn);

        return [em.Mul(left, level), em.Mul(right, level), drawn];

        // Where the pen is at 'when' seconds after the strike, swinging at 'rate' turns a second.
        (Slot X, Slot Y) Pen(Slot when, Slot rate)
        {
            var swing = em.Unary(OpCode.Exp, em.Mul(em.Binary(OpCode.Div, when, damping), -1f));
            var first = em.Mul(em.Mul(rate, when), Tau);
            var second = em.Mul(first, ratio);

            var x = em.Mul(em.Add(em.Unary(OpCode.Sin, first), em.Unary(OpCode.Sin, second)), 0.5f);
            var y = em.Mul(em.Add(em.Unary(OpCode.Cos, first), em.Unary(OpCode.Cos, em.Add(second, twist))), 0.5f);

            return (em.Mul(x, swing), em.Mul(y, swing));
        }
    }
}
