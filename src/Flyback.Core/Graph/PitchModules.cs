using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>Note, Quantiser and Tune: note numbers snapped to a scale and turned into frequencies.</summary>
internal static class PitchModules
{
    /// <summary>
    /// The scale quantiser. Named here because it is the one module whose
    /// instance carries a scale, and the editor and the assistant both ask
    /// whether a node is it — see <see cref="ScaleExtra"/>.
    /// </summary>
    internal const string QuantiserTypeId = "audio.quantiser";

    /// <summary>Note, Quantiser and Tune.</summary>
    internal static IEnumerable<NodeDef> Defs()
    {
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
                Num("note") with { Help = "The semitone it snapped to.", Display = PortDisplay.Note },
            ],
            (em, i) => Sounded(em, i[0], i[1], i[2]),
            "Pitch in note numbers, turned into a frequency.")
        {
            Words = "pitch from a note number",
        };

        yield return Quantiser();
        yield return Tune();
    }

    /// <summary>
    /// A note number as a frequency, and the semitone it was snapped to on the way:
    /// the whole of a Note.
    /// </summary>
    internal static Slot[] Sounded(Emitter em, Slot pitch, Slot octave, Slot cents)
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
            Pitched("in", 57f) with { Help = "A note number to snap: a sequence, a sweep, a MIDI note." },
            Num("hold", 0f, 0f, 1f) with
            {
                Lenient = true,
                Help = "Freezes the note while it is up. Patch the envelope's gate here so the pitch "
                    + "cannot slide mid-note.",
            },
        ],
        [Num("note") with { Help = "A note number: patch it into a Note to hear it.", Display = PortDisplay.Note }],
        EmitQuantiser,
        "Snaps what arrives to the nearest note the scale has switched on, in any octave, so "
        + "a sweep becomes a run up the scale. The switches are pitch classes: A on is every A. "
        + "All twelve on is the nearest semitone; none on is a wire. Audio only, like every "
        + "hold: the picture snaps continuously.")
    {
        Words = "quantize to a scale, in key",
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
            Pitched("in", 57f) with { Help = "A note number: a sequence, a MIDI note." },
            Num("transpose", 0f, -48f, 48f) with { Help = "Semitones, added first: a chord's root, or 12 for an octave up." },
            Num("hold", 0f, 0f, 1f) with { Lenient = true, Help = "Freezes the note while it is up, as a Quantiser's does." },
        ],
        [
            Num("hz") with { Help = "For an oscillator's 'freq'." },
            Num("note") with { Help = "The same pitch as a note number.", Display = PortDisplay.Note },
        ],
        (em, i) =>
        {
            var nought = em.Constant(0f);
            var snapped = Snapped(em, em.Add(i[0], i[1]), i[2], i.Scale);

            return Sounded(em, snapped, nought, nought);
        },
        "A Quantiser and a Note in one: a note number in, a frequency in the scale out.")
    {
        Words = "note number to a scale's pitch",
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
    /// <see cref="SequencerModules.HoldHeadroom"/>.
    /// </para>
    /// </remarks>
    private static Slot Frozen(Emitter em, Slot hold, Slot note)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var heldCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();

        var held = em.Mul(em.UnitRead(heldCell), SequencerModules.HoldHeadroom);
        var before = em.UnitRead(edgeCell);

        var up = em.Binary(OpCode.Step, em.Constant(EnvelopeModules.GateOpen), hold);

        // Taken while the hold is down, and on the evaluation it goes up — so
        // what is frozen is the note as it stood the moment the gate opened
        // rather than the one before it.
        var rise = em.Mul(up, em.Sub(one, before));
        var take = em.Binary(OpCode.Max, rise, em.Sub(one, up));

        // And wherever there is nothing to have held: the screen, and the first
        // evaluation of a program, which is what primes the cell.
        take = em.Binary(OpCode.Max, take, em.Sub(one, live));

        var next = em.Ternary(OpCode.Mix, held, note, take);

        em.UnitWrite(heldCell, em.Mul(next, 1f / SequencerModules.HoldHeadroom));
        em.UnitWrite(edgeCell, up);

        return next;
    }
}
