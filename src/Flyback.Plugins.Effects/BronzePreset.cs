using Flyback.Core.Graph;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A gamelan: sixteen gong cycles on a tempo that breathes, every part written in
/// the degrees of a five-note scale and thinned or figured out of one melody, and
/// a mandala with a ring for each tier of the orchestra.
/// </summary>
/// <remarks>
/// The piece has no tempo knob because it has no one tempo. How many beats have
/// gone by is a closed form of the clock — a steady pace with two sines taken off
/// it — so it crawls at the gong that opens it, runs through the middle and
/// crawls home, and the picture can work out where the music is from the time
/// alone. Nothing accumulates, so a frame drawn at any second is the frame that
/// second sounds like.
/// <para>
/// Everything that strikes is struck by that count. An envelope here is the
/// fraction left of a beat, or of two, four or sixteen of them, raised to a power:
/// it needs no trigger, it lengthens when the music slows the way a player's
/// stroke does, and the screen reads the same number the speakers do.
/// </para>
/// </remarks>
internal sealed class BronzePreset : PresetBench
{
    public const string Name = "Bronze";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string EuclidType = "flyback.voice.euclid";

    private const string SlewType = "flyback.voice.slew";

    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string RandomType = "flyback.voice.random";

    private const string FoldType = "flyback.voice.fold";

    private const string StarType = "flyback.picture.star";

    private const string PolygonType = "flyback.picture.polygon";

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string LayerType = "flyback.picture.layer";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>Beats a second, averaged over the whole piece: ninety a minute.</summary>
    private const float Pace = 1.5f;

    /// <summary>Beats to a gong, and gongs to one breath of the tempo.</summary>
    private const float Cycle = 16f;

    private const float Cycles = 16f;

    /// <summary>
    /// The breath, in radians a second. One turn of it is exactly the piece, so the
    /// sines below are back at nothing on the gong that starts it again.
    /// </summary>
    private const float Breath = MathF.Tau * Pace / (Cycle * Cycles);

    /// <summary>
    /// How far the pace swings, in beats a second. The first takes the tempo from
    /// fifty-one a minute at the ends to over a hundred in the middle; the second, at
    /// twice the rate, flattens that middle into a plateau and steepens the way up.
    /// </summary>
    private const float Swing = 0.45f;

    private const float Settle = 0.2f;

    /// <summary>
    /// Pelog, as five of its seven tones: two steps of a little over a semitone, a
    /// leap of nearly a major third, a small step and a leap home. Frequency ratios
    /// rather than note numbers, because no piano has these.
    /// </summary>
    private static readonly float[] Pelog = [1f, 1.0718f, 1.1688f, 1.4727f, 1.5783f];

    /// <summary>
    /// The melody, a degree to a beat. It leaves the gong tone and spends sixteen
    /// beats coming back down to it.
    /// </summary>
    private static readonly int[] Pokok = [0, 2, 3, 2, 4, 3, 2, 1, 2, 4, 5, 4, 3, 1, 2, 1];

    /// <summary>A Layer's blend mode, written as the state the module reads.</summary>
    private static NodeInstance Mode(NodeInstance layer, string mode)
    {
        layer.SetState("layer", new JsonObject { ["mode"] = mode });
        return layer;
    }

    /// <summary>The melody at every <paramref name="nth"/> beat, which is what a slower instrument plays.</summary>
    private static Step[] Thinned(int nth) =>
        [.. Pokok.Where((_, beat) => beat % nth == 0).Select(degree => new Step(degree))];

    /// <summary>
    /// Two octaves of the scale from <paramref name="root"/>, in hertz. A Sequencer
    /// holding these and read at a degree is the instrument's tuning: what comes out
    /// is a frequency, and no Note module is asked to round it to a piano's.
    /// </summary>
    private static Step[] Keys(float root) =>
        [.. Enumerable.Range(0, 2 * Pelog.Length)
            .Select(key => new Step(root * Pelog[key % Pelog.Length] * (1 << (key / Pelog.Length))))];

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new BronzePreset(modules).Assemble();
    }

    private BronzePreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        // Beats gone by: the steady pace, less the two sines. What is subtracted is
        // the integral of the tempo's swing, which is why each sine is divided by its
        // own rate — and why the tempo itself appears nowhere in the patch.
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var steady = Times(clock, Pace);
        var lean = Sine(Times(clock, Breath));
        var behind = Times(lean, Swing / Breath);
        var leanTwice = Sine(Times(clock, 2f * Breath));
        var behindTwice = Times(leanTwice, Settle / (2f * Breath));
        var beats = Less(Less(steady, behind), behindTwice);

        // The tiers. Each is a Stroke off the count at its own rate: what is left of
        // the stroke, to a power, is the envelope, and how far through it is comes
        // out beside it. The faster tiers are made where they are struck.
        var gongFall = Stroke(beats, 1f / Cycle, 5f);
        var fourFall = Stroke(beats, 0.25f, 3f);
        var twoFall = Stroke(beats, 0.5f, 3f);

        // The two slow counts themselves, which the picture turns by.
        var fourPos = Times(beats, 0.25f);
        var twoPos = Times(beats, 0.5f);

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // One number a gong saying how much orchestra there is. It opens with the
        // gong and the flute alone, fills as the tempo climbs, bursts at the top,
        // drops to almost nothing at full speed — the break a drummer calls — comes
        // back, and thins as it slows. Sixteen gongs and round again.
        var song = b.Add("seq.values", (1, 1f / Cycle));
        StepsExtra.Set(song,
        [
            new Step(0.1f), new Step(0.25f), new Step(0.4f), new Step(0.55f),
            new Step(0.7f), new Step(0.85f), new Step(1f), new Step(1f),
            new Step(0.3f), new Step(0.55f), new Step(0.85f), new Step(1f),
            new Step(1f), new Step(0.7f), new Step(0.4f), new Step(0.25f),
        ]);

        // The whole melody moved up the scale, a gong at a time. Degrees rather than
        // semitones, so what moves stays in the scale and every part that adds this
        // moves with it.
        var lift = b.Add("seq.values", (1, 1f / Cycle));
        StepsExtra.Set(lift, [new Step(0f), new Step(0f), new Step(2f), new Step(1f)]);

        // Irama: where the beat is slow the figuration doubles, so the surface runs
        // at the same speed over a melody half as fast. A Threshold read backwards —
        // the number on 'in', the song on 'edge' — is one when the song is under it.
        var slow = b.Add("math.step", (1, 0.28f));
        var figureRate = Span(slow, 0f, 1f, 4f, 8f);

        // Who is in is a Fade on the one number, made where each part is struck. They
        // change on a gong, where every stroke in the orchestra restarts anyway, so
        // nothing is cut off. The burst at the top is here because the chimes and the
        // cymbals both take it.
        var burst = Enters(Stroke(beats, 1f / Cycle, 70f), song, 0.9f, 0.95f);

        // The flute runs the other way, and fades rather than enters: three seconds
        // up and one and a half down, in decades of a second. On the screen a Slew is
        // a wire.
        var fluteIn = b.Add("math.clamp", (1, 0f), (2, 1f));
        var fluteSwell = b.Add(SlewType, (1, 0.4771f), (2, 0.1761f));

        b.Wire(beats, 0, song, 0)
         .Wire(beats, 0, lift, 0)
         .Wire(song, 0, slow, 0)
         .Wire(Span(song, 0.3f, 0.4f, 1f, 0f), 0, fluteIn, 0)
         .Wire(fluteIn, 0, fluteSwell, 0);

        Box("Arrangement");

        // --- the melody ------------------------------------------------------

        // One list, three speeds: the melody a beat at a time, every second note of
        // it for the instrument an octave down, and every fourth for the one below
        // that. The slow parts are the fast one thinned, so they agree by construction.
        var pokok = Degrees(Thinned(1), 1f);
        var calung = Degrees(Thinned(2), 0.5f);
        var jegog = Degrees(Thinned(4), 0.25f);

        // The figuration is one list too, and two players. It walks three notes —
        // the slow melody's and the two above — and the low pair belong to one
        // player and the high pair to the other, so the middle note is struck by
        // both. Neither part is a tune; the tune is what they make between them.
        var figure = b.Add("seq.values", (2, 0.85f));
        StepsExtra.Set(figure,
        [
            new Step(0f), new Step(1f, 1f, 0.7f), new Step(2f, 1f, 0.8f), new Step(0f, 1f, 0.7f),
            new Step(1f), new Step(2f, 1f, 0.7f), new Step(0f, 1f, 0.8f), new Step(1f, 1f, 0.7f),
            new Step(2f), new Step(1f, 1f, 0.7f), new Step(0f, 1f, 0.8f), new Step(2f, 1f, 0.7f),
            new Step(1f), new Step(0f, 1f, 0.7f), new Step(2f, 1f, 0.8f), new Step(1f, 1f, 0.7f),
        ]);
        var figureDegree = Sum(calung, figure);

        // How far through a figure's stroke it is. Its rate is a wire, so it cannot
        // be a tier above.
        var figureFall = Stroke(beats, 0f, 4f);

        b.Wire(figureRate, 0, figureFall, 1);

        // The flute's line, a note to four beats, high in the same scale.
        var air = b.Add("seq.values", (1, 0.25f), (2, 0.9f), (3, 0.1f));
        StepsExtra.Set(air,
        [
            new Step(2f), new Step(4f), new Step(3f), new Step(1f),
            new Step(2f), new Step(5f), new Step(4f), new Step(3f),
        ]);
        var airDegree = Sum(air, lift);

        b.Wire(beats, 0, figure, 0)
         .Wire(figureRate, 0, figure, 1)
         .Wire(beats, 0, air, 0);

        Box("Melody");

        // --- the gongs -------------------------------------------------------

        // The great gong, on the first beat of sixteen. Two sines a little over a
        // hertz apart, which is the slow wave a real one is tuned to have, and the
        // lower of them bent by a third at an inharmonic ratio while the stroke is
        // fresh. A millisecond or two of rise, as a share of the cycle, because a
        // sine switched on at full height is a click and down here a click is heard.
        var gongStroke = Product(gongFall, Rises(gongFall, 0f, 0.0015f, StrokePhase));
        var gongHz = b.Add("audio.frequency", (0, 69.3f));
        var gong = Sum(
            Bell(gongHz, gongStroke, 2.41f, 0.3f),
            Tone(Plus(gongHz, 1.3f), gongStroke));

        // The smaller gong answers it half way round, a pelog fifth up.
        var kempurStroke = Stroke(beats, 1f / Cycle, 9f, 0.5f);
        var kempur = Bell(b.Add("audio.frequency", (0, 102.1f)), kempurStroke, 2.41f, 0.35f);

        // And the timekeeper: one dry note on every beat, which is what the rest of
        // the orchestra is listening to while the tempo moves.
        var timeStroke = Enters(Stroke(beats, 1f, 16f), song, 0.36f, 0.4f);
        var kempli = Bell(b.Add("audio.frequency", (0, 620f)), timeStroke, 1.41f, 0.6f);

        Box("Gongs");

        // --- the low metal ---------------------------------------------------

        // Every fourth note of the melody, left to ring for the four beats it has.
        // The pair are tuned three and a half hertz apart — the shimmer that makes
        // bronze sound alive rather than struck.
        var jegogStroke = Product(fourFall, Rises(fourFall, 0f, 0.004f, StrokePhase));
        var jegogHz = Tuned(jegog, 138.6f);
        var jegogan = Sum(
            Bell(jegogHz, jegogStroke, 2.76f, 0.3f),
            Tone(Plus(jegogHz, 3.5f), jegogStroke));

        // Every second note, an octave up, the same way and beating faster.
        var calungStroke = Enters(
            Product(twoFall, Rises(twoFall, 0f, 0.006f, StrokePhase)), song, 0.15f, 0.2f);
        var calungHz = Tuned(calung, 277.2f);
        var calungPair = Sum(
            Bell(calungHz, calungStroke, 2.76f, 0.35f),
            Tone(Plus(calungHz, 5f), calungStroke));

        // And the melody itself, a beat a note. 2.76 is the second partial of a bar
        // free at both ends, which is what these keys are.
        var pokokStroke = Enters(Stroke(beats, 1f, 5f), song, 0.15f, 0.2f);
        var ugal = Bell(Tuned(pokok, 554.4f), pokokStroke, 2.76f, 0.45f);

        Box("Low Metal");

        // --- the figuration --------------------------------------------------

        // One stroke and one pitch for both players, since they never need
        // different ones, and an instrument each seven hertz apart. Whose note it is
        // is the step itself: nought is the first player's alone, two the second's,
        // and one is both.
        var figureStroke = Product(Enters(figureFall, song, 0.12f, 0.45f), figure, 1);
        var figureHz = Tuned(figureDegree, 1108.7f);
        var firstHand = b.Add("math.min", (1, 1f));
        var secondHand = b.Add("math.min", (1, 1f));
        var polosStroke = Product(figureStroke, firstHand);
        var sangsihStroke = Product(figureStroke, secondHand);
        var polos = Bell(figureHz, polosStroke, 2.76f, 0.3f);
        var sangsih = Bell(Plus(figureHz, 7f), sangsihStroke, 2.76f, 0.3f);

        b.Wire(From(2f, figure), 0, firstHand, 0)
         .Wire(figure, 0, secondHand, 0);

        Box("Figuration");

        // --- the chimes ------------------------------------------------------

        // The burst: the whole orchestra on the gong, for a twentieth of a second,
        // in the two gongs of each half that are marked full.

        // Five in sixteen, off the beat, a fourth of the scale above the melody. The
        // bell is folded after its envelope, so the fold opens with the stroke and
        // closes as it rings: brass at the front of the note and bronze at the back.
        var chimeHits = b.Add(EuclidType, (1, 4f), (2, 16f), (3, 5f), (4, 2f));
        var chimeStroke = Enters(
            Wired("math.max", Product(Stroke(beats, 4f, 6f), chimeHits, 1), burst), song, 0.6f, 0.7f);
        var chimeBell = Bell(Tuned(Plus(pokok, 3f), 554.4f), chimeStroke, 1.41f, 0.5f);
        var chimes = b.Add(FoldType, (1, 2.2f));

        b.Wire(beats, 0, chimeHits, 0)
         .Wire(chimeBell, 0, chimes, 0);

        Box("Chimes");

        // --- the cymbals -----------------------------------------------------

        // A Random's white, which the flute's breath is a band of as well.
        var hiss = b.Add(RandomType);

        // Every quarter of a beat, with the tresillo leaned on, and the top of a
        // Filter for the sizzle.
        var accents = b.Add(EuclidType, (1, 4f), (2, 8f), (3, 3f));
        var sizzle = b.Add(FilterType, (1, 6500f), (2, 0.3f));
        var cymbalStroke = Enters(
            Sum(Product(Stroke(beats, 4f, 8f), Span(accents, 0f, 1f, 0.3f, 1f, 1)), burst),
            song, 0.5f, 0.55f);
        var cymbals = Product(cymbalStroke, sizzle, 2);

        b.Wire(beats, 0, accents, 0)
         .Wire(hiss, 0, sizzle, 0);

        Box("Cymbals");

        // --- the drums -------------------------------------------------------

        // The pair of drums that lead a gamelan. The lower plays more as the
        // orchestra fills — its hits are the song — and its pitch is its own stroke
        // cubed, so the skin drops as it is let go.
        var lowHits = b.Add(EuclidType, (1, 4f), (2, 16f), (4, 3f));
        var lowStroke = Enters(Product(Stroke(beats, 4f, 4f), lowHits, 1), song, 0.5f, 0.55f);
        var lowDrum = Drum(lowStroke, 82f, 68f, 3f, 0f);

        var highHits = b.Add(EuclidType, (1, 4f), (2, 16f), (3, 5f), (4, 7f));
        var highStroke = Enters(Product(Stroke(beats, 4f, 7f), highHits, 1), song, 0.5f, 0.55f);
        var highDrum = Drum(highStroke, 210f, 120f, 3f, 0f);

        // A Drive for the hand on the skin. It normalizes as it goes, so this is
        // harmonics rather than level.
        var kendang = b.Add(DriveType, (1, 2.5f));

        b.Wire(beats, 0, lowHits, 0)
         .Wire(Span(song, 0.5f, 1f, 3f, 7f), 0, lowHits, 3)
         .Wire(beats, 0, highHits, 0)
         .Wire(Sum(lowDrum, highDrum), 0, kendang, 0);

        Box("Drums");

        // --- the flute -------------------------------------------------------

        // The one voice that is not struck. Its pitch glides — eighty milliseconds
        // either way — its phase is leaned on five times a second, which is vibrato
        // without touching the frequency, and the breath is the hiss through a band
        // an octave over the note.
        var glide = b.Add(SlewType, (1, -1.09691f), (2, -1.09691f));
        var vibrato = b.Add("osc.sine", (1, 5.2f), (3, 0.25f));
        var reed = b.Add("osc.triangle");
        var breathBand = b.Add(FilterType, (2, 0.5f));
        var blown = Sum(reed, Times(breathBand, 0.3f, 1));

        // Tongued by the Sequencer's own gate, slewed so that it is a breath and not
        // a switch, and swelling on a period that shares nothing with the beat.
        var tongue = b.Add(SlewType, (1, -1.2f), (2, -0.7f));
        var lungs = b.Add("osc.sine", (1, 0.11f), (3, 0.25f), (4, 0.75f));
        var suling = Product(Product(blown, Product(tongue, lungs)), fluteSwell);

        b.Wire(Tuned(airDegree, 554.4f), 0, glide, 0)
         .Wire(glide, 0, reed, 1)
         .Wire(vibrato, 0, reed, 2)
         .Wire(hiss, 0, breathBand, 0)
         .Wire(Times(glide, 2f), 0, breathBand, 1)
         .Wire(air, 1, tongue, 0);

        Box("Flute");

        // --- the room --------------------------------------------------------

        // A pavilion: open sides, a hard floor. One Reverb on a send, fully wet, fed
        // most by what should sound furthest away.
        var nearSend = b.Add("math.mixer", (1, 0.9f), (3, 0.35f), (5, 0.35f), (7, 0.4f));
        var farSend = b.Add("math.mixer", (1, 0.35f), (3, 0.25f), (5, 0.3f), (7, 0.2f));
        var room = b.Add(ReverbModule.TypeId, (1, 0.8f), (2, 0.75f), (3, 1f));

        b.Wire(suling, 0, nearSend, 0)
         .Wire(polos, 0, nearSend, 2)
         .Wire(ugal, 0, nearSend, 4)
         .Wire(chimes, 0, nearSend, 6)
         .Wire(sangsih, 0, farSend, 0)
         .Wire(gong, 0, farSend, 2)
         .Wire(calungPair, 0, farSend, 4)
         .Wire(cymbals, 0, farSend, 6)
         .Wire(Sum(nearSend, farSend), 0, room, 0);

        Box("Room");

        // --- the desk --------------------------------------------------------

        // Three Desks of four, chained by their buses. The two sides differ in one
        // thing that matters: the first player is on the left and the second on the
        // right, so the figuration crosses the room a note at a time.
        var low = b.Add(DeskType);
        var middle = b.Add(DeskType);

        // The last is the master. A trim well under unity, because on a gong every
        // stroke in the orchestra lands at once, and rails that should never be
        // reached.
        var top = b.Add(DeskType, (DeskTrim, 0.55f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        Channel(low, 1, 0.32f, gong);
        Channel(low, 2, 0.28f, kempur);
        Channel(low, 3, 0.21f, jegogan);
        Channel(low, 4, 0.33f, kendang);

        Channel(middle, 1, 0.16f, calungPair);
        Channel(middle, 2, 0.31f, ugal);
        Channel(middle, 3, 0.12f, kempli);
        Channel(middle, 4, 0.24f, chimes);

        Channel(top, 1, 0.35f, polos, sangsih);
        Channel(top, 2, 0.3f, cymbals);
        Channel(top, 3, 0.22f, suling);
        Channel(top, 4, 0.17f, room, room, rightFrom: 1);

        b.Wire(Chained(low, middle, top), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(top, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: plane ----------------------------------------------

        // The mandala turns with the count rather than the clock, so it slows when
        // the music does, and the low drum pushes it in a little on every hit.
        var turned = b.Add("space.rotate");
        var plane = b.Add("space.scale");
        var around = b.Add("space.polar");

        b.Wire(Times(beats, 0.04f), 0, turned, 2)
         .Wire(turned, 0, plane, 0)
         .Wire(turned, 1, plane, 1)
         .Wire(Span(lowStroke, 0f, 1f, 1f, 0.94f), 0, plane, 2)
         .Wire(plane, 0, around, 0)
         .Wire(plane, 1, around, 1);

        Box("Picture: Plane");

        // --- the picture: gong -----------------------------------------------

        // The gong is the boss at the middle, swelling when it is struck, and a wave
        // that leaves it: Rings with their offset run backwards by the cycle, so the
        // crests travel out, lit for as long as the gong rings.
        var boss = b.Add(CircleType);
        var bossFill = b.Add(FillType, (1, 0.004f));
        var bossLit = Product(bossFill, Span(gongStroke, 0f, 1f, 0.5f, 1.3f));
        var wave = b.Add("pattern.rings", (2, 5f));
        var crest = Product(Rises(wave, 0.9f, 1f), Times(gongStroke, 0.5f));

        b.Wire(plane, 0, boss, 0)
         .Wire(plane, 1, boss, 1)
         .Wire(Span(gongStroke, 0f, 1f, 0.1f, 0.17f), 0, boss, 2)
         .Wire(boss, 0, bossFill, 0)
         .Wire(plane, 0, wave, 0)
         .Wire(plane, 1, wave, 1)
         .Wire(Times(gongFall, -6f, StrokePhase), 0, wave, 3);

        Box("Picture: Gong");

        // --- the picture: low metal ------------------------------------------

        // Four beats is a square, which turns an eighth with each stroke — square,
        // diamond, square. The turn is the whole strokes gone by plus the start of
        // this one eased in, so it moves as the note sounds and then holds.
        var squareTurn = b.Add("space.rotate");
        var square = b.Add(PolygonType, (2, 0.3f), (3, 4f));
        var squareLine = b.Add(FillType, (1, 0.004f), (2, 0.016f));
        var squareLit = Product(Span(jegogStroke, 0f, 1f, 0.5f, 1.3f), squareLine, 1);

        b.Wire(plane, 0, squareTurn, 0)
         .Wire(plane, 1, squareTurn, 1)
         .Wire(Times(Sum(Floor(fourPos), Rises(fourFall, 0f, 0.25f, StrokePhase)), MathF.PI / 4f), 0, squareTurn, 2)
         .Wire(squareTurn, 0, square, 0)
         .Wire(squareTurn, 1, square, 1)
         .Wire(square, 0, squareLine, 0);

        // Two beats is eight points, turning against the square.
        var starTurn = b.Add("space.rotate");
        var star = b.Add(StarType, (2, 0.43f), (3, 8f), (4, 0.3f));
        var starLine = b.Add(FillType, (1, 0.004f), (2, 0.012f));
        var starLit = Product(Span(calungStroke, 0f, 1f, 0.4f, 1.3f), starLine, 1);

        b.Wire(plane, 0, starTurn, 0)
         .Wire(plane, 1, starTurn, 1)
         .Wire(Times(twoPos, -MathF.PI / 8f), 0, starTurn, 2)
         .Wire(starTurn, 0, star, 0)
         .Wire(starTurn, 1, star, 1)
         .Wire(star, 0, starLine, 0);

        Box("Picture: Low Metal");

        // --- the picture: score ----------------------------------------------

        // The melody, drawn: sixteen beads round a ring, each as far out as its note
        // is high. The angle is cut into sixteen sectors, and a second copy of the
        // melody's Sequencer is read at the sector instead of at the beat — a
        // sequencer follows whatever its 'in' is, and here that is a place rather
        // than a time. So every pixel asks the list what note belongs where it is.
        var sector = Plus(Times(around, 16f / MathF.Tau, 1), 8f);
        var bead = Floor(sector);
        var score = b.Add("seq.values", (1, 1f));
        StepsExtra.Set(score, Thinned(1));
        var beadOut = Span(Sum(score, lift), 0f, 7f, 0.52f, 0.68f);

        // Inside one sector the plane is flat enough to draw on: across is the
        // fraction through the sector, scaled to the arc it spans, and up is how far
        // the radius is from where this bead sits.
        var across = Times(Plus(Fraction(sector), -0.5f), MathF.Tau * 0.58f / 16f);
        var beadShape = b.Add(CircleType, (2, 0.034f));
        var beadFill = b.Add(FillType, (1, 0.004f));

        // The bead being played is the one whose number is the beat's. Both are
        // whole, so under a half apart is the same.
        var playing = Floor(Knobbed("math.mod", beats, Cycle));
        var apart = b.Add("math.abs");
        var elsewhere = b.Add("math.step", (0, 0.5f));
        var struck = Product(From(1f, elsewhere), pokokStroke);
        var beads = Product(beadFill, Span(struck, 0f, 1f, 0.45f, 1.6f));

        b.Wire(bead, 0, score, 0)
         .Wire(across, 0, beadShape, 0)
         .Wire(Less(around, beadOut), 0, beadShape, 1)
         .Wire(beadShape, 0, beadFill, 0)
         .Wire(Less(bead, playing), 0, apart, 0)
         .Wire(apart, 0, elsewhere, 1);

        Box("Picture: Score");

        // --- the picture: figuration -----------------------------------------

        // The interlock, as brickwork: a Checker laid on the polar plane, two courses
        // deep, so the tiles of one player sit between the tiles of the other all the
        // way round. Each course lights when its player strikes, and both on the note
        // they share. There are as many tiles as there are strokes to the cycle, so
        // the wall doubles when the figuration does.
        var bricks = b.Add("pattern.checker", (2, 1f));
        var course = b.Add(CircleType, (2, 0.786f));
        var band = b.Add(FillType, (1, 0.004f), (2, 0.143f));
        var whose = b.Add("math.mix");
        var wall = Product(Span(whose, 0f, 1f, 0.3f, 1.4f), band, 1);

        b.Wire(Product(Times(around, 4f / MathF.PI, 1), figureRate), 0, bricks, 0)
         .Wire(Times(around, 14f), 0, bricks, 1)
         .Wire(plane, 0, course, 0)
         .Wire(plane, 1, course, 1)
         .Wire(course, 0, band, 0)
         .Wire(sangsihStroke, 0, whose, 0)
         .Wire(polosStroke, 0, whose, 1)
         .Wire(bricks, 0, whose, 2);

        Box("Picture: Figuration");

        // --- the picture: rim ------------------------------------------------

        // The cymbals are sixteen needles round the rim and the chimes twelve points
        // inside the score, both drawn only as outlines and only as bright as the
        // stroke that is sounding.
        var needles = b.Add(StarType, (2, 0.98f), (3, 16f), (4, 0.8f));
        var needleLine = b.Add(FillType, (1, 0.004f), (2, 0.008f));
        var spokes = b.Add(StarType, (2, 0.5f), (3, 12f), (4, 0.6f));
        var spokeLine = b.Add(FillType, (1, 0.004f), (2, 0.006f));
        var rim = Sum(
            Product(cymbalStroke, needleLine, 1),
            Product(chimeStroke, spokeLine, 1));

        b.Wire(plane, 0, needles, 0)
         .Wire(plane, 1, needles, 1)
         .Wire(needles, 0, needleLine, 0)
         .Wire(plane, 0, spokes, 0)
         .Wire(plane, 1, spokes, 1)
         .Wire(spokes, 0, spokeLine, 0);

        Box("Picture: Rim");

        // --- the picture: flute ----------------------------------------------

        // The flute is the one thing that is not fixed to the wheel: a soft light
        // that circles on its own, further out the higher it plays.
        var orbit = Times(beats, 0.13f);
        var reach = Span(airDegree, 1f, 7f, 0.3f, 0.85f);
        var placed = b.Add("space.translate");
        var light = b.Add(CircleType, (2, 0.03f));
        var glow = b.Add(FillType, (1, 0.05f));
        var breathLit = Product(glow, fluteIn);

        b.Wire(Product(Through("math.cos", orbit), reach), 0, placed, 2)
         .Wire(Product(Sine(orbit), reach), 0, placed, 3)
         .Wire(placed, 0, light, 0)
         .Wire(placed, 1, light, 1)
         .Wire(light, 0, glow, 0);

        Box("Picture: Flute");

        // --- the picture: cloth ----------------------------------------------

        // Behind it all, kawung: the batik of four petals to a tile, which is a
        // four-pointed Star turned an eighth inside a Tile. It does not turn with the
        // wheel, and the gong lifts it out of the dark.
        var tiles = b.Add("space.tile", (2, 4f));
        var eighth = b.Add("space.rotate", (2, MathF.PI / 4f));
        var petals = b.Add(StarType, (2, 0.85f), (3, 4f), (4, 0.55f));
        var stitch = b.Add(FillType, (1, 0.02f), (2, 0.07f));
        var indigo = b.Add("color.rgb", (0, 0.14f), (1, 0.18f), (2, 0.5f));
        var cloth = b.Add("color.gain");

        b.Wire(tiles, 0, eighth, 0)
         .Wire(tiles, 1, eighth, 1)
         .Wire(eighth, 0, petals, 0)
         .Wire(eighth, 1, petals, 1)
         .Wire(petals, 0, stitch, 0)
         .Wire(indigo, 0, cloth, 0)
         .Wire(Product(Span(gongStroke, 0f, 1f, 0.35f, 0.9f), stitch, 1), 0, cloth, 1);

        Box("Picture: Cloth");

        // --- the picture: metal ----------------------------------------------

        // Everything struck is one ink, gold at the middle and copper at the rim —
        // except the second player's bricks and the flute, which are the green that
        // bronze goes when it is left out in the rain.
        var gold = b.Add("color.rgb", (0, 1f), (1, 0.78f), (2, 0.3f));
        var copper = b.Add("color.rgb", (0, 0.9f), (1, 0.4f), (2, 0.2f));
        var verdigris = b.Add("color.rgb", (0, 0.25f), (1, 0.8f), (2, 0.65f));
        var metal = b.Add("color.mix");
        var brickInk = b.Add("color.mix");
        var struckInk = b.Add("color.gain");
        var wallInk = b.Add("color.gain");
        var fluteInk = b.Add("color.gain");

        b.Wire(gold, 0, metal, 0)
         .Wire(copper, 0, metal, 1)
         .Wire(Rises(around, 0.1f, 1f), 0, metal, 2)
         .Wire(verdigris, 0, brickInk, 0)
         .Wire(gold, 0, brickInk, 1)
         .Wire(bricks, 0, brickInk, 2)
         .Wire(metal, 0, struckInk, 0)
         .Wire(Sum(Sum(Sum(bossLit, crest), Sum(squareLit, starLit)), Sum(beads, rim)), 0, struckInk, 1)
         .Wire(brickInk, 0, wallInk, 0)
         .Wire(wall, 0, wallInk, 1)
         .Wire(verdigris, 0, fluteInk, 0)
         .Wire(breathLit, 0, fluteInk, 1);

        var fresh = Mode(b.Add(LayerType), "add");

        b.Wire(Sum(Sum(struckInk, wallInk), fluteInk), 0, fresh, 0)
         .Wire(cloth, 0, fresh, 1);

        Box("Picture: Metal");

        // --- the picture: ringing --------------------------------------------

        // Bronze rings, so the picture does. The last frame is read from a little
        // nearer the middle than where it is drawn, which moves everything in it
        // outwards: each stroke leaves the wheel as a fading copy of itself. A long
        // memory while the orchestra is thin and a short one when it is full.
        // The brighter of the two rather than a blend, so an echo brighter than the
        // new frame keeps its brightness and reads as a wake.
        var printed = b.Add(TrailsType, (TrailsZoom, 0.982f), (TrailsAngle, -0.008f));

        // Here for 'radius', which darkens the corners, and graded by the song last
        // so the opening is muted and the burst is not.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var vignette = b.Add("math.clamp", (1, 0f), (2, 1f));
        var shaded = b.Add("color.gain");
        var graded = b.Add(GradeType, (2, 1.1f));

        b.Wire(fresh, 0, printed, 0)
         .Wire(Span(song, 0f, 1f, 0.92f, 0.8f), 0, printed, TrailsPersist)
         .Wire(Span(coord, 0.4f, 1.7f, 1f, 0.3f, 2), 0, vignette, 0)
         .Wire(printed, 0, shaded, 0)
         .Wire(vignette, 0, shaded, 1)
         .Wire(shaded, 0, graded, 0)
         .Wire(Span(song, 0f, 1f, 0.8f, 1.3f), 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Ringing");

        return b.Build();

        // A part: its list of degrees read at the beat, moved by the gong's lift.
        NodeInstance Degrees(Step[] steps, float rate)
        {
            var list = b.Add("seq.values", (1, rate));
            StepsExtra.Set(list, steps);
            b.Wire(beats, 0, list, 0);
            return Sum(list, lift);
        }

        // A degree to a frequency, through the instrument's keys.
        NodeInstance Tuned(NodeInstance degree, float root)
        {
            var keys = b.Add("seq.values", (1, 1f));
            StepsExtra.Set(keys, Keys(root));
            b.Wire(degree, 0, keys, 0);
            return keys;
        }
    }
}
