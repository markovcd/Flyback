using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// The one sink. Screen and speakers are sockets on a single module rather
    /// than two modules, and every patch has exactly one — see ADR-0037.
    /// </summary>
    public const string OutputTypeId = "output";
    
    /// <summary>
    /// The chart module. Named here because the shell roots the picture at one
    /// when it is the selected module, which is the only thing about a probe
    /// that is not ordinary — see <see cref="Compile.PatchCompiler"/>.
    /// </summary>
    public const string ProbeTypeId = "probe";
    
    /// <summary>
    /// The Probe read backwards: a loop swept across the picture at audio rate,
    /// so that what a chart draws of a signal, this hears of a field. Named here
    /// only for the tests and the preset that build one — the shell treats it as
    /// the ordinary module it is.
    /// </summary>
    public const string ScanTypeId = "scan";
    
    /// <summary>
    /// Whether a module is the sink. A patch always has one and never has two,
    /// so this is what keeps it out of the palette and out of the delete key —
    /// see <see cref="Patch.CanAdd"/> and <see cref="Patch.Remove"/>.
    /// </summary>
    public static bool IsSink(string typeId) => typeId == OutputTypeId;
    
    private static IEnumerable<NodeDef> Output()
    {
        yield return new NodeDef(
            OutputTypeId, "Output", "Output",
            [
                Col("color"),
                Num("left", 0f, -1f, 1f),
                Normalled("right", OutputLeftPort, -1f, 1f),
                Num("gain", 0.5f, 0f, 1f),
                Num("scan", 0f, 0f, 1f),
                Num("scan rate", 60f, 1f, 2000f),
            ],
            [],

            // Three results from one node: the screen reads the first and
            // the speakers the other two. Which of them a given program
            // takes is the only difference between the two compilations.
            (em, i) => [i[0], em.Mul(i[1], i[3]), em.Mul(i[2], i[3])],
            "The screen and the speakers. Everything upstream of 'color' is what you "
            + "see; everything upstream of 'left' is what you hear. Leave 'right' "
            + "unpatched and it carries 'left' through, as a normalled jack would. "
            + "'scan' at 0 drives the patch from Time; at 1 it sweeps the image and you "
            + "hear the picture, at 'scan rate' sweeps per second.");

        yield return new NodeDef(
            "audio.frequency", "Frequency", "Output",
            [Num("hz", 220f, 20f, 4000f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob in hertz rather than in the single digits the visual modules use. "
            + "Patch it into an oscillator's freq to work at audible pitches.");

        yield return new NodeDef(
            "audio.note", "Note", "Output",
            [
                Pitched("note", 57f),
                Num("octave"),
                Num("cents", 0f, -100f, 100f),
            ],
            [Num("hz"), Num("note")],
            (em, i) =>
            {
                // Octaves are twelve semitones, so anything patched into
                // 'octave' arrives on the same scale the note number is in
                // and the two simply add up before the snap.
                var wanted = em.Add(i[0], em.Mul(i[1], Pitch.Semitones));

                // Halfway between two notes is where the snap belongs, and a
                // floor of the note plus a half is that. Instant, with
                // nothing smoothing it: a quantiser that eased into its notes
                // would not be one. The click this once cost never came from
                // here — an accumulated phase (ADR-0030) takes a frequency
                // this steps and moves the waveform's slope rather than its
                // value, and a slope has no click in it.
                var note = em.Unary(OpCode.Floor, em.Add(wanted, 0.5f));

                // Detune is applied after the snap, which is the whole point
                // of having it: it is the one way to sit between two notes.
                var tuned = em.Add(note, em.Mul(i[2], 0.01f));
                var octaves = em.Mul(em.Add(tuned, -Pitch.ConcertNote), 1f / Pitch.Semitones);

                return
                [
                    em.Mul(em.Binary(OpCode.Pow, em.Constant(2f), octaves), Pitch.ConcertPitch),
                    note,
                ];
            },
            "Frequency, but in notes. Pick one on the knob — 57 is A3 — or patch a signal "
            + "in and it snaps to the nearest whole note on its way through, which is what "
            + "turns a sweep into a run up the chromatic scale. The change is instant and "
            + "silent: the oscillators carry their phase, so a pitch that steps bends the "
            + "waveform rather than breaking it. 'hz' goes to an oscillator's freq; 'note' "
            + "hands the snapped number on, so a second Note can play an interval off it.");

        yield return Probe();
        yield return Scan();
    }
    
    /// <summary>
    /// One socket and a picture of what arrives at it: the value over time,
    /// drawn as a chart rather than used as one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is measured here and nothing is read back. A chart of a signal is
    /// itself a function of (x, y, t) — the value at this column, against the
    /// height of this row — so the probe is an ordinary module emitting ordinary
    /// ops, and both backends draw it without knowing what it is.
    /// </para>
    /// <para>
    /// What makes it different from every other module is the domain its input
    /// is read over. Time runs across the picture instead of the clock, which is
    /// why <c>in</c> is <see cref="PortSpec.Swept"/>: the compiler leaves it
    /// alone until the sweep has been pushed, and everything upstream of it is
    /// then lowered reading that time rather than the frame's. x and y are
    /// pinned to nothing while it does, so what is charted is the signal at the
    /// middle of the picture — a module that draws with Coordinates has a value
    /// per pixel, and there is no one line that is all of them.
    /// </para>
    /// <para>
    /// The middle column is now; the left is the past and the right is the
    /// future, which a machine that is a pure function of t can show as readily
    /// as its history. The one thing it cannot show is memory: the video path
    /// evaluates pixels in parallel and passes no state, so an accumulated phase
    /// is the multiply it replaces and a delay line is a wire — see
    /// <see cref="OpCode.Phase"/>. What the probe draws of those is what the
    /// screen already makes of them, not what the speakers hear.
    /// </para>
    /// </remarks>
    private static NodeDef Probe()
    {
        // One grid square, in screen units. Eight of them across the middle of
        // the picture, which is what makes the grid the axis: however wide the
        // preview is, a square is an eighth of the window and a quarter of the
        // scale.
        const float division = 0.25f;

        return new NodeDef(
            ProbeTypeId, "Probe", "Output",
            [
                Swept("in"),
                Seconds("window", 0.3f),
                Num("scale", 1f, 0.01f, 16f),
            ],
            [Col("out")],
            (em, node) =>
            {
                var one = em.Constant(1f);
                var zero = em.Constant(0f);

                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);

                // The timebase is in decades, so that one knob reaches from a
                // fraction of an audio cycle to half a minute — see
                // PortDisplay.Duration. Two ops, and a signal patched into it
                // sweeps the chart exponentially, which is the only way a sweep
                // across that range is any use.
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

                var trace = em.Sub(one, em.Ternary(
                    OpCode.Smoothstep,
                    em.Constant(0.006f),
                    em.Constant(0.02f),
                    em.Unary(OpCode.Abs, em.Sub(y, height))));

                // Filled from the line down to zero, which is what keeps the
                // chart readable when the signal moves faster than the pixels
                // can follow: a trace alone breaks into dots there, and the fill
                // becomes the envelope a scope shows at the same sweep.
                var fill = em.Mul(
                    em.Binary(OpCode.Step, em.Binary(OpCode.Min, height, zero), y),
                    em.Binary(OpCode.Step, y, em.Binary(OpCode.Max, height, zero)));

                var grid = em.Add(Lattice(x), Lattice(y));
                var axes = em.Add(Axis(x), Axis(y));

                // A bar along whichever edge the signal has gone off, because a
                // value past the top of the chart is otherwise indistinguishable
                // from no signal at all.
                var over = em.Binary(OpCode.Step, one, em.Unary(OpCode.Abs, height));
                var edge = em.Binary(OpCode.Step, em.Constant(0.96f), em.Unary(OpCode.Abs, y));
                var side = em.Binary(OpCode.Step, zero, em.Mul(height, y));
                var clipped = em.Mul(em.Mul(over, edge), side);

                var ink = em.Add(
                    em.Add(em.Mul(grid, 0.09f), em.Mul(axes, 0.2f)),
                    em.Add(em.Mul(fill, 0.22f), trace));

                var lit = em.Mul(
                    em.Combine(em.Constant(0.45f), one, em.Constant(0.72f)),
                    ink);

                return
                [
                    em.Add(lit, em.Mul(em.Combine(one, em.Constant(0.25f), em.Constant(0.2f)), clipped)),
                ];

                // Lines every division, measured back into the coordinate's own
                // units so that both axes are ruled the same thickness whatever
                // the aspect ratio has done to x.
                Slot Lattice(Slot u)
                {
                    var cell = em.Unary(OpCode.Fract, em.Add(em.Mul(u, 1f / division), 0.5f));
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
            },
            "A chart of whatever is patched into it, in place of the picture. Select it and "
            + "the screen shows the value of its 'in' over time instead of the patch: the "
            + "middle column is now, the left is the past and the right is the future, and one "
            + "grid square is an eighth of 'window' across and a quarter of 'scale' up. "
            + "'window' is marked in decades so that one knob covers a single cycle of an "
            + "audible tone as well as a minute of an LFO — it reads as the time it is. Select "
            + "anything else and the picture comes back. It is an ordinary module besides — its "
            + "'out' is the chart as a color, so it can be patched into the Output to keep it "
            + "on screen. What it cannot show is memory: drawn rather than heard, an oscillator "
            + "does not accumulate its phase and a delay line passes straight through, so a "
            + "chart of either is what the screen makes of it rather than what the speakers do.");
    }

    /// <summary>
    /// One loop swept round the picture at audio rate, and the value it passes
    /// over: a field read as a waveform rather than a waveform drawn as a field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Probe the other way about, and the same mechanism upside down. A
    /// Probe pushes a <em>time</em> that varies across the picture and lowers
    /// its input under it; this pushes an <em>(x, y)</em> that varies along a
    /// loop. Everything upstream of <c>in</c> is therefore lowered reading a
    /// position on that loop instead of the pixel's own — see
    /// <see cref="PortSpec.Swept"/> — and what comes back is one ordinary
    /// scalar per evaluation, which is what a sample is.
    /// </para>
    /// <para>
    /// The loop is a circle and not a raster because of what the two do to the
    /// sound. A raster's <c>fract</c> jumps once a line, and a step in the
    /// waveform every cycle is a sawtooth edge that is there whatever the
    /// picture holds — which is why a scanned image mostly sounds like the scan.
    /// A circle closes on itself, so the sweep contributes no discontinuity at
    /// all and the whole of the waveform is the picture. What that leaves is
    /// wavetable synthesis with the field as the table: <c>radius</c>, <c>x</c>
    /// and <c>y</c> choose which loop through it is read, and moving them is the
    /// table sweep.
    /// </para>
    /// <para>
    /// Where on the loop one evaluation sits is the one thing the two sinks
    /// cannot agree on: the ear is at a moment and the eye is at a pixel. So the
    /// bearing is chosen on <see cref="Emitter.HasMemory"/> — the speakers take
    /// the accumulated phase, the screen takes the pixel's own angle from the
    /// centre, and every pixel then lands on the point of the loop it is looking
    /// at. One lowering serves both (ADR-0043), and the picture that falls out
    /// is the X-Y display an oscilloscope shows in the mode the Probe is not.
    /// </para>
    /// </remarks>
    private static NodeDef Scan()
    {
        // How far a value of one 'scale' pushes the trace off the loop, as a
        // fraction of the radius. Half, so a signal that fits reads as a ring
        // with a waveform around it rather than as a disc.
        const float swing = 0.5f;

        return new NodeDef(
            ScanTypeId, "Scan", "Output",
            [
                Swept("in"),
                Domain("clock"),
                Num("rate", 220f, 0f, 4000f),
                Num("radius", 0.5f, 0f),
                Num("x"),
                Num("y"),
                Num("scale", 1f, 0.01f, 16f),
            ],
            [Num("out"), Col("view")],
            (em, node) =>
            {
                var one = em.Constant(1f);

                var radius = node[3];
                var centreX = node[4];
                var centreY = node[5];

                // Where this pixel stands relative to the loop, which is the
                // eye's only way of asking where on the loop it is looking.
                var awayX = em.Sub(em.Load(OpCode.LoadX), centreX);
                var awayY = em.Sub(em.Load(OpCode.LoadY), centreY);

                // Accumulated rather than multiplied out, for ADR-0030's reason:
                // a rate that steps then bends the waveform instead of breaking
                // it, which is what makes this playable from a Note.
                var turns = em.Phase(node[1], node[2], em.Constant(0f));

                var angle = em.Ternary(
                    OpCode.Mix,
                    em.Binary(OpCode.Atan2, awayY, awayX),
                    em.Mul(turns, Tau),
                    em.HasMemory());

                // Read before the push, so it is the renderer's clock the sweep
                // is carried on and not something the sweep has just replaced.
                var now = em.Load(OpCode.LoadT);

                em.PushDomain(
                    em.Add(centreX, em.Mul(radius, em.Unary(OpCode.Cos, angle))),
                    em.Add(centreY, em.Mul(radius, em.Unary(OpCode.Sin, angle))),
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
            "Hears the picture. A circle is swept round the image 'rate' times a second and "
            + "whatever is patched into 'in' is read along it, so one turn of the loop is one "
            + "cycle of a waveform and 'rate' is the pitch. 'radius', 'x' and 'y' choose which "
            + "loop is read: the image is the wavetable and moving them sweeps through it, "
            + "which is the knob to reach for rather than the pitch. Patch Time into 'clock'. "
            + "A closed loop is the whole point — a raster's retrace would put a sawtooth edge "
            + "in every cycle whatever the picture held, and a circle contributes nothing of "
            + "its own. 'out' is the sample; 'view' is the loop drawn where it runs with the "
            + "value swinging the trace off it, which is the X-Y display to the Probe's chart. "
            + "A loop that follows the picture's own contours reads a constant and is silent — "
            + "a circle centred on Rings is the way to hear nothing, and moving it off centre "
            + "is the way to hear everything.");
    }
    
    /// <summary>An input that carries an earlier one through when left unpatched.</summary>
    private static PortSpec Normalled(string name, int from, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, 0f, min, max, from);
    
    /// <summary>
    /// An input the module reads over a domain of its own rather than over the
    /// pixel's — see <see cref="PortSpec.Swept"/>. Untyped like the maths
    /// modules', so a color may be looked at as readily as a scalar.
    /// </summary>
    private static PortSpec Swept(string name) =>
        new(name, PortKind.Any, Swept: true);
    
    /// <summary>A note number, which the editor writes out by name rather than as a number.</summary>
    private static PortSpec Pitched(string name, float value) =>
        new(name, PortKind.Scalar, value, 0f, 127f, -1, PortDisplay.Note);
 
    /// <summary>
    /// A length of time, held in decades of seconds and written out as the time
    /// it is — see <see cref="PortDisplay.Duration"/>. The range is a hundred
    /// microseconds to half a minute, which is one audio cycle at the bottom and
    /// a slow LFO at the top.
    /// </summary>
    private static PortSpec Seconds(string name, float value) =>
        new(name, PortKind.Scalar, value, -4f, 1.5f, -1, PortDisplay.Duration);
}