using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// The orbit of z under z → z² + c, heard a step at a time and drawn as the path
/// it takes: from nought in Mandelbrot mode, from a start point in Julia mode.
/// </summary>
/// <remarks>
/// The two modes are one iteration with a different start, since the Mandelbrot
/// orbit of c is the Julia orbit of nought. What the mode changes besides is the
/// plane the path is drawn on, the Mandelbrot's at rest or the Julia's, so that
/// it lines up drawn over the module of the same name.
/// <para>
/// The sound keeps the two latest points in cells and glides from one to the
/// next across each step, so it is a wave through the orbit rather than a
/// staircase. An orbit that leaves |z| ≤ 2 goes back to its start, so an
/// escaping one is a tone too, one step for each it took to escape; one settling
/// into a cycle of three is a tone a third of 'rate', and one that never settles
/// is noise. A cell reads nought on the screen, so the path is iterated afresh
/// there, a fixed number of steps.
/// </para>
/// </remarks>
internal static class OrbitModule
{
    public const string TypeId = "flyback.fractals.orbit";

    public const int RePort = 2;
    public const int ImPort = 3;
    public const int RatePort = 4;
    public const int InPort = 5;
    public const int StartRePort = 6;
    public const int StartImPort = 7;

    public const int LeftPort = 0;
    public const int RightPort = 1;
    public const int ColorPort = 2;
    public const int TracePort = 3;

    public const string StateKey = "orbit";
    public const string ModeKey = "mode";
    public const string Mandelbrot = "mandelbrot";
    public const string Julia = "julia";

    /// <summary>How many steps of the path the picture draws.</summary>
    public const int Steps = 24;

    /// <summary>|z|² past which an orbit has escaped and starts again.</summary>
    private const float Escaped = 4f;

    /// <summary>Half the width of the path, on the plane.</summary>
    private const float Line = 0.008f;

    /// <summary>The radius of the dot where the path starts, on the plane.</summary>
    private const float Dot = 0.035f;

    /// <summary>Further than any segment can be, so the first always wins.</summary>
    private const float Beyond = 1e4f;

    /// <summary>Where the DC blocker turns over, in hertz.</summary>
    private const float Floor = 20f;

    private const float Tau = 6.283185307179586f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Orbit", FractalsPlugin.Category,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
            new PortSpec("re", PortKind.Scalar, -0.12f, -2f, 1f),
            new PortSpec("im", PortKind.Scalar, 0.75f, -1.5f, 1.5f),
            new PortSpec("rate", PortKind.Scalar, 330f, 20f, 8000f) { Knee = 20f },
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("start re", PortKind.Scalar, 0f, -2f, 2f),
            new PortSpec("start im", PortKind.Scalar, 0f, -2f, 2f),
        ],
        [
            new PortSpec("left", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("right", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("color", PortKind.Color),
            new PortSpec("trace", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "The orbit of z under z² + c, stepped 'rate' times a second: 'left' is its real part and "
        + "'right' its imaginary. An orbit settling into a cycle of n is a tone at rate over n, "
        + "one that never settles is noise, and one that escapes starts again, a tone for how "
        + "fast it left. Set on the node: Mandelbrot mode starts z at nought, so 're' and 'im' "
        + "are a point on a Mandelbrot's map; Julia mode starts it at 'start re' and 'start im', "
        + "a pixel of the Julia set whose c is 're' and 'im'. 'trace' and 'color' draw the path "
        + "on that module's plane at rest, with a dot where it starts, so the two line up.")
    {
        Extras =
        [
            new SettingsExtra(StateKey,
            [
                new ExtraField.Choice(
                    ModeKey, "mode",
                    [
                        new ChoiceOption(Mandelbrot, "Mandelbrot: from nought"),
                        new ChoiceOption(Julia, "Julia: from the start point"),
                    ],
                    Mandelbrot),
            ]),
        ],
        Skin = Art.Skin("orbit"),
    };

    /// <summary>Sets an instance's mode, for a preset assembling one in code.</summary>
    public static NodeInstance InMode(NodeInstance node, string mode)
    {
        node.SetState(StateKey, new JsonObject { [ModeKey] = mode });

        return node;
    }

    /// <summary>The c the orbit is taken under, where it starts, and whether it is a Julia orbit.</summary>
    private readonly record struct Point(Slot Cx, Slot Cy, Slot StartX, Slot StartY, bool Julia);

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var julia = node.Extra<ExtraState>(StateKey)?.Chosen(ModeKey) == Julia;
        var zero = em.Constant(0f);

        var point = new Point(
            Escape.Held(em, node[RePort]),
            Escape.Held(em, node[ImPort]),
            julia ? Escape.Held(em, node[StartRePort]) : zero,
            julia ? Escape.Held(em, node[StartImPort]) : zero,
            julia);

        var (left, right) = Heard(em, node, point);
        var (color, trace) = Drawn(em, node, point);

        return [left, right, color, trace];
    }

    /// <summary>One step of the orbit, back to its start once it has escaped.</summary>
    private static (Slot X, Slot Y) Next(Emitter em, Slot zx, Slot zy, Point point)
    {
        var xx = em.Mul(zx, zx);
        var yy = em.Mul(zy, zy);
        var gone = em.Binary(OpCode.Step, em.Constant(Escaped), em.Add(xx, yy));

        return (
            em.Ternary(OpCode.Mix, em.Add(em.Sub(xx, yy), point.Cx), point.StartX, gone),
            em.Ternary(OpCode.Mix, em.Add(em.Mul(em.Add(zx, zx), zy), point.Cy), point.StartY, gone));
    }

    private static (Slot Left, Slot Right) Heard(Emitter em, EmitContext node, Point point)
    {
        var phase = em.Phase(node[InPort], node[RatePort], em.Constant(0f));

        int last = em.AllocateUnitSlot(), ax = em.AllocateUnitSlot(), ay = em.AllocateUnitSlot();
        int bx = em.AllocateUnitSlot(), by = em.AllocateUnitSlot();

        // A step is the phase falling back, by more than half a turn so that a
        // rate of nought never counts as one.
        var tick = em.Binary(OpCode.Step, em.Add(phase, 0.5f), em.UnitRead(last));
        em.UnitWrite(last, phase);

        var (fromX, fromY) = (em.UnitRead(ax), em.UnitRead(ay));
        var (toX, toY) = (em.UnitRead(bx), em.UnitRead(by));

        // Cells begin at nought, which is a Mandelbrot orbit's start but not a
        // Julia orbit's, so the first evaluation takes the start point instead.
        if (point.Julia)
        {
            var held = em.HasMemory();

            fromX = em.Ternary(OpCode.Mix, point.StartX, fromX, held);
            fromY = em.Ternary(OpCode.Mix, point.StartY, fromY, held);
            toX = em.Ternary(OpCode.Mix, point.StartX, toX, held);
            toY = em.Ternary(OpCode.Mix, point.StartY, toY, held);
        }

        var (nextX, nextY) = Next(em, toX, toY, point);

        fromX = em.Ternary(OpCode.Mix, fromX, toX, tick);
        fromY = em.Ternary(OpCode.Mix, fromY, toY, tick);
        toX = em.Ternary(OpCode.Mix, toX, nextX, tick);
        toY = em.Ternary(OpCode.Mix, toY, nextY, tick);

        em.UnitWrite(ax, fromX);
        em.UnitWrite(ay, fromY);
        em.UnitWrite(bx, toX);
        em.UnitWrite(by, toY);

        // An orbit sits off nought, so each side goes through a blocker at twenty hertz.
        var keep = em.Ternary(
            OpCode.Clamp,
            em.Sub(em.Constant(1f), em.Mul(em.Interval(), Tau * Floor)),
            em.Constant(0f),
            em.Constant(1f));

        return (
            Blocked(em, em.Ternary(OpCode.Mix, fromX, toX, phase), keep),
            Blocked(em, em.Ternary(OpCode.Mix, fromY, toY, phase), keep));
    }

    /// <summary>A one-pole DC blocker, halved to the rails an orbit's ±2 would pass.</summary>
    private static Slot Blocked(Emitter em, Slot signal, Slot keep)
    {
        int before = em.AllocateUnitSlot(), after = em.AllocateUnitSlot();

        var output = em.Add(em.Sub(signal, em.UnitRead(before)), em.Mul(em.UnitRead(after), keep));

        em.UnitWrite(before, signal);
        em.UnitWrite(after, output);

        return em.Ternary(OpCode.Clamp, em.Mul(output, 0.5f), em.Constant(-1f), em.Constant(1f));
    }

    private static (Slot Color, Slot Trace) Drawn(Emitter em, EmitContext node, Point point)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var (px, py) = point.Julia
            ? Escape.Plane(em, node[0], node[1], zero, zero, zero, JuliaModule.Span)
            : Escape.Plane(em, node[0], node[1], em.Constant(MandelbrotModule.Middle), zero, zero, MandelbrotModule.Span);

        var nearest = em.Constant(Beyond);
        var along = zero;

        // A Mandelbrot orbit is drawn from c, its first step from nought and the
        // one point on its plane it is the orbit of.
        var (startX, startY) = point.Julia ? (point.StartX, point.StartY) : (point.Cx, point.Cy);
        var (fromX, fromY) = (startX, startY);

        for (var step = 1; step < Steps; step++)
        {
            // Held where it left rather than sent back to its start, so an escape
            // is drawn as the one long stroke out.
            var (toX, toY) = Next(em, fromX, fromY, point);
            var left = em.Binary(
                OpCode.Step, em.Constant(Escaped), em.Add(em.Mul(fromX, fromX), em.Mul(fromY, fromY)));

            toX = em.Ternary(OpCode.Mix, toX, fromX, left);
            toY = em.Ternary(OpCode.Mix, toY, fromY, left);

            var far = Segment(em, px, py, fromX, fromY, toX, toY);

            var closer = em.Sub(one, em.Binary(OpCode.Step, nearest, far));
            along = em.Ternary(OpCode.Mix, along, em.Constant(step / (float)Steps), closer);
            nearest = em.Binary(OpCode.Min, nearest, far);

            (fromX, fromY) = (toX, toY);
        }

        var stroke = em.Sub(one, em.Ternary(OpCode.Smoothstep, zero, em.Constant(Line), nearest));
        var dot = em.Sub(
            one,
            em.Ternary(
                OpCode.Smoothstep, zero, em.Constant(Dot),
                em.Binary(OpCode.Hypot, em.Sub(px, startX), em.Sub(py, startY))));

        var trace = em.Binary(OpCode.Max, stroke, dot);

        // The gradient read from white through gold, early steps first.
        return (Escape.Gradient(em, em.Add(em.Mul(along, 0.6f), 0.42f), trace), trace);
    }

    /// <summary>How far p is from the segment a to b.</summary>
    private static Slot Segment(Emitter em, Slot px, Slot py, Slot ax, Slot ay, Slot bx, Slot by)
    {
        var pax = em.Sub(px, ax);
        var pay = em.Sub(py, ay);
        var bax = em.Sub(bx, ax);
        var bay = em.Sub(by, ay);

        var along = em.Binary(
            OpCode.Div,
            em.Add(em.Mul(pax, bax), em.Mul(pay, bay)),
            em.Add(em.Mul(bax, bax), em.Mul(bay, bay)));
        along = em.Ternary(OpCode.Clamp, along, em.Constant(0f), em.Constant(1f));

        return em.Binary(OpCode.Hypot, em.Sub(pax, em.Mul(bax, along)), em.Sub(pay, em.Mul(bay, along)));
    }
}
