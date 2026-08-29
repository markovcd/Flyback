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
    /// The chart of what was played. Named here for the same reason the Probe
    /// is — the shell roots the picture at one when it is selected — and for one
    /// more: the compiler has to know which nodes' inputs are extra roots of the
    /// audio program, though it asks that through
    /// <see cref="NodeDef.TapsSignal"/> rather than by name.
    /// </summary>
    public const string ScopeTypeId = "scope";

    /// <summary>Whether a module is one of the two the shell will show in place of the picture.</summary>
    public static bool IsChart(string typeId) => typeId is ProbeTypeId or ScopeTypeId;

    /// <summary>
    /// The level meter. Named here because what it reads is filled in from
    /// outside the program, and the thing doing the filling has to find it — see
    /// <see cref="Compile.Meters"/>.
    /// </summary>
    public const string MeterTypeId = "meter";

    /// <summary>
    /// How loud the speakers are, as a number the picture can use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap this closes was the sharpest one left in a machine whose whole
    /// premise is one patch making a picture and a sound: the picture could not
    /// hear. A Scope charts what was played, and everything else the eye knows
    /// about the ear it knows by sharing a modulator — the same sweep sent to two
    /// places, which is a patch saying two things at once rather than one half of
    /// it listening to the other.
    /// </para>
    /// <para>
    /// It taps its input the way a Scope does — the socket is a root of the
    /// speakers' program whether or not anything downstream reads this module,
    /// and is <see cref="PortSpec.Swept"/> so that the screen never lowers the
    /// signal chain behind it. That second part is the whole cost model: a
    /// picture driven by a bass line does not compute a bass line per pixel, it
    /// reads one number.
    /// </para>
    /// <para>
    /// And it reads it as a live input, which is the part worth noticing. There
    /// is no opcode here and no arithmetic: the module is two
    /// <see cref="OpCode.LoadLive"/>s and a divide, so what the picture does with
    /// the sound's loudness is exactly what it does with a note somebody is
    /// holding down — it is told. <see cref="Meters"/> does the telling, once a
    /// frame, from the same ring the Scope charts. The consequence that matters is
    /// that this survives to the shader, where a Scope cannot: a uniform is a
    /// uniform, and a table is a table.
    /// </para>
    /// <para>
    /// 'window' is how much of the past is being weighed, and it is the only
    /// smoothing there is. Short is a level that jumps on every hit; long is one
    /// that leans. It is read off the knob at compile time rather than out of a
    /// register, for the reason the Scope's is: what fills the answer in runs once
    /// a frame, outside the program, and cannot act on a value that arrives per
    /// sample.
    /// </para>
    /// <para>
    /// Both readings at once, off one pass over the window, because they answer
    /// different questions and a patch that wants the difference between them
    /// should not need two of these. 'scale' is an ordinary socket by contrast —
    /// it is applied to the number after it arrives, so it may be swept, and it is
    /// how a quiet signal is brought up to something a hue can use.
    /// </para>
    /// </remarks>
    private static NodeDef Meter() =>
        new NodeDef(
            MeterTypeId, "Meter", "Output",
            [
                Swept("in"),
                Seconds("window", -1.3f),
                Num("scale", 1f, 0.01f, 16f),
            ],
            [Num("level"), Num("peak")],
            (em, node) =>
            {
                // Nought where there is no instance to be addressed as — the
                // hidden module a normalled socket reads. Nothing outside could
                // fill in a reading for a node that is not in the patch.
                if (node.Node == Guid.Empty) return [em.Constant(0f), em.Constant(0f)];

                var scale = em.Binary(OpCode.Max, node[2], em.Constant(0.01f));

                return
                [
                    em.Binary(OpCode.Div, em.Live(Meters.Key(node.Node, Meters.Level)), scale),
                    em.Binary(OpCode.Div, em.Live(Meters.Key(node.Node, Meters.Peak)), scale),
                ];
            },
            "How loud the sound is, as a number to draw with. Patch the signal you want it to "
            + "listen to into 'in' — the Output's own 'left' for everything, or one voice for "
            + "just that — and drive a hue, a size or a brightness from what comes out. 'level' "
            + "is the loudness of the last 'window' seconds and is the steady one; 'peak' is the "
            + "furthest that stretch got from silence and is the one that hits. Both are 0 for "
            + "silence and about 1 for a signal at full scale, divided by 'scale' on the way "
            + "out, so turn that up to make a quiet signal reach. 'window' is the whole of the "
            + "smoothing: a few milliseconds follows every drum, half a second leans into the "
            + "music. Unlike a Scope this costs the picture nothing and keeps the GPU, because "
            + "what arrives is one number rather than a stretch of the past — it is played into "
            + "the patch the way a keyboard is. It reads nothing at all where no sound is "
            + "running: an exported still has no past to be loud in, and a movie is measured as "
            + "it is written.")
        {
            // Tapped but not charted: what it wants from the ring is two numbers
            // rather than a buffer, so no chart is allocated for it and nothing
            // refills one. See NodeDef.ChartsSignal.
            TapsSignal = true,
        };

    /// <summary>
    /// The Probe read backwards: a loop swept across the picture at audio rate,
    /// so that what a chart draws of a signal, this hears of a field. Named here
    /// only for the tests and the preset that build one — the shell treats it as
    /// the ordinary module it is.
    /// </summary>
    public const string ScanTypeId = "scan";

    /// <summary>
    /// The scale quantiser. Named here because it is the one module whose
    /// instance carries a scale, and the editor and the assistant both have to
    /// ask whether a given node is it — see <see cref="ScaleExtra"/>.
    /// </summary>
    public const string QuantiserTypeId = "audio.quantiser";
    
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
            "Video and audio outputs in one node. 'color' drives the screen; 'left' and 'right' drive the speakers. "
            + "'scan' sweeps the image over time when you need a visual signal.");

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
            "Pitch in note numbers. Patch in a signal to snap it to the nearest semitone, or use the knob for a direct note value.");

        yield return Quantiser();
        yield return Probe();
        yield return Scope();
        yield return Meter();
        yield return Scan();
    }

    /// <summary>
    /// One grid square, in screen units. Eight of them across the middle of the
    /// picture, which is what makes the grid the axis: however wide the preview
    /// is, a square is an eighth of the window and a quarter of the scale.
    /// </summary>
    private const float Division = 0.25f;

    /// <summary>
    /// A chart, given where the signal sits: the trace, the fill under it, the
    /// grid it is read against, and the bar along whichever edge it has run off.
    /// </summary>
    /// <remarks>
    /// Shared by the two modules that draw one, which arrive at
    /// <paramref name="height"/> by opposite routes — a Probe computes the
    /// signal at this column, a Scope looks up what was played at it — and are
    /// the same picture from there on. Worth one function rather than two
    /// copies, because what a reader wants of them is that a chart of the past
    /// and a chart of the future can be laid side by side and compared, and two
    /// copies is exactly how that stops being true.
    /// </remarks>
    /// <param name="y"></param>
    /// <param name="height">
    /// Where the trace goes, in screen units — the value already divided by
    /// whatever the top of the chart is worth.
    /// </param>
    /// <param name="now">
    /// Where to rule the bright vertical line that marks the moment, or null for
    /// a chart whose moment is the edge of the frame and so needs none — a line
    /// there would be half off the picture and a pixel wide, which is not a
    /// marker but the look of one.
    /// </param>
    /// <param name="glow">
    /// How brightly to draw the signal, per column, or null for evenly. What a
    /// chart of the past uses to say which end of it is now: a phosphor fading
    /// behind the beam, which is the one cue that works when the moment is the
    /// edge rather than a column. The grid is not dimmed with it — a graticule
    /// that faded would be unreadable exactly where the oldest values are.
    /// </param>
    /// <param name="across">
    /// How wide a grid square is, or null for the fixed <see cref="Division"/>.
    /// A Scope rules a fixed eight of them across the frame however wide it is,
    /// so its squares are a value rather than a constant — which is the whole
    /// difference between a graticule that says how long the window is and one
    /// that says how many screen units a column happens to be.
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
        Slot? glow = null)
    {
        var one = em.Constant(1f);
        var zero = em.Constant(0f);

        var trace = em.Sub(one, em.Ternary(
            OpCode.Smoothstep,
            em.Constant(0.006f),
            em.Constant(0.02f),
            em.Unary(OpCode.Abs, em.Sub(y, height))));

        // Filled from the line down to zero, which is what keeps the chart
        // readable when the signal moves faster than the pixels can follow: a
        // trace alone breaks into dots there, and the fill becomes the envelope
        // a scope shows at the same sweep.
        var fill = em.Mul(
            em.Binary(OpCode.Step, em.Binary(OpCode.Min, height, zero), y),
            em.Binary(OpCode.Step, y, em.Binary(OpCode.Max, height, zero)));

        // A bar along whichever edge the signal has gone off, because a value
        // past the top of the chart is otherwise indistinguishable from no
        // signal at all.
        var over = em.Binary(OpCode.Step, one, em.Unary(OpCode.Abs, height));
        var edge = em.Binary(OpCode.Step, em.Constant(0.96f), em.Unary(OpCode.Abs, y));
        var side = em.Binary(OpCode.Step, zero, em.Mul(height, y));
        var clipped = em.Mul(em.Mul(over, edge), side);

        var signal = em.Add(em.Mul(fill, 0.22f), trace);

        // Where the moment is, said one way or the other. A Probe recomputes
        // the signal either side of it, so its now is a column in the middle and
        // a rule down it is exactly right. A Scope's now is the edge of the
        // frame, where a rule would be half off the picture — so it says the
        // same thing by brightness instead, and passes no rule at all rather
        // than borrowing the Probe's, which down the centre of a scope would
        // look like it meant something and would mean half a window ago.
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
    /// The notes a freshly placed Quantiser snaps to: a major scale, which is
    /// the one every ear recognises and the one that makes the module audibly
    /// do something the moment it is placed.
    /// </summary>
    /// <remarks>
    /// Chromatic would be the neutral choice and is the wrong one: every note
    /// switched on is a scale that snaps to the nearest semitone, which the Note
    /// module already does — a module that arrives doing nothing is a module
    /// nobody finds out the point of.
    /// </remarks>
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>
    /// A pitch quantiser with a scale on it: the note nearest to what arrives,
    /// out of the ones the scale has switched on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scale is a list on the node rather than twelve sockets, for the
    /// reason ADR-0038 gives about a tune. Twelve switches would be twelve
    /// inputs on a module nobody could read, and unlike a knob none of them is
    /// a thing a patch could drive: what a scale holds is which notes exist,
    /// and that is a decision about the piece rather than a signal in it.
    /// </para>
    /// <para>
    /// What it lowers to depends on which notes are on, which is the whole
    /// reason the scale is a compile-time value — see
    /// <see cref="EmitContext.Scale"/>. Each note switched on contributes one
    /// candidate and the notes switched off contribute nothing, so a five-note
    /// scale is a little over half the ops of a nine-note one and the two ends
    /// of the range are special cases worth taking: all twelve is the nearest
    /// semitone and none at all is a wire.
    /// </para>
    /// </remarks>
    private static NodeDef Quantiser() => new(
        QuantiserTypeId, "Quantiser", "Output",
        [Pitched("in", 57f), Num("hold", 0f, 0f, 1f)],
        [Num("note")],
        EmitQuantiser,
        "Snaps what arrives to the nearest note the scale has switched on, in whatever "
        + "octave that lands in — so a sweep becomes a run up the scale and a wandering "
        + "signal becomes a tune. The twelve switches are pitch classes: turning A on puts "
        + "every A in the scale, not one of them. Patch its 'note' into a Note module to "
        + "hear it, or use it anywhere a stepped signal is wanted. All twelve on is the "
        + "nearest semitone, which is what a Note does on its own; none on is a wire, since "
        + "there is nothing to snap to. "
        + "'hold' freezes the note for as long as it is up: patch the same gate that opens "
        + "the envelope into it and the pitch is settled before the note starts and cannot "
        + "move until it has finished, which is the difference between a melody and one long "
        + "note sliding about. Left alone it snaps continuously, which is what it did before "
        + "the socket existed. Audio only, like every hold — a picture has no previous "
        + "evaluation to have held anything, so on the screen it snaps continuously whatever "
        + "is patched here.")
    {
        Extras = [new ScaleExtra(Major)],
    };

    /// <summary>
    /// One candidate per note in the scale, and the closest of them.
    /// </summary>
    /// <remarks>
    /// The nearest note of pitch class <c>p</c> to a signal <c>n</c> is
    /// <c>12·round((n − p)/12) + p</c> — the octave that puts <c>p</c> closest,
    /// which is a rounding rather than a search. Every scale note gives one, and
    /// the answer is whichever of them the signal is least far from. So there is
    /// no loop and no branch: a fixed candidate per switch that is on, and a
    /// running minimum over them, which is the same unrolling the Supersaw uses
    /// for its seven voices.
    /// <para>
    /// The division by twelve is done once, before the candidates, and the
    /// subtraction of the pitch class folds into the same constant the rounding
    /// adds — so a candidate costs a floor and three arithmetic ops rather than
    /// the six the formula reads as.
    /// </para>
    /// <para>
    /// Ties go to the note the scale names later, which after
    /// <see cref="Pitch.Scale"/> is always the higher pitch class. Reaching one
    /// takes a signal landing exactly halfway between two notes of the scale, so
    /// what settles it matters less than its being settled the same way twice.
    /// </para>
    /// </remarks>
    private static Slot[] EmitQuantiser(Emitter em, EmitContext node)
    {
        var signal = node[0];
        var scale = node.Scale;

        // Nothing switched on: there is no note to snap to, so what comes out is
        // what went in. The same answer a Delay with nothing to remember gives,
        // and it is what makes the twelve switches safe to turn off one at a
        // time — the module fades out of the patch rather than falling out of it.
        if (scale.Count == 0) return [Frozen(em, node, signal)];

        // Every note switched on: the nearest of all twelve is the nearest
        // semitone, and that is a rounding rather than twelve candidates.
        if (scale.Count == Pitch.Classes)
            return [Frozen(em, node, em.Unary(OpCode.Floor, em.Add(signal, 0.5f)))];

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

        return [Frozen(em, node, best)];
    }

    /// <summary>
    /// The note, frozen for as long as 'hold' is up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A level rather than an edge, and that is the decision worth recording.
    /// Every quantiser in a rack has a socket like this and most of them take a
    /// trigger — but a trigger has to be told apart from no trigger at all, and
    /// nothing here can: a socket resting on its knob hands the emit function a
    /// register like any other, so "nothing is patched" is not a question this
    /// can ask. A level answers it by not needing to. Nought is down, an
    /// unpatched socket is nought, and a module nobody has wired anything into
    /// snaps continuously exactly as it did before the socket existed.
    /// </para>
    /// <para>
    /// It also states the guarantee the right way round. An edge says when the
    /// note may change; a level says when it may not, and what anybody actually
    /// wants is that it cannot move while a note is sounding. Wire the gate that
    /// opens the envelope and the two are the same interval by construction.
    /// </para>
    /// <para>
    /// The cost is that a short trigger holds only for as long as it is up. That
    /// is the wrong shape for this socket, and there is nothing in the catalogue
    /// that makes one: every gate here — a Pulse's, a sequencer's, anything that
    /// drives an ADSR — is open for the length of a note.
    /// </para>
    /// <para>
    /// Two cells, and the same scaling the Sample &amp; Hold uses for the same
    /// reason: a cell is clamped to ±16 and a note number runs to 127. See
    /// <see cref="HoldHeadroom"/>.
    /// </para>
    /// </remarks>
    private static Slot Frozen(Emitter em, EmitContext node, Slot note)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var heldCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();

        var held = em.Mul(em.UnitRead(heldCell), HoldHeadroom);
        var before = em.UnitRead(edgeCell);

        var up = em.Binary(OpCode.Step, em.Constant(GateOpen), node[1]);

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
    private static NodeDef Probe() =>
        new NodeDef(
            ProbeTypeId, "Probe", "Output",
            [
                Swept("in"),
                Seconds("window", 0.3f),
                Num("scale", 1f, 0.01f, 16f),
            ],
            [Col("out")],
            (em, node) =>
            {
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

                // Ruled down the middle: the moment is a column here, with the
                // past to its left and the future to its right.
                return [Charted(em, x, y, height, now: zero)];
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
            + "chart of either is what the screen makes of it rather than what the speakers do. "
            + "A Scope shows that instead — what was actually played, which is the past only.");

    /// <summary>
    /// The Probe's opposite number: a chart of what the speakers have already
    /// played, rather than of what the screen computes the signal to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate module rather than a mode on the Probe, because the two
    /// genuinely disagree and both are right. A Probe is a second compile root
    /// (ADR-0040): it recomputes the signal at every column, which is why it can
    /// draw the future, and why it draws an oscillator without its accumulated
    /// phase and a delay line as a wire — the video path has no memory to show
    /// and does not pretend to. A Scope shows the one thing that has memory,
    /// because it does not compute anything: the speakers' program hands it one
    /// evaluation at a time, in order, and it keeps the last few thousand.
    /// </para>
    /// <para>
    /// Which means it is a chart with three cliffs, all of them the same cliff.
    /// It shows nothing until sound is switched on. It shows nothing the
    /// speakers do not reach — a signal wired only to the picture is not
    /// played, so there is nothing of it to have kept. And it shows only the
    /// past, because that is all anything that has already happened can be. Put
    /// a Probe and a Scope on the same node and the two charts will differ
    /// wherever the patch has memory in it; that difference is the useful thing
    /// about having both.
    /// </para>
    /// <para>
    /// Mechanically it is the only module in the catalogue whose input is a root
    /// of a program — see <see cref="NodeDef.TapsSignal"/> and
    /// <see cref="OpCode.Tap"/> — and the only one whose table changes while the
    /// program is running. Everything else about it is an ordinary chart, drawn
    /// by the same <see cref="Charted"/> the Probe uses.
    /// </para>
    /// </remarks>
    private static NodeDef Scope() =>
        new NodeDef(
            ScopeTypeId, "Scope", "Output",
            [
                // Swept, and never resolved: the whole point is that this
                // module does not evaluate its input. What is charted came from
                // the run that made the sound, and lowering the signal here as
                // well would put the whole chain into the picture's program to
                // compute a value nothing would look at.
                Swept("in"),
                Seconds("window", -1.7f),
                Num("scale", 1f, 0.01f, 16f),
            ],
            [Col("out")],
            (em, node) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);

                // How far x reaches, which is where the newest evaluation goes.
                // A Probe can leave the frame out of it because its signal is
                // defined at every column, so whatever the picture's shape it
                // has something to draw there; this one has a definite extent,
                // and an extent that did not reach the edges would be a chart
                // with the past cut off it and dead margins either side.
                var edge = em.Load(OpCode.LoadAspect);

                // The buffer holds exactly the window, whatever the window is —
                // something outside the program keeps it that way, see
                // Traces.Buffer — so all this has to say is how far across the
                // frame the column is. Which is why 'window' appears nowhere in
                // these ops: turning it changes what is put in the buffer, not
                // what is done with it.
                var age = em.Binary(OpCode.Div, em.Add(x, edge), em.Mul(edge, 2f));

                var played = node.Trace is { } trace
                    ? em.Table(em.Mul(age, trace.Seconds), trace)
                    : em.Constant(0f);

                var height = em.Binary(OpCode.Div, played, node[2]);

                // Eight divisions across whatever the frame turns out to be, so
                // the graticule keeps saying what it says however the window is
                // shaped: a square is an eighth of 'window' across and a quarter
                // of 'scale' up. They are wider than they are tall on a wide
                // preview, which is what a scope's graticule does too.
                //
                // No rule for the moment, because the moment is the right-hand
                // edge and a line there would be half off the picture. The
                // phosphor says it instead — full brightness at the beam, fading
                // back into the past, which is what a scope with a slow tube
                // looks like and reads instantly as which end is now.
                return
                [
                    Charted(
                        em, x, y, height,
                        across: em.Mul(edge, 0.25f),
                        glow: em.Add(em.Mul(age, 0.62f), 0.38f)),
                ];
            },
            "A chart of what the speakers actually played. Patch the signal you want to "
            + "watch into its 'in' and select it, and the screen shows the last 'window' "
            + "seconds of it, across the whole width. Time runs left to right: the right-hand "
            + "edge is now — the bright vertical line — and the left-hand edge is 'window' "
            + "ago. One grid square is an eighth of the window across and a quarter of "
            + "'scale' up, whatever shape the picture is. A Probe puts its line down the "
            + "middle because it has a future to draw on the other side of it; this one has "
            + "none, so the newest sample is the last column and there is nothing past it. "
            + "Unlike a Probe it shows memory: an oscillator's accumulated phase, a delay "
            + "line's tail, a sample playing, an envelope that was triggered. What it cannot "
            + "do is show the future, or anything at all while sound is off, or anything the "
            + "Output's 'left' and 'right' do not reach — it is a record of what was played, "
            + "so a branch of the patch that only draws was never played and has nothing to "
            + "show. Use a Probe for those. Its 'out' is the chart as a color, so it can be "
            + "patched into the Output to keep it on screen alongside the picture.")
        {
            TapsSignal = true,
            ChartsSignal = true,
        };

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