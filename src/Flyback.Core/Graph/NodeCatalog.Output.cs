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
    /// when it is selected — see <c>Compile.PatchCompiler</c>.
    /// </summary>
    public const string ProbeTypeId = "probe";

    /// <summary>
    /// The chart of what was played. Named here because the shell roots the
    /// picture at one when it is selected; the compiler finds the extra audio
    /// roots through <see cref="NodeDef.TapsSignal"/> rather than by name.
    /// </summary>
    public const string ScopeTypeId = "scope";

    /// <summary>
    /// The chart of what frequencies were played. Named here because the shell
    /// roots the picture at one when it is selected, as it does a Scope.
    /// </summary>
    public const string AnalyzerTypeId = "analyzer";

    /// <summary>Whether a module is one the shell will show in place of the picture.</summary>
    public static bool IsChart(string typeId) => typeId is ProbeTypeId or ScopeTypeId or AnalyzerTypeId;

    /// <summary>
    /// The level meter. Named here because what it reads is filled in from
    /// outside the program — see <c>Compile.Meters</c>.
    /// </summary>
    public const string MeterTypeId = "meter";

    /// <summary>
    /// How loud the speakers are, as a number the picture can use.
    /// </summary>
    /// <remarks>
    /// No opcode and no arithmetic: two <see cref="OpCode.LoadLive"/>s and a
    /// divide, filled in once a frame by <c>Meters</c> from the ring the
    /// Scope charts — so this survives to the shader, where a Scope cannot. The
    /// input is tapped and <see cref="PortSpec.Swept"/>, so a picture driven by a
    /// bass line reads one number rather than computing the line per pixel.
    /// 'window' is read off the knob at compile time, since what fills the answer
    /// in runs outside the program; 'scale' is applied after and may be swept.
    /// </remarks>
    private static NodeDef Meter() =>
        new NodeDef(
            MeterTypeId, "Meter", ModuleCategories.Measurement,
            [
                Swept("in") with { Help = "Patch the Output's 'left' here for everything, one voice for just that." },
                Seconds("window", -1.3f) with
                {
                    Help = "How far back both readings look. A few milliseconds follows every drum; "
                        + "half a second leans into the music.",
                },
                Num("scale", 1f, 0.01f, 16f) with { Help = "Divides both readings." },
            ],
            [
                Num("level") with { Help = "The loudness over 'window', about 1 at full scale." },
                Num("peak") with { Help = "The furthest from silence, about 1 at full scale. The one that hits." },
            ],
            (em, node) =>
            {
                // Nought where there is no instance to be addressed as — the
                // hidden module a normalled socket reads. Nothing outside could
                // fill in a reading for a node that is not in the patch.
                if (node.Node == Guid.Empty) return [em.Constant(0f), em.Constant(0f)];

                var scale = em.Binary(OpCode.Max, node[2], em.Constant(0.01f));

                return
                [
                    em.Binary(OpCode.Div, em.Live(MeterSignals.Key(node.Node, MeterSignals.Level)), scale),
                    em.Binary(OpCode.Div, em.Live(MeterSignals.Key(node.Node, MeterSignals.Peak)), scale),
                ];
            },
            "How loud the sound is, as a number to draw with: patch a signal into 'in' and drive a "
            + "hue, a size or a brightness from 'level' or 'peak'. Costs the picture nothing and "
            + "keeps the GPU. Reads nothing where no sound runs, as in an exported still.")
        {
            // Tapped but not charted: what it wants from the ring is two numbers
            // rather than a buffer, so no chart is allocated for it and nothing
            // refills one. See NodeDef.ChartsSignal.
            TapsSignal = true,
            Sinks = ModuleSinks.Video,
        };

    /// <summary>
    /// The Probe read backwards: a loop swept across the picture at audio rate,
    /// so what a chart draws of a signal, this hears of a field. Named here only
    /// for the tests and the preset that build one.
    /// </summary>
    public const string ScanTypeId = "scan";

    /// <summary>
    /// The scale quantiser. Named here because it is the one module whose
    /// instance carries a scale, and the editor and the assistant both ask
    /// whether a node is it — see <see cref="ScaleExtra"/>.
    /// </summary>
    public const string QuantiserTypeId = "audio.quantiser";
    
    /// <summary>
    /// Whether a module is the sink, which is what keeps it out of the palette
    /// and out of the delete key — see <see cref="Patch.CanAdd"/> and
    /// <see cref="Patch.Remove"/>.
    /// </summary>
    public static bool IsSink(string typeId) => typeId == OutputTypeId;
    
    private static IEnumerable<NodeDef> Output()
    {
        yield return new NodeDef(
            OutputTypeId, "Output", ModuleCategories.Output,
            [
                Col("color") with { Help = "The screen." },
                Num("left", 0f, -1f, 1f) with { PatchOnly = true, Help = "The left speaker." },
                Normalled("right", OutputLeftPort, -1f, 1f) with { Help = "The right speaker, carrying 'left' when nothing is patched." },
                Num("volume", 0.5f, 0f, 1f) with
                {
                    Help = "Multiplies both speakers. Nought closes them rather than feeding them silence.",
                },
            ],
            [],

            // Three results from one node: the screen reads the first and
            // the speakers the other two. Which of them a given program
            // takes is the only difference between the two compilations.
            (em, i) => [i[0], em.Mul(i[1], i[3]), em.Mul(i[2], i[3])],
            "The patch's one Output: the screen and the speakers.");

        yield return new NodeDef(
            "audio.note", "Note", ModuleCategories.Pitch,
            [
                Pitched("note", 57f) with { Help = "A signal is snapped to the nearest semitone; the knob is a direct note value." },
                new PortSpec("octave", PortKind.Scalar, 0f, -4f, 4f, Display: PortDisplay.Integer)
                {
                    Help = "Whole octaves, added before the snap.",
                },
                Num("cents", 0f, -100f, 100f) with { Help = "Hundredths of a semitone, added after the snap: the one way between two notes." },
            ],
            [
                Num("hz") with { Help = "The frequency, for an oscillator's 'freq'." },
                Num("note") with { Help = "The semitone it snapped to." },
            ],
            (em, i) => Sounded(em, i[0], i[1], i[2]),
            "Pitch in note numbers, turned into a frequency.");

        yield return Quantiser();
        yield return Tune();
        yield return Probe();
        yield return Scope();
        yield return Analyzer();
        yield return Meter();
        yield return Scan();
    }

    /// <summary>
    /// One grid square, in screen units. Eight across the middle of the picture,
    /// so a square is an eighth of the window and a quarter of the scale however
    /// wide the preview is.
    /// </summary>
    private const float Division = 0.25f;

    /// <summary>
    /// A chart, given where the signal sits: the trace, the fill under it, the
    /// grid it is read against, and the bar along whichever edge it has run off.
    /// Shared by the Probe and the Scope, which arrive at
    /// <paramref name="height"/> by opposite routes and are the same picture from
    /// there on, so the two can be laid side by side and compared.
    /// </summary>
    /// <param name="y"></param>
    /// <param name="height">
    /// Where the trace goes, in screen units — the value already divided by
    /// whatever the top of the chart is worth.
    /// </param>
    /// <param name="now">
    /// Where to rule the line marking the moment, or null for a chart whose
    /// moment is the edge of the frame, where a rule would be half off it.
    /// </param>
    /// <param name="glow">
    /// How brightly to draw the signal, per column, or null for evenly — a
    /// phosphor fading behind the beam, which is how a chart of the past says
    /// which end is now. The grid is not dimmed with it.
    /// </param>
    /// <param name="across">
    /// How wide a grid square is, or null for the fixed <see cref="Division"/>.
    /// A Scope rules eight across the frame however wide it is, so its squares
    /// are a value rather than a constant.
    /// </param>
    /// <param name="ground">
    /// Where the fill under the trace runs down to, or null for the middle of the
    /// chart. A waveform swings either side of nought; a spectrum stands up from
    /// the bottom edge.
    /// </param>
    /// <param name="em"></param>
    /// <param name="x"></param>
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
    /// A note number as a frequency, and the semitone it was snapped to on the way:
    /// the whole of a Note.
    /// </summary>
    private static Slot[] Sounded(Emitter em, Slot pitch, Slot octave, Slot cents)
    {
        // Octaves are twelve semitones, so anything patched into
        // 'octave' arrives on the same scale the note number is in
        // and the two simply add up before the snap.
        var wanted = em.Add(pitch, em.Mul(octave, Pitch.Semitones));

        // Halfway between two notes is where the snap belongs, and a
        // floor of the note plus a half is that. Instant, with nothing
        // smoothing it: an accumulated phase (ADR-0030) moves the
        // waveform's slope rather than its value, and a slope has no
        // click in it.
        var note = em.Unary(OpCode.Floor, em.Add(wanted, 0.5f));

        // Detune is applied after the snap, which is the whole point
        // of having it: it is the one way to sit between two notes.
        var tuned = em.Add(note, em.Mul(cents, 0.01f));
        var octaves = em.Mul(em.Add(tuned, -Pitch.ConcertNote), 1f / Pitch.Semitones);

        return
        [
            em.Mul(em.Binary(OpCode.Pow, em.Constant(2f), octaves), Pitch.ConcertPitch),
            note,
        ];
    }

    /// <summary>
    /// The notes a freshly placed Quantiser snaps to: a major scale, so the
    /// module audibly does something the moment it is placed. Chromatic would
    /// snap to the nearest semitone, which the Note module already does.
    /// </summary>
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>
    /// A pitch quantiser with a scale on it: the note nearest to what arrives,
    /// out of the ones the scale has switched on.
    /// </summary>
    /// <remarks>
    /// The scale is a list on the node rather than twelve sockets (ADR-0038):
    /// which notes exist is a decision about the piece, not a signal in it. It
    /// is a compile-time value — see <see cref="EmitContext.Scale"/> — so each
    /// note switched on costs one candidate, and the ends of the range are worth
    /// taking: all twelve is the nearest semitone, none at all is a wire.
    /// </remarks>
    private static NodeDef Quantiser() => new(
        QuantiserTypeId, "Quantiser", ModuleCategories.Pitch,
        [
            Pitched("in", 57f),
            Num("hold", 0f, 0f, 1f) with
            {
                Lenient = true,
                Help = "Freezes the note while it is up. Patch the envelope's gate here so the pitch "
                    + "cannot slide mid-note.",
            },
        ],
        [Num("note") with { Help = "A note number: patch it into a Note to hear it." }],
        EmitQuantiser,
        "Snaps what arrives to the nearest note the scale has switched on, in any octave, so "
        + "a sweep becomes a run up the scale. The switches are pitch classes: A on is every A. "
        + "All twelve on is the nearest semitone; none on is a wire. Audio only, like every "
        + "hold: the picture snaps continuously.")
    {
        Extras = [new ScaleExtra(Major)],
    };

    /// <summary>
    /// One candidate per note in the scale, and the closest of them.
    /// </summary>
    /// <remarks>
    /// The nearest note of pitch class <c>p</c> to a signal <c>n</c> is
    /// <c>12·round((n − p)/12) + p</c>, so there is no loop and no branch: a
    /// fixed candidate per switch that is on, and a running minimum over them.
    /// The divide by twelve is hoisted and the pitch class folds into the
    /// rounding's constant, leaving a floor and three ops per candidate. Ties go
    /// to the note the scale names later, which is the higher pitch class.
    /// </remarks>
    private static Slot[] EmitQuantiser(Emitter em, EmitContext node) =>
        [Snapped(em, node[0], node[1], node.Scale)];

    /// <summary>
    /// A transposition, a Quantiser and a Note: a line of a part moved onto the
    /// chord, held to the scale, and turned into the frequency an oscillator wants.
    /// </summary>
    /// <remarks>
    /// The three are nearly always patched in that order and nothing is wanted from
    /// between them, so this is their arithmetic with the Note's 'octave' and
    /// 'cents' at rest. The scale is the Quantiser's, carried the same way.
    /// </remarks>
    private static NodeDef Tune() => new(
        "audio.tune", "Tune", ModuleCategories.Pitch,
        [
            Pitched("in", 57f),
            Num("transpose", 0f, -48f, 48f) with { Help = "Semitones, added first: a chord's root, or 12 for an octave up." },
            Num("hold", 0f, 0f, 1f) with { Lenient = true, Help = "Freezes the note while it is up, as a Quantiser's does." },
        ],
        [
            Num("hz") with { Help = "For an oscillator's 'freq'." },
            Num("note") with { Help = "The same pitch as a note number." },
        ],
        (em, i) =>
        {
            var nought = em.Constant(0f);
            var snapped = Snapped(em, em.Add(i[0], i[1]), i[2], i.Scale);

            return Sounded(em, snapped, nought, nought);
        },
        "A Quantiser and a Note in one: a note number in, a frequency in the scale out.")
    {
        Extras = [new ScaleExtra(Major)],
    };

    /// <summary>What arrives, snapped to the scale and frozen while 'hold' is up.</summary>
    private static Slot Snapped(Emitter em, Slot signal, Slot hold, IReadOnlyList<int> scale)
    {

        // Nothing switched on: there is no note to snap to, so what comes out is
        // what went in. The same answer a Delay with nothing to remember gives,
        // and it is what makes the twelve switches safe to turn off one at a
        // time — the module fades out of the patch rather than falling out of it.
        if (scale.Count == 0) return Frozen(em, hold, signal);

        // Every note switched on: the nearest of all twelve is the nearest
        // semitone, and that is a rounding rather than twelve candidates.
        if (scale.Count == Pitch.Classes)
            return Frozen(em, hold, em.Unary(OpCode.Floor, em.Add(signal, 0.5f)));

        // The signal in octaves. Every candidate is a floor of this plus a
        // constant, so it is worth one op here rather than one per note.
        var octaves = em.Mul(signal, 1f / Pitch.Semitones);

        var best = em.Constant(0f);
        var nearest = em.Constant(0f);

        for (var i = 0; i < scale.Count; i++)
        {
            // round((signal - class) / 12), with the shift and the half folded
            // into the one constant a floor needs.
            var octave = em.Unary(
                OpCode.Floor,
                em.Add(octaves, 0.5f - scale[i] / Pitch.Semitones));

            var candidate = em.Add(em.Mul(octave, Pitch.Semitones), scale[i]);
            var away = em.Unary(OpCode.Abs, em.Sub(signal, candidate));

            if (i == 0)
            {
                best = candidate;
                nearest = away;
                continue;
            }

            // 1 where this candidate is at least as close as the best so far,
            // and the two Mixes are the branch a register machine does not have.
            var closer = em.Binary(OpCode.Step, away, nearest);

            best = em.Ternary(OpCode.Mix, best, candidate, closer);
            nearest = em.Ternary(OpCode.Mix, nearest, away, closer);
        }

        return Frozen(em, hold, best);
    }

    /// <summary>
    /// The note, frozen for as long as 'hold' is up.
    /// </summary>
    /// <remarks>
    /// A level rather than an edge, because a socket resting on its knob hands
    /// the emit function a register like any other: "nothing is patched" is not a
    /// question this can ask, and a level answers it by not needing to — nought
    /// is down, so an unwired module snaps continuously. It also states the
    /// guarantee the right way round: the note cannot move while a note sounds,
    /// and wiring the envelope's gate makes the two intervals the same one. The
    /// cost is that a short trigger holds only while it is up.
    /// <para>
    /// Two cells, and the same scaling the Sample &amp; Hold uses: a cell is
    /// clamped to ±16 and a note number runs to 127. See
    /// <see cref="HoldHeadroom"/>.
    /// </para>
    /// </remarks>
    private static Slot Frozen(Emitter em, Slot hold, Slot note)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var heldCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();

        var held = em.Mul(em.UnitRead(heldCell), HoldHeadroom);
        var before = em.UnitRead(edgeCell);

        var up = em.Binary(OpCode.Step, em.Constant(GateOpen), hold);

        // Taken while the hold is down, and on the evaluation it goes up — so
        // what is frozen is the note as it stood the moment the gate opened
        // rather than the one before it.
        var rise = em.Mul(up, em.Sub(one, before));
        var take = em.Binary(OpCode.Max, rise, em.Sub(one, up));

        // And wherever there is nothing to have held: the screen, and the first
        // evaluation of a program, which is what primes the cell.
        take = em.Binary(OpCode.Max, take, em.Sub(one, live));

        var next = em.Ternary(OpCode.Mix, held, note, take);

        em.UnitWrite(heldCell, em.Mul(next, 1f / HoldHeadroom));
        em.UnitWrite(edgeCell, up);

        return next;
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
                Swept("in"),
                Seconds("window", 0.3f) with { Help = "The timebase: a grid square across is an eighth of it." },
                Num("scale", 1f, 0.01f, 16f) with { Help = "The value at the top edge. A grid square up is a quarter of it." },
            ],
            [Col("out") with { Help = "The chart as a color." }],
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
            Sinks = ModuleSinks.Video,
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
                Swept("in"),
                Seconds("window", -1.7f) with { Help = "How much is shown across the frame. A grid square is an eighth of it." },
                Num("scale", 1f, 0.01f, 16f) with { Help = "The value at the top edge. A grid square up is a quarter of it." },
            ],
            [Col("out") with { Help = "The chart as a color." }],
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
    /// the Scope's cliffs, and the table read that keeps it off the shader.
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
                Swept("in"),
                Seconds("window", -1f) with { Help = "How long it listens. Short follows every note, long settles." },
                Num("range", 96f, 12f, 144f) with { Help = "In dB: how far below the top edge the bottom is." },
                Num("scale", 1f, 0.01f, 16f) with { Help = "The top edge is a full-scale sine divided by this." },
            ],
            [Col("out") with { Help = "The chart as a color." }],
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
            TapsSignal = true,
            ChartsSignal = true,
            ChartsSpectrum = true,
            Sinks = ModuleSinks.Video,
        };
    }

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
    private static NodeDef Scan()
    {
        // How far a value of one 'scale' pushes the trace off the loop, as a
        // fraction of the radius. Half, so a signal that fits reads as a ring
        // with a waveform around it rather than as a disc.
        const float swing = 0.5f;

        return new NodeDef(
            ScanTypeId, "Scan", ModuleCategories.Measurement,
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
                    em.Mul(turns, Tau),
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
            + "and all.");
    }
    
    /// <summary>An input that carries an earlier one through when left unpatched.</summary>
    private static PortSpec Normalled(string name, int from, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, 0f, min, max, from);
    
    /// <summary>
    /// An input the module reads over a domain of its own rather than over the
    /// pixel's — see <see cref="PortSpec.Swept"/>. Untyped, so a color may be
    /// looked at as readily as a scalar.
    /// </summary>
    private static PortSpec Swept(string name) =>
        new(name, PortKind.Any, Swept: true);
    
    /// <summary>A note number, which the editor writes out by name rather than as a number.</summary>
    private static PortSpec Pitched(string name, float value) =>
        new(name, PortKind.Scalar, value, 0f, 127f, -1, PortDisplay.Note);
 
    /// <summary>
    /// A length of time, held in decades of seconds and written out as the time
    /// it is — see <see cref="PortDisplay.Duration"/>. A hundred microseconds to
    /// half a minute: one audio cycle at the bottom, a slow LFO at the top.
    /// </summary>
    private static PortSpec Seconds(string name, float value) =>
        new(name, PortKind.Scalar, value, -4f, 1.5f, -1, PortDisplay.Duration);
}