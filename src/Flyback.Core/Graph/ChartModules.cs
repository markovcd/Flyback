using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>Probe, Scope and Analyzer: a signal drawn as a chart.</summary>
internal static class ChartModules
{
    /// <summary>
    /// The chart module. Named here because the shell roots the picture at one
    /// when it is selected — see <c>Compile.PatchCompiler</c>.
    /// </summary>
    internal const string ProbeTypeId = "probe";

    /// <summary>
    /// The chart of what was played. Named here because the shell roots the
    /// picture at one when it is selected; the compiler finds the extra audio
    /// roots through <see cref="NodeDef.TapsSignal"/> rather than by name.
    /// </summary>
    internal const string ScopeTypeId = "scope";

    /// <summary>
    /// The chart of what frequencies were played. Named here because the shell
    /// roots the picture at one when it is selected, as it does a Scope.
    /// </summary>
    internal const string AnalyzerTypeId = "analyzer";

    /// <summary>Whether a module is one the shell will show in place of the picture.</summary>
    internal static bool IsChart(string typeId) => typeId is ProbeTypeId or ScopeTypeId or AnalyzerTypeId or BeamModule.TypeId;

    /// <summary>Probe, Scope and Analyzer.</summary>
    internal static IEnumerable<NodeDef> Defs()
    {
        yield return Probe();
        yield return Scope();
        yield return Analyzer();
    }

    /// <summary>
    /// One grid square, in screen units. Eight across the middle of the picture,
    /// so a square is an eighth of the window and a quarter of the scale however
    /// wide the preview is.
    /// </summary>
    private const float Division = 0.25f;

    /// <summary>The help on the sockets the charts share.</summary>
    private const string ChartHelp = "The chart as a color.",
        TopEdgeHelp = "The value at the top edge. A grid square up is a quarter of it.";

    /// <summary>
    /// The trace, its fill, the grid and the off-edge bar, shared by the Probe and
    /// the Scope so the two draw the same picture.
    /// </summary>
    /// <param name="height">The trace, already scaled to screen units.</param>
    /// <param name="now">Where to rule the present moment, or null for none.</param>
    /// <param name="across">A grid square's width, or null for <see cref="Division"/>.</param>
    /// <param name="glow">Per-column trace brightness, or null for even.</param>
    /// <param name="ground">Where the fill runs down to, or null for the middle.</param>
    private static Slot Charted(
        Emitter em,
        Slot x,
        Slot y,
        Slot height,
        Slot? now = null,
        Slot? across = null,
        Slot? glow = null,
        Slot? ground = null)
    {
        var one = em.Constant(1f);
        var zero = em.Constant(0f);
        var floor = ground ?? zero;

        var trace = em.Sub(one, em.Ternary(
            OpCode.Smoothstep,
            em.Constant(0.006f),
            em.Constant(0.02f),
            em.Unary(OpCode.Abs, em.Sub(y, height))));

        // Filled from the line down to the ground, which is what keeps the chart
        // readable when the signal moves faster than the pixels can follow: a
        // trace alone breaks into dots there, and the fill becomes the envelope
        // a scope shows at the same sweep.
        var fill = em.Mul(
            em.Binary(OpCode.Step, em.Binary(OpCode.Min, height, floor), y),
            em.Binary(OpCode.Step, y, em.Binary(OpCode.Max, height, floor)));

        // A bar along whichever edge the signal has gone off, because a value
        // past the top of the chart is otherwise indistinguishable from no
        // signal at all.
        var over = em.Binary(OpCode.Step, one, em.Unary(OpCode.Abs, height));
        var edge = em.Binary(OpCode.Step, em.Constant(0.96f), em.Unary(OpCode.Abs, y));
        var side = em.Binary(OpCode.Step, zero, em.Mul(height, y));
        var clipped = em.Mul(em.Mul(over, edge), side);

        var signal = em.Add(em.Mul(fill, 0.22f), trace);

        // Where the moment is, said one way or the other. A Probe's now is a
        // column in the middle and takes a rule down it; a Scope's is the edge
        // of the frame, so it says the same thing by brightness and passes no
        // rule at all.
        if (glow is { } brightness) signal = em.Mul(signal, brightness);

        var grid = em.Add(Lattice(x, across ?? em.Constant(Division)), Lattice(y, em.Constant(Division)));
        var axes = now is { } when ? em.Add(Axis(em.Sub(x, when)), Axis(y)) : Axis(y);

        var ink = em.Add(
            em.Add(em.Mul(grid, 0.09f), em.Mul(axes, 0.2f)),
            signal);

        var lit = em.Mul(em.Combine(em.Constant(0.45f), one, em.Constant(0.72f)), ink);

        return em.Add(lit, em.Mul(em.Combine(one, em.Constant(0.25f), em.Constant(0.2f)), clipped));

        // Lines every division, measured back into the coordinate's own units so
        // that both axes are ruled the same thickness whatever the division is
        // and whatever the aspect ratio has done to x.
        Slot Lattice(Slot u, Slot division)
        {
            var cell = em.Unary(OpCode.Fract, em.Add(em.Binary(OpCode.Div, u, division), 0.5f));
            var away = em.Mul(em.Unary(OpCode.Abs, em.Add(cell, -0.5f)), division);

            return em.Sub(one, em.Ternary(
                OpCode.Smoothstep, em.Constant(0.0015f), em.Constant(0.005f), away));
        }

        // Zero on the vertical, now on the horizontal.
        Slot Axis(Slot u) => em.Sub(one, em.Ternary(
            OpCode.Smoothstep,
            em.Constant(0.004f),
            em.Constant(0.012f),
            em.Unary(OpCode.Abs, u)));
    }

    /// <summary>
    /// One socket and a picture of what arrives at it: the value over time,
    /// drawn as a chart rather than used as one.
    /// </summary>
    /// <remarks>
    /// An ordinary module emitting ordinary ops — a chart of a signal is itself a
    /// function of (x, y, t) — so both backends draw it without knowing what it
    /// is. What is different is the domain: <c>in</c> is
    /// <see cref="PortSpec.Swept"/>, so everything upstream is lowered reading
    /// the column's time rather than the frame's, with x and y pinned to nothing.
    /// The middle column is now and the right is the future. Memory is the one
    /// thing it cannot show: on the video path an accumulated phase is a multiply
    /// and a delay line is a wire — see <see cref="OpCode.Phase"/>.
    /// </remarks>
    private static NodeDef Probe() =>
        new NodeDef(
            ProbeTypeId, "Probe", ModuleCategories.Measurement,
            [
                Swept("in") with { Help = "What to chart, drawn across time around now." },
                Seconds("window", 0.3f) with { Help = "The timebase: a grid square across is an eighth of it." },
                Num("scale", 1f, 0.01f, 16f) with { Help = TopEdgeHelp },
            ],
            [Col("out") with { Help = ChartHelp }],
            (em, node) =>
            {
                var zero = em.Constant(0f);

                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);

                // The timebase is in decades, so one knob reaches from a fraction
                // of an audio cycle to half a minute — see PortDisplay.Duration.
                // A signal patched into it sweeps the chart exponentially.
                var window = em.Binary(OpCode.Pow, em.Constant(10f), node[1]);

                // Time across the picture: the middle column is the moment the
                // frame is for, and a column half the window to the right of it
                // is half a window later.
                var when = em.Add(em.Load(OpCode.LoadT), em.Mul(x, em.Mul(window, 0.5f)));

                em.PushDomain(zero, zero, when);
                var value = em.Coerce(node.Resolve(0), 1);
                em.PopDomain();

                // Where that value sits on the screen. 'scale' is the value at
                // the top edge, so a signal that fits reads directly off the
                // grid and one that does not runs off it — which the marker
                // below is for.
                var height = em.Binary(OpCode.Div, value, node[2]);

                // Ruled down the middle: the moment is a column here, with the
                // past to its left and the future to its right.
                return [Charted(em, x, y, height, now: zero)];
            },
            "A chart of 'in' in place of the picture while it is selected: the middle column is "
            + "now, the left the past, the right the future. It cannot show memory: drawn rather "
            + "than heard, an oscillator does not accumulate phase and a delay passes straight "
            + "through. A Scope shows what the speakers actually played.")
        {
            Words = "chart a signal",
            Sinks = ModuleSinks.Video,
            MergesVoices = true,
        };

    /// <summary>
    /// The Probe's opposite number: a chart of what the speakers have already
    /// played, rather than of what the screen computes the signal to be.
    /// </summary>
    /// <remarks>
    /// A separate module rather than a mode, because the two disagree and both
    /// are right: a Probe recomputes the signal per column (ADR-0040) and can
    /// draw the future but has no memory to show, while this computes nothing and
    /// keeps the last few thousand evaluations the speakers made. So it is blank
    /// until sound is on, blank for a signal the speakers never reach, and only
    /// ever the past. Mechanically it is the one module whose input is a root of
    /// a program — see <see cref="NodeDef.TapsSignal"/> and
    /// <see cref="OpCode.Tap"/> — and the one whose table changes as it runs.
    /// </remarks>
    private static NodeDef Scope() =>
        new NodeDef(
            ScopeTypeId, "Scope", ModuleCategories.Measurement,
            [
                // Swept and never resolved: this module does not evaluate its
                // input. Lowering the signal here as well would put the whole
                // chain into the picture's program to compute a value nothing
                // would look at.
                Swept("in") with { Help = "The sound to show, as it was played." },
                Seconds("window", -1.7f) with { Help = "How much is shown across the frame. A grid square is an eighth of it." },
                Num("scale", 1f, 0.01f, 16f) with { Help = TopEdgeHelp },
            ],
            [Col("out") with { Help = ChartHelp }],
            (em, node) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);

                // How far x reaches, which is where the newest evaluation goes.
                // This chart has a definite extent, and one that did not reach
                // the edges would have the past cut off it and dead margins
                // either side.
                var edge = em.Load(OpCode.LoadAspect);

                // The buffer holds exactly the window, whatever the window is —
                // see Traces.Buffer — so all this says is how far across the
                // frame the column is. Which is why 'window' appears nowhere in
                // these ops.
                var age = em.Binary(OpCode.Div, em.Add(x, edge), em.Mul(edge, 2f));

                var played = node.Trace is { } trace
                    ? em.Table(em.Mul(age, trace.Seconds), trace)
                    : em.Constant(0f);

                var height = em.Binary(OpCode.Div, played, node[2]);

                // Eight divisions across whatever the frame turns out to be, so a
                // square is an eighth of 'window' across and a quarter of 'scale'
                // up however the window is shaped.
                //
                // No rule for the moment, because the moment is the right-hand
                // edge and a line there would be half off the picture. The
                // phosphor says it instead: full brightness at the beam, fading
                // back into the past.
                return
                [
                    Charted(
                        em, x, y, height,
                        across: em.Mul(edge, 0.25f),
                        glow: em.Add(em.Mul(age, 0.62f), 0.38f)),
                ];
            },
            "A chart of what the speakers actually played: select it and the screen shows the "
            + "last 'window' seconds of 'in', newest at the right edge. Unlike a Probe it shows "
            + "memory (an oscillator's phase, a delay's tail, a sample, an envelope) but only what "
            + "reaches the Output's 'left' or 'right', and nothing while sound is off.")
        {
            Words = "waveform display",
            TapsSignal = true,
            ChartsSignal = true,
            Sinks = ModuleSinks.Video,
        };

    /// <summary>
    /// The Scope's chart turned sideways into frequency: how loud each part of the
    /// spectrum was over the last stretch the speakers played.
    /// </summary>
    /// <remarks>
    /// A Scope in every respect but what fills its buffer. The input is tapped and
    /// never lowered into the picture, and the buffer is refilled once a frame —
    /// with <c>Spectra.Chart</c> rather than a resampling, which is the
    /// whole of <see cref="NodeDef.ChartsSpectrum"/>. So it inherits all three of
    /// the Scope's cliffs, and the table read the shader takes as a texture.
    /// <para>
    /// The buffer holds linear amplitude and the decibels are taken here, so
    /// 'range' and 'scale' are sockets and a chart can be swept without the refill
    /// knowing. 'window' is read at compile time, as a Scope's is.
    /// </para>
    /// </remarks>
    private static NodeDef Analyzer()
    {
        // Eight rows down however far 'range' reaches, and the grid's columns on
        // the decades — 100 Hz, 1 kHz and 10 kHz — rather than evenly from the
        // edge, since a decade is what a log axis is read in.
        var decades = Math.Log10(SpectrumAxis.Highest / SpectrumAxis.Lowest);
        var firstDecade = (float)(Math.Log10(100d / SpectrumAxis.Lowest) / decades);

        // Twenty over the natural log of ten, which turns a natural log of an
        // amplitude into decibels.
        var decibels = (float)(20d / Math.Log(10d));

        return new NodeDef(
            AnalyzerTypeId, "Analyzer", ModuleCategories.Measurement,
            [
                // Swept and never resolved, for the Scope's reason.
                Swept("in") with { Help = "The sound whose frequencies are shown." },
                Seconds("window", -1f) with { Help = "How long it listens. Short follows every note, long settles." },
                Num("range", 96f, 12f, 144f) with { Help = "In dB: how far below the top edge the bottom is." },
                Num("scale", 1f, 0.01f, 16f) with { Help = "How loud a sine reaches the top edge: 1 is full scale, 0.1 is 20 dB down." },
            ],
            [Col("out") with { Help = ChartHelp }],
            (em, node) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);
                var edge = em.Load(OpCode.LoadAspect);

                // How far across the frame the column is, which is how far along
                // the frequency axis — the buffer is already laid out in log
                // frequency, so this is the Scope's arithmetic and no more.
                var along = em.Binary(OpCode.Div, em.Add(x, edge), em.Mul(edge, 2f));

                // Stretched so the right-hand edge lands on the last point rather
                // than past it, where the table read would be silence.
                var amplitude = node.Trace is { Samples.Length: > 1 } trace
                    ? em.Table(em.Mul(along, (trace.Samples.Length - 1f) / trace.SampleRate), trace)
                    : em.Constant(0f);

                var relative = em.Binary(OpCode.Div, amplitude, em.Binary(OpCode.Max, node[3], em.Constant(0.01f)));

                // Floored far below the deepest 'range' reaches, so silence is the
                // bottom of the chart rather than the log's guard value of nought,
                // which would be the top.
                var level = em.Mul(
                    em.Unary(OpCode.Log, em.Binary(OpCode.Max, relative, em.Constant(1e-8f))),
                    decibels);

                // Nought decibels at the top edge and minus 'range' at the bottom.
                // Held just above the bottom, so a quiet band is a line along it
                // rather than the bar that says a value has run off the chart.
                var range = em.Binary(OpCode.Max, node[2], em.Constant(1f));
                var height = em.Binary(
                    OpCode.Max,
                    em.Add(em.Mul(em.Binary(OpCode.Div, level, range), 2f), 1f),
                    em.Constant(-0.999f));

                var ruled = em.Add(x, em.Mul(edge, 1f - 2f * firstDecade));

                return
                [
                    Charted(
                        em, ruled, y, height,
                        across: em.Mul(edge, 2f / (float)decades),
                        ground: em.Constant(-1f)),
                ];
            },
            "A spectrum of what the speakers actually played: select it and the screen shows how "
            + "loud each frequency of 'in' was over the last 'window' seconds, 20 Hz to 20 kHz on a "
            + "log scale, gridded at 100 Hz, 1 kHz and 10 kHz. A bar along the top is louder than "
            + "the chart. Resolution is about 12 Hz. Like a Scope, it shows only what reaches the "
            + "Output's 'left' or 'right', and nothing while sound is off.")
        {
            Words = "spectrum display",
            TapsSignal = true,
            ChartsSignal = true,
            ChartsSpectrum = true,
            Sinks = ModuleSinks.Video,
        };
    }
}
