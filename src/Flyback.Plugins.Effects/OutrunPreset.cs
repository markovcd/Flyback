using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole synthwave track — ninety-six bars of four dark chords, gated pads, a
/// rolling bass and a gated-reverb snare — under a picture that is drawn rather than grown:
/// a slatted sun over a ridge, and a grid running to the horizon a line to the beat.
/// </summary>
/// <remarks>
/// The other big presets make a texture and let the music move it. This one draws a
/// scene, which is a different use of the same modules: every region of the frame is
/// a mask made of a comparison, every mask mixes one color over what is behind it,
/// and the order of the mixes is the depth. The perspective is one division — how far
/// under the horizon a pixel is, turned over — and everything on the ground is
/// ordinary flat pattern read at the plane that division gives back.
/// <para>
/// The harmony is written once, as four roots: A, F, B and E, with the bass falling
/// a tritone from the second to the third. No part says what kind of chord stands on
/// any of them. A third is a little over three and a half semitones above the root and
/// a fifth is seven, and both go through a Quantiser set to A harmonic minor — which
/// has no F sharp, so the fifth over B lands on F and the chord is diminished, and has
/// a G sharp, so the chord over E is the major one that pulls back to A.
/// </para>
/// </remarks>
internal sealed class OutrunPreset : PresetBench
{
    public const string Name = "Outrun";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string SupersawType = "flyback.voice.osc";

    private const string SlewType = "flyback.voice.slew";

    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string CellsType = "flyback.picture.cells";

    private const string PolygonType = "flyback.picture.polygon";

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string PosteriseType = "flyback.picture.posterise";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>A harmonic minor, written from its root: A, B, C, D, E, F and G sharp.</summary>
    private static readonly int[] Scale = [9, 11, 0, 2, 4, 5, 8];

    /// <summary>
    /// Between a minor third and a major one. Added to a root and snapped to the scale
    /// it lands on whichever of the two the scale has. A hair over the middle, because
    /// over F the scale has both: the G sharp and the A are a semitone apart, and the
    /// chord wants the A.
    /// </summary>
    private const float Third = 3.6f;

    /// <summary>Where the ground meets the sky, a little under the middle of the frame.</summary>
    private const float Horizon = -0.08f;

    /// <summary>How far over the middle of the frame the sun's own middle is.</summary>
    private const float SunHeight = 0.24f;

    /// <summary>
    /// The three colors that are both blended and drawn with: the pink of the
    /// horizon, the hot pink at the foot of the sun, and the cyan of the neon.
    /// </summary>
    private const float PinkRed = 1f, PinkGreen = 0.35f, PinkBlue = 0.5f;

    private const float HotRed = 1f, HotGreen = 0.18f, HotBlue = 0.55f;

    private const float CyanRed = 0.1f, CyanGreen = 0.9f, CyanBlue = 1f;

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new OutrunPreset(modules).Assemble();
    }

    private OutrunPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        // A hundred and twelve a minute. Every part reads the count of beats rather
        // than the clock, so the tempo is this one knob and nothing can disagree
        // about it.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 112f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        // The count as an envelope: what is left of the sixteenth, cubed, is a pluck
        // that cannot drift off the grid because it is the grid. Every drum below is
        // a Stroke off the same count.
        var pluck = Stroke(beats, 4f, 3f);

        // Eight bars is one step of the arrangement.
        var phraseGone = Fraction(Times(beats, 1f / 32f));

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // One number for each eight bars saying how much track there is: intro,
        // intro, build, build, drop, drop, breakdown, build, and three of the peak
        // before the outro. No part has a lane of its own; each decides below how
        // much of this number it needs before it comes in.
        var song = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(song,
        [
            new Step(0.15f), new Step(0.3f), new Step(0.45f), new Step(0.6f),
            new Step(0.8f), new Step(0.85f), new Step(0.35f), new Step(0.5f),
            new Step(0.95f), new Step(1f), new Step(1f), new Step(0.2f),
        ]);

        // The same number for what fades rather than enters: four seconds up and two
        // down, in decades of a second. On the screen a Slew is a wire.
        var swell = b.Add(SlewType, (1, 0.60206f), (2, 0.30103f));

        // The last of every four phrases ends in a riser and a fill, read off the
        // sequencer's own index: three times it has a fraction of three quarters
        // exactly there. The very last is the outro, which is masked out.
        var turning = b.Add("math.step", (0, 0.7f));
        var notTheEnd = b.Add("math.step", (1, 0.9f));
        var turns = Product(turning, notTheEnd);
        var ramp = Product(Rises(phraseGone, 0.5f, 1f), turns);

        // Four on the floor, from a little under halfway up. The kick's Fade is here
        // rather than with the kick because the fill wants to know the same thing.
        var kickStroke = Enters(Stroke(beats, 1f, 5f), song, 0.42f, 0.48f);

        // How hard the pads are chopped. Whole in the intro and the breakdown, and
        // cut to the sixteenths once the drums are in.
        var chop = Rises(song, 0.4f, 0.6f);

        // The last two beats of a phrase, wherever there are drums to fill them.
        var lastTwo = b.Add("math.step", (0, 0.9375f));
        var fill = Product(lastTwo, kickStroke, FadeGate);

        // The harmony, a bar at a time: A, F, B and E, two bars each. From F the bass
        // drops a tritone to the B under it, and E is the way home. It is the only
        // place the chords are written down.
        var root = b.Add("seq.notes", (1, 0.25f));
        StepsExtra.Set(root,
        [
            new Step(45f), new Step(45f), new Step(41f), new Step(41f),
            new Step(35f), new Step(35f), new Step(40f), new Step(40f),
        ]);

        b.Wire(beats, 0, song, 0)
         .Wire(song, 0, swell, 0)
         .Wire(Fraction(Times(song, 3f, 2)), 0, turning, 1)
         .Wire(song, 2, notTheEnd, 0)
         .Wire(phraseGone, 0, lastTwo, 1)
         .Wire(beats, 0, root, 0);

        Box("Arrangement");

        // --- the kick --------------------------------------------------------

        // The pitch is the level to the fourth power, so there is one envelope and
        // the beater is over long before the shell is.
        var kick = Drum(kickStroke, 45f, 125f, 4f, 2.5f);

        // The sidechain: the kick's level on the bass.
        var duck = Ducking(kickStroke, 0.65f);

        Box("Kick");

        // --- the snare -------------------------------------------------------

        // Two and four: half the beat, offset by half a cycle, restarts on every
        // backbeat. A band of Hiss for the wires and a Drum that does not sweep for the
        // shell.
        var backbeat = Stroke(beats, 0.5f, 9f, 0.5f);
        var snareStroke = Enters(backbeat, song, 0.55f, 0.62f);
        var snareDry = Sum(
            Hiss(snareStroke, 1900f, 0.3f, "band", 2.5f),
            Drum(snareStroke, 190f, 0f, 1f, 0f));

        // And the sound of the decade: a hall far too big for a drum, shut off a
        // fifth of a second after the hit instead of being left to die. The gate is
        // the same ramp that struck the drum, so it cannot open late.
        var hall = b.Add(ReverbModule.TypeId, (1, 0.9f), (2, 0.85f), (3, 1f));
        var gate = From(1f, Rises(backbeat, 0.2f, 0.26f, StrokePhase));
        var snareL = Sum(snareDry, Times(Product(gate, hall), 0.8f));
        var snareR = Sum(snareDry, Times(Product(gate, hall, 1), 0.8f));

        b.Wire(snareDry, 0, hall, 0);

        Box("Snare");

        // --- the hats --------------------------------------------------------

        // Closed on every sixteenth and open on the off-beat, which is half a beat
        // late and rings three times as long.
        var hatStroke = Enters(
            Sum(Times(Stroke(beats, 4f, 7f), 0.5f), Times(Stroke(beats, 1f, 3f, 0.5f), 0.7f)),
            song, 0.4f, 0.45f);
        var hats = Hiss(hatStroke, 8000f, 0.2f, "high");

        Box("Hats");

        // --- the toms --------------------------------------------------------

        // The fill: eight sixteenths down a drum kit nobody could afford, pitched by
        // how far through the phrase it has got, so the run falls without a list.
        var tomStroke = Product(Stroke(beats, 4f, 2.5f), fill);
        var toms = Drum(tomStroke, 0f, 60f, 3f, 0f);

        b.Wire(Span(phraseGone, 0.9375f, 1f, 230f, 95f), 0, toms, DrumPitch);

        Box("Toms");

        // --- the bass --------------------------------------------------------

        // Root and octave in rolling sixteenths, over whatever the harmony is on.
        var bassLine = b.Add("seq.values", (1, 4f), (2, 0.7f), (3, 0.03f));
        StepsExtra.Set(bassLine,
        [
            new Step(0f), new Step(0f, 1f, 0.8f), new Step(12f, 1f, 0.9f), new Step(0f, 1f, 0.8f),
            new Step(0f), new Step(12f, 1f, 0.8f), new Step(0f, 1f, 0.9f), new Step(12f, 1f, 0.85f),
        ]);
        var bassHz = Through("audio.note", Sum(bassLine, root));
        var bassSaw = b.Add("osc.saw", (3, 0.8f));
        var bassTone = b.Add(FilterType, (2, 0.4f));
        var bassGrit = b.Add(DriveType, (1, 3f));

        // The sub, an octave under and added after the Drive so that it stays a sine.
        var sub = b.Add("osc.sine", (3, 0.5f));
        var bass = Enters(
            Product(Sum(bassGrit, Product(sub, bassLine, 1)), duck, DuckGain), song, 0.25f, 0.3f);

        // How far the pluck opens the Filter is the arrangement.
        b.Wire(beats, 0, bassLine, 0)
         .Wire(bassHz, 0, bassSaw, 1)
         .Wire(bassSaw, 0, bassTone, 0)
         .Wire(Plus(Product(pluck, Span(swell, 0f, 1f, 400f, 2600f)), 120f), 0, bassTone, 1)
         .Wire(Product(bassTone, bassLine, 1), 0, bassGrit, 0)
         .Wire(Times(bassHz, 0.5f), 0, sub, 1);

        Box("Bass");

        // --- the arp ---------------------------------------------------------

        // Up the chord and back in sixteenths: root, fifth, octave, the third over
        // that, and the fifth over that. The list is the same over every chord and the
        // Quantiser makes it fit — over B, where the fifth it asks for is not in the
        // scale, what it plays is the tritone.
        var arpLine = b.Add("seq.values", (1, 4f));
        StepsExtra.Set(arpLine,
        [
            new Step(0f), new Step(7f), new Step(12f), new Step(12f + Third),
            new Step(19f), new Step(12f + Third), new Step(12f), new Step(7f),
            new Step(0f), new Step(7f), new Step(12f), new Step(12f + Third),
            new Step(19f), new Step(24f), new Step(19f), new Step(12f + Third),
        ]);
        var arpOsc = b.Add("osc.pulse", (3, 0.35f), (4, 0.8f));
        var arpTone = b.Add(FilterType, (2, 0.3f));

        b.Wire(beats, 0, arpLine, 0)
         .Wire(InKey(Sum(arpLine, root), Scale, 12f), 0, arpOsc, 1)
         .Wire(Product(arpOsc, pluck), 0, arpTone, 0)
         .Wire(Span(swell, 0f, 1f, 900f, 4200f), 0, arpTone, 1);

        Box("Arp");

        // --- the pad ---------------------------------------------------------

        // Seven detuned saws on the root, and a saw each on the third and the fifth the
        // scale allows.
        var padNote = Plus(root, 12f);
        var padHz = Through("audio.note", padNote);
        var strings = b.Add(SupersawType, (2, 0.3f), (3, 0.8f), (5, 0.5f));
        var third = b.Add("osc.saw", (3, 0.35f));
        var fifth = b.Add("osc.saw", (3, 0.3f));
        var padTone = b.Add(FilterType, (2, 0.2f));

        // The trance gate: a list of ones and noughts at the sixteenth, slewed by a
        // few milliseconds so that it chops without clicking, and mixed against a
        // steady one by how hard the section wants it.
        var chopLine = b.Add("seq.values", (1, 4f));
        StepsExtra.Set(chopLine,
        [
            new Step(1f), new Step(0f), new Step(1f), new Step(1f),
            new Step(0f), new Step(1f), new Step(1f), new Step(0f),
            new Step(1f), new Step(1f), new Step(0f), new Step(1f),
            new Step(1f), new Step(0f), new Step(1f), new Step(0f),
        ]);
        var chopped = b.Add(SlewType, (1, -2.3f), (2, -1.4f));
        var padGate = b.Add("math.mix", (0, 1f));

        // The Chorus is what makes it stereo: 'out' and 'wide' are swept in opposite
        // directions, which is wider than panning and costs one module.
        var pad = b.Add(ChorusModule.TypeId, (1, 0.2f), (2, 0.7f), (3, 0.6f));

        b.Wire(padHz, 0, strings, 1)
         .Wire(InKey(padNote, Scale, Third), 0, third, 1)
         .Wire(InKey(padNote, Scale, 7f), 0, fifth, 1)
         .Wire(Sum(Sum(strings, third), fifth), 0, padTone, 0)
         .Wire(Span(swell, 0f, 1f, 700f, 3200f), 0, padTone, 1)
         .Wire(beats, 0, chopLine, 0)
         .Wire(chopLine, 0, chopped, 0)
         .Wire(chopped, 0, padGate, 1)
         .Wire(chop, 0, padGate, 2)
         .Wire(Product(Product(padTone, padGate), Span(swell, 0f, 1f, 1f, 0.6f)), 0, pad, 0);

        Box("Pad");

        // --- the lead --------------------------------------------------------

        // The one melody that was written down, in eighths over four bars, kept back
        // for the peak. It is heard over A and F and then again over B and E, so it
        // keeps to what both halves can bear: the E and the F a semitone over it, and
        // in the second bar the B, D and F of the diminished chord, which is the
        // tritone said out loud. The rests hold their pitch, so the glide slides
        // between the notes either side of a gap rather than up from nothing.
        var leadLine = b.Add("seq.notes", (1, 2f), (2, 0.85f), (3, 0.06f));
        StepsExtra.Set(leadLine,
        [
            new Step(76f), new Step(76f, 1f, 0f), new Step(76f), new Step(77f),
            new Step(76f), new Step(76f, 1f, 0f), new Step(74f), new Step(74f, 1f, 0f),
            new Step(71f), new Step(71f, 1f, 0f), new Step(74f), new Step(74f, 1f, 0f),
            new Step(77f), new Step(76f), new Step(74f), new Step(74f, 1f, 0f),
            new Step(76f), new Step(76f, 1f, 0f), new Step(76f), new Step(76f, 1f, 0f),
            new Step(71f), new Step(71f, 1f, 0f), new Step(69f), new Step(71f),
            new Step(72f), new Step(72f, 1f, 0f), new Step(71f), new Step(71f, 1f, 0f),
            new Step(69f), new Step(69f, 1f, 0f), new Step(64f), new Step(69f, 1f, 0.8f),
        ]);

        // Forty milliseconds of glide, a second voice a hair sharp, and a vibrato that
        // leans on the phase rather than the frequency.
        var glide = b.Add(SlewType, (1, -1.4f), (2, -1.4f));
        var leadHz = Through("audio.note", glide);
        var vibrato = b.Add("osc.sine", (1, 5.5f), (3, 0.15f));
        var leadSaw = b.Add("osc.saw", (3, 0.7f));
        var leadPulse = b.Add("osc.pulse", (3, 0.4f), (4, 0.5f));
        var leadTone = b.Add(FilterType, (1, 3400f), (2, 0.35f));
        var tongue = b.Add(SlewType, (1, -2.5f), (2, -1.2f));
        var lead = Enters(Product(leadTone, tongue), song, 0.9f, 0.94f);

        b.Wire(beats, 0, leadLine, 0)
         .Wire(leadLine, 0, glide, 0)
         .Wire(leadHz, 0, leadSaw, 1)
         .Wire(vibrato, 0, leadSaw, 2)
         .Wire(Times(leadHz, 1.006f), 0, leadPulse, 1)
         .Wire(Sum(leadSaw, leadPulse), 0, leadTone, 0)
         .Wire(leadLine, 1, tongue, 0);

        Box("Lead");

        // --- the riser -------------------------------------------------------

        // A Hiss whose band climbs nearly five octaves in four bars. The ramp that
        // moves the band also opens it.
        var riser = Hiss(ramp, 250f, 0.55f, "band", 1.4f);

        b.Wire(Span(ramp, 0f, 1f, 250f, 7000f), 0, riser, HissCutoff);

        Box("Riser");

        // --- the space -------------------------------------------------------

        // An Echo of three sixteenths on the left and two more on the right — a dotted
        // eighth and the beat after it — and one hall on a send for what should sound
        // far away.
        var send = b.Add("math.mixer", (1, 0.6f), (3, 0.7f), (5, 0.3f));
        var taps = Echo(send, beat, 3f, 2f, 0.45f, 1f);
        var roomSend = b.Add("math.mixer", (1, 0.7f), (3, 0.4f), (5, 0.5f), (7, 0.5f));
        var room = b.Add(ReverbModule.TypeId, (1, 0.85f), (2, 0.8f), (3, 1f));

        b.Wire(arpTone, 0, send, 0)
         .Wire(lead, 0, send, 2)
         .Wire(toms, 0, send, 4)
         .Wire(lead, 0, roomSend, 0)
         .Wire(arpTone, 0, roomSend, 2)
         .Wire(pad, 0, roomSend, 4)
         .Wire(toms, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        // Three Desks of four, chained by their buses into one of twelve. Left and
        // right differ in which echo tap and which side of the snare's hall, the
        // Chorus and the Reverb they carry.
        var drums = b.Add(DeskType);
        var music = b.Add(DeskType);

        // The last in the chain is the master: a trim under unity, and rails that
        // should never be reached.
        var space = b.Add(DeskType, (DeskTrim, 0.34f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        Channel(drums, 1, 0.85f, kick);
        Channel(drums, 2, 0.55f, snareL, snareR);
        Channel(drums, 3, 0.3f, hats);
        Channel(drums, 4, 0.55f, toms);

        Channel(music, 1, 0.7f, bass);
        Channel(music, 2, 0.4f, arpTone);
        Channel(music, 3, 0.5f, pad, pad, rightFrom: 1);
        Channel(music, 4, 0.45f, lead);

        Channel(space, 1, 0.45f, taps, taps, rightFrom: EchoRight);
        Channel(space, 2, 0.5f, room, room, rightFrom: 1);
        Channel(space, 3, 0.4f, riser);

        b.Wire(Chained(drums, music, space), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(space, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: sky ------------------------------------------------

        // Three colors up the frame: the pink of the horizon, violet over it, and
        // nearly black at the top. Each Blend is one band of the gradient.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var pink = b.Add("color.rgb", (0, PinkRed), (1, PinkGreen), (2, PinkBlue));
        var violet = b.Add("color.rgb", (0, 0.32f), (1, 0.06f), (2, 0.5f));
        var zenith = b.Add("color.rgb", (0, 0.04f), (1, 0.01f), (2, 0.16f));
        var dusk = b.Add("color.mix");
        var sky = b.Add("color.mix");

        // Stars are Cells: the distance to the nearest point is nothing at the point
        // itself, so under a threshold it is a dot. Each twinkles on its own cell's
        // number, only the upper sky has any, and the hats make them all flare.
        var cells = b.Add(CellsType, (3, 16f), (4, 0.9f));
        var twinkle = Span(
            Sine(Sum(Times(cells, 50f, 2), Times(clock, 2.5f))), -1f, 1f, 0.25f, 1f);
        var stars = Product(
            Product(Product(From(1f, Rises(cells, 0.03f, 0.08f)), twinkle), Rises(coord, 0.15f, 0.6f, 1)),
            Span(hatStroke, 0f, 1f, 1f, 2.2f));

        b.Wire(pink, 0, dusk, 0)
         .Wire(violet, 0, dusk, 1)
         .Wire(Rises(coord, Horizon, 0.45f, 1), 0, dusk, 2)
         .Wire(dusk, 0, sky, 0)
         .Wire(zenith, 0, sky, 1)
         .Wire(Rises(coord, 0.3f, 1f, 1), 0, sky, 2)
         .Wire(coord, 0, cells, 0)
         .Wire(coord, 1, cells, 1);

        Box("Picture: Sky");

        // --- the picture: sun ------------------------------------------------

        // A Circle with the plane slid down under it, swelling a little on the kick,
        // and filled twice: once hard for the disc and once very soft for the haze
        // round it.
        var sunY = Plus(coord, -SunHeight, 1);
        var disc = b.Add(CircleType);
        var sunFill = b.Add(FillType, (1, 0.004f));
        var haze = b.Add(FillType, (1, 0.5f));

        // Yellow at the top and hot pink at the bottom.
        var yellow = b.Add("color.rgb", (0, 1f), (1, 0.92f), (2, 0.35f));
        var hot = b.Add("color.rgb", (0, HotRed), (1, HotGreen), (2, HotBlue));
        var sunColor = b.Add("color.mix");

        // The slats. Nine bands down the disc, sliding downwards a band every four
        // beats, with a gap at the foot of each that is nothing above the middle and
        // over half the band at the bottom. A Threshold with the gap on its edge is
        // the whole venetian blind: a pixel shows where it is further through its
        // band than the gap is wide.
        var slat = Fraction(Sum(Times(sunY, 9f), Times(beats, 0.25f)));
        var open = b.Add("math.step");
        var sun = Product(sunFill, open);

        b.Wire(coord, 0, disc, 0)
         .Wire(sunY, 0, disc, 1)
         .Wire(Span(kickStroke, 0f, 1f, 0.4f, 0.42f), 0, disc, 2)
         .Wire(disc, 0, sunFill, 0)
         .Wire(disc, 0, haze, 0)
         .Wire(yellow, 0, sunColor, 0)
         .Wire(hot, 0, sunColor, 1)
         .Wire(Span(sunY, 0.3f, -0.4f, 0f, 1f), 0, sunColor, 2)
         .Wire(Span(sunY, 0.14f, -0.4f, 0f, 0.7f), 0, open, 0)
         .Wire(slat, 0, open, 1);

        // The triangle every record sleeve had, round the sun and never over it,
        // drawn as an outline and lit by the snare.
        var triangle = b.Add(PolygonType, (2, 0.66f), (3, 3f));
        var edge = b.Add(FillType, (1, 0.004f), (2, 0.014f));
        var neon = Product(
            Product(Span(snareStroke, 0f, 1f, 0.55f, 1.7f), edge, 1), From(1f, sunFill));

        b.Wire(coord, 0, triangle, 0)
         .Wire(sunY, 0, triangle, 1)
         .Wire(triangle, 0, edge, 0);

        Box("Picture: Sun");

        // --- the picture: ridge ----------------------------------------------

        // Mountains are one row of a Noise field, read along x with y pinned, so
        // every column of the frame has one height. They stand only at the sides —
        // the height is scaled by how far from the middle the column is — which
        // leaves the sun its gap.
        var row = b.Add("value", (0, 3.7f));
        var rock = b.Add("pattern.noise", (2, 1.3f), (3, 2.2f));
        var ridge = Plus(
            Product(Span(rock, 0f, 1f, 0.05f, 0.5f), Rises(Size(coord), 0.3f, 1.3f)), Horizon);

        // Under the ridge is mountain. A Threshold with the pixel's height on its
        // edge and the ridge on its input is one wherever the ridge is the higher.
        var under = b.Add("math.step");
        var drop = Wired("math.sub", ridge, coord, 0, 1);

        // Lit along the crest, and wired down the faces: lines a fixed distance apart
        // in x, slanted by how far under the crest they are.
        var crest = From(1f, Rises(Size(drop), 0f, 0.012f));
        var faces = Rises(
            Size(Plus(Fraction(Sum(Times(coord, 8f), Times(drop, 5f))), -0.5f)), 0.44f, 0.49f);
        var magenta = b.Add("color.rgb", (0, 1f), (1, 0.2f), (2, 0.75f));
        var mountain = b.Add("color.gain", (2, 0.03f));

        b.Wire(coord, 0, rock, 0)
         .Wire(row, 0, rock, 1)
         .Wire(coord, 1, under, 0)
         .Wire(ridge, 0, under, 1)
         .Wire(magenta, 0, mountain, 0)
         .Wire(Wired("math.max", crest, Times(faces, 0.3f)), 0, mountain, 1);

        Box("Picture: Ridge");

        // --- the picture: grid -----------------------------------------------

        // The perspective, which is one division. How far under the horizon a pixel
        // is, turned over, is how far away the ground there is: nothing at the bottom
        // of the frame and endless at the horizon. Held off nought, because at the
        // horizon itself that is one over nothing.
        var depth = Knobbed("math.max", From(Horizon, coord, 1), 0.004f);
        var away = b.Add("math.div", (0, 1f));

        // The ground plane, flat again: across is x pushed out by the distance, and
        // along is the distance with the count of beats added, so the ground arrives
        // a line to the beat.
        var acrossLine = Size(Plus(Fraction(Times(Product(coord, away), 1.5f)), -0.5f));
        var alongLine = Size(Plus(Fraction(Sum(Times(away, 0.5f), beats)), -0.5f));

        // A line's width on the ground has to grow with the distance to stay one
        // width on the screen — by the distance for the lines running away, and by
        // its square for the ones coming towards, which perspective squeezes twice.
        // Each is a Smoothstep with that width on its upper edge.
        var acrossInk = b.Add("math.smoothstep", (0, 0f));
        var alongInk = b.Add("math.smoothstep", (0, 0f));
        var lines = From(1f, Wired("math.min", acrossInk, alongInk));

        // Faded out before the horizon, where the lines are closer than a pixel, and
        // struck by the bass. The color is the chord: magenta under B, cyan under A.
        var bassSeen = Enters(pluck, song, 0.25f, 0.3f);
        var cyan = b.Add("color.rgb", (0, CyanRed), (1, CyanGreen), (2, CyanBlue));
        var gridColor = b.Add("color.mix");
        var floor = b.Add("color.rgb", (0, 0.07f), (1, 0f), (2, 0.14f));
        var gridInk = b.Add("color.gain");
        var ground = Sum(gridInk, floor);

        b.Wire(depth, 0, away, 1)
         .Wire(acrossLine, 0, acrossInk, 2)
         .Wire(Knobbed("math.min", Times(away, 0.02f), 0.5f), 0, acrossInk, 1)
         .Wire(alongLine, 0, alongInk, 2)
         .Wire(Knobbed("math.min", Times(Product(away, away), 0.006f), 0.5f), 0, alongInk, 1)
         .Wire(magenta, 0, gridColor, 0)
         .Wire(cyan, 0, gridColor, 1)
         .Wire(Span(root, 35f, 45f, 0f, 1f), 0, gridColor, 2)
         .Wire(gridColor, 0, gridInk, 0)
         .Wire(
             Product(Product(lines, Rises(depth, 0.01f, 0.2f)), Span(bassSeen, 0f, 1f, 0.75f, 1.6f)),
             0, gridInk, 1);

        Box("Picture: Grid");

        // --- the picture: scene ----------------------------------------------

        // The depth of the scene is the order of these. The sky with its stars and
        // the sun's haze; the sun mixed over that where a slat shows; the triangle
        // added; the mountains over all of it, and the ground over them.
        var withSun = b.Add("color.mix");
        var withRidge = b.Add("color.mix");
        var below = b.Add("math.step", (1, Horizon));
        var withGround = b.Add("color.mix");

        // And the line of light where they meet, which the kick brightens.
        var seam = Power(From(1f, Rises(Size(Plus(coord, -Horizon, 1)), 0f, 0.07f)), 2f);

        b.Wire(Ink(Sum(sky, stars), Times(haze, 0.4f), HotRed, HotGreen, HotBlue), 0, withSun, 0)
         .Wire(sunColor, 0, withSun, 1)
         .Wire(sun, 0, withSun, 2)
         .Wire(Ink(withSun, neon, CyanRed, CyanGreen, CyanBlue), 0, withRidge, 0)
         .Wire(mountain, 0, withRidge, 1)
         .Wire(under, 0, withRidge, 2)
         .Wire(coord, 1, below, 0)
         .Wire(withRidge, 0, withGround, 0)
         .Wire(ground, 0, withGround, 1)
         .Wire(below, 0, withGround, 2);

        var scene = Ink(
            withGround, Product(seam, Span(kickStroke, 0f, 1f, 0.6f, 1.3f)), PinkRed, PinkGreen, PinkBlue);

        Box("Picture: Scene");

        // --- the picture: tape -----------------------------------------------

        // What a worn cassette does to it. The last frame read from a little to one
        // side is a ghost that trails everything bright; scanlines are a sine down
        // the frame on the brightness; and the riser takes the colors away a level at
        // a time until the drop gives them back.
        var taped = b.Add(TrailsType, (TrailsDx, 0.007f), (TrailsPersist, 0.6f));
        var banded = b.Add(PosteriseType);
        var scanned = b.Add("color.gain");

        // Darkened at the corners, and graded by the section last, so the intro is
        // muted and the peak is not.
        var shaded = Vignette(scanned, 0.5f, 2f, 0.35f);
        var graded = b.Add(GradeType, (2, 1.08f));

        b.Wire(scene, 0, taped, 0)
         .Wire(taped, 0, banded, 0)
         .Wire(Span(ramp, 0f, 1f, 32f, 4f), 0, banded, 1)
         .Wire(banded, 0, scanned, 0)
         .Wire(Span(Sine(Times(coord, 400f, 1)), -1f, 1f, 0.86f, 1f), 0, scanned, 1)
         .Wire(shaded, 0, graded, 0)
         .Wire(Span(song, 0f, 1f, 0.85f, 1.35f), 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Tape");

        return b.Build();
    }
}
