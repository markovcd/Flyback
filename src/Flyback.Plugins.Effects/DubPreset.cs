using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Dub techno to be played rather than listened to: a kick, hats and a sub that run
/// on their own, four keys of chord over them, and eight knobs on the panel that
/// are the performance.
/// </summary>
/// <remarks>
/// The other big presets are finished tracks. This one is half of one, and the
/// missing half is the player: the genre is a minor chord held down while somebody
/// rides the filter, the echo and the room, so the chord is four MIDI Ins on voices
/// 1 to 4 (ADR-0062) and everything worth riding is a knob on the panel (ADR-0086).
/// Nothing is bound to a controller, because which controller is the player's to
/// say: a knob learns one from the panel.
/// <para>
/// A knob moves the picture as well as the sound wherever the two are one idea. The
/// echo's feedback is how long a ring's trail lasts, the room is how fast the trails
/// drift outwards, the resonance is how far the rings wobble, and the cutoff is how
/// much light is in the fog. Each voice is a ring: as wide as its note is high,
/// colored by which of the twelve notes it is, and as bright as a Meter says the
/// voice is loud — an envelope has no memory on the screen, and a Meter is told.
/// </para>
/// <para>
/// The sub takes its root from the first voice, folded into one octave, so the
/// first key of a chord names the bass under it. Until a key has been struck it
/// stays on A, which is the key the bottom row of a computer keyboard plays in.
/// </para>
/// </remarks>
internal sealed class DubPreset : PresetBench
{
    public const string Name = "Dub";

    public const int Voices = 4;

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string DriveType = NodeCatalog.DriveTypeId;

    private const string EuclidType = "flyback.voice.euclid";

    private const string RandomType = NodeCatalog.RandomTypeId;

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string FractalType = "flyback.picture.fractal";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>The Filter's resonance, and the ADSR's three stages a knob reaches.</summary>
    private const int FilterResonance = 2;

    private const int AdsrDecay = 2;

    private const int AdsrSustain = 3;

    private const int AdsrRelease = 4;

    /// <summary>The A the sub rests on, an octave and a half under the keyboard's bottom row.</summary>
    private const float BassRoot = 33f;

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new DubPreset(modules).Assemble();
    }

    private DubPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the panel -------------------------------------------------------

        // Eight, which is a row on most controllers. Each rests where the patch
        // sounds like the genre with nobody touching it.
        var cutoff = Panel("Cutoff", 0.45f);
        var resonance = Panel("Resonance", 0.35f);
        var pluck = Panel("Pluck", 0.5f);
        var decay = Panel("Decay", 0.4f);
        var echo = Panel("Echo", 0.6f);
        var space = Panel("Space", 0.5f);
        var drums = Panel("Drums", 0.8f);
        var bass = Panel("Bass", 0.8f);

        // --- the clock -------------------------------------------------------

        // A hundred and twenty-two a minute. Every part reads the count of beats, so
        // the drums are envelopes off the grid rather than things triggered on it.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 122f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        Box("Clock");

        // --- the knobs as signals --------------------------------------------

        // A cutoff is heard in octaves, so the knob is an exponent: a hundred and
        // fifty hertz at the bottom and a little over five octaves above it at the
        // top. Thirty milliseconds of Slew takes the steps out of a controller's
        // hundred and twenty-eight values; on the screen a Slew is a wire.
        var cutoffHz = Times(Through("math.exp", Times(Smoothed(cutoff), 3.67f)), 150f);

        // How many times over the envelope opens the filter.
        var pluckDepth = Times(Smoothed(pluck), 6f);

        Box("Knobs");

        // --- the kick --------------------------------------------------------

        // Four on the floor, low and soft. What is left of the beat to the fifth
        // power is the envelope, and the pitch falls with it.
        var kickStroke = Stroke(beats, 1f, 5f);
        var kick = Drum(kickStroke, 47f, 110f, 4f, 1.8f);

        // The sidechain: the chords and the sub lean away from the kick, by as much
        // as the kick is up.
        var duck = Ducking(kickStroke, 0f);
        Follows(duck, 3, drums, 0f, 0.55f);

        Box("Kick");

        // --- the hats --------------------------------------------------------

        // Open on the off-beat, and sixteenths under it that come and go on a Wander.
        var hatStroke = Sum(
            Times(Stroke(beats, 1f, 3.5f, 0.5f), 0.7f),
            Product(Times(Stroke(beats, 4f, 8f), 0.3f), Wander(0.07f, 2f, 0.3f)));
        var hats = Hiss(hatStroke, 9000f, 0.15f, "high");

        Box("Hats");

        // --- the rim ---------------------------------------------------------

        // Two and four, all of it a band of noise, and most of what is heard of it
        // is the echo.
        var rimStroke = Stroke(beats, 0.5f, 12f, 0.5f);
        var rim = Hiss(rimStroke, 1700f, 0.5f, "band", 2.5f, seed: 1f);

        Box("Rim");

        // --- the conga -------------------------------------------------------

        // Five in sixteen, turned so that it never lands with the kick.
        var congaHits = b.Add(EuclidType, (1, 4f), (2, 16f), (3, 5f), (4, 3f), (EuclidCurve, 6f));
        var conga = b.Add("flyback.voice.drum", (DrumPitch, 185f), (3, 45f), (4, 2f), (5, 0f));

        b.Wire(beats, 0, congaHits, 0)
         .Wire(congaHits, EuclidStroke, conga, 1);

        Box("Conga");

        // --- the voices ------------------------------------------------------

        var keys = new NodeInstance[Voices];
        var heard = new NodeInstance[Voices];
        var chord = b.Add("math.mixer", (1, 0.8f), (3, 0.8f), (5, 0.8f), (7, 0.8f));

        for (var voice = 0; voice < Voices; voice++)
        {
            keys[voice] = b.Add(NodeCatalog.MidiTypeId);
            keys[voice].SetState(
                MidiExtra.StateKey, new JsonObject { [MidiExtra.IndexField] = (float)(voice + 1) });

            var hz = Through("audio.note", keys[voice]);

            // One knob from a stab to a pad: the decay, the sustain it falls to and
            // the release all lengthen together.
            var envelope = b.Add(NodeCatalog.AdsrTypeId, (1, -2.3f));
            Follows(envelope, AdsrDecay, decay, -1.2f, 0.5f);
            Follows(envelope, AdsrSustain, decay, 0.05f, 0.7f);
            Follows(envelope, AdsrRelease, decay, -1f, 0.6f);

            // Two saws a few cents apart, each voice by its own amount so that a chord
            // does not beat in step, and a square an octave under them.
            var saw = b.Add("osc.saw", (3, 0.5f));
            var beside = b.Add("osc.saw", (3, 0.5f));
            var under = b.Add("osc.square", (3, 0.3f));
            var tone = b.Add(FilterType);
            Follows(tone, FilterResonance, resonance, 0.05f, 0.85f);

            // The knob's cutoff, opened by the envelope and held under the top of the
            // Filter's range.
            var opened = Knobbed(
                "math.min", Product(cutoffHz, Plus(Product(envelope, pluckDepth), 1f)), 11000f);

            b.Wire(keys[voice], 1, envelope, 0)
             .Wire(hz, 0, saw, 1)
             .Wire(Times(hz, 1.004f + 0.0015f * voice), 0, beside, 1)
             .Wire(Times(hz, 0.5f), 0, under, 1)
             .Wire(Sum(Sum(saw, beside), under), 0, tone, 0)
             .Wire(opened, 0, tone, 1);

            // As loud as the key was struck, which a typist's never varies and a
            // keyboard's does.
            var voiced = Product(Product(tone, envelope), keys[voice], 2);

            // The voice as the picture knows it: a twelfth of a second of loudness.
            heard[voice] = b.Add(NodeCatalog.MeterTypeId, (1, -1.1f), (2, 0.15f));

            b.Wire(voiced, 0, heard[voice], 0)
             .Wire(voiced, 0, chord, voice * 2);

            Box($"Voice {voice + 1}");
        }

        // --- the chord -------------------------------------------------------

        // A little Drive for the warmth of a worn sampler, and a Chorus to make one
        // signal two.
        var warm = b.Add(DriveType, (1, 1.5f));
        var wide = b.Add(ChorusModule.TypeId, (1, 0.3f), (2, 0.5f), (3, 0.5f));
        var chordLeft = Product(wide, duck, DuckGain);
        var chordRight = Wired("math.mul", wide, duck, 1, DuckGain);

        b.Wire(chord, 0, warm, 0)
         .Wire(warm, 0, wide, 0);

        Box("Chord");

        // --- the sub ---------------------------------------------------------

        // The first voice's note folded into the octave over the low A. A hundred and
        // twenty is ten octaves, so adding it changes no pitch class and keeps what
        // is folded over nought. Velocity is nought until the first key is struck
        // and never again, which is what holds the sub on A until then.
        var folded = Plus(Knobbed("math.mod", Plus(keys[0], 120f - BassRoot), 12f), BassRoot);
        var struck = b.Add("math.step", (0, 0.01f));
        var root = b.Add("math.mix", (0, BassRoot));

        // Two bars of off-beats, with the seventh under the root at the end of the
        // first and the fifth under it at the end of the second. A rest holds the
        // pitch it follows, so the glide has somewhere to come from.
        var bassLine = b.Add("seq.values", (1, 4f), (2, 0.75f), (3, 0.05f));
        StepsExtra.Set(bassLine,
        [
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0.7f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(-2f, 1f, 0.8f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(3f, 1f, 0.8f), new Step(3f, 1f, 0f), new Step(0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(-5f, 1f, 0.8f), new Step(-5f, 1f, 0.7f), new Step(-5f, 1f, 0f),
        ]);

        // Fifty milliseconds of glide on the hertz: a Note snaps to the semitone, so
        // the slide has to come after it.
        var bassHz = b.Add(SlewType, (1, -1.3f), (2, -1.3f));
        var sub = b.Add("osc.sine", (3, 0.9f));
        var edge = b.Add("osc.triangle", (3, 0.2f));
        var bassTone = b.Add(FilterType, (1, 320f), (2, 0.1f));

        // The gate with its corners taken off: three milliseconds up, an eighth of a
        // second down.
        var bassGate = b.Add(SlewType, (1, -2.5f), (2, -0.9f));
        var subOut = Product(Product(bassTone, bassGate), duck, DuckGain);

        b.Wire(keys[0], 2, struck, 1)
         .Wire(folded, 0, root, 1)
         .Wire(struck, 0, root, 2)
         .Wire(beats, 0, bassLine, 0)
         .Wire(Through("audio.note", Sum(bassLine, root)), 0, bassHz, 0)
         .Wire(bassHz, 0, sub, 1)
         .Wire(Times(bassHz, 2f), 0, edge, 1)
         .Wire(Sum(sub, edge), 0, bassTone, 0)
         .Wire(bassLine, 1, bassGate, 0);

        Box("Sub");

        // --- the dust --------------------------------------------------------

        // A record that has been played too often: pink hiss that never stops, and a
        // crackle. The crackle is a Random a hundred and eighty times a second that
        // lets a burst of its own white through on the few values near the top.
        var hiss = Hiss(null, 4500f, 0f, "low", 0.35f, "pink", 3f);
        var chance = b.Add(RandomType, (1, 180f), (2, 5f));
        var tick = b.Add("math.step", (0, 0.988f));
        var dust = Sum(hiss, Times(Product(tick, chance), 0.6f));

        b.Wire(chance, 2, tick, 1);

        Box("Dust");

        // --- the space -------------------------------------------------------

        // The echo every record of this kind is made of: three sixteenths and then
        // two more, fed back by the knob, and each side darkened on the way out so
        // that the repeats sit behind what is played.
        var send = b.Add("math.mixer", (1, 0.7f), (3, 0.5f), (5, 0.4f));
        var taps = Echo(send, beat, 3f, 2f, 0.6f, 1f);
        Follows(taps, EchoFeedback, echo, 0.2f, 0.9f);

        var tapsLeft = b.Add(FilterType, (1, 2200f), (2, 0.1f));
        var tapsRight = b.Add(FilterType, (1, 2200f), (2, 0.1f));

        // The room's size stays where it is, because a delay line that changes length
        // while it rings bends what is in it. The knob is how long it rings and how
        // much of it comes back.
        var roomSend = b.Add("math.mixer", (1, 0.6f), (3, 0.4f), (5, 0.4f), (7, 0.15f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.85f), (3, 1f));
        Follows(room, 2, space, 0.5f, 0.93f);

        b.Wire(warm, 0, send, 0)
         .Wire(rim, 0, send, 2)
         .Wire(conga, 0, send, 4)
         .Wire(taps, 0, tapsLeft, 0)
         .Wire(taps, EchoRight, tapsRight, 0)
         .Wire(warm, 0, roomSend, 0)
         .Wire(tapsLeft, 0, roomSend, 2)
         .Wire(rim, 0, roomSend, 4)
         .Wire(hats, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        // Three Desks chained by their buses. The Drums knob is all four faders of
        // the first and the Bass knob one of the second, each reaching the level the
        // part was mixed at where the knob rests.
        var drumDesk = b.Add(DeskType);
        var musicDesk = b.Add(DeskType);
        var spaceDesk = b.Add(DeskType, (DeskTrim, 0.28f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.9f));

        Channel(drumDesk, 1, 0.8f, kick);
        Channel(drumDesk, 2, 0.3f, hats);
        Channel(drumDesk, 3, 0.35f, rim);
        Channel(drumDesk, 4, 0.35f, conga);

        for (var channel = 1; channel <= 4; channel++) Ridden(drumDesk, channel, drums);

        Channel(musicDesk, 1, 0.75f, subOut);
        Channel(musicDesk, 2, 0.85f, chordLeft, chordRight);
        Channel(musicDesk, 3, 0.2f, dust);
        Ridden(musicDesk, 1, bass);

        Channel(spaceDesk, 1, 0.6f, tapsLeft, tapsRight);
        Channel(spaceDesk, 2, 0.45f, room, room, rightFrom: 1);
        Follows(spaceDesk, 5, space, 0.1f, 0.8f);

        b.Wire(Chained(drumDesk, musicDesk, spaceDesk), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(spaceDesk, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: fog ------------------------------------------------

        // Slow Fractal noise in one cold color, with as much light in it as the
        // filter is open.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var fog = b.Add(FractalType, (3, 1.6f), (4, 0.55f));
        var light = Times(fog, 0.7f);
        Follows(light, 1, cutoff, 0.5f, 1.5f);

        var cold = b.Add("color.rgb", (0, 0.12f), (1, 0.3f), (2, 0.36f));
        var mist = b.Add("color.gain");

        // The sub is a glow along the bottom of the frame, as high as the Bass knob.
        var low = From(1f, Rises(coord, -1f, -0.3f, 1));
        var glow = Ink(mist, Times(Product(low, bassLine, 1), bass, 0f, 0.5f), 0.9f, 0.35f, 0.1f);

        b.Wire(Times(clock, 0.05f), 0, fog, 2)
         .Wire(cold, 0, mist, 0)
         .Wire(light, 0, mist, 1);

        Box("Picture: Fog");

        // --- the picture: rings ----------------------------------------------

        // The resonance is how far the fog pushes the rings out of round: the same
        // field added to every ring's distance, so they bend together.
        var wobble = Times(Plus(fog, -0.5f), resonance, 0f, 0.35f);

        NodeInstance? rings = null;

        for (var voice = 0; voice < Voices; voice++)
        {
            // Three octaves of keyboard from the middle of the frame to its edge, and
            // a note under them held off the middle, where the kick is.
            var ring = b.Add(CircleType);
            var drawn = b.Add(FillType, (1, 0.012f));

            // As bright as the voice is loud, and never quite dark while its key is
            // down. The line thickens with the level too, so a stab is seen to land.
            var level = Wired(
                "math.max", Knobbed("math.min", heard[voice], 1.5f), Times(keys[voice], 0.25f, 1));

            // Which of the twelve notes it is, as a hue between teal and magenta, so a
            // note is always the color it was and an octave is the same color further
            // out. The warm end of the wheel is left to the kick and the sub.
            var tint = b.Add("color.hsv", (1, 0.45f), (2, 1f));
            var lit = b.Add("color.gain");

            b.Wire(coord, 0, ring, 0)
             .Wire(coord, 1, ring, 1)
             .Wire(Knobbed("math.max", Span(keys[voice], 48f, 84f, 0.14f, 1f), 0.1f), 0, ring, 2)
             .Wire(Sum(ring, wobble), 0, drawn, 0)
             .Wire(Plus(Times(level, 0.025f), 0.004f), 0, drawn, 2)
             .Wire(Span(Fraction(Times(keys[voice], 1f / 12f)), 0f, 1f, 0.47f, 0.87f), 0, tint, 0)
             .Wire(tint, 0, lit, 0)
             .Wire(Product(level, drawn, 1), 0, lit, 1);

            rings = rings is null ? lit : Sum(rings, lit);
        }

        Box("Picture: Rings");

        // --- the picture: scene ----------------------------------------------

        // The kick is a disc in the middle that every ring is drawn round, swelling
        // on the beat and gone when the drums are down.
        var kickSeen = Times(kickStroke, drums, 0f, 1f);
        var disc = b.Add(CircleType);
        var pulse = b.Add(FillType, (1, 0.06f));
        var scene = Ink(Sum(glow, rings!), Product(pulse, kickSeen), 0.75f, 0.95f, 1f);

        b.Wire(coord, 0, disc, 0)
         .Wire(coord, 1, disc, 1)
         .Wire(Span(kickSeen, 0f, 1f, 0.02f, 0.08f), 0, disc, 2)
         .Wire(disc, 0, pulse, 0);

        // Scanlines, the corners darkened, and a little more contrast than it had.
        var scanned = b.Add("color.gain");
        var graded = b.Add(GradeType, (1, 1.15f), (2, 1.1f));

        // The echo, seen. The last frame is read a little smaller than it was, which
        // pushes it outwards, so a ring that was struck goes on spreading after it
        // has stopped sounding: for as long as the echo feeds back, and as fast as
        // the room is big. It comes last because the last frame is whatever reached
        // the Output, and anything after the Trails would be applied to the trail
        // again on every pass.
        var trails = b.Add(TrailsType, (TrailsAngle, 0.003f));
        Follows(trails, TrailsPersist, echo, 0.9f, 0.992f);
        Follows(trails, TrailsZoom, space, 0.998f, 0.985f);

        b.Wire(scene, 0, scanned, 0)
         .Wire(Span(Sine(Times(coord, 380f, 1)), -1f, 1f, 0.88f, 1f), 0, scanned, 1)
         .Wire(Vignette(scanned, 0.5f, 2f, 0.4f), 0, graded, 0)
         .Wire(graded, 0, trails, 0)
         .Wire(trails, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Scene");

        return b.Build();
    }

    /// <summary>A Slew of thirty milliseconds each way, for a knob that is heard.</summary>
    private NodeInstance Smoothed(PatchControl knob)
    {
        var slew = b.Add(SlewType, (1, -1.5f), (2, -1.5f));
        Follows(slew, 0, knob, 0f, 1f);
        return slew;
    }

    /// <summary>
    /// A Desk's fader handed to a panel knob: nothing with the knob down, and the
    /// level the channel was given where the knob rests.
    /// </summary>
    private static void Ridden(NodeInstance desk, int channel, PatchControl knob)
    {
        var fader = (channel - 1) * 3 + 2;

        Follows(desk, fader, knob, 0f, desk.InputValues[fader] / knob.Value);
    }
}
