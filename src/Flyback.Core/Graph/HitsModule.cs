using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Hits: the kick, snare and hats heard in a sound, as gates.</summary>
internal static class HitsModule
{
    /// <summary>The type id of the Hits module.</summary>
    internal const string TypeId = "audio.hits";

    /// <summary>The sockets, by position.</summary>
    internal const int InPort = 0, LowPort = 1, HighPort = 2, SensitivityPort = 3, LengthPort = 4, RestPort = 5, MemoryPort = 6;

    /// <summary>The outputs, by position: three gates, then how hard each band was hit.</summary>
    /// <remarks>Named for the code that reads them, tests included: <see cref="Emit"/> fills them in this order.</remarks>
    internal const int KickOut = 0, SnareOut = 1, HatsOut = 2, KickLevelOut = 3, SnareLevelOut = 4, HatsLevelOut = 5;

    /// <summary>A band's quick level, followed up in a millisecond and down in forty, as a Slew times it.</summary>
    private const float QuickRise = 1e-3f, QuickFall = 0.04f;

    /// <summary>
    /// A band's recent level, slow to rise and quick to fall, so a hit is a sudden rise
    /// over it: a held note or a slow wobble rises no faster than this does.
    /// </summary>
    private const float BeforeRise = 0.15f, BeforeFall = 0.02f;

    /// <summary>The quietest a hit can be, so silence and hiss never open a gate.</summary>
    private const float Quietest = 8e-4f;

    /// <summary>How long a band stands out before it counts, so a click across every band is its own drum's.</summary>
    private const float Confirm = 4e-3f;

    /// <summary>How far under the level that opened it a band falls before it can open again.</summary>
    private const float ReArm = 0.7f;

    /// <summary>
    /// The share of the loudest band under which the snare's is only hearing a kick's click:
    /// about 22 dB, since a snare is seldom further under the kick than that.
    /// </summary>
    private const float SnareSpill = 0.08f;

    /// <summary>The same for the others, about 34 dB, since hats often sit that far under.</summary>
    private const float Spill = 0.02f;

    /// <summary>The kick remembers its loudest hits this many times longer than the others.</summary>
    private const float KickMemory = 5f;

    /// <summary>The longest a gate stays open or a rest lasts, inside what a cell holds.</summary>
    private const float Longest = 10f;

    /// <summary>A Slew's time is to within a hundredth of the target; a time constant is that over ln 100.</summary>
    private static readonly float TimeConstants = MathF.Log(100f);

    /// <summary>
    /// Hears the drums in a sound: a kick, a snare and hats, each a gate that opens on a hit.
    /// </summary>
    /// <remarks>
    /// The sound is split into three bands, each filtered at least twice over so one drum spills
    /// less into the next. A band is hit when its quick level jumps: well over its level
    /// of a moment before, so a held note or a slow wobble does not count, and to at
    /// least a share of its loudest recent hits and a sliver of the loudest band's, so
    /// spill from the other drums does not either. All three are ratios, so it works at
    /// any volume. Once a gate opens it stays open for 'length', and the band cannot be
    /// hit again until 'rest' has passed and its level has fallen back, so one drum is
    /// one hit.
    /// </remarks>
    internal static NodeDef Def() => new(
        TypeId, "Hits", ModuleCategories.Measurement,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f) { Help = "The sound to listen to: a Line In, or any part of the patch." },
            new PortSpec("low", PortKind.Scalar, 180f, 40f, 1_000f)
            {
                Knee = 20f,
                Help = "The top of the kick's band, in hertz. The snare's starts a little over twice this.",
            },
            new PortSpec("high", PortKind.Scalar, 5_000f, 1_000f, 12_000f)
            {
                Knee = 20f,
                Help = "The bottom of the hats' band, in hertz. The snare's ends a little over half of this.",
            },
            Num("sensitivity", 0.5f, 0f, 1f) with
            {
                Help = "How little a band has to stand out to count as a hit. Higher hears quieter hits, and more spill.",
            },
            new PortSpec("length", PortKind.Scalar, -1.3f, -4f, 1f, -1, PortDisplay.Duration)
            {
                Help = "How long a gate stays open on each hit.",
            },
            new PortSpec("rest", PortKind.Scalar, -1.15f, -4f, 1f, -1, PortDisplay.Duration)
            {
                Help = "The shortest time between two hits of one band, so a drum that rings is heard once.",
            },
            Seconds("memory", 0.6f) with
            {
                Help = "How long a band remembers its loudest hits. The kick remembers five times as long, "
                    + "so in a breakdown a bass line does not pass for a kick.",
            },
        ],
        [
            new PortSpec("kick") { Help = "A gate, open for 'length' on each hit under 'low'." },
            new PortSpec("snare") { Help = "A gate, open for 'length' on each hit between the two bands: snares and claps." },
            new PortSpec("hats") { Help = "A gate, open for 'length' on each hit over 'high': hats and cymbals." },
            new PortSpec("kick level") { Help = "How loud the kick's band is against its loudest recent hits, 0 to 1." },
            new PortSpec("snare level") { Help = "How loud the snare's band is against its loudest recent hits, 0 to 1." },
            new PortSpec("hats level") { Help = "How loud the hats' band is against its loudest recent hits, 0 to 1." },
        ],
        Emit,
        "Hears the drums in a sound, so the music can drive the picture: patch a Line In into "
        + "'in' and an envelope's gate from 'kick', 'snare' or 'hats'. Each band is judged "
        + "against its own recent level, so it needs no gain and works at any volume. "
        + "Audio only: silent on the picture.")
    {
        Words = "onset detection, beat detection, drum trigger, kick snare hats from the microphone",
        Sinks = ModuleSinks.Audio,
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var none = em.Constant(0f);
        var flat = none;
        var sound = node[InPort];

        var low = node[LowPort];
        var high = node[HighPort];
        var low2 = em.Mul(low, 2.2f);
        var high2 = em.Mul(high, 0.6f);

        var kickBand = LowPass(LowPass(sound, low), low);
        // Three high-passes, not two: the kick is the loudest thing in most music, and spills most.
        var snareBand = LowPass(LowPass(HighPass(HighPass(HighPass(sound, low2), low2), low2), high2), high2);
        var hatsBand = HighPass(HighPass(sound, high), high);

        // Sensitivity 0.5 asks a band to jump to 1.6 times its recent level and to a
        // third of its loudest; all the way up, 1.2 times and a tenth.
        var shy = em.Sub(em.Constant(1f), em.Ternary(OpCode.Clamp, node[SensitivityPort], none, em.Constant(1f)));
        var over = em.Add(em.Mul(shy, 0.8f), 1.2f);
        var share = em.Add(em.Mul(shy, 0.5f), 0.1f);

        var length = Duration(node[LengthPort]);
        var rest = Duration(node[RestPort]);
        var memory = Seconds(node[MemoryPort]);

        var kickHeard = Levels(kickBand, em.Mul(memory, KickMemory));
        var snareHeard = Levels(snareBand, memory);
        var hatsHeard = Levels(hatsBand, memory);

        // What one drum spills into the next band is a sliver of the loudest band.
        var loudest = em.Binary(OpCode.Max, em.Binary(OpCode.Max, kickHeard.Peaks, snareHeard.Peaks), hatsHeard.Peaks);

        var (kick, kickLevel) = Band(kickHeard, em.Mul(loudest, Spill));
        var (snare, snareLevel) = Band(snareHeard, em.Mul(loudest, SnareSpill));
        var (hats, hatsLevel) = Band(hatsHeard, em.Mul(loudest, Spill));

        return
        [
            em.Mul(kick, live),
            em.Mul(snare, live),
            em.Mul(hats, live),
            em.Mul(kickLevel, live),
            em.Mul(snareLevel, live),
            em.Mul(hatsLevel, live),
        ];

        Slot LowPass(Slot dry, Slot cutoff) => FilterModule.Responses(em, dry, cutoff, flat)[0];

        Slot HighPass(Slot dry, Slot cutoff) => FilterModule.Responses(em, dry, cutoff, flat)[2];

        (Slot Quick, Slot Before, Slot Peaks) Levels(Slot signal, Slot remembers)
        {
            var rectified = em.Unary(OpCode.Abs, signal);
            var quick = Follow(rectified, em.Constant(QuickRise), em.Constant(QuickFall));
            var before = Follow(quick, em.Constant(BeforeRise), em.Constant(BeforeFall));
            var peaks = Follow(quick, em.Constant(QuickRise), remembers);
            return (quick, before, peaks);
        }

        (Slot Gate, Slot Level) Band((Slot Quick, Slot Before, Slot Peaks) heard, Slot spill)
        {
            var step = em.Interval();
            var (quick, before, peaks) = heard;

            var bar = em.Binary(
                OpCode.Max,
                em.Binary(OpCode.Max, em.Mul(before, over), em.Mul(peaks, share)),
                em.Binary(OpCode.Max, spill, em.Constant(Quietest)));
            var above = em.Binary(OpCode.Step, bar, quick);
            var fallen = em.Sub(em.Constant(1f), em.Binary(OpCode.Step, em.Mul(bar, ReArm), quick));

            // Four cells, all nought to begin with: whether the last hit is still
            // sounding, the rest left before the next, how long the gate has left, and
            // how long the band has stood out, since a drum's first click reaches
            // every band before the low one has risen to say whose it is.
            var standingCell = em.AllocateUnitSlot();
            var spentCell = em.AllocateUnitSlot();
            var restingCell = em.AllocateUnitSlot();
            var openCell = em.AllocateUnitSlot();
            var spent = em.UnitRead(spentCell);
            var resting = em.UnitRead(restingCell);
            var open = em.UnitRead(openCell);

            var standing = em.UnitRead(standingCell);
            var ready = em.Mul(em.Sub(em.Constant(1f), spent), em.Binary(OpCode.Step, resting, none));
            var stood = em.Mul(em.Add(standing, step), em.Mul(ready, above));
            var hit = em.Binary(OpCode.Step, em.Constant(Confirm), stood);
            em.UnitWrite(standingCell, em.Mul(stood, em.Sub(em.Constant(1f), hit)));

            em.UnitWrite(spentCell, em.Binary(OpCode.Max, hit, em.Mul(spent, em.Sub(em.Constant(1f), fallen))));
            em.UnitWrite(restingCell, em.Ternary(OpCode.Mix, CountDown(resting), rest, hit));

            var left = em.Ternary(OpCode.Mix, CountDown(open), length, hit);
            em.UnitWrite(openCell, left);

            var gate = em.Sub(em.Constant(1f), em.Binary(OpCode.Step, left, none));
            var level = em.Ternary(
                OpCode.Clamp,
                em.Binary(OpCode.Div, quick, em.Binary(OpCode.Max, peaks, em.Constant(Quietest))),
                none,
                em.Constant(1f));

            return (gate, level);

            Slot CountDown(Slot seconds) => em.Binary(OpCode.Max, em.Sub(seconds, step), none);
        }

        // A one-pole follower, quick or slow each way, timed as a Slew is.
        Slot Follow(Slot target, Slot rise, Slot fall)
        {
            var cell = em.AllocateUnitSlot();
            var held = em.UnitRead(cell);
            var time = em.Ternary(OpCode.Mix, fall, rise, em.Binary(OpCode.Step, held, target));
            var closed = em.Sub(
                em.Constant(1f),
                em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, em.Interval(), em.Mul(time, 1f / TimeConstants)))));
            var next = em.Ternary(OpCode.Mix, held, target, closed);
            em.UnitWrite(cell, next);
            return next;
        }

        Slot Seconds(Slot decades) => em.Binary(OpCode.Pow, em.Constant(10f), decades);

        Slot Duration(Slot decades) => em.Ternary(OpCode.Clamp, Seconds(decades), none, em.Constant(Longest));
    }
}
