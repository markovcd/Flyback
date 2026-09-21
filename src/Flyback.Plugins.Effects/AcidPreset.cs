using Flyback.Core.Graph;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole acid techno track — a hundred and twenty-eight bars of a 303 line in two
/// themes, a galloping bass under it, a breakdown, three builds and a second 303 that
/// answers the first — and a picture driven by the same signals that drive the sound.
/// </summary>
/// <remarks>
/// The line is the instrument the music is named after, so it is built the way the
/// instrument is rather than the way a synth voice usually is. A step can be accented
/// and can slide into the next, and both are in the pattern rather than on a knob. The
/// filter is two Filters in a row, which is four poles. Its cutoff is counted in octaves
/// and raised to a power of two at the end, so the envelope sweeps the way an ear hears
/// a sweep. And an accent does not only open the filter further: it charges a Slew that
/// takes a quarter of a second to fall, so accents close together climb on each other's
/// backs, which is the wow a run of them makes.
/// <para>
/// The harmony is a root a bar, added to everything pitched before its Note, so a change
/// of chord moves the line, the bass and the pad in parallel. That is how the machine
/// itself transposes a pattern, and it is why no part here needs a scale.
/// </para>
/// </remarks>
internal sealed class AcidPreset : PresetBench
{
    public const string Name = "Acid";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string DriveType = NodeCatalog.DriveTypeId;

    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string DecayType = "flyback.voice.decay";

    private const string StrokeType = "flyback.voice.stroke";

    private const string SupersawType = "flyback.voice.osc";

    private const string FractalType = "flyback.picture.fractal";

    private const string PaletteType = "flyback.picture.palette";

    private const string PosteriseType = "flyback.picture.posterise";

    /// <summary>Where a Fractal keeps how many octaves it builds, and under what.</summary>
    private const string FractalState = "fractal";

    private const string OctaveField = "octaves";

    /// <summary>A Filter's third output: what is over the cutoff.</summary>
    private const int High = 2;

    /// <summary>
    /// How hard a step of the line is played. Both are over a half, which is where a
    /// Decay takes a gate to have opened, and an accent is told from a plain note by
    /// which side of the gap between them the gate is on.
    /// </summary>
    private const float Accent = 1f;

    private const float Plain = 0.7f;

    /// <summary>One step of a 303 pattern: a pitch, how hard, and whether it slides into the next.</summary>
    private readonly record struct Played(float Note, float Level, bool Slides = false);

    private static Played Hit(float note, bool slides = false) => new(note, Plain, slides);

    private static Played Hard(float note, bool slides = false) => new(note, Accent, slides);

    /// <summary>A rest keeps a pitch, so nothing jumps while it is silent.</summary>
    private static Played Rest(float note) => new(note, 0f);

    /// <summary>
    /// The first theme: a bar that sits on a low A and keeps leaving it — up an octave
    /// and sliding back, a flat second on the third beat, which is the note that makes
    /// it acid rather than blues.
    /// </summary>
    private static readonly Played[] First =
    [
        Hard(45f), Hit(45f), Hit(57f, slides: true), Hit(55f),
        Hard(45f), Rest(45f), Hit(48f), Hit(45f, slides: true),
        Hard(46f), Hit(45f), Hard(52f), Rest(52f),
        Hit(45f), Hit(43f, slides: true), Hard(45f), Hit(48f),
    ];

    /// <summary>
    /// The second: busier and higher, with no rest until the second beat is over and the
    /// flat second an octave up, slid into from the note under it.
    /// </summary>
    private static readonly Played[] Second =
    [
        Hard(45f), Hit(57f), Hit(45f), Hard(52f, slides: true),
        Hit(50f), Hit(45f), Hard(48f), Rest(48f),
        Hit(45f), Hard(57f, slides: true), Hit(58f), Hit(45f),
        Hard(55f), Hit(52f, slides: true), Hit(55f), Hit(45f),
    ];

    /// <summary>
    /// How many octaves the field builds. A choice on the node rather than a
    /// socket, so it is written as state — the shape the module's own helper
    /// writes, said here because this assembly cannot call it.
    /// </summary>
    private static NodeInstance Octaves(NodeInstance node, int count)
    {
        node.SetState(FractalState, new JsonObject
        {
            [OctaveField] = JsonValue.Create(count.ToString()),
        });

        return node;
    }

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new AcidPreset(modules).Assemble();
    }

    private AcidPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    /// <summary>A Mix between two sources, read at the same output of each.</summary>
    private NodeInstance Either(NodeInstance a, NodeInstance c, NodeInstance t, int from = 0)
    {
        var mix = b.Add("math.mix");
        b.Wire(a, from, mix, 0).Wire(c, from, mix, 1).Wire(t, 0, mix, 2);
        return mix;
    }

    /// <summary>Two to the power of a signal: octaves turned into a ratio.</summary>
    private NodeInstance Doublings(NodeInstance octaves)
    {
        var ratio = b.Add("math.pow", (0, 2f));
        b.Wire(octaves, 0, ratio, 1);
        return ratio;
    }

    /// <summary>
    /// One 303 pattern as the two Sequencers it takes: the notes, and the marks that say
    /// how they are joined.
    /// </summary>
    /// <remarks>
    /// A slide is two things a step apart. The step that slides holds its gate the whole
    /// way into the next, and the step it slides into arrives by glide and is not struck.
    /// So the marks carry both: a step's volume is whether it slides, and is wired to the
    /// notes' gate length, and its value is whether the step before it did — the same
    /// list turned by one, which is worked out here so that it is written once.
    /// <para>
    /// The marks' value is read rather than its gate, because a gate dips at every step
    /// and the glide is wanted exactly there.
    /// </para>
    /// </remarks>
    private (NodeInstance Notes, NodeInstance Marks) Pattern(NodeInstance beats, Played[] steps)
    {
        var notes = b.Add("seq.notes", (1, 4f), (3, 0f));
        StepsExtra.Set(notes, [.. steps.Select(s => new Step(s.Note, 1f, s.Level))]);

        var marks = b.Add("seq.values", (1, 4f), (2, 1f), (3, 0f));
        StepsExtra.Set(marks,
        [
            .. steps.Select((s, i) => new Step(
                steps[(i + steps.Length - 1) % steps.Length].Slides ? 1f : 0f,
                1f,
                s.Slides ? 1f : 0f)),
        ]);

        b.Wire(beats, 0, notes, 0)
         .Wire(beats, 0, marks, 0)
         .Wire(Span(marks, 0f, 1f, 0.55f, 1f, 1), 0, notes, 2);

        return (notes, marks);
    }

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        // A hundred and thirty a minute, which is where this music lives. Every part
        // reads the count of beats rather than the clock, so the tempo is this one knob.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 130f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        // Eight bars is one step of the arrangement.
        var phraseGone = Fraction(Times(beats, 1f / 32f));

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // One number for each eight bars saying how much track there is, and each part
        // decides below how much of it it needs. An intro and a build, four phrases of
        // the first drop, a phrase stripped back and a second build, the second drop, a
        // breakdown with no drums at all, a third build, three phrases of everything and
        // the way out. The builds are all at six tenths, which is how they are known.
        var song = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(song,
        [
            new Step(0.3f), new Step(0.6f), new Step(0.8f), new Step(0.8f),
            new Step(0.85f), new Step(0.85f), new Step(0.45f), new Step(0.6f),
            new Step(1f), new Step(1f), new Step(0.05f), new Step(0.6f),
            new Step(1f), new Step(1f), new Step(0.95f), new Step(0.3f),
        ]);

        // Which theme it is, a phrase at a time: the second for the back half of the
        // first drop, and from the breakdown through the build into the last drop, so
        // that the first theme coming back is the track coming home. A lane of its own,
        // and a switch, because a pattern half way between two patterns is neither.
        var theme = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(theme,
        [
            new Step(0f), new Step(0f), new Step(0f), new Step(0f),
            new Step(1f), new Step(1f), new Step(0f), new Step(0f),
            new Step(0f), new Step(0f), new Step(1f), new Step(1f),
            new Step(1f), new Step(0f), new Step(0f), new Step(0f),
        ]);

        // And the hand on the cutoff knob, which is the other half of how this music is
        // arranged: where it is left for each phrase. It gets there over six seconds and
        // comes back over three, in decades of a second, so it is turned rather than set.
        var knob = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(knob,
        [
            new Step(0.15f), new Step(0.4f), new Step(0.7f), new Step(0.85f),
            new Step(0.7f), new Step(0.9f), new Step(0.3f), new Step(0.5f),
            new Step(0.85f), new Step(1f), new Step(0.35f), new Step(0.5f),
            new Step(0.9f), new Step(1f), new Step(0.8f), new Step(0.2f),
        ]);
        var turned = b.Add(SlewType, (1, 0.78f), (2, 0.48f));

        // A build is a phrase at six tenths. Its second half is a ramp, which the riser,
        // the roll and the knob all climb, and its last bar has no kick and no bass in
        // it, so the drop lands on a bar of nothing.
        var atLeast = b.Add("math.step", (0, 0.57f));
        var atMost = b.Add("math.step", (1, 0.63f));
        var building = Product(atLeast, atMost);
        var ramp = Product(Rises(phraseGone, 0.5f, 1f), building);
        var lastBar = b.Add("math.step", (0, 0.875f));
        var hush = From(1f, Product(lastBar, building));

        // The knob with the ramp on it, and a little of a hand that is never quite still.
        var hand = Sum(Sum(turned, Times(ramp, 0.3f)), Wander(0.07f, 4.1f, -0.06f, 0.06f));

        // The harmony, a bar at a time, in semitones from A. The first theme stays home
        // for six bars and goes up to C and down to G to come back; the second rocks
        // between F and G and ends on E, which wants the A.
        var firstRoots = b.Add("seq.values", (1, 0.25f));
        StepsExtra.Set(firstRoots,
        [
            new Step(0f), new Step(0f), new Step(0f), new Step(0f),
            new Step(0f), new Step(0f), new Step(3f), new Step(-2f),
        ]);
        var secondRoots = b.Add("seq.values", (1, 0.25f));
        StepsExtra.Set(secondRoots,
        [
            new Step(-4f), new Step(-4f), new Step(-2f), new Step(-2f),
            new Step(-4f), new Step(-4f), new Step(-2f), new Step(-5f),
        ]);

        b.Wire(beats, 0, song, 0)
         .Wire(beats, 0, theme, 0)
         .Wire(beats, 0, knob, 0)
         .Wire(knob, 0, turned, 0)
         .Wire(song, 0, atLeast, 1)
         .Wire(song, 0, atMost, 0)
         .Wire(phraseGone, 0, lastBar, 1)
         .Wire(beats, 0, firstRoots, 0)
         .Wire(beats, 0, secondRoots, 0);

        var root = Either(firstRoots, secondRoots, theme);

        Box("Arrangement");

        // --- the kick --------------------------------------------------------

        // Four on the floor, from the first bar, and out for the breakdown. The pitch is
        // the level to the fifth power, so there is one envelope and the beater is over
        // long before the shell is.
        var kickStroke = Enters(Product(Stroke(beats, 1f, 5f), hush), song, 0.1f, 0.15f);
        var kick = Drum(kickStroke, 46f, 170f, 5f, 3f);

        // The sidechain: the kick's level on the bass and the pad.
        var duck = Ducking(kickStroke, 0.7f);

        Box("Kick");

        // --- the acid line ---------------------------------------------------

        // Both themes run the whole time and the lane chooses between them, so either
        // is in the right place in its bar whenever it is switched to.
        var (firstNotes, firstMarks) = Pattern(beats, First);
        var (secondNotes, secondMarks) = Pattern(beats, Second);
        var gate = Either(firstNotes, secondNotes, theme, 1);
        var slidInto = Either(firstMarks, secondMarks, theme);

        // The glide is on the frequency rather than on the note, because a Note snaps to
        // the semitone and a slide through semitones is a run. A millisecond where
        // there is no slide, which is no glide at all, and sixty where there is.
        var lineHz = Through("audio.note", Sum(Either(firstNotes, secondNotes, theme), root));
        var glideTime = Span(slidInto, 0f, 1f, -3f, -1.22f);
        var glide = b.Add(SlewType);
        var saw = b.Add("osc.saw", (3, 0.9f));

        // A step that was slid into is not struck: the filter's envelope carries on down
        // from the note before. It falls faster with the knob shut than open, an eighth
        // of a second to a third, which is the other knob a hand is on.
        var squelch = b.Add(DecayType, (1, -3f), (3, 0.4f));

        // The accent. Whether this step has one is which side of the gap its gate is on,
        // and what it adds is the envelope through a Slew that rises at once and takes a
        // quarter of a second to fall — so two accents close together start the second
        // from where the first had got to, and a run of them climbs.
        var push = b.Add(SlewType, (1, -2.3f), (2, -0.6f));

        // The cutoff in octaves over a hundred and eighty hertz: where the hand has the
        // knob, the envelope by as much as the knob allows it, and the accent on top.
        // Shut, a note peaks under a kilohertz and bubbles; open and accented it is past
        // anything a filter takes away, and what is heard is the saw and the Drive.
        var octaves = Sum(
            Sum(Span(hand, 0f, 1f, 0f, 3f), Product(squelch, Span(hand, 0f, 1f, 1.8f, 3f))),
            Times(push, 0.9f));
        var cutoff = Times(Doublings(octaves), 180f);

        // Four poles: a Filter with nearly no resonance into one with nearly all of it.
        // Twice the slope is what makes the sweep a gesture rather than a tone control,
        // and the ringing goes into the Drive after it, which is where the snarl is.
        var pole = b.Add(FilterType, (2, 0.1f));
        var ringing = b.Add(FilterType);

        // Nearly flat, which is the point: the machine's volume envelope barely moves
        // and everything heard happening to a note is the filter. A Slew on the gate
        // also rides over the dip between a sliding note and the next.
        var level = b.Add(SlewType, (1, -2.7f), (2, -1.5f));
        var drive = b.Add(DriveType);

        // And its 'high' taken from a third Filter, at a hundred and fifty hertz. The
        // machine has no bottom octave to speak of, and here that octave belongs to
        // the kick and the bass: a line that is all fundamental sits on both.
        var thinned = b.Add(FilterType, (1, 150f), (2, 0.1f));

        b.Wire(lineHz, 0, glide, 0)
         .Wire(glideTime, 0, glide, 1)
         .Wire(glideTime, 0, glide, 2)
         .Wire(glide, 0, saw, 1)
         .Wire(Product(gate, From(1f, slidInto)), 0, squelch, 0)
         .Wire(Span(hand, 0f, 1f, -0.9f, -0.45f), 0, squelch, 2)
         .Wire(Product(Rises(gate, 0.75f, 0.95f), squelch), 0, push, 0)
         .Wire(saw, 0, pole, 0)
         .Wire(cutoff, 0, pole, 1)
         .Wire(pole, 0, ringing, 0)
         .Wire(cutoff, 0, ringing, 1)
         .Wire(gate, 0, level, 0)
         .Wire(Product(ringing, level), 0, drive, 0)
         .Wire(drive, 0, thinned, 0);

        Box("Acid Line");

        // --- the answer ------------------------------------------------------

        // A second machine for the second drop and the last, set to its square wave, well
        // over an octave up, and playing in the gaps: nothing on the first beat, where the line
        // is loudest, and most of what it has to say at the end of the bar. One Filter
        // rather than two, so it is thinner than the line as well as higher.
        var answer = b.Add("seq.notes", (1, 4f), (2, 0.6f), (3, 0.02f));
        StepsExtra.Set(answer,
        [
            new Step(64f, 1f, 0f), new Step(64f, 1f, 0f), new Step(64f), new Step(64f, 1f, 0f),
            new Step(64f, 1f, 0f), new Step(67f, 1f, 0f), new Step(67f, 1f, Plain), new Step(69f, 1f, Plain),
            new Step(69f, 1f, 0f), new Step(72f, 1f, 0f), new Step(72f), new Step(69f, 1f, Plain),
            new Step(69f, 1f, 0f), new Step(67f, 1f, Plain), new Step(69f), new Step(69f, 1f, 0f),
        ]);
        var square = b.Add("osc.pulse", (3, 0.5f), (4, 0.8f));
        var chirp = b.Add(DecayType, (1, -3f), (2, -0.75f), (3, 0.7f));
        var answerTone = b.Add(FilterType, (2, 0.8f));
        var answerLevel = b.Add(SlewType, (1, -2.7f), (2, -1.5f));
        var answerDrive = b.Add(DriveType, (1, 3f));
        var answered = Enters(answerDrive, song, 0.9f, 0.94f);

        b.Wire(beats, 0, answer, 0)
         .Wire(Through("audio.note", Sum(answer, root)), 0, square, 1)
         .Wire(answer, 1, chirp, 0)
         .Wire(square, 0, answerTone, 0)
         .Wire(
             Times(Doublings(Sum(Times(chirp, 3f), Span(hand, 0f, 1f, 0f, 1.5f))), 300f),
             0, answerTone, 1)
         .Wire(answer, 1, answerLevel, 0)
         .Wire(Product(answerTone, answerLevel), 0, answerDrive, 0);

        Box("Answer");

        // --- the bass --------------------------------------------------------

        // The gallop: nothing where the kick is, and the two sixteenths after the
        // off-beat, with a pick-up and an octave at the end of the bar. The kick and
        // this are one rhythm shared out between two instruments.
        var bassLine = b.Add("seq.values", (1, 4f), (2, 0.8f), (3, 0.04f));
        StepsExtra.Set(bassLine,
        [
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0.75f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0.75f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0.75f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0.6f), new Step(0f), new Step(12f, 1f, 0.85f),
        ]);

        // An octave under the line, and a root that is under A is taken up an octave
        // instead: F and E an octave down are under what most speakers have, and a
        // bass that goes up for the second theme is a lift.
        var under = b.Add("math.step", (1, -1.5f));
        var bassHz = Through("audio.note", Sum(Sum(Plus(root, 33f), Times(under, 12f)), bassLine));

        // A saw through a Filter the same sixteenth plucks, for the part of a bass that
        // is heard, and how far the pluck opens it is the hand on the knob again.
        var bassSaw = b.Add("osc.saw", (3, 0.8f));
        var bassTone = b.Add(FilterType, (2, 0.35f));
        var bassGrit = b.Add(DriveType, (1, 3f));

        // And a sine at the same pitch for the part that is felt, added after the Drive
        // so that it stays a sine.
        var sub = b.Add("osc.sine", (3, 0.85f));
        var bass = Enters(
            Product(Product(Sum(bassGrit, Product(sub, bassLine, 1)), duck, DuckGain), hush), song, 0.4f, 0.45f);

        b.Wire(root, 0, under, 0)
         .Wire(beats, 0, bassLine, 0)
         .Wire(bassHz, 0, bassSaw, 1)
         .Wire(bassSaw, 0, bassTone, 0)
         .Wire(
             Plus(Product(Stroke(beats, 4f, 2.5f), Span(hand, 0f, 1f, 350f, 1100f)), 90f),
             0, bassTone, 1)
         .Wire(Product(bassTone, bassLine, 1), 0, bassGrit, 0)
         .Wire(bassHz, 0, sub, 1);

        Box("Bass");

        // --- the hats --------------------------------------------------------

        // Closed on every sixteenth, at four strengths that repeat every beat so the
        // off-beat leans, and open on the off-beat once the drop has come: half a beat
        // late and three times as long.
        var lean = b.Add("seq.values", (1, 4f));
        StepsExtra.Set(lean, [new Step(0.55f), new Step(0.3f), new Step(0.8f), new Step(0.4f)]);
        var shut = Enters(Product(Stroke(beats, 4f, 7f), lean), song, 0.2f, 0.25f);
        var open = Enters(Stroke(beats, 1f, 3f, 0.5f), song, 0.7f, 0.75f);
        var hats = Hiss(Sum(shut, Times(open, 0.7f)), 8000f, 0.2f, "high");

        b.Wire(beats, 0, lean, 0);

        Box("Hats");

        // --- the clap --------------------------------------------------------

        // Two and four: half the beat, offset by half a cycle. A clap is several hands
        // not quite together, so the first twentieth of the stroke is three short bursts
        // — thirty-two to the beat — and the tail is let through only after them.
        var backbeat = Stroke(beats, 0.5f, 12f, 0.5f);
        var after = Rises(backbeat, 0.045f, 0.05f, StrokePhase);
        var hands = Sum(Product(Stroke(beats, 32f, 1.5f), From(1f, after)), Product(backbeat, after));
        var clap = Hiss(Enters(hands, song, 0.55f, 0.58f), 1300f, 0.5f, "band", 3.5f, seed: 1f);

        Box("Clap");

        // --- the builds ------------------------------------------------------

        // A snare roll in sixteenths that doubles for the last bar, getting louder as the
        // square of the ramp, and a Hiss whose band climbs nearly five octaves under it.
        var rollRate = Plus(Times(lastBar, 4f), 4f);
        var roll = b.Add(StrokeType, (3, 2.5f));
        var snare = Hiss(Product(roll, Power(ramp, 2f)), 2200f, 0.35f, "band", 2.5f, seed: 2f);
        var riser = Hiss(ramp, 250f, 0.55f, "band", 1.4f, seed: 3f);

        // And what the drop lands on: one stroke to the phrase, steep enough that eight
        // bars of it is two seconds of cymbal, wherever there is a drop to mark.
        var crash = Hiss(
            Enters(Stroke(beats, 1f / 32f, 16f), song, 0.75f, 0.78f), 5000f, 0.1f, "high", 0.9f, seed: 4f);

        b.Wire(beats, 0, roll, 0)
         .Wire(rollRate, 0, roll, 1)
         .Wire(Span(ramp, 0f, 1f, 250f, 7000f), 0, riser, HissCutoff);

        var noises = Sum(Sum(snare, riser), crash);

        Box("Builds");

        // --- the pad ---------------------------------------------------------

        // The chord the line only implies, for the places the drums are thin enough to
        // hear it in: the intro, the breakdown and the way out. Seven detuned saws on the
        // root and a saw each on the minor third and the fifth, counted in semitones
        // rather than found in a scale, so the chord moves in parallel with everything
        // else. It follows the arrangement through a Slew, two seconds to go and half a
        // second to come, because a chord that cuts out is a mistake and one that
        // arrives with the silence is not.
        var padNote = Plus(root, 57f);
        var strings = b.Add(SupersawType, (2, 0.3f), (3, 0.8f), (5, 0.5f));
        var third = b.Add("osc.saw", (3, 0.3f));
        var fifth = b.Add("osc.saw", (3, 0.3f));
        var padTone = b.Add(FilterType, (2, 0.2f));
        var thin = b.Add(SlewType, (1, 0.3f), (2, -0.3f));

        // The Chorus is what makes it stereo: 'out' and 'wide' are swept in opposite
        // directions, which is wider than panning and costs one module.
        var pad = b.Add(ChorusModule.TypeId, (1, 0.2f), (2, 0.7f), (3, 0.6f));

        b.Wire(Through("audio.note", padNote), 0, strings, 1)
         .Wire(Through("audio.note", Plus(padNote, 3f)), 0, third, 1)
         .Wire(Through("audio.note", Plus(padNote, 7f)), 0, fifth, 1)
         .Wire(Sum(Sum(strings, third), fifth), 0, padTone, 0)
         .Wire(Span(hand, 0f, 1f, 1500f, 4000f), 0, padTone, 1)
         .Wire(song, 0, thin, 0)
         .Wire(Enters(Product(padTone, duck, DuckGain), thin, 0.5f, 0.35f), 0, pad, 0);

        Box("Pad");

        // --- the space -------------------------------------------------------

        // Three sixteenths on the left and two on the right, side by side rather than in
        // a row: each tap hears the two machines and repeats on its own, so the sides
        // drift apart and come back. How long they hang about is a Wander.
        var taps = Echo(Wired("math.add", thinned, Times(answered, 0.3f), High), beat, 3f, 2f, 0.32f, 0.32f, sideBySide: true);

        // One room on a send, for what should sound further off than the drums.
        var roomSend = b.Add("math.mixer", (1, 0.5f), (3, 0.6f), (5, 0.4f), (7, 0.15f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.8f), (2, 0.75f), (3, 1f));

        // The slow weather, which keeps the patch changing once the patterns have been
        // heard: the Drive after the filter from a purr to a snarl, and the resonance
        // from ringing to whistling. Never down to nothing — a 303 with no resonance is
        // not quiet, it is a different instrument.
        b.Wire(Wander(0.06f, 2.31f, 0.24f, 0.6f), 0, taps, EchoFeedback)
         .Wire(Wander(0.09f, 0.29f, 2.2f, 6.5f), 0, drive, 1)
         .Wire(Wander(0.04f, 1.7f, 0.6f, 0.93f), 0, ringing, 2)
         .Wire(clap, 0, roomSend, 0)
         .Wire(pad, 0, roomSend, 2)
         .Wire(answered, 0, roomSend, 4)
         .Wire(thinned, High, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        // Two Desks of four, chained by their buses into one of eight. Left and right
        // differ in which echo tap and which side of the Chorus and the Reverb they
        // carry, and in how the hats and the clap lean: half again on the right for the
        // one and the reverse for the other. A Desk has one level for both sides of a
        // channel, so the lean is on the signal.
        var drums = b.Add(DeskType);

        // The last in the chain is the master: a trim under unity, and rails that
        // should never be reached.
        var music = b.Add(DeskType, (DeskTrim, 0.55f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        Channel(drums, 1, 0.75f, kick);
        Channel(drums, 2, 0.5f, hats, Times(hats, 1.45f));
        Channel(drums, 3, 0.6f, clap, Times(clap, 0.62f));
        Channel(drums, 4, 0.4f, noises);

        Channel(music, 1, 0.75f, bass);
        Channel(music, 2, 1f, taps, taps, rightFrom: EchoRight);
        Channel(music, 3, 0.45f, pad, pad, rightFrom: 1);
        Channel(music, 4, 0.5f, room, room, rightFrom: 1);

        b.Wire(Chained(drums, music), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(music, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: geometry -------------------------------------------

        // One clock read at three speeds: seconds are seconds, and what differs between
        // these is only how much of them each part wants.
        var spin = Times(clock, 0.04f);
        var boil = Times(clock, 0.22f);
        var crawl = Times(clock, 0.015f);

        // x and y take no wire anywhere in this chain: each is normalled to
        // Coordinates, so it reads the pixel's own position (ADR-0050). The kick moves
        // the light, and it is doing three things — the zoom here, the brightness, and
        // the twist on the feedback — so the breakdown is the picture holding still.
        var placed = TurnedThenZoomed();

        // And the sequencer moves the frame: where the line has got to in its bar is
        // how many wedges the fold has, so the picture rebuilds itself once a bar
        // rather than once a note.
        var fold = b.Add("space.kaleidoscope");

        // Read from the folded plane rather than the flat one, so the field is
        // itself symmetric — warping by anything asymmetric here would quietly
        // undo the fold and leave the picture looking like ordinary noise.
        // Five octaves, because detail is the whole of what is being looked at.
        var field = Octaves(b.Add(FractalType, (3, 2.4f), (4, 0.55f)), 5);

        // The hand on the knob again, and this is the wire the preset is built round:
        // the same signal that opens the filter opens the warp and, below, the palette.
        var bend = b.Add("space.warp");

        // The line's gate widens the rings, so a sixteenth arrives as a band rather
        // than only as a change of color. Rings are a sine, so most of the frame is
        // dark and only the crests survive as filaments.
        var bands = b.Add("pattern.rings");
        var filament = Rises(bands, 0.2f, 0.9f);

        b.Wire(spin, 0, placed, TransformAngle)
         .Wire(Span(kickStroke, 0f, 1f, 0.98f, 1.22f), 0, placed, TransformZoom)

         .Wire(placed, 0, fold, 0)
         .Wire(placed, 1, fold, 1)
         .Wire(Span(firstNotes, 0f, 1f, 3f, 9f, 2), 0, fold, 2)

         .Wire(fold, 0, field, 0)
         .Wire(fold, 1, field, 1)
         .Wire(boil, 0, field, 2)

         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(field, 1, bend, 2)
         .Wire(Span(hand, 0f, 1f, 0.15f, 0.6f), 0, bend, 3)

         .Wire(bend, 0, bands, 0)
         .Wire(bend, 1, bands, 1)
         .Wire(Span(firstNotes, 0f, 1f, 2.2f, 5.5f, 1), 0, bands, 2)
         .Wire(crawl, 0, bands, 3);

        Box("Picture: Geometry");

        // --- the picture: color ----------------------------------------------

        // A Palette rather than an HSV hue, which is the difference between a handful
        // of colors that go together and every color there is. Where in it to look is
        // the field plus the slowest of the three clocks, wrapped rather than clamped
        // because a palette is a loop — and moved most of half way round it by the
        // second theme, so a change of theme is a change of color.
        var where = Fraction(Sum(Sum(Times(field, 0.7f), crawl), Times(theme, 0.4f)));

        // And how wide the palette is comes off the knob, which is the correspondence
        // the whole patch is arranged around: a hum is tints of one color, and a
        // filter screaming is a full spectrum.
        var palette = b.Add(PaletteType, (1, 2f), (3, 0.5f), (4, 0.55f));

        // The kick again, as brightness. Past one on purpose, with the Clamp after
        // it: a value past one is not brighter, it is only wrong.
        var visible = b.Add("math.clamp", (1, 0f), (2, 1f));
        var inked = b.Add("color.gain", (2, 0f));

        // Bands rather than a gradient, because techno is a hard-edged music. The
        // count comes off the arrangement, so the sections are visible as well as
        // audible.
        var flat = b.Add(PosteriseType);

        b.Wire(where, 0, palette, 0)
         .Wire(Span(hand, 0f, 1f, 0.06f, 0.42f), 0, palette, 2)
         .Wire(Product(filament, Span(kickStroke, 0f, 1f, 0.6f, 1.7f)), 0, visible, 0)
         .Wire(palette, 0, inked, 0)
         .Wire(visible, 0, inked, 1)
         .Wire(inked, 0, flat, 0)
         .Wire(Span(song, 0f, 1f, 5f, 26f), 0, flat, 1);

        Box("Picture: Color");

        // --- the picture: feedback -------------------------------------------

        // The last frame, zoomed in a hair and turned by an amount the kick sets, so
        // the trail lurches on the beat rather than drifting evenly.
        var trail = b.Add(TrailsType, (TrailsZoom, 1.03f), (TrailsPersist, 0.88f));

        b.Wire(Span(kickStroke, 0f, 1f, 0.01f, 0.045f), 0, trail, TrailsAngle)
         .Wire(flat, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Feedback");

        return b.Build();
    }
}
