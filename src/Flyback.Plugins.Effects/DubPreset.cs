using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Roots dub to be played rather than listened to: a one drop, a bass line and a skank
/// that drop in and out every few bars and are thrown into the echo, four keys of organ
/// over them, and six knobs on the panel that are the performance.
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
/// The whole piece is in A minor, the riddim going between A minor and D minor two bars
/// at a time. Every key is snapped to the scale, and the computer keyboard is laid out in it.
/// </para>
/// </remarks>
internal sealed class DubPreset : PresetBench
{
    public const string Name = "Dub";

    public const int Voices = 4;

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    private const string Mastering = "flyback.mastering";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string DriveType = NodeCatalog.DriveTypeId;

    private const string NoiseType = NodeCatalog.NoiseTypeId;

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string FractalType = "flyback.picture.fractal";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>The Filter's resonance.</summary>
    private const int FilterResonance = 2;

    /// <summary>The key the piece is in, and the keys are held to.</summary>
    private static readonly KeyboardScale Key = new(9, "aeolian");

    /// <summary>The A the bass line is written over, an octave and a half under the keyboard's bottom row.</summary>
    private const int BassRoot = 33;

    /// <summary>
    /// The two sentences of Jamaican Patois, by Nesnad on Wikimedia Commons, under
    /// CC BY-SA 3.0: that nothing he says makes sense, and a father angry to see his
    /// daughter with a dread. This assembly's resources (see <see cref="PresetFiles.Embedded"/>).
    /// </summary>
    private const string NoSense = "no-sense.wav";

    private const string Dread = "dread.wav";

    /// <summary>The Tune's note number, after its frequency.</summary>
    private const int TunedNote = 1;

    /// <summary>The skank's chord over A, A minor seventh, and how far each note falls for D minor seventh.</summary>
    private static readonly int[] Skank = [57, 60, 64, 67];

    private static readonly int[] Falls = [0, 0, 2, 2];

    /// <summary>The Arrangement's parts: what plays in each section, how hard it is thrown into the echo, and the rolls.</summary>
    private const int Kick = 0, Rim = 1, Hats = 2, Hand = 3, Bass = 4, Skanks = 5, Throw = 6, Rolls = 7;

    /// <summary>
    /// Two bars a section, a riddim that never stops for long: a skank and the rim to open
    /// with, the drums and bass coming in behind a roll, then a mix that takes a part out
    /// for two bars and puts it back, leaving the bass alone, the drums alone or the rim
    /// and the bass, and flashing the skank into the echo. It ends with everything thrown
    /// into the echo and two bars of nothing but the echo.
    /// </summary>
    private static readonly float[][] Mix =
    [
        [0, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0],
        [1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0],
        [0, 1, 1, 1, 1, 1, 0, 1, 0.6f, 0.6f, 1, 1, 0, 1, 1, 0.5f, 1, 1, 1, 1, 0, 0, 0.5f, 1, 1, 1, 0, 1, 1, 0.5f, 0, 0],
        [0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 0, 0, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0],
        [0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0],
        [1, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0.5f, 0, 0, 1, 0, 1, 1, 1, 0, 1, 0, 0, 0.5f, 1, 1, 0, 0, 1, 1, 1, 1, 0],
        [0.8f, 0.6f, 0.3f, 0.3f, 0.3f, 0.5f, 0.6f, 0.4f, 0.5f, 0.8f, 1, 0.5f, 0.7f, 0.4f, 0.5f, 1,
         0.3f, 0.3f, 0.6f, 0.4f, 0.5f, 0.5f, 1, 1, 0.4f, 0.5f, 0.7f, 0.4f, 0.5f, 0.7f, 1, 1],
        [0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0],
    ];

    /// <summary>The piece's length in beats: thirty-two sections of two bars.</summary>
    private const int Beats = 256;

    private const float Tempo = 74f;

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        if (!modules.HasProvider(Mastering))
            throw new InvalidOperationException(
                $"it needs the Mastering plugin ({Mastering}), which is not installed.");

        return new DubPreset(modules).Assemble();
    }

    private DubPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the panel -------------------------------------------------------

        // Each rests where the patch sounds like the genre with nobody touching it.
        var cutoff = Panel("Cutoff", 0.45f);
        var resonance = Panel("Resonance", 0.35f);
        var pluck = Panel("Pluck", 0.5f);
        var decay = Panel("Decay", 0.5f);
        var echo = Panel("Echo", 0.6f);
        var space = Panel("Space", 0.5f);

        // --- the clock -------------------------------------------------------

        // Seventy-four a minute, where roots dub sits. Every part reads the count of
        // beats, so the drums are envelopes off the grid rather than things triggered on it.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, Tempo));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // Cut on the downbeat of each section, the way a desk's mutes are.
        var mix = Arranged(beats, 1f / 8f, [.. Mix.Select(Levels)]);

        // D minor for the second two bars of every four, A minor for the first.
        var onD = Formula("step(1, a * 0.125 % 2)", beats);

        Box("Arrangement");

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

        // The one drop: nothing on the one, and the kick on three with the rim. Low and
        // round, the pitch falling with what is left of the stroke.
        var kickLevel = Formula("pow(1 - fract(a * 0.25 + 0.5), 20) * b", beats, new Read(mix, Kick));
        var kick = Drum(kickLevel, 52f, 120f, 5f, 1.5f);

        // The sidechain: the chords and the bass lean away from the kick, by as much
        // as the kick is up.
        var duck = Ducking(kickLevel, 0.44f);

        Box("Kick");

        // --- the rim ---------------------------------------------------------

        // A cross-stick on three: a click of noise and a knock under it. Most of what
        // is heard of it is the echo.
        var rimStroke = Formula("pow(1 - fract(a * 0.25 + 0.5), 45) * b", beats, new Read(mix, Rim));
        var rim = Sum(
            Hiss(rimStroke, 2200f, 0.5f, "band", 2.5f, seed: 1f),
            Times(Drum(rimStroke, 380f, 60f, 6f, 0.5f), 0.5f));

        Box("Rim");

        // --- the hats and the shaker ------------------------------------------

        // Eighths swung to the triplet, the one after the beat the louder, played a
        // little harder or softer on a Wander. Under them a shaker's sixteenths,
        // leaning on the "and".
        var hatLevel = Formula("(pow(1 - fract(a), 14) * 0.5 + pow(1 - fract(a + 1 / 3), 14)) * b * c",
            beats, Wander(0.1f, 2f, 0.6f), new Read(mix, Hats));
        var shake = Formula("pow(1 - fract(a * 4), 6) * (0.35 + pow(1 - fract(a + 0.5), 3) * 0.65) * b",
            beats, new Read(mix, Hats));
        var hats = Sum(Hiss(hatLevel, 9000f, 0.15f, "high"), Hiss(shake, 6000f, 0.3f, "band", 0.6f, seed: 4f));

        Box("Hats");

        // --- the hand drum and the rolls -------------------------------------

        // The funde's heartbeat: a light stroke just before one and three, and the
        // heavy one on them.
        var heartbeat = Formula("(pow(1 - fract(a * 0.5), 9) + pow(1 - fract(a * 0.5 + 0.125), 9) * 0.5) * b",
            beats, new Read(mix, Hand));
        var hand = Drum(heartbeat, 140f, 45f, 2f, 0f);

        // Down the toms in sixteenths through the last beat of a section, into the next.
        var roll = Formula("pow(1 - fract(a * 4), 5) * step(7, a % 8) * b", beats, new Read(mix, Rolls));
        var toms = b.Add("flyback.voice.drum", (3, 60f), (4, 3f), (5, 0.3f));

        b.Wire(roll, 0, toms, 1)
         .Wire(Formula("220 - floor(fract(a) * 4) * 35", beats), 0, toms, DrumPitch);

        var percussion = Sum(hand, toms);

        Box("Percussion");

        // --- the voices ------------------------------------------------------

        var keys = new NodeInstance[Voices];
        var tuned = new NodeInstance[Voices];
        var heard = new NodeInstance[Voices];
        var chord = b.Add("math.mixer", (1, 0.8f), (3, 0.8f), (5, 0.8f), (7, 0.8f));

        for (var voice = 0; voice < Voices; voice++)
        {
            keys[voice] = b.Add(NodeCatalog.MidiTypeId);
            keys[voice].SetState(
                MidiExtra.StateKey, new JsonObject { [MidiExtra.IndexField] = (float)(voice + 1) });

            // A key off the scale plays the nearest note on it.
            var hz = tuned[voice] = InKey(keys[voice], [.. Key.Row.Select(note => note % 12)]);

            // An organ: one knob from the short bubble of a reggae organ to one held
            // down, the decay, the sustain it falls to and the release lengthening together.
            var envelope = b.Add(NodeCatalog.AdsrTypeId, (1, -2.3f));
            Follows(envelope, 2, decay, -1.2f, 0.2f);
            Follows(envelope, 3, decay, 0.3f, 0.9f);
            Follows(envelope, 4, decay, -1.3f, -0.3f);

            // Four drawbars: the note, its octave, the twelfth and two octaves up.
            var drawbars = Formula("a + b * 0.5 + c * 0.35 + d * 0.3",
                Oscillator("osc.sine", hz), Oscillator("osc.sine", Times(hz, 2f)),
                Oscillator("osc.sine", Times(hz, 3f)), Oscillator("osc.sine", Times(hz, 4f)));

            var tone = b.Add(FilterType);
            Follows(tone, FilterResonance, resonance, 0.05f, 0.85f);

            // The knob's cutoff, opened by the envelope, and held under the top of the
            // Filter's range.
            var opened = Formula("min(a * (b * c + 1) * 2, 11000)", cutoffHz, envelope, pluckDepth);

            b.Wire(keys[voice], 1, envelope, 0)
             .Wire(drawbars, 0, tone, 0)
             .Wire(opened, 0, tone, 1);

            // As loud as the key was struck, which a typist's never varies and a
            // keyboard's does.
            var voiced = Formula("a * b * c", tone, envelope, new Read(keys[voice], 2));

            // The voice as the picture knows it: a twelfth of a second of loudness.
            heard[voice] = b.Add(NodeCatalog.MeterTypeId, (1, -1.1f), (2, 0.15f));

            b.Wire(voiced, 0, heard[voice], 0)
             .Wire(voiced, 0, chord, voice * 2);

            Box($"Voice {voice + 1}");
        }

        // --- the chord -------------------------------------------------------

        // A little Drive for the warmth of a worn sampler, and a Chorus for the turning
        // of a Leslie.
        var warm = b.Add(DriveType, (1, 1.5f));
        var wide = b.Add(ChorusModule.TypeId, (1, 0.3f), (2, 0.5f), (3, 0.5f));
        var chordLeft = Product(wide, duck, DuckGain);
        var chordRight = Wired("math.mul", wide, duck, 1, DuckGain);

        b.Wire(chord, 0, warm, 0)
         .Wire(warm, 0, wide, 0);

        Box("Chord");

        // --- the skank -------------------------------------------------------

        // A minor seventh, and D minor seventh with two notes moved down a tone.
        NodeInstance? skankSaws = null;

        for (var voice = 0; voice < Voices; voice++)
        {
            var note = Formula($"{Skank[voice]} - a * {Falls[voice]}", onD);
            var skankSaw = Oscillator("osc.saw", Through("audio.note", note));

            skankSaws = skankSaws is null ? skankSaw : Sum(skankSaws, skankSaw);
        }

        // A tenth of a second of chord through a filter that drifts open and shut over
        // half a minute: one skank a bar, on four, and the echo plays the rest.
        var skankStroke = Stroke(beats, 0.25f, 28f, 0.25f);
        var skankLevel = Product(skankStroke, mix, Skanks);
        var skankTone = b.Add(FilterType, (FilterResonance, 0.45f));
        var skank = Formula("a * b * 0.6", skankTone, skankLevel);

        b.Wire(skankSaws!, 0, skankTone, 0)
         .Wire(Formula("min(a * (1 + b * 2) * c, 8000)", cutoffHz, skankStroke, Wander(0.03f, 5f, 0.6f, 1.6f)),
             0, skankTone, 1);

        Box("Skank");

        // --- the bass --------------------------------------------------------

        // Two bars leaving the one to the kick: up from the root to the fifth, and back
        // down through the third to the seventh and the fifth under it, over A and then
        // over D. A rest holds the pitch it follows, so the glide has somewhere to come from.
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

        // A sine with a little saw, driven for the harmonics a small speaker can play,
        // and a filter that opens with each note's thump.
        var body = Formula("a - b * 0.5", Oscillator("osc.sine", bassHz), Oscillator("osc.saw", bassHz));
        var fat = b.Add(DriveType, (1, 5f));
        var bassEnvelope = b.Add(NodeCatalog.AdsrTypeId, (1, -2.5f), (2, -0.6f), (3, 0.7f), (4, -1.3f));
        var bassTone = b.Add(FilterType, (FilterResonance, 0.2f));
        var bassOut = Formula("a * b * c * d * 1.6", bassTone, bassEnvelope, new Read(duck, DuckGain), new Read(mix, Bass));

        b.Wire(beats, 0, bassLine, 0)
         .Wire(Through("audio.note", Formula($"a + {BassRoot} + b * 5", bassLine, onD)), 0, bassHz, 0)
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

        // --- the voice -------------------------------------------------------

        // Two sentences of Patois, each read from the moment it is due so it lands on
        // its bar, and silent before and after its clip. Each is dry at its start so the
        // words are heard, and more and more of it goes into an echo of its own as it
        // goes, the words giving way to the repeats.
        NodeInstance Spoken(string file, string due, out NodeInstance thrown)
        {
            var at = Formula(due, beats, beat);
            var line = b.Add(NodeCatalog.SampleTypeId, (1, 2f));
            SampleExtra.Set(line, file);

            b.Wire(at, 0, line, 0);
            thrown = Formula("smoothstep(0.3, 1, a / b)", at, new Read(line, 1));
            return line;
        }

        // The first in the second bar and again where only the rim and the bass play,
        // the second over the bass alone and where the kick next drops out.
        var first = Spoken(NoSense, "(a % 256 - 4 - step(96, a % 256) * 92) / b", out var firstThrown);
        var second = Spoken(Dread, "(a % 256 - 160 - step(208, a % 256) * 48) / b", out var secondThrown);

        var said = Formula("a * (1 - b * 0.6) + c * (1 - d * 0.6)", first, firstThrown, second, secondThrown);
        var voiceTaps = Echo(
            Formula("a * b + c * d", first, firstThrown, second, secondThrown), beat, 3f, 2f, 0.82f, 1f);
        var voiceLeft = b.Add(FilterType, (1, 1800f), (2, 0.2f));
        var voiceRight = b.Add(FilterType, (1, 1800f), (2, 0.2f));

        b.Wire(voiceTaps, 0, voiceLeft, 0)
         .Wire(voiceTaps, EchoRight, voiceRight, 0);

        Box("Voice");

        // --- the space -------------------------------------------------------

        // The echo every record of this kind is made of: three sixteenths and then
        // two more, fed back by the knob, and each side darkened on the way out so
        // that the repeats sit behind what is played. The skank goes in hardest; the
        // organ and the rim as hard as the mix throws them.
        var send = b.Add("math.mixer", (5, 0.4f), (7, 0.9f));
        var taps = Echo(send, beat, 3f, 2f, 0.6f, 1f);
        Follows(taps, EchoFeedback, echo, 0.2f, 0.9f);

        var tapsLeft = b.Add(FilterType, (1, 2200f), (2, 0.1f));
        var tapsRight = b.Add(FilterType, (1, 2200f), (2, 0.1f));

        // The room's size stays where it is, because a delay line that changes length
        // while it rings bends what is in it. The knob is how long it rings and how
        // much of it comes back.
        var roomSend = b.Add("math.mixer", (1, 0.5f), (3, 0.4f), (5, 0.4f), (7, 0.15f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.85f), (3, 1f));
        Follows(room, 2, space, 0.5f, 0.93f);

        b.Wire(warm, 0, send, 0)
         .Wire(mix, Throw, send, 1)
         .Wire(rim, 0, send, 2)
         .Wire(mix, Throw, send, 3)
         .Wire(percussion, 0, send, 4)
         .Wire(skank, 0, send, 6)
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
        var spaceDesk = b.Add(DeskType, (DeskTrim, 0.25f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.9f));

        Channel(drumDesk, 1, 0.8f, Times(kick, 2f));
        Channel(drumDesk, 2, 0.8f, hats);
        Channel(drumDesk, 3, 0.7f, rim);
        Channel(drumDesk, 4, 0.7f, percussion);

        Channel(musicDesk, 1, 0.75f, bassOut);
        Channel(musicDesk, 2, 0.85f, chordLeft, chordRight);
        Channel(musicDesk, 3, 0.2f, dust);
        Channel(musicDesk, 4, 0.9f, skank);

        Channel(spaceDesk, 1, 0.6f, tapsLeft, tapsRight);
        Channel(spaceDesk, 2, 0.45f, room, room, rightFrom: 1);
        Follows(spaceDesk, 5, space, 0.1f, 0.8f);
        Channel(spaceDesk, 3, 1f, said);
        Channel(spaceDesk, 4, 1f, voiceLeft, voiceRight);

        Chained(drumDesk, musicDesk, spaceDesk);

        Box("Desk", output);

        // --- the master ------------------------------------------------------

        // Nothing under thirty hertz, a little more weight under the bass, the mud
        // taken out of the low middle and air on top; then wider, with the kick and
        // the bass kept in the middle; then glued and held under a decibel.
        var equalized = b.Add("flyback.mastering.eq",
            (2, 30f), (3, 90f), (4, 1.5f), (5, 350f), (6, -2f), (7, 0.8f), (8, 9000f), (9, 2.5f));
        var wider = b.Add("flyback.mastering.width", (2, 1.2f), (3, 150f));
        var glued = b.Add("flyback.mastering.maximizer", (2, 0.4f), (3, 1f));

        b.Wire(spaceDesk, 0, equalized, 0)
         .Wire(spaceDesk, 1, equalized, 1)
         .Wire(equalized, 0, wider, 0)
         .Wire(equalized, 1, wider, 1)
         .Wire(wider, 0, glued, 0)
         .Wire(wider, 1, glued, 1)
         .Wire(glued, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(glued, 1, output, NodeCatalog.OutputRightPort);

        Box("Master", output);

        // --- the picture: fog ------------------------------------------------

        // Slow Fractal noise in one cold color, with as much light in it as the
        // filter is open, and a flash of it on every skank.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var fog = b.Add(FractalType, (3, 1.6f), (4, 0.55f));
        var light = Times(fog, 0.7f);
        Follows(light, 1, cutoff, 0.5f, 1.5f);

        var cold = b.Add("color.rgb", (0, 0.12f), (1, 0.3f), (2, 0.36f));
        var mist = b.Add("color.gain");

        // The bass is a glow along the bottom of the frame.
        var low = From(1f, Rises(coord, -1f, -0.3f, 1));
        var glow = Ink(mist, Times(Product(Product(low, bassLine, 1), mix, Bass), 0.4f), 0.9f, 0.35f, 0.1f);

        b.Wire(Times(clock, 0.05f), 0, fog, 2)
         .Wire(cold, 0, mist, 0)
         .Wire(Sum(light, Times(skankLevel, 0.5f)), 0, mist, 1);

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
            // down. The line thickens with the level too, so a struck chord is seen to land.
            var level = Wired(
                "math.max", Knobbed("math.min", heard[voice], 1.5f), Times(keys[voice], 0.25f, 1));

            // Which of the twelve notes it is, as a hue between teal and magenta, so a
            // note is always the color it was and an octave is the same color further
            // out. The warm end of the wheel is left to the kick and the bass.
            var tint = b.Add("color.hsv", (1, 0.45f), (2, 1f));
            var lit = b.Add("color.gain");

            b.Wire(coord, 0, ring, 0)
             .Wire(coord, 1, ring, 1)
             .Wire(Knobbed("math.max", Span(tuned[voice], 48f, 84f, 0.14f, 1f, TunedNote), 0.1f), 0, ring, 2)
             .Wire(Sum(ring, wobble), 0, drawn, 0)
             .Wire(Plus(Times(level, 0.025f), 0.004f), 0, drawn, 2)
             .Wire(Span(Fraction(Times(tuned[voice], 1f / 12f, TunedNote)), 0f, 1f, 0.47f, 0.87f), 0, tint, 0)
             .Wire(tint, 0, lit, 0)
             .Wire(Product(level, drawn, 1), 0, lit, 1);

            rings = rings is null ? lit : Sum(rings, lit);
        }

        Box("Picture: Rings");

        // --- the picture: scene ----------------------------------------------

        // The kick is a disc in the middle that every ring is drawn round, swelling
        // on the beat and gone while the kick is out.
        var kickSeen = Times(kickLevel, 0.8f);
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

        b.Patch.Keyboard = Key;
        b.Patch.Length = Math.Round(Beats * 60.0 / Tempo, 2);

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
}
