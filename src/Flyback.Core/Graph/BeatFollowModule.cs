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
    internal const int BeatsPort = 0, PhasePort = 1, BpmPort = 2, LockedPort = 3, BeatPort = 4, ResetPort = 5;

    /// <summary>The shortest gap between two hits, in seconds: anything sooner is the same hit seen twice.</summary>
    private const float Refractory = 0.06f;

    /// <summary>How far a gap may stray from a whole number of beats, as a share of it, and still count as on the beat.</summary>
    private const float Tolerance = 0.12f;

    /// <summary>The longest gap, in beats, still read as part of the beat rather than a break in it.</summary>
    private const float LongestGap = 4.5f;

    /// <summary>How many hits in a row off the tempo held make it start over from the latest gap.</summary>
    private const float MissesToRestart = 4f;

    /// <summary>How near a beat, as a share of one, a hit has to land to pull the beat toward it.</summary>
    private const float Near = 0.2f;

    /// <summary>The share of a hit's distance from the beat that the beat moves to meet it.</summary>
    private const float Pull = 0.5f;

    /// <summary>The seconds over which a pull is let out, so 'beats' never runs backward.</summary>
    private const float Ease = 0.15f;

    /// <summary>How many beats without a hit on the beat it still says it is locked.</summary>
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
            Num("bpm", 120f, 20f, 300f) with { Help = "The tempo it hears, in BPM. Nought until it has heard two hits." },
            Num("locked", 0f, 0f, 1f) with { Help = "High while hits keep landing on the beat, low once they have stopped for four beats." },
            Num("beat", 0f, 0f, 1f) with { Help = "Fires on every beat, through a break too." },
            Num("reset", 0f, 0f, 1f) with { Help = "Fires when it first locks and whenever it starts over at a new tempo." },
        ],
        Emit,
        "Hears the tempo in a gate of hits and keeps a beat to it: patch a Hits' 'kick' into 'in' to play "
        + "along with the music the Line In hears. Each gap between hits is read as a whole number of beats, "
        + "a hit near the beat pulls the beat onto it, and in a break it carries on at the last tempo. "
        + "Sound only: carry what it gives to the picture through a Bridge.")
    {
        Words = "tempo from a kick, beat tracking, tap tempo",
        Sinks = ModuleSinks.Audio,
    };

    /// <summary>
    /// A tempo measured from the gaps between hits, and a beat run at it that hits pull into line.
    /// </summary>
    /// <remarks>
    /// Once a tempo is held, a gap is read against it as a quarter, a half or a whole number of beats up
    /// to four; one that fits moves the tempo by a share of 'settle', and one that does not is a hit off
    /// the beat and leaves it be. Four misses in a row are a new tempo, taken from the latest gap folded
    /// into 'slowest' to 'fastest', which is also how the first gap is read. A hit near a beat moves the
    /// beat half the way toward it, let out over <see cref="Ease"/> so the count only ever goes forward.
    /// </remarks>
    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
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
        var heardCell = em.AllocateUnitSlot();
        var periodCell = em.AllocateUnitSlot();
        var missesCell = em.AllocateUnitSlot();
        var sinceGoodCell = em.AllocateUnitSlot();
        var oweCell = em.AllocateUnitSlot();
        var posCell = em.AllocateUnitSlot();
        var countCell = em.AllocateUnitSlot();

        var wasUp = em.UnitRead(upCell);
        var since = em.Add(em.UnitRead(sinceCell), dt);
        var heard = em.UnitRead(heardCell);
        var period = em.UnitRead(periodCell);
        var misses = em.UnitRead(missesCell);
        var sinceGood = em.Add(em.UnitRead(sinceGoodCell), dt);
        var owe = em.UnitRead(oweCell);
        var pos = em.UnitRead(posCell);
        var count = em.UnitRead(countCell);

        var up = em.Binary(OpCode.Step, em.Constant(EnvelopeModules.GateOpen), node[InPort]);
        var hit = em.Mul(em.Mul(up, em.Sub(one, wasUp)), em.Binary(OpCode.Step, em.Constant(Refractory), since));
        var gapped = em.Mul(hit, heard);
        var held = em.Binary(OpCode.Step, em.Constant(1e-6f), period);

        // The gap as beats of the tempo held, and the nearest of a quarter, a half or a whole number.
        var ratio = em.Binary(OpCode.Div, since, period);
        var whole = em.Binary(OpCode.Max, one, em.Unary(OpCode.Floor, em.Add(ratio, 0.5f)));
        var part = em.Ternary(OpCode.Mix, em.Constant(0.25f), em.Constant(0.5f), em.Binary(OpCode.Step, em.Constant(0.375f), ratio));
        var beatsApart = em.Ternary(OpCode.Mix, part, whole, em.Binary(OpCode.Step, em.Constant(0.75f), ratio));
        var stray = em.Unary(OpCode.Abs, em.Sub(em.Binary(OpCode.Div, ratio, beatsApart), one));

        var judged = em.Mul(em.Mul(gapped, held), em.Binary(OpCode.Step, ratio, em.Constant(LongestGap)));
        var fits = em.Binary(OpCode.Step, stray, em.Constant(Tolerance));
        var good = em.Mul(judged, fits);
        var missed = em.Mul(judged, em.Sub(one, fits));
        var missesNow = em.Mul(em.Add(misses, missed), em.Sub(one, good));

        var plausible = em.Mul(
            em.Binary(OpCode.Step, em.Mul(shortest, 0.5f), since),
            em.Binary(OpCode.Step, since, em.Mul(longest, 2.2f)));
        var lost = em.Binary(OpCode.Max, em.Sub(one, held), em.Binary(OpCode.Step, em.Constant(MissesToRestart), missesNow));
        var restart = em.Mul(em.Mul(gapped, plausible), lost);

        // The gap doubled or halved until it lies between the fastest beat and twice that, then held to the range.
        var octaves = em.Unary(OpCode.Floor, em.Binary(OpCode.Div,
            em.Unary(OpCode.Log, em.Binary(OpCode.Div, since, shortest)), em.Constant(MathF.Log(2f))));
        var folded = em.Ternary(OpCode.Clamp,
            em.Mul(since, em.Binary(OpCode.Pow, em.Constant(2f), em.Unary(OpCode.Neg, octaves))), shortest, longest);

        var share = em.Mul(
            em.Binary(OpCode.Div, one, em.Binary(OpCode.Max, node[SettlePort], one)),
            em.Binary(OpCode.Min, beatsApart, one));
        var heardPeriod = em.Binary(OpCode.Div, since, beatsApart);
        var followed = em.Ternary(OpCode.Clamp, em.Ternary(OpCode.Mix, period, heardPeriod, share), shortest, longest);
        var periodNow = em.Ternary(OpCode.Mix, em.Ternary(OpCode.Mix, period, followed, good), folded, restart);
        var rate = em.Binary(OpCode.Div, one, periodNow);

        // The beat carried forward, with what is still owed of the last pull let out.
        var paid = em.Mul(owe, em.Binary(OpCode.Min, one, em.Mul(dt, 1f / Ease)));
        var carried = em.Add(em.Add(pos, em.Mul(dt, rate)), paid);
        var nearest = em.Unary(OpCode.Floor, em.Add(carried, 0.5f));
        var off = em.Sub(carried, nearest);
        var pulls = em.Mul(
            em.Mul(em.Mul(hit, held), em.Sub(one, restart)),
            em.Binary(OpCode.Step, em.Unary(OpCode.Abs, off), em.Constant(Near)));
        var owed = em.Mul(em.Sub(em.Sub(owe, paid), em.Mul(pulls, em.Mul(off, Pull))), em.Sub(one, restart));
        var posNow = em.Ternary(OpCode.Mix, carried, nearest, restart);

        var goodNow = em.Binary(OpCode.Max, good, restart);

        em.UnitWrite(upCell, up);
        em.UnitWrite(sinceCell, em.Mul(since, em.Sub(one, hit)));
        em.UnitWrite(heardCell, em.Binary(OpCode.Max, heard, hit));
        em.UnitWrite(periodCell, periodNow);
        em.UnitWrite(missesCell, em.Mul(missesNow, em.Sub(one, restart)));
        em.UnitWrite(sinceGoodCell, em.Mul(sinceGood, em.Sub(one, goodNow)));
        em.UnitWrite(oweCell, owed);
        em.ClockWrite(posCell, posNow);

        var beats = em.Add(posNow, em.Mul(em.Mul(node[NudgePort], 0.001f), rate));
        var counted = em.Unary(OpCode.Floor, beats);
        em.ClockWrite(countCell, counted);

        var heldNow = em.Binary(OpCode.Step, em.Constant(1e-6f), periodNow);
        var ticked = em.Binary(OpCode.Step, em.Constant(0.5f), em.Sub(counted, count));
        var beat = em.Mul(em.Binary(OpCode.Max, em.Mul(ticked, heldNow), restart), memory);
        var locked = em.Mul(heldNow, em.Binary(OpCode.Step, sinceGood, em.Mul(periodNow, LockedFor)));

        return
        [
            em.Mul(beats, memory),
            em.Mul(em.Unary(OpCode.Fract, beats), memory),
            em.Mul(em.Mul(rate, 60f), memory),
            em.Mul(locked, memory),
            beat,
            em.Mul(restart, memory),
        ];
    }
}
