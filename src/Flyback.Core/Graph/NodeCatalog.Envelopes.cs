using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string AdsrTypeId = "env.adsr";

    /// <summary>
    /// Where the gate counts as open. A gate is a switch rather than a level —
    /// the sequencer's own is shaped, so it spends a little time between the two
    /// — and halfway is the one threshold that does not favour either edge.
    /// </summary>
    private const float GateOpen = 0.5f;

    /// <summary>
    /// The shortest a stage may be asked to take, in seconds. The knobs cannot
    /// reach below this, but a signal patched into one can, and a stage of no
    /// duration is a division by nothing rather than an instant one.
    /// </summary>
    private const float ShortestStage = 1e-4f;

    private static IEnumerable<NodeDef> Envelopes()
    {
        yield return new NodeDef(
            AdsrTypeId, "ADSR", "Sequencer",
            [
                Num("gate", 0f, 0f, 1f),
                Seconds("attack", -2f),
                Seconds("decay", -1f),
                Num("sustain", 0.7f, 0f, 1f),
                Seconds("release", -0.6f),
            ],
            [Num("out")],
            EmitAdsr,
            "The shape a note has over time, from a gate. It rises to one over 'attack', "
            + "falls to 'sustain' over 'decay', stays there for as long as the gate is held, "
            + "and falls to nothing over 'release' once it is let go — so a sequencer's "
            + "'gate' into here and this into a multiply is a run of notes with edges rather "
            + "than a switch. The three times are marked in decades, from 100 µs to half a "
            + "minute. Audio only: a picture is one evaluation with nothing before it, so "
            + "there is no time for the shape to happen in and what comes out is the gate "
            + "itself, the way a Delay with nothing to remember is a wire.");
    }

    /// <summary>
    /// The envelope, as four straight lines and a latch.
    /// </summary>
    /// <remarks>
    /// Stateful, and holding that state in two of the one-evaluation cells
    /// <see cref="Emitter.AllocateUnitSlot"/> hands out rather than in an opcode
    /// of its own — the same way the Unit Delay carries a cycle round, and the
    /// same way ADR-0041 has a plugin hold a filter's integrators. Nothing in the
    /// engine had to change for it.
    /// <para>
    /// One cell is the level. The other is the whole of what makes this an
    /// envelope rather than a slew limiter: whether the peak has been reached
    /// since the gate opened. Without it, "am I still attacking?" would have to
    /// be read off the level — and the level passes back down through every
    /// value it rose through, so the answer would flip back to attacking the
    /// moment the decay started and the two stages would chatter against each
    /// other. Latched instead, and cleared by the gate closing, which is also
    /// what makes a note retrigger from wherever its release had got to.
    /// </para>
    /// <para>
    /// Straight lines rather than the exponentials an analogue envelope makes,
    /// because a knob that says a tenth of a second should take a tenth of a
    /// second: an exponential approach never quite arrives, and every stage would
    /// need a threshold to decide it had.
    /// </para>
    /// <para>
    /// One evaluation of every program answers <see cref="Emitter.HasMemory"/>
    /// with no, because the cell behind it is read before it has been written —
    /// so the first evaluation hands out the gate rather than the envelope,
    /// exactly as the filter hands out its dry input there. It is one sample at
    /// the very start of a program and only where the gate is already open at
    /// it, which is not a case a patch can be left sitting in; the alternative
    /// is a second cell spent on a state the module is never in again.
    /// </para>
    /// </remarks>
    private static Slot[] EmitAdsr(Emitter em, EmitContext node)
    {
        // Neither of these is a socket. The interval is how far the clock moved
        // since the last evaluation, which is what turns a time in seconds into a
        // distance to travel now; the flag says whether there is any memory
        // behind this program at all. Both belong to the emitter, because both
        // answer the same for everything in one program — ADR-0042.
        var step = em.Interval();
        var live = em.HasMemory();

        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var shortest = em.Constant(ShortestStage);

        var open = em.Binary(OpCode.Step, em.Constant(GateOpen), node[0]);
        var sustain = em.Ternary(OpCode.Clamp, node[3], zero, one);

        // The knobs are exponents, the way the Probe's window is: one control
        // reaching from a click to half a minute has to be marked in decades or
        // the whole of the useful end is in its first millimetre.
        var attack = Duration(node[1]);
        var decay = Duration(node[2]);
        var release = Duration(node[4]);

        var levelCell = em.AllocateUnitSlot();
        var peakCell = em.AllocateUnitSlot();

        var level = em.UnitRead(levelCell);

        // Latched while the gate is held, and cleared with it. Multiplying by the
        // gate is both halves at once: it can only be set while open, and it is
        // gone the moment the gate is not.
        var peaked = em.Mul(
            open,
            em.Binary(OpCode.Max, em.UnitRead(peakCell), em.Binary(OpCode.Step, one, level)));

        var rising = em.Mul(open, em.Sub(one, peaked));

        // How far each stage travels in this evaluation. The decay covers the
        // gap between the peak and the sustain rather than the whole range, so
        // that its knob is how long the fall takes whatever it is falling to.
        var toPeak = em.Binary(OpCode.Div, step, attack);
        var toSustain = em.Binary(OpCode.Div, em.Mul(em.Sub(one, sustain), step), decay);
        var toSilence = em.Binary(OpCode.Div, step, release);

        // Every stage is written as a move with a wall at the end of it, so that
        // arriving is what stops each one rather than a test somewhere else.
        var attacking = em.Binary(OpCode.Min, em.Add(level, toPeak), one);
        var decaying = em.Binary(OpCode.Max, em.Sub(level, toSustain), sustain);
        var releasing = em.Binary(OpCode.Max, em.Sub(level, toSilence), zero);

        var held = em.Ternary(OpCode.Mix, decaying, attacking, rising);
        var next = em.Ternary(OpCode.Mix, releasing, held, open);

        em.UnitWrite(levelCell, next);
        em.UnitWrite(peakCell, peaked);

        // What it means where there is nothing to remember. A picture is one
        // evaluation with nothing before it, so there is no time for a shape to
        // happen in, and the envelope becomes a wire — the gate itself, the way
        // a Delay with nothing to remember passes its input straight through.
        //
        // The sustain looks like the better answer and is not: it is the level a
        // held gate settles at, but every percussive sound sets it to nothing, so
        // a drum would draw a black screen and read as a patch that does not
        // work. An envelope that is open whenever its gate is at least shows the
        // rhythm, which is the part of it a picture can carry.
        return [em.Ternary(OpCode.Mix, em.Ternary(OpCode.Clamp, node[0], zero, one), next, live)];

        // Ten to the knob, held above a length that can actually be divided by.
        Slot Duration(Slot decades) => em.Binary(
            OpCode.Max,
            em.Binary(OpCode.Pow, em.Constant(10f), decades),
            shortest);
    }
}
