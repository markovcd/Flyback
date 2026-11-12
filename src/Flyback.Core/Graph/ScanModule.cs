using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Scan: the picture heard along a loop swept round it.</summary>
internal static class ScanModule
{
    /// <summary>
    /// The Probe read backwards: a loop swept across the picture at audio rate,
    /// so what a chart draws of a signal, this hears of a field. Named here only
    /// for the tests and the preset that build one.
    /// </summary>
    internal const string TypeId = "scan";

    /// <summary>
    /// One loop swept round the picture at audio rate, and the value it passes
    /// over: a field read as a waveform rather than a waveform drawn as a field.
    /// </summary>
    /// <remarks>
    /// The Probe upside down. A Probe pushes a <em>time</em> that varies across
    /// the picture; this pushes an <em>(x, y)</em> that varies along a loop, so
    /// everything upstream of <c>in</c> is lowered reading a position on it — see
    /// <see cref="PortSpec.Swept"/> — and hands back one scalar per evaluation.
    /// <para>
    /// A circle rather than a raster because a raster's <c>fract</c> jumps once a
    /// line, and that sawtooth edge is there whatever the picture holds. A closed
    /// loop contributes no discontinuity, which leaves wavetable synthesis with
    /// the field as the table.
    /// </para>
    /// <para>
    /// Where on the loop an evaluation sits is the one thing the sinks cannot
    /// agree on, so the bearing is chosen on <see cref="Emitter.HasMemory"/>: the
    /// speakers take the accumulated phase, the screen takes the pixel's own
    /// angle from the center. One lowering serves both (ADR-0043).
    /// </para>
    /// </remarks>
    internal static NodeDef Def()
    {
        // How far a value of one 'scale' pushes the trace off the loop, as a
        // fraction of the radius. Half, so a signal that fits reads as a ring
        // with a waveform around it rather than as a disc.
        const float swing = 0.5f;

        return new NodeDef(
            TypeId, "Scan", ModuleCategories.Measurement,
            [
                Swept("in") with { Help = "What is read along the loop: the wavetable." },
                Domain("clock", "What carries the sweep round the loop: Time without a wire."),
                Num("rate", 220f, 0f, 4000f) with { Knee = 0.02f, Help = "Turns a second: the pitch, in hertz." },
                Num("radius", 0.5f, 0f) with { Help = "The size of the loop." },
                Num("x", 0f, -2f, 2f) with { Help = "The loop's center, across." },
                Num("y", 0f, -2f, 2f) with { Help = "The loop's center, up." },
                Num("scale", 1f, 0.01f, 16f) with { Help = "The value that pushes the drawn trace half a radius off the loop." },
            ],
            [
                Num("out") with { Help = "The sample." },
                Col("view") with { Help = "The loop drawn over the picture." },
            ],
            (em, node) =>
            {
                var one = em.Constant(1f);

                var radius = node[3];
                var centerX = node[4];
                var centerY = node[5];

                // Where this pixel stands relative to the loop, which is the
                // eye's only way of asking where on the loop it is looking.
                var awayX = em.Sub(em.Load(OpCode.LoadX), centerX);
                var awayY = em.Sub(em.Load(OpCode.LoadY), centerY);

                // Accumulated rather than multiplied out, for ADR-0030's reason:
                // a rate that steps then bends the waveform instead of breaking
                // it, which is what makes this playable from a Note.
                var turns = em.Phase(node[1], node[2], em.Constant(0f));

                var angle = em.Ternary(
                    OpCode.Mix,
                    em.Binary(OpCode.Atan2, awayY, awayX),
                    em.Mul(turns, MathF.Tau),
                    em.HasMemory());

                // Read before the push, so it is the renderer's clock the sweep
                // is carried on and not something the sweep has just replaced.
                var now = em.Load(OpCode.LoadT);

                em.PushDomain(
                    em.Add(centerX, em.Mul(radius, em.Unary(OpCode.Cos, angle))),
                    em.Add(centerY, em.Mul(radius, em.Unary(OpCode.Sin, angle))),
                    now);
                var value = em.Coerce(node.Resolve(0), 1);
                em.PopDomain();

                // The display: the loop drawn where it runs, with the value it
                // is passing over pushing the trace off it. A circle is the only
                // path whose point-at-this-bearing is closed form, and that is
                // what keeps this one evaluation a pixel rather than a march.
                var away = em.Binary(OpCode.Hypot, awayX, awayY);

                var trace = em.Add(
                    radius,
                    em.Mul(em.Mul(em.Binary(OpCode.Div, value, node[6]), radius), swing));

                var lit = em.Sub(one, em.Ternary(
                    OpCode.Smoothstep,
                    em.Constant(0.006f),
                    em.Constant(0.02f),
                    em.Unary(OpCode.Abs, em.Sub(away, trace))));

                // The unmodulated loop under it, faint, because a trace swinging
                // about nothing is otherwise indistinguishable from one sitting
                // still at the wrong radius.
                var guide = em.Sub(one, em.Ternary(
                    OpCode.Smoothstep,
                    em.Constant(0.002f),
                    em.Constant(0.006f),
                    em.Unary(OpCode.Abs, em.Sub(away, radius))));

                return
                [
                    value,
                    em.Mul(
                        em.Combine(em.Constant(0.45f), one, em.Constant(0.72f)),
                        em.Add(lit, em.Mul(guide, 0.25f))),
                ];
            },
            "Hears the picture. A circle is swept round the image 'rate' times a second and 'in' "
            + "is read along it, so one turn is one cycle of a waveform and 'rate' is the pitch. "
            + "'radius', 'x' and 'y' pick the loop: the image is the wavetable, and moving them "
            + "sweeps through it. A loop along the picture's own contours reads a constant and is "
            + "silent: a circle centered on Rings hears nothing, and moving it off center hears "
            + "everything. At 'radius' 0, 'x' and 'y' are the path: a sawtooth into 'x' times "
            + "Coordinates' 'aspect', with a slow one into 'y', is a raster scan, retrace edge "
            + "and all.")
        {
            Words = "hear the picture",
        };
    }
}
