using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// As many notes as a sequence may hold. Nothing structural stops it being
    /// more now that the steps are a list rather than a row of sockets — this is
    /// a budget on op count, which grows with the notes and is paid per pixel on
    /// the video path. See ADR-0038.
    /// </summary>
    public const int MaxSteps = 32;
    
    /// <summary>A minor pentatonic, so a Note Sequencer plays a tune the moment it is dropped.</summary>
    private static readonly Step[] DefaultRiff =
        [.. new[] { 57f, 60f, 62f, 64f, 67f, 64f, 62f, 60f }.Select(n => new Step(n))];
    
    /// <summary>Up and back down — a shape, rather than the ramp 'index' already hands out.</summary>
    private static readonly Step[] DefaultShape =
        [.. new[] { 0f, 0.25f, 0.5f, 0.75f, 1f, 0.75f, 0.5f, 0.25f }.Select(v => new Step(v))];
    
    /// <summary>
    /// The shortest the gate's edges may be made, as a fraction of a step. A
    /// knob turned to nothing would otherwise put the click back, and a gate
    /// that clicks is not one — so the knob shapes the edge and this decides
    /// that there is one. Two thousandths of a step is well under a millisecond
    /// at any tempo, which is a hard attack rather than a discontinuity.
    /// </summary>
    private const float ShortestGateEdge = 0.002f;
    
    public const string TempoTypeId = "seq.tempo";

    /// <summary>
    /// The sample and hold. Named here because a preset builds one and because
    /// it is the module a patch reaches for when a signal has to stop moving
    /// between one note and the next.
    /// </summary>
    public const string HoldTypeId = "seq.hold";

    /// <summary>Seconds in a minute, which is the whole of what a tempo knob converts.</summary>
    private const float Minute = 60f;

    /// <summary>
    /// What a held value is divided by on its way into a cell and multiplied by
    /// on the way out.
    /// </summary>
    /// <remarks>
    /// A cell is clamped to ±16 — see <see cref="DelayState.WriteUnit"/> — and
    /// that bound is not negotiable from here: it is the only place a cycle
    /// drawn as wires can be caught running away, since nothing in a loop of
    /// wires is obliged to have a coefficient under one in it.
    /// <para>
    /// It is a sensible bound for a signal and a useless one for the two things
    /// anybody most wants to hold. A note number runs to 127 and a frequency to
    /// thousands, and either would come back pinned at sixteen — a wrong note,
    /// silently, with the patch looking perfectly correct. So this module keeps
    /// what it holds on a scale of its own. Two multiplies, a power of two so
    /// they are exact, and the bound moves to ±4096.
    /// </para>
    /// <para>
    /// The clamp still catches a Hold wired into its own input. It now catches
    /// it four thousand times further out, which is still finite, still pinned
    /// at the rails rather than turned to NaN, and still clamped again at the
    /// sink like everything else.
    /// </para>
    /// </remarks>
    private const float HoldHeadroom = 256f;

    private static IEnumerable<NodeDef> Sequencers()
    {
        // Everything here counts in beats a second, because everything here is a
        // frequency and that is what a frequency is. Nobody writes music in
        // those: a tempo is a number between about 60 and 180 and it is written
        // down in beats a minute. This is the one module that knows the
        // difference, exactly as Frequency is the one that knows a pitch is in
        // hertz rather than in the single digits a picture is drawn from.
        yield return new NodeDef(
            TempoTypeId, "Tempo", "Sequencer",
            [Num("bpm", 120f, 20f, 300f)],
            [Num("out")],
            (em, node) => [em.Mul(node[0], 1f / Minute)],
            "A knob in beats per minute, handed on as beats per second. Patch 'out' into a "
            + "sequencer's 'rate' for one step a beat, or through a Multiply first for "
            + "anything faster — four for sixteenths. It is also what drives a Pulse to give "
            + "a drum something to be triggered by.");

        yield return new NodeDef(
            HoldTypeId, "Sample & Hold", "Sequencer",
            [Num("in"), Num("trigger", 0f, 0f, 1f)],
            [Num("out")],
            EmitHold,
            "Catches whatever is on 'in' the moment 'trigger' goes up, and holds it until the "
            + "next time. What it is for is the gap between a signal that is always moving and "
            + "a note that must not: patch a wandering signal into 'in' and the same gate that "
            + "opens the envelope into 'trigger', and the pitch is settled before the note "
            + "starts and stays settled until it has finished. Without it a signal crossing "
            + "into the next note halfway through one is heard as that note sliding, since an "
            + "oscillator carries its phase and there is no click to mark the change. Audio "
            + "only: a picture is one evaluation with nothing before it, so there is nothing "
            + "to have held and 'in' passes straight through, the way a Delay with nothing to "
            + "remember is a wire.");

        yield return StepSequencer(
            "seq.notes", "Note Sequencer", DefaultRiff, PortDisplay.Note, (0f, 127f),
            "A list of notes, edited in the inspector rather than on the node. A note is a "
            + "number — 57 reads as A3 — with a length in steps and a volume, and volume "
            + "silences a note without losing it, so it doubles as its level. Send 'out' to a "
            + "Note and 'gate' to something that multiplies the tone, so a rest is heard as "
            + "one. 'shape' is how long the gate takes to open and close, as a fraction of the "
            + "note: turn it up for a swell, and it never quite reaches nothing, because a "
            + "gate that switched outright would click. 'index' is how far through the pattern "
            + "the sequence has got, which is the output to reach for on the screen.");

        yield return StepSequencer(
            "seq.values", "Sequencer", DefaultShape, PortDisplay.Number, (0f, 1f),
            "The same list as plain signals rather than as notes — a hue, a scale, a "
            + "threshold, or a pitch by way of a Frequency. 'in' is a domain the way an "
            + "oscillator's is: stop it and the sequence stops, run it backwards and it runs "
            + "backwards, run it twice as fast and so does the pattern.");
    }

    
    /// <summary>
    /// Builds one of the two step sequencers. They differ only in what a step's
    /// knob means — a note number or an ordinary signal — because nothing below
    /// this line can tell the difference: the same stepped value is a melody at
    /// the speakers and a change of color on the screen
    /// ([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)).
    /// </summary>
    /// <param name="name">What it is called in the palette and on the node.</param>
    /// <param name="notes">The tune a freshly placed one carries.</param>
    /// <param name="display">How a note's value reads — by name on the Note Sequencer.</param>
    /// <param name="range">The span a note's value is edited within.</param>
    /// <param name="id">The module's type id, which is what a saved patch names it by.</param>
    /// <param name="description">The line the inspector shows under it.</param>
    private static NodeDef StepSequencer(
        string id,
        string name,
        Step[] notes,
        PortDisplay display,
        (float Min, float Max) range,
        string description) => new(
        id, name, "Sequencer",
        [
            Domain("in"),
            Num("rate", 4f, 0f, 32f),
            Num("gate length", 0.5f, 0f, 1f),
            Num("shape", 0.02f, 0f, 0.5f),
        ],
        [Num("out"), Num("gate"), Num("index")],
        EmitSequence,
        description)
    {
        Extras = [new StepsExtra(new StepSpec(notes, display, range))],
    };
    
    /// <summary>
    /// One cell for what is being held and one for the trigger as it was, which
    /// is the whole module: an edge is a rise only if there was nothing to rise
    /// from.
    /// </summary>
    /// <remarks>
    /// Stateful, and holding that state in the one-evaluation cells
    /// <see cref="Emitter.AllocateUnitSlot"/> hands out rather than in an opcode
    /// of its own — the same way the ADSR holds its level and ADR-0041 has a
    /// plugin hold a filter's integrators. Nothing in the engine had to change
    /// for it, which is the third time that has been true and is worth counting.
    /// <para>
    /// The level cell is read before it is written and both happen inside one
    /// evaluation, so what comes out on the evaluation the trigger rises is the
    /// <em>new</em> sample rather than the one before it. That matters here more
    /// than it does for an envelope: the gate that fires this is usually the gate
    /// that opens the envelope, so a value that arrived one evaluation late would
    /// be the previous note's pitch heard at the start of this one.
    /// </para>
    /// <para>
    /// <see cref="Emitter.HasMemory"/> answers no on the video path and on the
    /// very first evaluation of an audio one, and both are handled by the same
    /// term: it takes a sample. On the screen that makes it a wire, which is
    /// what a hold means where there is no before; at the start of a program it
    /// primes the cell, so the first note is the signal rather than the nothing a
    /// cell begins at. Without that the patch would play one note of silence, or
    /// worse, whatever nought means to whatever is downstream.
    /// </para>
    /// </remarks>
    private static Slot[] EmitHold(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var heldCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();

        // Back onto the scale the patch is using — see HoldHeadroom.
        var held = em.Mul(em.UnitRead(heldCell), HoldHeadroom);
        var before = em.UnitRead(edgeCell);

        // Up rather than high: what matters is the crossing, so a trigger left
        // open holds one sample rather than tracking.
        var up = em.Binary(OpCode.Step, em.Constant(GateOpen), node[1]);
        var rise = em.Mul(up, em.Sub(one, before));

        // Either edge or nothing to remember. Max is the or, both being 0 or 1.
        var take = em.Binary(OpCode.Max, rise, em.Sub(one, live));
        var next = em.Ternary(OpCode.Mix, held, node[0], take);

        em.UnitWrite(heldCell, em.Mul(next, 1f / HoldHeadroom));
        em.UnitWrite(edgeCell, up);

        return [next];
    }

    /// <summary>
    /// Which step the sequence is on is a function of where its input has got
    /// to, not of what it played before, so a sequencer needs no state at all —
    /// unlike a delay ([0027](0027-delay-lines-give-the-audio-path-a-memory.md))
    /// or an accumulated phase ([0030](0030-oscillators-accumulate-their-phase.md)).
    /// It draws the same thing it plays, and the video path pays nothing for it.
    /// </summary>
    /// <remarks>
    /// Selection is a sum of windows rather than a branch, because the machine
    /// has no branches. Each window is the difference between two thresholds on
    /// where the sequence has got to, so adjacent windows share an edge and
    /// exactly one is ever open — which makes the sum the selected note and
    /// nothing else.
    /// <para>
    /// The notes are values rather than registers, so every boundary between
    /// them is a literal the compiler works out here: a running sum of the
    /// lengths costs no ops at all, however uneven they are.
    /// </para>
    /// </remarks>
    private static Slot[] EmitSequence(Emitter em, EmitContext node)
    {
        var notes = node.Steps;

        // A sequence with nothing in it holds still rather than dividing by a
        // total of nothing. An empty list only reaches here from a file.
        if (notes.Count == 0)
            return [em.Constant(0f), em.Constant(0f), em.Constant(0f)];

        // Where each note starts, and where the pattern ends. Folded rather than
        // emitted — this is the whole reason a note may have its own length
        // without the module costing more than one that may not.
        var count = notes.Count;
        var starts = new float[count + 1];
        for (var s = 0; s < count; s++) starts[s + 1] = starts[s] + notes[s].Length;

        var total = starts[count];

        // How far the input has travelled, counted in steps. Modulo is floored,
        // so an input running backwards runs the sequence backwards rather than
        // falling off the front of it.
        var travelled = em.Mul(node[0], node[1]);

        // Every note the same length is the ordinary case and the cheap one.
        // There the sequence can be counted in whole notes, so the edges fall on
        // integers, the fraction through a note is a plain Fract and which note
        // is playing is one division — which is what this module always did, and
        // costs what it always cost. Only an uneven pattern pays for its
        // unevenness, and it pays by measuring everything against a position
        // rather than an index.
        var even = notes.All(s => s.Length == notes[0].Length);

        Slot cursor;
        float[] thresholds;
        Slot within;
        Slot which;

        if (even)
        {
            var unit = notes[0].Length;

            // Left alone at the usual length of one, so the commonest pattern
            // emits exactly the ops it emitted before notes had a length.
            var counted = unit == 1f
                ? travelled
                : em.Binary(OpCode.Div, travelled, em.Constant(unit));

            var index = em.Unary(OpCode.Floor,
                em.Binary(OpCode.Mod, counted, em.Constant(count)));

            cursor = index;
            thresholds = [.. Enumerable.Range(0, count + 1).Select(s => (float)s)];
            within = em.Unary(OpCode.Fract, counted);
            which = em.Binary(OpCode.Div, index, em.Constant(count));
        }
        else
        {
            cursor = em.Binary(OpCode.Mod, travelled, em.Constant(total));
            thresholds = starts;
            within = default;
            which = default;
        }

        var edges = new Slot[count + 1];
        edges[0] = em.Constant(1f);
        edges[count] = em.Constant(0f);

        for (var s = 1; s < count; s++)
            edges[s] = em.Binary(OpCode.Step, em.Constant(thresholds[s]), cursor);

        Slot value = default, open = default, start = default, span = default, ordinal = default;

        for (var s = 0; s < count; s++)
        {
            var window = em.Sub(edges[s], edges[s + 1]);

            var here = em.Mul(window, em.Constant(notes[s].Value));
            var sounds = em.Mul(window, em.Constant(notes[s].Volume));

            (value, open) = s == 0 ? (here, sounds) : (em.Add(value, here), em.Add(open, sounds));

            if (even) continue;

            // Where this note starts, how long it lasts and where it sits in the
            // list — selected the same way its value is, because with uneven
            // notes none of the three is a function of the position alone.
            var from = em.Mul(window, em.Constant(starts[s]));
            var wide = em.Mul(window, em.Constant(notes[s].Length));
            var at = em.Mul(window, em.Constant(s / (float)count));

            (start, span, ordinal) = s == 0
                ? (from, wide, at)
                : (em.Add(start, from), em.Add(span, wide), em.Add(ordinal, at));
        }

        if (!even)
        {
            // How far through the note we are, 0 to 1, so a long note opens and
            // closes its gate over its own length rather than over a step's.
            within = em.Binary(OpCode.Div, em.Sub(cursor, start), span);
            which = ordinal;
        }

        // The gate shuts partway through each note, so two identical notes in a
        // row are two notes rather than one held twice as long.
        //
        // It ramps rather than switches, and that is not a nicety. A switched
        // gate steps the amplitude by its whole height in one sample, which is a
        // discontinuity — and oversampling
        // ([0023](0023-oversample-the-audio-path.md)) band-limits one of those
        // rather than removing it, so it is heard as a click at every note.
        // Ramping also hides the other edge in here: because the envelope is
        // zero at each boundary, a note whose volume differs from the last
        // one's fades in at its own level instead of jumping to it.
        var shape = em.Ternary(OpCode.Clamp, node[3], em.Constant(ShortestGateEdge), em.Constant(1f));
        var length = em.Ternary(OpCode.Clamp, node[2], em.Constant(0f), em.Constant(1f));

        var opening = em.Ternary(OpCode.Smoothstep, em.Constant(0f), shape, within);
        var closing = em.Sub(em.Constant(1f),
            em.Ternary(OpCode.Smoothstep, em.Sub(length, shape), length, within));

        return
        [
            value,
            em.Mul(em.Mul(open, opening), closing),
            which,
        ];
    }
}