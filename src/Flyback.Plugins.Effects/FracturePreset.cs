using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Drum and bass at a hundred and seventy, turning into IDM: a break that is
/// synthesized rather than sampled and then chopped anyway, over a Reese bass, and a
/// picture that is cut into strips by the same list that cuts the drums.
/// </summary>
/// <remarks>
/// A sampled break is chopped by playing its slices out of order. There is no sample
/// here, but there is the thing a sample is read by: every drum is a Stroke and a
/// Sequencer, neither has a memory, and both follow whatever is on their 'in'. So the
/// kit does not read the count of beats. It reads a second count made from it — which
/// eighth of the bar, then how far through that eighth — and the chopping is done to
/// that: a list says which eighth plays instead, a die multiplies the fraction so the
/// slice repeats inside itself, and now and then the fraction runs backwards, which
/// turns every envelope in the kit round and is a reversed drum. One bent clock, and
/// the whole break goes with it.
/// <para>
/// The picture reads the same three numbers. The frame is eight strips for the eight
/// slices, and they slide apart by how far the list has moved the slice from where it
/// belongs, so the drums being whole is the picture being whole.
/// </para>
/// <para>
/// There are two themes and the track is the first, the second, and the first again.
/// The first is a bass that sits on F and falls away from it, under bells a die
/// plays. The second turns that over: a bass that climbs through the scale's three
/// major chords, and a slow written tune sung over it in place of the bells.
/// </para>
/// </remarks>
internal sealed class FracturePreset : PresetBench
{
    public const string Name = "Fracture";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string SupersawType = "flyback.voice.osc";

    private const string EuclidType = "flyback.voice.euclid";

    private const string RandomType = "flyback.voice.random";

    private const string SlewType = "flyback.voice.slew";

    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string BoxType = "flyback.picture.box";

    private const string PolygonType = "flyback.picture.polygon";

    private const string FillType = "flyback.picture.fill";

    private const string PosteriseType = "flyback.picture.posterise";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>A Random's third output: a new value 'rate' times across its domain, held.</summary>
    private const int Held = 2;

    /// <summary>F natural minor, written from its root: F, G, A flat, B flat, C, D flat and E flat.</summary>
    private static readonly int[] Scale = [5, 7, 8, 10, 0, 1, 3];

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new FracturePreset(modules).Assemble();
    }

    private FracturePreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        // A hundred and seventy a minute, and the count of beats everything that is
        // not a drum reads.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 170f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        // Eight bars is one step of the arrangement.
        var phraseGone = Fraction(Times(beats, 1f / 32f));

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // One number for each eight bars saying how much track there is: two of intro,
        // two of build, the drop played straight, the same drop chopped, a breakdown
        // and a build, then four of everything before it thins out. The third quarter —
        // the breakdown, the build and the first phrase back at full — is the second
        // theme, and the last quarter is the first one come home.
        var song = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(song,
        [
            new Step(0.15f), new Step(0.3f), new Step(0.45f), new Step(0.6f),
            new Step(0.8f), new Step(0.8f), new Step(0.9f), new Step(0.9f),
            new Step(0.2f), new Step(0.5f), new Step(0.6f), new Step(0.95f),
            new Step(1f), new Step(1f), new Step(0.85f), new Step(0.25f),
        ]);

        // The same number for what fades rather than enters: four seconds up and two
        // down, in decades of a second. On the screen a Slew is a wire.
        var swell = b.Add(SlewType, (1, 0.60206f), (2, 0.30103f));

        // Whether the break is being chopped: a switch, thrown on the phrase, because
        // half way between two places in a bar is not a place.
        var chopped = b.Add("math.step", (0, 0.87f));

        // The last of every four phrases ends in a riser, read off the sequencer's own
        // index: four times it has a fraction of three quarters exactly there. The very
        // last is the outro, which is masked out.
        var turning = b.Add("math.step", (0, 0.7f));
        var notTheEnd = b.Add("math.step", (1, 0.9f));
        var ramp = Product(Rises(phraseGone, 0.5f, 1f), Product(turning, notTheEnd));

        // Whether it is the second theme: from half way through the list to three
        // quarters, read off the same index. A switch, like the chop, because a bass
        // line half way between two tunes is neither.
        var pastHalf = b.Add("math.step", (0, 0.47f));
        var shortOfLast = b.Add("math.step", (1, 0.72f));
        var second = Product(pastHalf, shortOfLast);

        b.Wire(beats, 0, song, 0)
         .Wire(song, 0, swell, 0)
         .Wire(song, 0, chopped, 1)
         .Wire(Fraction(Times(song, 4f, 2)), 0, turning, 1)
         .Wire(song, 2, notTheEnd, 0)
         .Wire(song, 2, pastHalf, 1)
         .Wire(song, 2, shortOfLast, 0);

        Box("Arrangement");

        // --- the cut ---------------------------------------------------------

        // One die an eighth. Most of what it throws changes nothing: the top quarter of
        // its range is a roll — the slice played two, three or four times inside itself —
        // and the bottom sixth is a slice played backwards.
        var die = b.Add(RandomType, (1, 2f), (2, 3f));
        var rolls = Plus(
            Product(Plus(Knobbed("math.max", Floor(Span(die, 0.45f, 1f, 1f, 4.99f, Held)), 1f), -1f), chopped),
            1f);
        var backwards = b.Add("math.step", (1, -0.7f));
        var reversed = Product(backwards, chopped);

        // Which eighth of the bar it is, and the list of which eighth is played instead:
        // two bars that start as they should and come apart. Left unchopped the slice is
        // its own number, which is the break as written.
        var eighths = Times(beats, 2f);
        var straight = Floor(Knobbed("math.mod", eighths, 8f));
        var cuts = b.Add("seq.values", (1, 2f));
        StepsExtra.Set(cuts,
        [
            new Step(0f), new Step(1f), new Step(2f), new Step(3f),
            new Step(4f), new Step(5f), new Step(2f), new Step(3f),
            new Step(0f), new Step(1f), new Step(0f), new Step(1f),
            new Step(4f), new Step(2f), new Step(6f), new Step(7f),
        ]);
        var slice = b.Add("math.mix");

        // How far through the slice, multiplied by the roll and wrapped, and turned over
        // where the die said backwards.
        var through = Fraction(Product(Fraction(eighths), rolls));
        var within = b.Add("math.mix");

        // The kit's clock: the slice and the place in it, back in beats.
        var drumBeats = Times(Sum(slice, within), 0.5f);

        // How far the list has moved this slice from where it belongs, which is what the
        // picture slides its strips by.
        var moved = Product(Less(cuts, straight), chopped);

        b.Wire(beats, 0, die, 0)
         .Wire(die, Held, backwards, 0)
         .Wire(beats, 0, cuts, 0)
         .Wire(straight, 0, slice, 0)
         .Wire(cuts, 0, slice, 1)
         .Wire(chopped, 0, slice, 2)
         .Wire(through, 0, within, 0)
         .Wire(From(1f, through), 0, within, 1)
         .Wire(reversed, 0, within, 2);

        Box("Cut");

        // --- the break -------------------------------------------------------

        // One bar of sixteenths, and one list for two drums: a step's value is how hard
        // the kick is hit there and its volume is how hard the snare is, so the pattern
        // is written once and cannot disagree with itself. Kick, kick, snare, the ghost
        // notes either side of the third beat, two kicks and the snare again.
        var pattern = b.Add("seq.values", (1, 4f), (2, 1f));
        StepsExtra.Set(pattern,
        [
            new Step(1f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0.8f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 1f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.35f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0.35f), new Step(1f, 1f, 0f), new Step(0.7f, 1f, 0f),
            new Step(0f, 1f, 1f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.4f),
        ]);


        // A sixteenth at this tempo is under ninety milliseconds, so the fall is left
        // nearly straight: a steeper one is over before the drum has a body. The click at
        // the front is the Drum's own, whose pitch falls by a power of the level.
        var kickStroke = Enters(Product(Stroke(drumBeats, 4f, 1.3f), pattern), song, 0.42f, 0.48f);
        var kick = Drum(kickStroke, 55f, 240f, 5f, 4f);

        // A Drum for the shell, and white noise for the wires: the band round two
        // kilohertz for the crack and what is over it for the air.
        var snareStroke = Enters(Product(Stroke(drumBeats, 4f, 1.6f), pattern, 1), song, 0.42f, 0.48f);
        var noise = b.Add(RandomType, (2, 1f));
        var rattle = b.Add(FilterType, (1, 2100f), (2, 0.3f));
        var wires = Sum(Times(rattle, 3.5f, 1), Times(rattle, 1.2f, 2));
        var snare = Sum(Drum(snareStroke, 200f, 170f, 4f, 3f), Product(snareStroke, wires));

        // Eleven sixteenths in sixteen, off the same bent clock, so a roll rolls the hats
        // too.
        var hatHits = b.Add(EuclidType, (1, 4f), (2, 16f), (3, 11f));
        var hatStroke = Enters(Product(Stroke(drumBeats, 4f, 4f), hatHits, 1), song, 0.25f, 0.3f);
        var sizzle = b.Add(FilterType, (1, 8500f), (2, 0.2f));
        var hats = Times(Product(hatStroke, sizzle, 2), 0.8f);

        // And the other thing done to a break: a Sample and Hold clocked at five
        // kilohertz is a sampler with not enough of them. Only while the die is rolling.
        // The kit through one Drive, which brings the quiet parts up while the loud ones
        // stop moving: the ghost notes and the hats arrive at the level of the hits, and
        // that is most of what a break sounds like.
        var kit = b.Add(DriveType, (1, 3f));
        var crush = Product(Rises(die, 0.2f, 0.5f, Held), chopped);
        var ticks = b.Add("osc.square");
        var coarse = b.Add(NodeCatalog.HoldTypeId);
        var drums = b.Add("math.mix");

        b.Wire(drumBeats, 0, pattern, 0)
         .Wire(noise, 0, rattle, 0)
         .Wire(drumBeats, 0, hatHits, 0)
         .Wire(noise, 0, sizzle, 0)
         .Wire(b.Add("audio.frequency", (0, 5200f)), 0, ticks, 1)
         .Wire(Sum(Sum(kick, snare), hats), 0, kit, 0)
         .Wire(kit, 0, coarse, 0)
         .Wire(ticks, 0, coarse, 1)
         .Wire(kit, 0, drums, 0)
         .Wire(coarse, 0, drums, 1)
         .Wire(crush, 0, drums, 2);

        Box("Break");

        // --- the bass --------------------------------------------------------

        // A note to two beats over eight bars: F, F, A flat, and down through E flat to
        // D flat and back. It sits on F and keeps falling away from it, which is the
        // first theme: minor, and going nowhere.
        var firstLine = b.Add("seq.notes", (1, 0.5f));
        StepsExtra.Set(firstLine,
        [
            new Step(29f), new Step(29f), new Step(29f), new Step(29f),
            new Step(29f), new Step(29f), new Step(32f), new Step(32f),
            new Step(27f), new Step(27f), new Step(27f), new Step(27f),
            new Step(25f), new Step(25f), new Step(27f), new Step(27f),
        ]);

        // The second theme's is the other way up. It starts where the first one ended,
        // on D flat, and climbs — D flat, E flat, A flat — through the three major chords
        // the same scale has, to a bar on C that wants the F back. The pad is built on
        // whichever line this is, so the harmony changes with it and nothing else has
        // to be told.
        var secondLine = b.Add("seq.notes", (1, 0.5f));
        StepsExtra.Set(secondLine,
        [
            new Step(25f), new Step(25f), new Step(25f), new Step(25f),
            new Step(27f), new Step(27f), new Step(27f), new Step(27f),
            new Step(32f), new Step(32f), new Step(32f), new Step(32f),
            new Step(24f), new Step(24f), new Step(24f), new Step(36f),
        ]);
        var bassLine = b.Add("math.mix");

        // Thirty milliseconds of glide, because a Reese slides.
        var glide = b.Add(SlewType, (1, -1.5f), (2, -1.5f));
        var bassHz = Through("audio.note", glide);

        // The Reese itself: two saws a little over one per cent apart, which beat against
        // each other slowly enough to hear as movement, a Filter that a Wander opens and
        // closes, and a Phaser to keep the beating from ever settling.
        var sawLow = b.Add("osc.saw", (3, 0.6f));
        var sawHigh = b.Add("osc.saw", (3, 0.6f));
        var bassOpen = Wander(0.3f, 5f, 250f, 1400f);
        var bassTone = b.Add(FilterType, (2, 0.35f));
        var swirl = b.Add(PhaserModule.TypeId, (1, 0.15f), (2, 0.8f), (3, 0.5f), (4, 0.6f));
        var grit = b.Add(DriveType, (1, 2.5f));

        // The sub stays a sine, and the kick ducks both.
        var sub = b.Add("osc.sine", (3, 0.45f));
        var bass = Enters(
            Product(Sum(grit, sub), Span(kickStroke, 0f, 1f, 1f, 0.4f)), song, 0.62f, 0.7f);

        b.Wire(beats, 0, firstLine, 0)
         .Wire(beats, 0, secondLine, 0)
         .Wire(firstLine, 0, bassLine, 0)
         .Wire(secondLine, 0, bassLine, 1)
         .Wire(second, 0, bassLine, 2)
         .Wire(bassLine, 0, glide, 0)
         .Wire(Times(bassHz, 2f), 0, sawLow, 1)
         .Wire(Times(bassHz, 2.025f), 0, sawHigh, 1)
         .Wire(Sum(sawLow, sawHigh), 0, bassTone, 0)
         .Wire(bassOpen, 0, bassTone, 1)
         .Wire(bassTone, 0, swirl, 0)
         .Wire(swirl, 0, grit, 0)
         .Wire(bassHz, 0, sub, 1);

        Box("Bass");

        // --- the pad ---------------------------------------------------------

        // The chord over the bass, three octaves up: the root on seven detuned saws, and
        // a third and a seventh that are asked for between the notes and snapped to the
        // scale, so they are minor or major as the scale has them.
        var padNote = Plus(bassLine, 36f);
        var strings = b.Add(SupersawType, (2, 0.3f), (3, 0.8f), (5, 0.45f));
        var third = b.Add("osc.triangle", (3, 0.4f));
        var seventh = b.Add("osc.triangle", (3, 0.3f));
        var padTone = b.Add(FilterType, (2, 0.2f));
        var pad = b.Add(ChorusModule.TypeId, (1, 0.17f), (2, 0.7f), (3, 0.6f));

        b.Wire(Through("audio.note", padNote), 0, strings, 1)
         .Wire(Through("audio.note", Snapped(Plus(padNote, 3.6f))), 0, third, 1)
         .Wire(Through("audio.note", Snapped(Plus(padNote, 10.4f))), 0, seventh, 1)
         .Wire(Sum(Sum(strings, third), seventh), 0, padTone, 0)
         .Wire(Wander(0.07f, 1f, 600f, 2600f), 0, padTone, 1)
         .Wire(Product(padTone, Span(swell, 0f, 1f, 1f, 0.5f)), 0, pad, 0);

        Box("Pad");

        // --- the bells -------------------------------------------------------

        // A tune nobody wrote: a second die thrown on every sixteenth, an octave either
        // side of a high F, snapped to the scale. How many sixteenths of the bar sound is
        // a Wander, and a Euclid spreads whatever number arrives evenly again. They
        // belong to the first theme, and stop for the second.
        var notes = b.Add(RandomType, (1, 4f), (2, 9f));
        var bellHits = b.Add(EuclidType, (1, 4f), (2, 16f));
        var bellStroke = Product(Product(Stroke(beats, 4f, 4f), bellHits, 1), From(1f, second));
        var bells = Bell(
            Through("audio.note", Snapped(Plus(Times(notes, 12f, Held), 77f))), bellStroke, 3.5f, 0.3f);

        b.Wire(beats, 0, notes, 0)
         .Wire(beats, 0, bellHits, 0)
         .Wire(Wander(0.1f, 2f, 3f, 9f), 0, bellHits, 3);

        Box("Bells");

        // --- the lead --------------------------------------------------------

        // The second theme's tune, which is everything the bells are not: written down,
        // sung rather than struck, and a note to the beat where they are four — eight
        // bars in one breath over the climbing bass. It rises a third and a fifth through
        // each chord and comes down by step, and its last bar walks up the scale into
        // the F the first theme starts on. The rests hold their pitch, so the glide slides
        // between the notes either side of a gap.
        var leadLine = b.Add("seq.notes", (1, 1f), (2, 0.9f), (3, 0.06f));
        StepsExtra.Set(leadLine,
        [
            new Step(77f), new Step(77f, 1f, 0f), new Step(80f), new Step(80f, 1f, 0f),
            new Step(84f), new Step(84f, 1f, 0f), new Step(82f), new Step(80f),
            new Step(79f), new Step(79f, 1f, 0f), new Step(82f), new Step(82f, 1f, 0f),
            new Step(87f), new Step(87f, 1f, 0f), new Step(85f), new Step(82f),
            new Step(84f), new Step(84f, 1f, 0f), new Step(84f, 1f, 0f), new Step(82f),
            new Step(80f), new Step(80f, 1f, 0f), new Step(75f), new Step(75f, 1f, 0f),
            new Step(79f), new Step(79f, 1f, 0f), new Step(79f, 1f, 0f), new Step(75f),
            new Step(79f), new Step(80f), new Step(82f), new Step(84f),
        ]);

        // Sixty milliseconds of glide, a triangle with a sine an octave over it, a
        // vibrato that leans on the phase, and a tongue slow enough to be a breath.
        var leadGlide = b.Add(SlewType, (1, -1.2f), (2, -1.2f));
        var leadHz = Through("audio.note", leadGlide);
        var vibrato = b.Add("osc.sine", (1, 5.2f), (3, 0.12f));
        var voice = b.Add("osc.triangle", (3, 0.8f));
        var over = b.Add("osc.sine", (3, 0.25f));
        var tongue = b.Add(SlewType, (1, -2.1f), (2, -0.8f));
        var leadStroke = Product(tongue, second);
        var lead = Product(Sum(voice, over), leadStroke);

        b.Wire(beats, 0, leadLine, 0)
         .Wire(leadLine, 0, leadGlide, 0)
         .Wire(leadHz, 0, voice, 1)
         .Wire(vibrato, 0, voice, 2)
         .Wire(Times(leadHz, 2f), 0, over, 1)
         .Wire(leadLine, 1, tongue, 0);

        Box("Lead");

        // --- the space -------------------------------------------------------

        // The riser: the noise through a band that climbs nearly five octaves in four
        // bars. The ramp that moves the band also opens it.
        var sweep = b.Add(FilterType, (2, 0.55f));
        var riser = Times(Product(ramp, sweep, 1), 1.4f);

        // A dotted eighth on the left and the beat after it on the right, worked out from
        // the tempo, and one hall for what should sound far away.
        var sixteenths = Times(beat, 4f);
        var dotted = b.Add("math.div", (0, 3f));
        var straightTime = b.Add("math.div", (0, 2f));
        var tapL = b.Add(DelayModule.TypeId, (2, 0.5f), (3, 1f));
        var tapR = b.Add(DelayModule.TypeId, (2, 0f), (3, 1f));
        var roomSend = b.Add("math.mixer", (1, 0.6f), (3, 0.5f), (5, 0.25f), (7, 0.7f));
        var room = b.Add(ReverbModule.TypeId, (1, 0.85f), (2, 0.8f), (3, 1f));

        b.Wire(noise, 0, sweep, 0)
         .Wire(Span(ramp, 0f, 1f, 250f, 7000f), 0, sweep, 1)
         .Wire(sixteenths, 0, dotted, 1)
         .Wire(sixteenths, 0, straightTime, 1)
         .Wire(Sum(bells, Times(lead, 0.6f)), 0, tapL, 0)
         .Wire(dotted, 0, tapL, 1)
         .Wire(tapL, 0, tapR, 0)
         .Wire(straightTime, 0, tapR, 1)
         .Wire(bells, 0, roomSend, 0)
         .Wire(pad, 0, roomSend, 2)
         .Wire(snare, 0, roomSend, 4)
         .Wire(lead, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        // Two Desks of four, chained by their buses into one of eight. Left and right
        // differ in which echo tap and which side of the Chorus and the Reverb they carry.
        var rhythm = b.Add(DeskType);
        var air = b.Add(DeskType, (DeskTrim, 0.34f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        Channel(rhythm, 1, 1f, drums);
        Channel(rhythm, 2, 0.4f, bass);
        Channel(rhythm, 3, 0.4f, pad, pad, rightFrom: 1);
        Channel(rhythm, 4, 0.4f, riser);
        Channel(air, 1, 0.4f, bells);
        Channel(air, 2, 0.4f, tapL, tapR);
        Channel(air, 3, 0.45f, room, room, rightFrom: 1);
        Channel(air, 4, 0.5f, lead);

        var master = Chained(rhythm, air);

        b.Wire(master, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(master, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: strips ---------------------------------------------

        // The frame in eight strips, one a slice. Each slides sideways by how far the
        // list has moved the slice playing — a different way for each strip, off a sine
        // of its number — so while the break is whole the picture is, and when the break
        // jumps the picture is cut where the bar is. The bass leans on the same axis: a
        // ripple down the frame as deep as its Filter is open.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var strip = Floor(Times(Plus(coord, 1f, 1), 4f));
        var farMoved = b.Add("math.clamp", (1, -2f), (2, 2f));
        var slide = Product(Times(Sine(Plus(Times(strip, 2.4f), 1f)), 0.12f), farMoved);
        var ripple = Product(
            Sine(Sum(Times(coord, 9f, 1), Times(beats, 2f))),
            Enters(Span(bassOpen, 250f, 1400f, 0f, 0.03f), song, 0.62f, 0.7f));
        var cutX = Sum(Sum(slide, ripple), coord);

        // A roll is the picture as many times smaller, and the kick pushes it in.
        var plane = b.Add("space.scale");

        b.Wire(moved, 0, farMoved, 0)
         .Wire(cutX, 0, plane, 0)
         .Wire(coord, 1, plane, 1)
         .Wire(Product(rolls, Span(kickStroke, 0f, 1f, 1f, 0.9f)), 0, plane, 2);

        Box("Picture: Strips");

        // --- the picture: tunnel ---------------------------------------------

        // Two tunnels, one of squares and one of triangles, turning against each other.
        // The fraction of a distance is bands of it; each is cut to black and white and
        // the two are compared, so what shows is where they differ. Both run outwards
        // with the beat.
        var squares = Tunnel(true, 0.04f, 3f, 0.25f);
        var triangles = Tunnel(false, -0.07f, 2.2f, 0.125f);
        var differ = Size(Less(squares, triangles));

        // A slice played backwards is the picture turned inside out.
        var facing = b.Add("math.mix");

        // White, and the snare's orange, as bright as the kick says. The second theme
        // is seen as well as heard: the white goes lilac for as long as it lasts.
        var white = b.Add("color.mix");
        var paper = b.Add("color.rgb", (0, 0.92f), (1, 0.94f), (2, 0.96f));
        var lilac = b.Add("color.rgb", (0, 0.72f), (1, 0.62f), (2, 1f));
        var orange = b.Add("color.rgb", (0, 1f), (1, 0.35f), (2, 0.05f));
        var struck = b.Add("color.mix");
        var tunnel = b.Add("color.gain");

        b.Wire(differ, 0, facing, 0)
         .Wire(From(1f, differ), 0, facing, 1)
         .Wire(reversed, 0, facing, 2)
         .Wire(paper, 0, white, 0)
         .Wire(lilac, 0, white, 1)
         .Wire(second, 0, white, 2)
         .Wire(white, 0, struck, 0)
         .Wire(orange, 0, struck, 1)
         .Wire(snareStroke, 0, struck, 2)
         .Wire(struck, 0, tunnel, 0)
         .Wire(Product(facing, Span(kickStroke, 0f, 1f, 0.6f, 1.15f)), 0, tunnel, 1);

        Box("Picture: Tunnel");

        // --- the picture: sparks ---------------------------------------------

        // The bells are squares on a grid of four to the unit, and so is the lead while
        // it has their place. Each cell has a number of
        // its own — a sine of where it is, scattered — the die that chose the note is
        // added to it, and the top seventh of what results lights: a different handful of
        // cells for every note.
        var cellX = Times(coord, 4f);
        var cellY = Times(coord, 4f, 1);
        var scatter = Fraction(Times(
            Sine(Sum(Times(Floor(cellX), 12.9898f), Times(Floor(cellY), 78.233f))), 43758.5f));
        var chosen = b.Add("math.step", (0, 0.85f));
        var tile = b.Add(BoxType, (2, 0.3f), (3, 0.3f), (4, 0f));
        var tileFill = b.Add(FillType, (1, 0.02f));
        var green = b.Add("color.rgb", (0, 0.3f), (1, 1f), (2, 0.5f));
        var sparks = b.Add("color.gain");

        // And the hats are grain: a Random read across the frame instead of along the
        // clock, which is a different speck at every pixel and a new set every frame.
        var speck = b.Add(RandomType, (1, 64f), (2, 6f));
        var grain = Product(Times(Size(speck), 0.22f), hatStroke);

        b.Wire(Fraction(Sum(Sum(scatter, Times(notes, 3.7f, Held)), Times(leadLine, 0.37f))), 0, chosen, 1)
         .Wire(Plus(Fraction(cellX), -0.5f), 0, tile, 0)
         .Wire(Plus(Fraction(cellY), -0.5f), 0, tile, 1)
         .Wire(tile, 0, tileFill, 0)
         .Wire(green, 0, sparks, 0)
         .Wire(Product(Product(tileFill, chosen), Sum(bellStroke, leadStroke)), 0, sparks, 1)
         .Wire(Sum(Sum(Times(coord, 97.3f), Times(coord, 413.7f, 1)), Times(clock, 60f)), 0, speck, 0);

        Box("Picture: Sparks");

        // --- the picture: print ----------------------------------------------

        // A short trail, smeared sideways. While the drums are crushed the picture is
        // too: a Posterise is the same thing done to a color as the Sample and Hold does
        // to a sound, fewer levels instead of fewer moments.
        var trailed = b.Add(TrailsType, (TrailsZoom, 0.99f), (TrailsDx, 0.004f));
        var banded = b.Add(PosteriseType);

        // Darkened at the corners, and graded by the section last.
        var vignette = b.Add("math.clamp", (1, 0f), (2, 1f));
        var shaded = b.Add("color.gain");
        var graded = b.Add(GradeType, (2, 1.1f));

        b.Wire(Sum(Sum(tunnel, sparks), grain), 0, trailed, 0)
         .Wire(Span(song, 0f, 1f, 0.85f, 0.6f), 0, trailed, TrailsPersist)
         .Wire(trailed, 0, banded, 0)
         .Wire(Span(crush, 0f, 1f, 24f, 3f), 0, banded, 1)
         .Wire(Span(coord, 0.5f, 2f, 1f, 0.35f, 2), 0, vignette, 0)
         .Wire(banded, 0, shaded, 0)
         .Wire(vignette, 0, shaded, 1)
         .Wire(shaded, 0, graded, 0)
         .Wire(Span(song, 0f, 1f, 0.8f, 1.3f), 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Print");

        return b.Build();

        // A note snapped to the scale.
        NodeInstance Snapped(NodeInstance note)
        {
            var snap = b.Add(NodeCatalog.QuantiserTypeId);
            ScaleExtra.Set(snap, Scale);
            b.Wire(note, 0, snap, 0);
            return snap;
        }

        // One tunnel: the plane turned, a distance from the middle, and the fraction of
        // that run outwards by the beat and cut in half. A square's distance is the larger
        // of how far across and how far up, which keeps its corners however far out it
        // is; a triangle's is a Polygon of three sides and almost no size.
        NodeInstance Tunnel(bool square, float turn, float bands, float speed)
        {
            var turned = b.Add("space.rotate");
            var cut = b.Add("math.step", (0, 0.5f));

            b.Wire(plane, 0, turned, 0)
             .Wire(plane, 1, turned, 1)
             .Wire(Times(beats, turn), 0, turned, 2);

            NodeInstance distance;

            if (square)
            {
                distance = Wired("math.max", Size(turned), Size(turned, 1));
            }
            else
            {
                distance = b.Add(PolygonType, (2, 0.001f), (3, 3f));
                b.Wire(turned, 0, distance, 0).Wire(turned, 1, distance, 1);
            }

            b.Wire(Fraction(Less(Times(distance, bands), Times(beats, speed))), 0, cut, 1);

            return cut;
        }
    }
}
