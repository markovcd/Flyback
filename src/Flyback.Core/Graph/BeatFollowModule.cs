using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Beat Follow: a tempo and a beat heard from a gate of hits, such as a kick.</summary>
internal static class BeatFollowModule
{
    /// <summary>The type id of the Beat Follow module.</summary>
    internal const string TypeId = "seq.follow";

    /// <summary>The input sockets, by index.</summary>
    internal const int InPort = 0, SlowestPort = 1, FastestPort = 2, SettlePort = 3, NudgePort = 4;

    /// <summary>The output sockets, by index.</summary>
    /// <remarks>Named for the code that reads them, tests included: <see cref="Emit"/> fills them in this order.</remarks>
    internal const int BeatsPort = 0, PhasePort = 1, BpmPort = 2, LockedPort = 3, BeatPort = 4, ResetPort = 5;

    /// <summary>The shortest gap between two hits, in seconds: anything sooner is the same hit seen twice.</summary>
    private const float Refractory = 0.06f;

    /// <summary>How far a gap may stray from a whole number of beats, as a share of it, and still count as on the beat.</summary>
    private const float Tolerance = 0.12f;

    /// <summary>The longest gap, in beats, between two hits on the beat that still measures the tempo.</summary>
    private const float LongestGap = 8.5f;

    /// <summary>How near a beat, as a share of one, a hit has to land to pull the beat toward it.</summary>
    private const float Near = 0.1f;

    /// <summary>The share of a hit's distance from the beat that the beat moves to meet it.</summary>
    private const float Pull = 0.25f;

    /// <summary>The seconds over which a pull is let out, so 'beats' never runs backward.</summary>
    private const float Ease = 0.15f;

    /// <summary>How many beats without a hit on the beat it still says it is locked; after that two even gaps start it over.</summary>
    private const float LockedFor = 4f;

    internal static NodeDef Def() => new(
        TypeId, "Beat Follow", ModuleCategories.Timing,
        [
            Num("in", 0f, 0f, 1f) with
            {
                Lenient = true,
                Help = "The hits to follow, as a gate: a Hits' 'kick', a drum pad's gate, a panel button tapped on the beat.",
            },
            Num("slowest", 80f, 20f, 300f) with
            {
                Help = "The slowest tempo it may settle on, in BPM. Hits further apart are heard as halves or quarters of it.",
            },
            Num("fastest", 160f, 20f, 300f) with
            {
                Help = "The fastest tempo it may settle on, in BPM. Keep it twice 'slowest' so every tempo has one answer.",
            },
            Num("settle", 4f, 1f, 32f) with
            {
                Help = "How many hits a change of tempo takes to follow. Low follows a drummer, high holds steady through fills.",
            },
            Num("nudge", 0f, -100f, 100f) with
            {
                Help = "Milliseconds to run ahead of the hits, or behind them below nought. Turn it until the picture lands on the kick.",
            },
        ],
        [
            Num("beats") with
            {
                Help = "The beats since it first locked, counting on smoothly and carrying on through a break. "
                    + "Patch it into a sequencer's 'in' for one step a beat.",
            },
            Num("phase", 0f, 0f, 1f) with { Help = "How far through the beat it is, from 0 on the beat to just short of 1." },
            Num("bpm", 120f, 20f, 300f) with { Help = "The tempo it hears, in BPM. Nought until it has heard two even gaps." },
            Num("locked", 0f, 0f, 1f) with { Help = "High while hits keep landing on the beat, low once they have stopped for four beats." },
            Num("beat", 0f, 0f, 1f) with { Help = "Fires on every beat, through a break too." },
            Num("reset", 0f, 0f, 1f) with { Help = "Fires when it first locks and whenever it starts over at a new tempo." },
        ],
        Emit,
        "Hears the tempo in a gate of hits and keeps a beat to it: patch a Hits' 'kick' into 'in' to play "
        + "along with the music the Line In hears. Two even gaps between hits set the tempo; after that a "
        + "hit near the beat pulls the beat onto it and fine-tunes the tempo, hits off the beat are let be, "
        + "and in a break it carries on at the last tempo. "
        + "Sound only: carry what it gives to the picture through a Bridge.")
    {
        Words = "tempo from a kick, beat tracking, tap tempo",
        Sinks = ModuleSinks.Audio,
    };

    /// <summary>
    /// A beat run at a tempo, kept in line by the hits that land near it.
    /// </summary>
    /// <remarks>
    /// Only a hit within <see cref="Near"/> of a beat counts: it pulls the beat a quarter of the way onto it,
    /// let out over <see cref="Ease"/> so the count only ever goes forward, and the time since the last
    /// such hit, read as a whole number of beats, moves the tempo by a share of 'settle'. Hits off the
    /// beat (a snare in the kick's band, a fill) change nothing. Once no hit has landed on the beat for
    /// <see cref="LockedFor"/> beats, two gaps in a row that agree, each folded into 'slowest' to
    /// 'fastest', are a new tempo with this hit on the beat; the first lock is found the same way.
    /// </remarks>
    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);
        var dt = em.Interval();
        var memory = em.HasMemory();

        // The beat's shortest and longest length in seconds, whichever way round the two tempos are set.
        var quickest = em.Binary(OpCode.Max, em.Binary(OpCode.Max, node[SlowestPort], node[FastestPort]), one);
        var slowest = em.Binary(OpCode.Max, em.Binary(OpCode.Min, node[SlowestPort], node[FastestPort]), one);
        var shortest = em.Binary(OpCode.Div, em.Constant(60f), quickest);
        var longest = em.Binary(OpCode.Div, em.Constant(60f), slowest);

        var upCell = em.AllocateUnitSlot();
        var sinceCell = em.AllocateUnitSlot();
        var sinceBeatCell = em.AllocateUnitSlot();
        var lastFoldCell = em.AllocateUnitSlot();
        var periodCell = em.AllocateUnitSlot();
        var oweCell = em.AllocateUnitSlot();
        var posCell = em.AllocateUnitSlot();
        var countCell = em.AllocateUnitSlot();

        var wasUp = em.UnitRead(upCell);
        var since = em.Add(em.UnitRead(sinceCell), dt);
        var sinceBeat = em.Add(em.UnitRead(sinceBeatCell), dt);
        var lastFold = em.UnitRead(lastFoldCell);
        var period = em.UnitRead(periodCell);
        var owe = em.UnitRead(oweCell);
        var pos = em.UnitRead(posCell);
        var count = em.UnitRead(countCell);

        var up = em.Binary(OpCode.Step, em.Constant(EnvelopeModules.GateOpen), node[InPort]);
        var hit = em.Mul(em.Mul(up, em.Sub(one, wasUp)), em.Binary(OpCode.Step, em.Constant(Refractory), since));
        var held = em.Binary(OpCode.Step, em.Constant(1e-6f), period);

        // The beat carried forward at the tempo held, with what is still owed of the last pull let out.
        var paid = em.Mul(owe, em.Binary(OpCode.Min, one, em.Mul(dt, 1f / Ease)));
        var carried = em.Add(em.Add(pos, em.Mul(dt, em.Binary(OpCode.Div, one, period))), paid);
        var nearest = em.Unary(OpCode.Floor, em.Add(carried, 0.5f));
        var off = em.Sub(carried, nearest);
        var onBeat = em.Mul(em.Mul(hit, held), em.Binary(OpCode.Step, em.Unary(OpCode.Abs, off), em.Constant(Near)));
        var locked = em.Mul(held, em.Binary(OpCode.Step, sinceBeat, em.Mul(period, LockedFor)));

        // The gap since the last hit, doubled or halved until it lies between the fastest beat and twice that.
        var octaves = em.Unary(OpCode.Floor, em.Binary(OpCode.Div,
            em.Unary(OpCode.Log, em.Binary(OpCode.Div, since, shortest)), em.Constant(MathF.Log(2f))));
        var folded = em.Ternary(OpCode.Clamp,
            em.Mul(since, em.Binary(OpCode.Pow, em.Constant(2f), em.Unary(OpCode.Neg, octaves))), shortest, longest);
        var plausible = em.Mul(
            em.Binary(OpCode.Step, em.Mul(shortest, 0.5f), since),
            em.Binary(OpCode.Step, since, em.Mul(longest, 2.2f)));
        var agree = em.Mul(
            em.Binary(OpCode.Step, em.Constant(1e-6f), lastFold),
            em.Binary(OpCode.Step,
                em.Unary(OpCode.Abs, em.Sub(em.Binary(OpCode.Div, folded, lastFold), one)), em.Constant(Tolerance)));
        var restart = em.Mul(em.Mul(em.Mul(hit, plausible), agree), em.Sub(one, locked));

        // The time since the last hit on the beat, as a whole number of beats.
        var ratio = em.Binary(OpCode.Div, sinceBeat, period);
        var beatsApart = em.Binary(OpCode.Max, one, em.Unary(OpCode.Floor, em.Add(ratio, 0.5f)));
        var stray = em.Unary(OpCode.Abs, em.Sub(em.Binary(OpCode.Div, ratio, beatsApart), one));
        var fits = em.Mul(
            em.Binary(OpCode.Step, stray, em.Constant(Tolerance)),
            em.Binary(OpCode.Step, ratio, em.Constant(LongestGap)));
        var measured = em.Mul(onBeat, fits);

        var share = em.Binary(OpCode.Div, one, em.Binary(OpCode.Max, node[SettlePort], one));
        var followed = em.Ternary(OpCode.Clamp,
            em.Ternary(OpCode.Mix, period, em.Binary(OpCode.Div, sinceBeat, beatsApart), share), shortest, longest);
        var periodNow = em.Ternary(OpCode.Mix, em.Ternary(OpCode.Mix, period, followed, measured), folded, restart);
        var rate = em.Binary(OpCode.Div, one, periodNow);

        var pulled = em.Mul(onBeat, em.Sub(one, restart));
        var owed = em.Mul(em.Sub(em.Sub(owe, paid), em.Mul(pulled, em.Mul(off, Pull))), em.Sub(one, restart));
        var posNow = em.Ternary(OpCode.Mix, carried, nearest, restart);
        var anchored = em.Binary(OpCode.Max, onBeat, restart);

        em.UnitWrite(upCell, up);
        em.UnitWrite(sinceCell, em.Mul(since, em.Sub(one, hit)));
        em.UnitWrite(sinceBeatCell, em.Mul(sinceBeat, em.Sub(one, anchored)));
        em.UnitWrite(lastFoldCell, em.Ternary(OpCode.Mix, lastFold, em.Mul(folded, plausible), hit));
        em.UnitWrite(periodCell, periodNow);
        em.UnitWrite(oweCell, owed);
        em.ClockWrite(posCell, posNow);

        var beats = em.Add(posNow, em.Mul(em.Mul(node[NudgePort], 0.001f), rate));
        var counted = em.Unary(OpCode.Floor, beats);
        em.ClockWrite(countCell, counted);

        var heldNow = em.Binary(OpCode.Step, em.Constant(1e-6f), periodNow);
        var ticked = em.Binary(OpCode.Step, em.Constant(0.5f), em.Sub(counted, count));
        var beat = em.Mul(em.Binary(OpCode.Max, em.Mul(ticked, heldNow), restart), memory);
        var lockedNow = em.Binary(OpCode.Max, em.Mul(locked, em.Sub(one, restart)), restart);

        return
        [
            em.Mul(beats, memory),
            em.Mul(em.Unary(OpCode.Fract, beats), memory),
            em.Mul(em.Mul(rate, 60f), memory),
            em.Mul(lockedNow, memory),
            beat,
            em.Mul(restart, memory),
        ];
    }
}
