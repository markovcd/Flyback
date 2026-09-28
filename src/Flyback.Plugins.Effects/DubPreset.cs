using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Dub techno to be played rather than listened to: eight sections of drums, a dub
/// bass line and chord stabs thrown into the echo, four keys of chord over them whose
/// sound changes with the section, and eight knobs on the panel that are the performance.
/// </summary>
/// <remarks>
/// The chord is four MIDI Ins on voices 1 to 4 (ADR-0062) and everything worth
/// riding is a panel knob (ADR-0086), left unbound so the player picks a controller.
/// <para>
/// Each knob moves the picture too: feedback is trail length, room is drift,
/// resonance is wobble, cutoff is fog brightness. Each voice is a ring sized by
/// pitch, colored by note name and lit by a Meter, since an envelope has no memory
/// on the screen.
/// </para>
/// <para>
/// The bass line and the stabs follow the keys: the bass the first voice folded into
/// one octave, the stabs every voice folded into the octave over A3. Both play A minor
/// until a key is struck.
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

    private const string NoiseType = NodeCatalog.NoiseTypeId;

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string FractalType = "flyback.picture.fractal";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>The Filter's resonance.</summary>
    private const int FilterResonance = 2;

    /// <summary>The A the bass rests on, an octave and a half under the keyboard's bottom row.</summary>
    private const int BassRoot = 33;

    /// <summary>The A the stabs are folded over.</summary>
    private const int StabRoot = 57;

    /// <summary>The chord the stabs play until a key is struck: A minor seventh.</summary>
    private static readonly int[] Resting = [57, 60, 64, 67];

    /// <summary>The first Arrangement's parts: what plays in each section, and where the keys are sent.</summary>
    private const int Kick = 0, Hats = 1, Rim = 2, Conga = 3, Bass = 4, Stabs = 5, Throw = 6, Wash = 7;

    /// <summary>The second's: the sound the keys play in each section.</summary>
    private const int Attack = 0, Decay = 1, Sustain = 2, Release = 3, Saws = 4, Organ = 5, Tines = 6, Bright = 7;

    /// <summary>
    /// A sound for the keys: an envelope in decades of a second, how much of each of the
    /// three sources, how far the filter opens, and how much goes to the echo and the room.
    /// </summary>
    private readonly record struct Sound(
        float Attack, float Decay, float Sustain, float Release,
        float Saws, float Organ, float Tines, float Bright, float Throw, float Wash);

    private static readonly Sound Pad = new(-0.5f, 0.3f, 0.8f, 0.1f, 1f, 0f, 0f, 0.6f, 0.35f, 0.9f);

    private static readonly Sound Stab = new(-2.5f, -0.8f, 0f, -1f, 1f, 0f, 0f, 1.3f, 1f, 0.5f);

    private static readonly Sound Keys = new(-2.5f, 0.2f, 0.1f, -0.5f, 0f, 0f, 0.6f, 1f, 0.6f, 0.6f);

    private static readonly Sound Bubble = new(-2.3f, -0.8f, 0.35f, -1.2f, 0f, 1f, 0f, 2f, 0.7f, 0.4f);

    /// <summary>
    /// Eight bars a section: intro, the kick and bass arriving, the groove, a dub with
    /// no kick, everything, a breakdown, the peak and the way out. No two sections in a
    /// row give the keys the same sound.
    /// </summary>
    private static readonly Sound[] Sections = [Pad, Stab, Keys, Bubble, Stab, Pad, Bubble, Keys];

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
        var decay = Panel("Decay", 0.5f);
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

        // --- the arrangement -------------------------------------------------

        // What plays in each section. Cut on the downbeat, since a kick faded in loses
        // its first hit.
        var parts = Arranged(beats, 1f / 32f,
            Levels(0, 1, 1, 0, 1, 0, 1, 1),
            Levels(0.4f, 0.7f, 1, 0.5f, 1, 0.3f, 1, 0.6f),
            Levels(0.7f, 1, 1, 1, 1, 0.8f, 1, 1),
            Levels(0, 0, 1, 1, 1, 0, 1, 0.5f),
            Levels(0, 1, 1, 1, 1, 0, 1, 1),
            Levels(1, 0.5f, 1, 0, 0.6f, 1, 1, 0.8f),
            Levels([.. Sections.Select(s => s.Throw)]),
            Levels([.. Sections.Select(s => s.Wash)]));

        // The keys' sound, a third of a second from one to the next so a held chord
        // turns into the new sound rather than clicking.
        var sound = Arranged(beats, 1f / 32f,
            Levels([.. Sections.Select(s => s.Attack)]),
            Levels([.. Sections.Select(s => s.Decay)]),
            Levels([.. Sections.Select(s => s.Sustain)]),
            Levels([.. Sections.Select(s => s.Release)]),
            Levels([.. Sections.Select(s => s.Saws)]),
            Levels([.. Sections.Select(s => s.Organ)]),
            Levels([.. Sections.Select(s => s.Tines)]),
            Levels([.. Sections.Select(s => s.Bright)]));
        sound.InputValues[2] = 0.02f;

        Box("Arrangement");

        // --- the knobs as signals --------------------------------------------

        // A cutoff is heard in octaves, so the knob is an exponent: a hundred and
        // fifty hertz at the bottom and a little over five octaves above it at the
        // top. Thirty milliseconds of Slew takes the steps out of a controller's
        // hundred and twenty-eight values; on the screen a Slew is a wire.
        var cutoffHz = Times(Through("math.exp", Times(Smoothed(cutoff), 3.67f)), 150f);

        // How many times over the envelope opens the filter.
        var pluckDepth = Times(Smoothed(pluck), 6f);

        // The section's decay and release, each lengthened or shortened by the knob.
        var longer = Knobbed("math.add", sound, 0f, Decay);
        Follows(longer, 1, decay, -0.6f, 0.6f);
        var later = Knobbed("math.add", sound, 0f, Release);
        Follows(later, 1, decay, -0.6f, 0.6f);

        Box("Knobs");

        // --- the kick --------------------------------------------------------

        // Four on the floor, low and soft. What is left of the beat to the fifth
        // power is the envelope, and the pitch falls with it.
        var kickLevel = Product(Stroke(beats, 1f, 5f), parts, Kick);
        var kick = Drum(kickLevel, 47f, 110f, 4f, 1.8f);

        // The sidechain: the chords and the bass lean away from the kick, by as much
        // as the kick is up.
        var duck = Ducking(kickLevel, 0f);
        Follows(duck, 3, drums, 0f, 0.55f);

        Box("Kick");

        // --- the hats --------------------------------------------------------

        // Open on the off-beat, and sixteenths under it that come and go on a Wander.
        var hatStroke = Sum(
            Times(Stroke(beats, 1f, 3.5f, 0.5f), 0.7f),
            Product(Times(Stroke(beats, 4f, 8f), 0.3f), Wander(0.07f, 2f, 0.3f)));
        var hats = Hiss(Product(hatStroke, parts, Hats), 9000f, 0.15f, "high");

        Box("Hats");

        // --- the rim ---------------------------------------------------------

        // Two and four, all of it a band of noise, and most of what is heard of it
        // is the echo.
        var rimStroke = Stroke(beats, 0.5f, 12f, 0.5f);
        var rim = Hiss(Product(rimStroke, parts, Rim), 1700f, 0.5f, "band", 2.5f, seed: 1f);

        Box("Rim");

        // --- the conga -------------------------------------------------------

        // Five in sixteen, turned so that it never lands with the kick.
        var congaHits = b.Add(EuclidType, (1, 4f), (2, 16f), (3, 5f), (4, 3f), (EuclidCurve, 6f));
        var conga = b.Add("flyback.voice.drum", (DrumPitch, 185f), (3, 45f), (4, 2f), (5, 0f));

        b.Wire(beats, 0, congaHits, 0)
         .Wire(Formula("a * b", new Read(congaHits, EuclidStroke), new Read(parts, Conga)), 0, conga, 1);

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

            // The section's envelope: a pad, a stab, an electric piano or an organ's bubble.
            var envelope = b.Add(NodeCatalog.AdsrTypeId);

            // Two saws a few cents apart, each voice by its own amount so that a chord
            // does not beat in step, and a square an octave under them.
            var saw = Oscillator("osc.saw", hz, 0.5f);
            var beside = Oscillator("osc.saw", Times(hz, 1.004f + 0.0015f * voice), 0.5f);
            var under = Oscillator("osc.square", Times(hz, 0.5f), 0.3f);

            // Three drawbars of organ, and a tine struck as hard as the envelope is up.
            var drawbars = Formula("a + b * 0.5 + c * 0.25",
                Oscillator("osc.sine", hz), Oscillator("osc.sine", Times(hz, 2f)), Oscillator("osc.sine", Times(hz, 3f)));
            var tine = Bell(hz, envelope, 1f, 1.1f);

            var tone = b.Add(FilterType);
            Follows(tone, FilterResonance, resonance, 0.05f, 0.85f);

            // The knob's cutoff, opened by the envelope and by the section, and held
            // under the top of the Filter's range.
            var opened = Formula("min(a * (b * c + 1) * d, 11000)",
                cutoffHz, envelope, pluckDepth, new Read(sound, Bright));

            b.Wire(keys[voice], 1, envelope, 0)
             .Wire(sound, Attack, envelope, 1)
             .Wire(longer, 0, envelope, 2)
             .Wire(sound, Sustain, envelope, 3)
             .Wire(later, 0, envelope, 4)
             .Wire(Formula("(a + b + c) * d", saw, beside, under, new Read(sound, Saws)), 0, tone, 0)
             .Wire(opened, 0, tone, 1);

            // The tine carries its own envelope and is not filtered.
            var played = Formula("(a + b * c) * d", tone, drawbars, new Read(sound, Organ), envelope);

            // As loud as the key was struck, which a typist's never varies and a
            // keyboard's does.
            var voiced = Product(Formula("a + b * c", played, tine, new Read(sound, Tines)), keys[voice], 2);

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

        // --- the stabs -------------------------------------------------------

        // The chord last struck, folded into the octave over A3, hit on the off-beat of
        // one and of three and left to the echo. Velocity is nought until a key is
        // struck and never again, which holds each note on A minor until then.
        NodeInstance? stabSaws = null;

        for (var voice = 0; voice < Voices; voice++)
        {
            var note = Formula(
                $"mix({Resting[voice]}, (a + {120 - StabRoot}) % 12 + {StabRoot}, step(0.01, b))",
                keys[voice], new Read(keys[voice], 2));
            var stabSaw = Oscillator("osc.saw", Through("audio.note", note));

            stabSaws = stabSaws is null ? stabSaw : Sum(stabSaws, stabSaw);
        }

        // A tenth of a second of chord through a filter that drifts open and shut
        // over half a minute, which is the sound of this music.
        var stabStroke = Stroke(beats, 0.5f, 8f, 0.75f);
        var stabLevel = Product(stabStroke, parts, Stabs);
        var stabTone = b.Add(FilterType, (FilterResonance, 0.45f));
        var stabs = Formula("a * b * 0.6", stabTone, stabLevel);

        b.Wire(stabSaws!, 0, stabTone, 0)
         .Wire(Formula("min(a * (1 + b * 2) * c, 8000)", cutoffHz, stabStroke, Wander(0.03f, 5f, 0.6f, 1.6f)),
             0, stabTone, 1);

        Box("Stabs");

        // --- the bass --------------------------------------------------------

        // The first voice's note folded into the octave over the low A. A hundred and
        // twenty is ten octaves, so adding it changes no pitch class and keeps what
        // is folded over nought.
        var root = Formula(
            $"mix({BassRoot}, (a + {120 - BassRoot}) % 12 + {BassRoot}, step(0.01, b))",
            keys[0], new Read(keys[0], 2));

        // Two bars leaving the one to the kick: up from the root to the fifth, and back
        // down through the third to the seventh and the fifth under it. A rest holds
        // the pitch it follows, so the glide has somewhere to come from.
        var bassLine = b.Add("seq.values", (1, 4f), (2, 0.9f), (3, 0.05f));
        StepsExtra.Set(bassLine,
        [
            new Step(0f, 2f, 0f), new Step(0f, 3f), new Step(0f, 1f, 0f),
            new Step(3f, 2f), new Step(5f, 2f), new Step(7f, 4f), new Step(7f, 2f, 0f),
            new Step(7f, 2f, 0f), new Step(7f, 2f), new Step(5f), new Step(3f),
            new Step(0f, 4f), new Step(0f, 2f, 0f), new Step(-2f, 2f), new Step(-5f, 2f),
        ]);

        // Twenty milliseconds of glide on the hertz: a Note snaps to the semitone, so
        // the slide has to come after it.
        var bassHz = b.Add(SlewType, (1, -1.7f), (2, -1.7f));

        // A sine with a little triangle, driven for the harmonics a small speaker can
        // play, and a filter that opens with each note's thump.
        var body = Formula("a - b * 0.5", Oscillator("osc.sine", bassHz), Oscillator("osc.saw", bassHz));
        var fat = b.Add(DriveType, (1, 5f));
        var bassEnvelope = b.Add(NodeCatalog.AdsrTypeId, (1, -2.5f), (2, -0.6f), (3, 0.7f), (4, -1.3f));
        var bassTone = b.Add(FilterType, (FilterResonance, 0.2f));
        var bassOut = Formula("a * b * c * d * 1.6", bassTone, bassEnvelope, new Read(duck, DuckGain), new Read(parts, Bass));

        b.Wire(beats, 0, bassLine, 0)
         .Wire(Through("audio.note", Sum(bassLine, root)), 0, bassHz, 0)
         .Wire(body, 0, fat, 0)
         .Wire(fat, 0, bassTone, 0)
         .Wire(Formula("350 + a * 900", bassEnvelope), 0, bassTone, 1)
         .Wire(bassLine, 1, bassEnvelope, 0);

        Box("Bass");

        // --- the dust --------------------------------------------------------

        // A record that has been played too often: pink hiss that never stops, and a
        // crackle. The crackle is a Noise a hundred and eighty times a second that
        // lets a burst of its own white through on the few values near the top.
        var hiss = Hiss(null, 4500f, 0f, "low", 0.35f, "pink", 3f);
        var chance = b.Add(NoiseType, (1, 180f), (2, 5f));
        var tick = b.Add("math.step", (0, 0.988f));
        var dust = Sum(hiss, Times(Product(tick, chance), 0.6f));

        b.Wire(chance, 2, tick, 1);

        Box("Dust");

        // --- the space -------------------------------------------------------

        // The echo every record of this kind is made of: three sixteenths and then
        // two more, fed back by the knob, and each side darkened on the way out so
        // that the repeats sit behind what is played. The stabs go in hardest; the
        // keys as hard as their section's sound wants.
        var send = b.Add("math.mixer", (3, 0.5f), (5, 0.4f), (7, 0.9f));
        var taps = Echo(send, beat, 3f, 2f, 0.6f, 1f);
        Follows(taps, EchoFeedback, echo, 0.2f, 0.9f);

        var tapsLeft = b.Add(FilterType, (1, 2200f), (2, 0.1f));
        var tapsRight = b.Add(FilterType, (1, 2200f), (2, 0.1f));

        // The room's size stays where it is, because a delay line that changes length
        // while it rings bends what is in it. The knob is how long it rings and how
        // much of it comes back.
        var roomSend = b.Add("math.mixer", (3, 0.4f), (5, 0.4f), (7, 0.15f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.85f), (3, 1f));
        Follows(room, 2, space, 0.5f, 0.93f);

        b.Wire(warm, 0, send, 0)
         .Wire(parts, Throw, send, 1)
         .Wire(rim, 0, send, 2)
         .Wire(conga, 0, send, 4)
         .Wire(stabs, 0, send, 6)
         .Wire(taps, 0, tapsLeft, 0)
         .Wire(taps, EchoRight, tapsRight, 0)
         .Wire(warm, 0, roomSend, 0)
         .Wire(parts, Wash, roomSend, 1)
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
        var spaceDesk = b.Add(DeskType, (DeskTrim, 0.25f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.9f));

        Channel(drumDesk, 1, 0.8f, kick);
        Channel(drumDesk, 2, 0.45f, hats);
        Channel(drumDesk, 3, 0.35f, rim);
        Channel(drumDesk, 4, 0.35f, conga);

        for (var channel = 1; channel <= 4; channel++) Ridden(drumDesk, channel, drums);

        Channel(musicDesk, 1, 0.75f, bassOut);
        Channel(musicDesk, 2, 0.85f, chordLeft, chordRight);
        Channel(musicDesk, 3, 0.2f, dust);
        Channel(musicDesk, 4, 0.9f, stabs);
        Ridden(musicDesk, 1, bass);

        Channel(spaceDesk, 1, 0.6f, tapsLeft, tapsRight);
        Channel(spaceDesk, 2, 0.45f, room, room, rightFrom: 1);
        Follows(spaceDesk, 5, space, 0.1f, 0.8f);

        b.Wire(Chained(drumDesk, musicDesk, spaceDesk), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(spaceDesk, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: fog ------------------------------------------------

        // Slow Fractal noise in one cold color, with as much light in it as the
        // filter is open, and a flash of it on every stab.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var fog = b.Add(FractalType, (3, 1.6f), (4, 0.55f));
        var light = Times(fog, 0.7f);
        Follows(light, 1, cutoff, 0.5f, 1.5f);

        var cold = b.Add("color.rgb", (0, 0.12f), (1, 0.3f), (2, 0.36f));
        var mist = b.Add("color.gain");

        // The bass is a glow along the bottom of the frame, as high as the Bass knob.
        var low = From(1f, Rises(coord, -1f, -0.3f, 1));
        var glow = Ink(mist, Times(Product(Product(low, bassLine, 1), parts, Bass), bass, 0f, 0.5f), 0.9f, 0.35f, 0.1f);

        b.Wire(Times(clock, 0.05f), 0, fog, 2)
         .Wire(cold, 0, mist, 0)
         .Wire(Sum(light, Times(stabLevel, 0.5f)), 0, mist, 1);

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
            // out. The warm end of the wheel is left to the kick and the bass.
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
        // on the beat and gone when the drums are down or out.
        var kickSeen = Times(kickLevel, drums, 0f, 1f);
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

    /// <summary>An oscillator at a frequency that is a wire.</summary>
    private NodeInstance Oscillator(string type, NodeInstance hz, float amp = 1f)
    {
        var osc = b.Add(type, (3, amp));
        b.Wire(hz, 0, osc, 1);
        return osc;
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
