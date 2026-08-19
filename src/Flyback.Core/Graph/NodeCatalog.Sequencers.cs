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
    
    private static IEnumerable<NodeDef> Sequencers()
    {
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
        DefaultSteps = notes,
        StepDisplay = display,
        StepRange = range,
    };
    
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