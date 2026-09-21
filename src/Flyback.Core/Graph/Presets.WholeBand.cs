using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

public static partial class Presets
{
    /// <summary>
    /// A whole song out of nothing but the engine's own modules: a kick, a snare
    /// and hats, a bass through a filter, two plucked strings, a pad and a lead,
    /// in a room — arranged into twelve phrases of eight bars, with a picture every
    /// one of the parts moves.
    /// </summary>
    /// <remarks>
    /// The arrangement is three lanes of one step a phrase, and each part decides
    /// what it needs of them. Nothing in a lane has any memory, so the picture reads
    /// the same lanes the sound does — but it never reads a filter or an envelope,
    /// which mean something else where there is no evaluation before this one.
    /// </remarks>
    public static Patch WholeBand(ModuleCatalog modules) => new Band(modules).Assemble();

    /// <summary>The wiring Whole band says a hundred times, said once.</summary>
    /// <remarks>
    /// Every method adds exactly the catalogue module its name stands for, so what
    /// is built with these is what <see cref="PatchBuilder.Add(string, ValueTuple{int, float}[])"/>
    /// and <see cref="PatchBuilder.Wire"/> would have built by hand, module for module.
    /// </remarks>
    private sealed class Band(ModuleCatalog modules)
    {
        private readonly PatchBuilder b = new(modules);

        /// <summary>How many modules were already in a box when the last one was closed.</summary>
        private int boxed;

        /// <summary>The Tempo's second output: the count of beats so far.</summary>
        private const int Beats = 1;

        /// <summary>A Sequencer's second output.</summary>
        private const int Gate = 1;

        /// <summary>A Filter's outputs.</summary>
        private const int Low = 0, High = 2;

        /// <summary>
        /// A natural minor on A, and the G sharp as well: the one note the E chord
        /// has that the key does not.
        /// </summary>
        private static readonly int[] Key = [0, 2, 4, 5, 7, 8, 9, 11];

        /// <summary>
        /// A chord's third, in semitones over its root — between the minor and the
        /// major, so that snapping to <see cref="Key"/> gives whichever the root
        /// calls for: C over A, B over G, A over F, and G sharp over E.
        /// </summary>
        private const float Third = 3.6f;

        private NodeInstance tempo = null!;

        /// <summary>Everything added since the last box, drawn as one.</summary>
        private void Box(string name, params NodeInstance[] except)
        {
            b.Group(name, [.. b.Patch.Nodes.Skip(boxed).Except(except)]);
            boxed = b.Patch.Nodes.Count;
        }

        private NodeInstance Wired(string type, NodeInstance a, NodeInstance c, int from = 0, int second = 0)
        {
            var node = b.Add(type);
            b.Wire(a, from, node, 0).Wire(c, second, node, 1);
            return node;
        }

        private NodeInstance Knobbed(string type, NodeInstance a, float by, int from = 0)
        {
            var node = b.Add(type, (1, by));
            b.Wire(a, from, node, 0);
            return node;
        }

        private NodeInstance Through(string type, NodeInstance a, int from = 0)
        {
            var node = b.Add(type);
            b.Wire(a, from, node, 0);
            return node;
        }

        private NodeInstance Times(NodeInstance a, float by, int from = 0) => Knobbed("math.mul", a, by, from);

        private NodeInstance Plus(NodeInstance a, float by, int from = 0) => Knobbed("math.add", a, by, from);

        private NodeInstance Sum(NodeInstance a, NodeInstance c) => Wired("math.add", a, c);

        private NodeInstance Product(NodeInstance a, NodeInstance c, int from = 0, int second = 0) =>
            Wired("math.mul", a, c, from, second);

        /// <summary>A Remap: one range onto another, neither end clamped.</summary>
        private NodeInstance Span(
            NodeInstance a, float inLow, float inHigh, float outLow, float outHigh, int from = 0)
        {
            var node = b.Add("math.remap", (1, inLow), (2, inHigh), (3, outLow), (4, outHigh));
            b.Wire(a, from, node, 0);
            return node;
        }

        /// <summary>A Threshold on a lane: whether the song has got as far as a part needs.</summary>
        private NodeInstance Reached(NodeInstance lane, float level)
        {
            var node = b.Add("math.step", (0, level));
            b.Wire(lane, 0, node, 1);
            return node;
        }

        /// <summary>A Mix as a switch between two sources, read at the same output of each.</summary>
        private NodeInstance Either(NodeInstance a, NodeInstance c, NodeInstance t, int from = 0)
        {
            var mix = b.Add("math.mix");
            b.Wire(a, from, mix, 0).Wire(c, from, mix, 1).Wire(t, 0, mix, 2);
            return mix;
        }

        /// <summary>A Filter cornered at <paramref name="hz"/>; wire its 'cutoff' instead and the corner is a signal.</summary>
        private NodeInstance Filtered(NodeInstance a, float hz = 800f, float resonance = 0f, int from = 0)
        {
            var filter = b.Add(NodeCatalog.FilterTypeId, (1, hz), (2, resonance));
            b.Wire(a, from, filter, 0);
            return filter;
        }

        /// <summary>A Slew that takes <paramref name="decades"/> (ten to the power, in seconds) either way.</summary>
        private NodeInstance Slewed(NodeInstance a, float decades, int from = 0)
        {
            var slew = b.Add(NodeCatalog.SlewTypeId, (1, decades), (2, decades));
            b.Wire(a, from, slew, 0);
            return slew;
        }

        /// <summary>
        /// What is left of each <paramref name="rate"/>th of a beat, to the power
        /// <paramref name="curve"/>: an envelope with no memory, so the screen reads it
        /// as well as the speakers do.
        /// </summary>
        private NodeInstance Stroke(NodeInstance position, float rate, float curve)
        {
            var left = b.Add("math.sub", (0, 1f));
            b.Wire(Through("math.fract", Times(position, rate, Beats)), 0, left, 1);
            return Knobbed("math.pow", left, curve);
        }

        /// <summary>A sequencer on the beat, at so many steps to one.</summary>
        private NodeInstance Steps(string type, float rate, float gateLength, float shape, params Step[] steps)
        {
            var node = b.Add(type, (1, rate), (2, gateLength), (3, shape));
            StepsExtra.Set(node, steps);
            b.Wire(tempo, Beats, node, 0);
            return node;
        }

        /// <summary>A lane of the arrangement: one step to a phrase of eight bars, open the whole way.</summary>
        private NodeInstance Lane(params Step[] steps) => Steps("seq.values", 1f / 32f, 1f, 0f, steps);

        /// <summary>A step nothing is struck on.</summary>
        private static Step Rest(float value = 0f, float length = 1f) => new(value, length, 0f);

        /// <summary>A drum pattern, where a step's value is how hard and a nought is a rest.</summary>
        private static Step[] Hits(params float[] strengths) =>
            [.. strengths.Select(s => s > 0f ? new Step(s) : Rest())];

        public Patch Assemble()
        {
            // --- the clock -------------------------------------------------------

            // Every part reads the count of beats rather than the clock, so the tempo
            // is this one knob. The clock itself is here for the picture's drifts.
            tempo = b.Add(NodeCatalog.TempoTypeId, (0, 112f));
            var clock = b.Add(NodeCatalog.TimeTypeId);

            Box("Clock");

            // --- the song --------------------------------------------------------

            // How much band there is, a phrase at a time: an intro of strings and pad,
            // the drums, two verses, a chorus of two phrases, a verse, a bridge with
            // no drums in it, the chorus again, and a way out that ends where the
            // intro begins. The step's volume is spare, so it carries the one thing
            // that does not rise with the rest: how loud the pad is, which is most of
            // what there is in the bridge and least in a verse.
            var song = Lane(
                new Step(0.1f, 1f, 0.9f), new Step(0.3f, 1f, 0.8f), new Step(0.6f, 1f, 0.45f),
                new Step(0.7f, 1f, 0.5f), new Step(1f, 1f, 0.75f), new Step(1f, 1f, 0.75f),
                new Step(0.7f, 1f, 0.5f), new Step(0.2f), new Step(1f, 1f, 0.8f),
                new Step(1f, 1f, 0.8f), new Step(0.45f, 1f, 0.6f), new Step(0.1f, 1f, 0.9f));

            // Which half of the song it is: nought is the verse, and its chords and
            // its bass and its kick, and one is the chorus. The volume is whether the
            // lead plays at all — not in the intro or the first verse, and alone over
            // the pad in the bridge.
            var theme = Lane(
                Rest(), Rest(), Rest(), new Step(0f),
                new Step(1f), new Step(1f), new Step(0f), new Step(0f),
                new Step(1f), new Step(1f), new Step(0f), Rest());

            // And which phrases end in a fill: the ones a chorus begins or ends after.
            var turn = Lane(
                new Step(0f), new Step(0f), new Step(0f), new Step(1f),
                new Step(0f), new Step(1f), new Step(0f), new Step(1f),
                new Step(0f), new Step(1f), new Step(0f), new Step(0f));

            // The fill is the last bar of such a phrase, and a ramp across it.
            var phrase = Through("math.fract", Times(tempo, 1f / 32f, Beats));
            var filling = Product(Reached(phrase, 0.875f), turn);
            var ramp = Span(phrase, 0.875f, 1f, 0.35f, 1f);

            // The harmony, a bar at a time, in semitones from A. The verse falls
            // A minor, G, F, E and the E is major; the chorus rises F, C, G to A
            // minor, and the second time round stops on the E that wants the verse.
            var verseRoots = Steps("seq.values", 0.25f, 1f, 0f,
                new Step(0f), new Step(-2f), new Step(-4f), new Step(-5f));
            var chorusRoots = Steps("seq.values", 0.25f, 1f, 0f,
                new Step(-4f), new Step(3f), new Step(-2f), new Step(0f),
                new Step(-4f), new Step(3f), new Step(-5f), new Step(-5f));
            var root = Either(verseRoots, chorusRoots, theme);

            Box("Song");

            // --- the noise -------------------------------------------------------

            // White, which the hats and the snare each take a band of.
            var hiss = b.Add(NodeCatalog.RandomTypeId);

            Box("Noise");

            // --- the kick --------------------------------------------------------

            // One and three and the push before each in the verse, which leaves two
            // and four to the snare; four on the floor in the chorus. A step's value
            // is how hard it is hit, held by a Sample & Hold from the moment it is,
            // because the step has moved on long before the drum has finished.
            var verseKick = Steps("seq.values", 4f, 0.5f, 0.01f, Hits(
                1f, 0f, 0f, 0f, 0f, 0f, 0.75f, 0f, 0.95f, 0f, 0f, 0f, 0f, 0f, 0.6f, 0f));
            var chorusKick = Steps("seq.values", 4f, 0.5f, 0.01f, Hits(
                1f, 0f, 0f, 0f, 0.9f, 0f, 0f, 0f, 0.95f, 0f, 0f, 0f, 0.9f, 0f, 0.6f, 0f));

            var kickGate = Product(Either(verseKick, chorusKick, theme, Gate), Reached(song, 0.25f));
            var kickHard = Wired(NodeCatalog.HoldTypeId, Either(verseKick, chorusKick, theme), kickGate);

            // Two envelopes and a sine, which is the whole of a kick drum: one shapes
            // how loud it is, the shorter one what pitch it is.
            var kickLevel = b.Add(NodeCatalog.AdsrTypeId, (1, -2.9f), (2, -0.62f), (3, 0f), (4, -1.1f));
            var kickSweep = b.Add(NodeCatalog.AdsrTypeId, (1, -3.3f), (2, -1.35f), (3, 0f), (4, -1.8f));
            var kickBody = b.Add("osc.sine");

            // Past the rails and held to them, so the top of each cycle is flat: the
            // knock a sine has not got.
            var kick = b.Add("math.clamp", (1, -1f), (2, 1f));

            // The sidechain: the kick's level on the bass and the pad, followed at
            // once so the duck has the envelope's own shape.
            var duck = b.Add(NodeCatalog.DuckTypeId, (3, 0.55f), (5, -4f), (6, -4f));

            b.Wire(kickLevel, 0, duck, 2)
             .Wire(kickGate, 0, kickLevel, 0)
             .Wire(kickGate, 0, kickSweep, 0)
             .Wire(Span(kickSweep, 0f, 1f, 46f, 200f), 0, kickBody, 1)
             .Wire(Times(Product(Product(kickBody, kickLevel), kickHard), 1.7f), 0, kick, 0);

            Box("Kick");

            // --- the hats --------------------------------------------------------

            // The pattern is how hard each sixteenth is played and nothing else: the
            // envelope is what is left of the sixteenth, to the tenth power, which
            // needs no trigger. An open one on the off-beat, a third as steep, is the
            // chorus. The gate is not used by the hats at all — the snare's fill is
            // what plays it.
            var hatSeq = Steps("seq.values", 4f, 0.4f, 0.01f,
                new Step(0.8f), new Step(0.3f), new Step(0.55f), new Step(0.3f),
                new Step(0.75f), new Step(0.3f), new Step(0.6f), new Step(0.35f),
                new Step(0.8f), new Step(0.3f), new Step(0.55f), new Step(0.3f),
                new Step(0.75f), new Step(0.35f), new Step(0.65f), new Step(0.5f));

            var shut = Product(Product(Stroke(tempo, 4f, 10f), hatSeq), Reached(song, 0.25f));

            var offBeat = b.Add("math.sub", (0, 1f));
            b.Wire(Through("math.fract", Plus(tempo, 0.5f, Beats)), 0, offBeat, 1);
            var open = Product(Knobbed("math.pow", offBeat, 3f), theme);

            var hatLevel = Sum(shut, Times(open, 0.6f));
            var hats = Product(Filtered(hiss, 7000f), hatLevel, High);

            Box("Hats");

            // --- the snare -------------------------------------------------------

            // Two and four, and a ghost before the bar turns over. In a fill it is
            // the hats' sixteenths instead, as hard as the ramp has got to — both are
            // a Mix used as a switch, because half way between two patterns is neither.
            var snareSeq = Steps("seq.values", 4f, 0.5f, 0.01f, Hits(
                0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.95f, 0f, 0f, 0.45f));

            var snareGate = b.Add("math.mix");
            var snareHard = b.Add("math.mix");

            b.Wire(Product(snareSeq, Reached(song, 0.5f), Gate), 0, snareGate, 0)
             .Wire(hatSeq, Gate, snareGate, 1)
             .Wire(filling, 0, snareGate, 2)
             .Wire(snareSeq, 0, snareHard, 0)
             .Wire(ramp, 0, snareHard, 1)
             .Wire(filling, 0, snareHard, 2);

            // The wires are the noise with its bottom and its top taken off, and the
            // shell is a sine under them that is gone sooner: the envelope squared.
            var snareLevel = b.Add(NodeCatalog.AdsrTypeId, (1, -3.3f), (2, -0.8f), (3, 0f), (4, -1.2f));
            var wires = Product(Times(Filtered(Filtered(hiss, 1100f), 6500f, from: High), 2.2f, Low), snareLevel);
            var shell = b.Add("osc.sine", (1, 185f), (3, 0.6f));
            var snare = Product(
                Sum(wires, Product(shell, Product(snareLevel, snareLevel))),
                Wired(NodeCatalog.HoldTypeId, snareHard, snareGate));

            b.Wire(snareGate, 0, snareLevel, 0);

            Box("Snare");

            // --- the bass --------------------------------------------------------

            // A bar of each, in semitones over the chord. The verse is uneven and that
            // is the groove: the dotted eighth into a sixteenth is what stops it
            // walking. The chorus is straight eighths, which is what makes it run.
            var verseBass = Steps("seq.values", 2f, 0.6f, 0.02f,
                new Step(0f, 1.5f), new Step(0f, 0.5f, 0.6f), new Step(7f, 1f, 0.85f), new Step(0f, 1f, 0.7f),
                new Step(0f, 1.5f), new Step(12f, 0.5f, 0.7f), new Step(0f, 1f, 0.85f), new Step(7f, 1f, 0.75f));
            var chorusBass = Steps("seq.values", 2f, 0.7f, 0.02f,
                new Step(0f), new Step(0f, 1f, 0.7f), new Step(0f, 1f, 0.85f), new Step(0f, 1f, 0.7f),
                new Step(0f), new Step(0f, 1f, 0.7f), new Step(12f, 1f, 0.85f), new Step(0f, 1f, 0.75f));

            var bassGate = Product(Either(verseBass, chorusBass, theme, Gate), Reached(song, 0.4f));
            var bassHz = Through("audio.note", Plus(Sum(Either(verseBass, chorusBass, theme), root), 33f));

            // The gate is as high as the note is loud, so smoothed it is the accent,
            // and the envelope times it is what opens the filter as well as the note.
            var bassEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -3f), (2, -0.9f), (3, 0.4f), (4, -1.2f));
            var pluck = Product(bassEnv, Slewed(bassGate, -1.6f));

            // The filter rings, and its cutoff runs from seventy hertz with the
            // envelope shut to as far up as the song has got.
            var saw = b.Add("osc.saw", (3, 0.8f));
            var cutoff = b.Add("math.remap", (1, 0f), (2, 1f), (3, 70f));
            var filter = Filtered(saw, resonance: 0.8f);

            b.Wire(bassGate, 0, bassEnv, 0)
             .Wire(bassHz, 0, saw, 1)
             .Wire(pluck, 0, cutoff, 0)
             .Wire(Span(song, 0f, 1f, 900f, 2600f), 0, cutoff, 4)
             .Wire(cutoff, 0, filter, 1);

            // Driven, and a sine at the same pitch added after it so that it stays a sine.
            var grit = b.Add(NodeCatalog.DriveTypeId, (1, 3f));
            var sub = b.Add("osc.sine", (3, 0.75f));
            var bass = Product(Sum(grit, Product(sub, pluck)), duck, second: 2);

            b.Wire(Product(filter, pluck, Low), 0, grit, 0)
             .Wire(bassHz, 0, sub, 1);

            Box("Bass");

            // --- the strings -----------------------------------------------------

            // Two plucked strings a sixteenth apart, one on the eighths and one between
            // them, each playing the chord from a list written once: a Tune adds the
            // root and snaps what comes of it to the key, so the same figure is minor
            // over A and major over F. The second string waits for the drums.
            var firstArp = Steps("seq.values", 2f, 0.5f, 0.02f,
                new Step(57f), new Step(64f, 1f, 0.7f), new Step(69f, 1f, 0.8f), new Step(64f, 1f, 0.7f),
                new Step(69f + Third, 1f, 0.9f), new Step(64f, 1f, 0.7f), new Step(69f, 1f, 0.8f),
                new Step(64f, 1f, 0.7f));
            var secondArp = Steps("seq.values", 2f, 0.5f, 0.02f,
                new Step(69f), new Step(69f + Third), new Step(76f), new Step(69f + Third),
                new Step(69f), new Step(76f), new Step(69f + Third), new Step(69f));

            var firstTune = b.Add("audio.tune");
            var secondTune = b.Add("audio.tune");
            ScaleExtra.Set(firstTune, Key);
            ScaleExtra.Set(secondTune, Key);

            var firstPlucked = b.Add(NodeCatalog.StringTypeId, (3, -0.15f));
            var secondPlucked = b.Add(NodeCatalog.StringTypeId, (3, -0.15f));

            // A pluck is a burst of noise and the String loses very little of its top
            // by itself, so each goes through a lowpass, which opens as the song fills
            // — from a nylon string to a steel one — and is made up after it for what
            // it took.
            var stringTone = Span(song, 0f, 1f, 900f, 2200f);

            NodeInstance Mellowed(NodeInstance plucked)
            {
                var filter = Filtered(plucked);
                b.Wire(stringTone, 0, filter, 1);
                return Times(filter, 2.2f, Low);
            }

            var firstString = Mellowed(firstPlucked);
            var secondString = Mellowed(secondPlucked);

            b.Wire(Plus(tempo, -0.25f, Beats), 0, secondArp, 0)
             .Wire(firstArp, 0, firstTune, 0)
             .Wire(root, 0, firstTune, 1)
             .Wire(secondArp, 0, secondTune, 0)
             .Wire(root, 0, secondTune, 1)
             .Wire(firstArp, Gate, firstPlucked, 1)
             .Wire(firstTune, 0, firstPlucked, 2)
             .Wire(Product(secondArp, Reached(song, 0.2f), Gate), 0, secondPlucked, 1)
             .Wire(secondTune, 0, secondPlucked, 2);

            // A side each, and half of both in the middle.
            var strings = Sum(firstString, secondString);
            var between = Times(strings, 0.5f);
            var stringsL = Sum(firstString, between);
            var stringsR = Sum(secondString, between);

            Box("Strings");

            // --- the pad ---------------------------------------------------------

            // The chord itself: three pulses whose widths are swept at three speeds,
            // so each beats against itself and none of them with another. The root
            // and the fifth are sums, and only the third needs the key.
            var padThird = b.Add("audio.tune", (0, 57f + Third));
            ScaleExtra.Set(padThird, Key);
            b.Wire(root, 0, padThird, 1);

            NodeInstance Voice(NodeInstance hz, float sweep)
            {
                var width = b.Add("osc.sine", (1, sweep), (3, 0.22f), (4, 0.5f));
                var pulse = b.Add("osc.pulse", (4, 0.5f));
                b.Wire(hz, 0, pulse, 1).Wire(width, 0, pulse, 3);
                return pulse;
            }

            var padRoot = Voice(Through("audio.note", Plus(root, 57f)), 0.17f);
            var padMiddle = Voice(padThird, 0.23f);
            var padFifth = Voice(Through("audio.note", Plus(root, 64f)), 0.29f);

            // The third leans left and the fifth right, and the lowpass on each side
            // opens with the song.
            var padL = b.Add("math.mixer", (1, 0.8f), (3, 0.9f), (5, 0.35f));
            var padR = b.Add("math.mixer", (1, 0.8f), (3, 0.35f), (5, 0.9f));
            var padTone = Span(song, 0f, 1f, 700f, 2400f);
            var padToneL = Filtered(padL);
            var padToneR = Filtered(padR);

            // A chord that cuts out is a mistake and one that swells is not, so its
            // level gets where the lane says over a couple of seconds.
            var padLevel = Product(Slewed(song, 0.4f, Gate), duck, second: 2);

            b.Wire(padRoot, 0, padL, 0).Wire(padMiddle, 0, padL, 2).Wire(padFifth, 0, padL, 4)
             .Wire(padRoot, 0, padR, 0).Wire(padMiddle, 0, padR, 2).Wire(padFifth, 0, padR, 4)
             .Wire(padTone, 0, padToneL, 1)
             .Wire(padTone, 0, padToneR, 1);

            var padOutL = Product(padToneL, padLevel, Low);
            var padOutR = Product(padToneR, padLevel, Low);

            Box("Pad");

            // --- the lead --------------------------------------------------------

            // The verse's is twenty sixteenths — five beats, against the bar's four —
            // so it never lands on the same chord the same way twice. A rest is a
            // volume rather than a note, so the pitch stays where it was and the
            // notes either side of it are one phrase.
            var hook = Steps("seq.notes", 4f, 0.62f, 0.045f,
                new Step(69f), new Step(72f, 1f, 0.8f), new Step(76f, 1f, 0.9f), new Step(72f, 1f, 0.6f),
                new Step(77f), new Step(76f, 1f, 0.85f), Rest(76f), new Step(74f, 1f, 0.9f),
                new Step(71f, 1f, 0.8f), new Step(74f, 1f, 0.7f), new Step(79f), new Step(77f, 1f, 0.85f),
                new Step(76f, 1f, 0.9f), new Step(74f, 1f, 0.6f), new Step(72f, 1f, 0.95f), Rest(72f),
                new Step(71f, 1f, 0.85f), new Step(69f), new Step(67f, 1f, 0.7f), new Step(69f, 1f, 0.8f));

            // The chorus's is four bars of long notes in eighths, a chord tone at the
            // top of each bar, ending on the E that belongs to both of its last chords.
            var tune = Steps("seq.notes", 2f, 0.85f, 0.03f,
                new Step(72f, 3f), new Step(69f, 1f, 0.8f), new Step(72f, 2f, 0.9f), new Step(77f, 2f),
                new Step(76f, 4f), new Step(74f, 2f, 0.85f), new Step(72f, 2f, 0.9f),
                new Step(74f, 3f), new Step(71f, 1f, 0.8f), new Step(74f, 2f, 0.9f), new Step(79f, 2f),
                new Step(76f, 6f), Rest(76f, 2f));

            var leadStep = Either(hook, tune, theme);
            var leadGate = Product(Either(hook, tune, theme, Gate), theme, 0, Gate);

            var leadNote = Through("audio.note", leadStep);

            // The twin, off the first Note's 'note' output: the detune goes on after
            // the snap because cents are the one control that can sit between two
            // semitones. Swung by a sine, so the beating rate keeps changing.
            var vibrato = b.Add("osc.sine", (1, 5.4f), (3, 9f));
            var wide = b.Add("audio.note");
            var leadA = b.Add("osc.saw", (3, 0.7f));
            var leadB = b.Add("osc.saw", (3, 0.7f));

            // A fifth over the tune, on a triangle so it fills rather than competes.
            var fifthOsc = b.Add("osc.triangle", (3, 0.5f));
            var swell = b.Add("osc.sine", (1, 0.043f), (3, 0.5f), (4, 0.5f));
            var fifth = Product(fifthOsc, swell);

            // Plucked in the verse and sung in the chorus: the sustain is the theme.
            var leadEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -2.5f), (2, -0.95f), (4, -0.85f));

            // Left and right differ in which saw they carry and in nothing else, which
            // is where the width comes from. Each goes through a lowpass the envelope
            // opens, so a note starts bright and closes as it is held.
            var leadTone = Span(leadEnv, 0f, 1f, 500f, 5200f);
            var leadToneL = Filtered(Sum(leadA, fifth));
            var leadToneR = Filtered(Sum(leadB, fifth));
            var leadLevel = Product(leadEnv, Span(theme, 0f, 1f, 0.6f, 0.9f));

            b.Wire(leadNote, 1, wide, 0)
             .Wire(vibrato, 0, wide, 2)
             .Wire(leadNote, 0, leadA, 1)
             .Wire(wide, 0, leadB, 1)
             .Wire(Through("audio.note", Plus(leadStep, 7f)), 0, fifthOsc, 1)
             .Wire(leadGate, 0, leadEnv, 0)
             .Wire(Span(theme, 0f, 1f, 0.3f, 0.7f), 0, leadEnv, 3)
             .Wire(leadTone, 0, leadToneL, 1)
             .Wire(leadTone, 0, leadToneR, 1);

            var leadL = Product(leadToneL, leadLevel, Low);
            var leadR = Product(leadToneR, leadLevel, Low);

            Box("Lead");

            // --- the room --------------------------------------------------------

            // What should sound further off than the drums, darkened, into a small
            // room and nothing of it dry.
            var send = b.Add("math.mixer", (1, 0.5f), (3, 0.4f), (5, 0.5f), (7, 0.12f));
            var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.3f), (2, 0.5f), (3, 1f));

            b.Wire(snare, 0, send, 0)
             .Wire(Sum(leadL, leadR), 0, send, 2)
             .Wire(strings, 0, send, 4)
             .Wire(hats, 0, send, 6)
             .Wire(Filtered(send, 2600f), Low, room, 0);

            Box("Room");

            // --- the desk --------------------------------------------------------

            // Two Desks of four, chained by their buses into one of eight. The hats
            // lean right: a Desk has one level for both sides of a channel, so the
            // lean is on the signal.
            var drums = b.Add("math.desk", (2, 0.85f), (5, 0.6f), (8, 0.4f), (11, 0.8f));

            // The last in the chain is the master: a trim under unity, and rails that
            // should never be reached.
            var music = b.Add("math.desk", (2, 0.8f), (5, 0.42f), (8, 0.6f), (11, 0.17f), (14, 0.7f));

            var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.62f));

            b.Wire(kick, 0, drums, 0)
             .Wire(snare, 0, drums, 3)
             .Wire(hats, 0, drums, 6)
             .Wire(Times(hats, 1.45f), 0, drums, 7)
             .Wire(bass, 0, drums, 9)

             .Wire(stringsL, 0, music, 0)
             .Wire(stringsR, 0, music, 1)
             .Wire(padOutL, 0, music, 3)
             .Wire(padOutR, 0, music, 4)
             .Wire(leadL, 0, music, 6)
             .Wire(leadR, 0, music, 7)
             .Wire(room, 0, music, 9)
             .Wire(room, 1, music, 10)

             .Wire(drums, 2, music, 12)
             .Wire(drums, 3, music, 13)
             .Wire(music, 0, output, NodeCatalog.OutputLeftPort)
             .Wire(music, 1, output, NodeCatalog.OutputRightPort);

            Box("Desk", output);

            // --- the picture: geometry -------------------------------------------

            // One clock read at four speeds: seconds are seconds, and what differs
            // between these is only how much of them each part wants.
            var spin = Times(clock, 0.055f);
            var boil = Times(clock, 0.18f);
            var drift = Times(clock, 0.4f);
            var crawl = Times(clock, 0.02f);

            // The chord moves the frame: it is added to the rotation and is how many
            // wedges the fold has, so the picture rebuilds itself once a bar. The kick
            // moves the light, and it is doing three things at once — the zoom here,
            // the brightness, and the twist on the feedback — so the bridge is the
            // picture holding still.
            var placed = b.Add("space.transform");
            placed.SetState(
                NodeCatalog.TransformStateKey,
                new JsonObject { [NodeCatalog.TransformOrderKey] = NodeCatalog.TurnThenZoom });

            var fold = b.Add("space.kaleidoscope");

            // Geometry alone looks like geometry, so the plane is bent by a field
            // read from inside the fold — symmetric, so it repeats with the wedges
            // rather than quietly undoing them. How far is a slow breath, and a
            // shove from the first string each time it is plucked.
            var field = b.Add("pattern.noise", (3, 2.1f));
            var breath = b.Add("osc.sine", (1, 0.071f), (3, 0.25f), (4, 0.45f));
            var bend = b.Add("space.warp");

            // The lead's gate widens the rings, so a note arrives as a band rather
            // than as a change of color alone, and there are more of them the more
            // song there is. Rings are a sine, so most of the frame is dark and only
            // the crests survive as filaments.
            var bands = b.Add("pattern.rings");
            var filament = b.Add("math.smoothstep", (0, 0.2f), (1, 0.95f));

            b.Wire(Sum(spin, Times(root, 0.08f)), 0, placed, TransformAngle)
             .Wire(Span(kickGate, 0f, 1f, 0.96f, 1.3f), 0, placed, TransformZoom)

             .Wire(placed, 0, fold, 0)
             .Wire(placed, 1, fold, 1)
             .Wire(Span(root, -5f, 3f, 4f, 10f), 0, fold, 2)

             .Wire(fold, 0, field, 0)
             .Wire(fold, 1, field, 1)
             .Wire(boil, 0, field, 2)

             .Wire(fold, 0, bend, 0)
             .Wire(fold, 1, bend, 1)
             .Wire(field, 0, bend, 2)
             .Wire(Sum(breath, Times(firstArp, 0.15f, Gate)), 0, bend, 3)

             .Wire(bend, 0, bands, 0)
             .Wire(bend, 1, bands, 1)
             .Wire(Sum(Span(leadGate, 0f, 1f, 2.2f, 4.4f), Times(song, 1.6f)), 0, bands, 2)
             .Wire(drift, 0, bands, 3)
             .Wire(bands, 0, filament, 2);

            Box("Picture: Geometry");

            // --- the picture: color ----------------------------------------------

            // The lead moves the hue, a twelfth of the wheel to the semitone, so an
            // octave is the same color and a tune is a walk round it. The field and
            // the slowest clock go under it so a note is never quite the same color
            // twice, and the chorus starts from the other side of the wheel. Wrapped
            // rather than clamped, because a hue is a wheel.
            var hue = Through("math.fract", Sum(
                Sum(Times(leadStep, 1f / 12f), Times(field, 0.9f)),
                Sum(crawl, Times(theme, 0.45f))));

            // The bass's gate takes the color out of the image between its notes,
            // which is the same rhythm the ear is getting from it, and the snare takes
            // most of the rest: a backbeat is a flash of white.
            var saturation = Product(
                Span(bassGate, 0f, 1f, 0.55f, 0.95f),
                Span(snareGate, 0f, 1f, 1f, 0.3f));

            // The kick as brightness, and the hats as a flicker on top of it. Past one
            // on purpose, with the Clamp after it.
            var glow = Sum(Span(kickGate, 0f, 1f, 0.75f, 1.7f), Times(hatLevel, 0.35f));
            var visible = b.Add("math.clamp", (1, 0f), (2, 1f));
            var fresh = b.Add("color.hsv");

            b.Wire(Product(filament, glow), 0, visible, 0)
             .Wire(hue, 0, fresh, 0)
             .Wire(saturation, 0, fresh, 1)
             .Wire(visible, 0, fresh, 2);

            Box("Picture: Color");

            // --- the picture: feedback -------------------------------------------

            // Two readings of the last frame turning opposite ways, red from one and
            // green and blue from the other. That makes a chromatic tunnel, with no
            // lens anywhere in it.
            var inward = b.Add("space.transform", (TransformZoom, 1.035f));
            var pastIn = b.Add("feedback");
            var warm = b.Add("color.split");

            var outward = b.Add("space.transform", (TransformZoom, 0.972f), (TransformAngle, -0.016f));
            var pastOut = b.Add("feedback");
            var cool = b.Add("color.split");

            // How long a trail lasts is how much song there is.
            var ghost = b.Add("color.rgb");
            var trail = b.Add("color.gain", (2, 0f));

            // Max rather than a blend, for FeedbackTunnel's reason: a trail brighter
            // than the new frame keeps its brightness, which is what makes a streak
            // read as a streak rather than as a smeared copy.
            var combine = b.Add("math.max");

            b.Wire(Span(kickGate, 0f, 1f, 0.012f, 0.05f), 0, inward, TransformAngle)
             .Wire(inward, 0, pastIn, 0)
             .Wire(inward, 1, pastIn, 1)
             .Wire(pastIn, 0, warm, 0)

             .Wire(outward, 0, pastOut, 0)
             .Wire(outward, 1, pastOut, 1)
             .Wire(pastOut, 0, cool, 0)

             .Wire(warm, 0, ghost, 0)
             .Wire(cool, 1, ghost, 1)
             .Wire(cool, 2, ghost, 2)
             .Wire(ghost, 0, trail, 0)
             .Wire(Span(song, 0f, 1f, 0.78f, 0.9f), 0, trail, 1)

             .Wire(trail, 0, combine, 0)
             .Wire(fresh, 0, combine, 1)
             .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

            Box("Picture: Feedback");

            return b.Build();
        }
    }
}
