using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole chiptune track — a hundred and four bars of a game's first level, from the
/// title screen to the key change — played on the four voices a console had, then
/// mastered the way nobody could master them at the time, under a side-scroller drawn a
/// pixel at a time.
/// </summary>
/// <remarks>
/// The voices are the console's and are built the way its chip built them. The two
/// melodic ones are pulses with the three widths it had — an eighth, a quarter and a
/// half — so a change of part is a change of width rather than of patch. The bass is a
/// triangle read through sixteen steps, which is the buzz under a real one. And the drums
/// are noise that is held for a sample of a slower clock rather than being white, which
/// is what makes chip noise crunch instead of hiss. Chords are what the chip could not
/// play, so as it did, the arp plays one a note at a time at thirty-seconds, fast enough
/// that the ear hears the chord.
/// <para>
/// The harmony is written three times, once for each progression, as twenty-four bars
/// on one list, and the arrangement chooses among them by moving the list's input: the
/// count of beats into the phrase, plus thirty-two for each progression over the first.
/// A melody is a list of its own, and a lane says which melody, if any, the lead plays.
/// Nothing but the key change moves every part at once, and it is one number added to
/// every pitch before its Note.
/// </para>
/// <para>
/// What is new is the end of the chain. Everything pitched goes through a Duck keyed by
/// the kick, so the music ducks under every hit; then the drums are added
/// back, and an EQ, a Width and a Limiter finish it. The width has something to widen
/// because the arp leans left and the harmony right, as two pulse channels on a pair of
/// speakers would. It is a Limiter rather than the Maximizer because the Maximizer is
/// three compressors and a limiter on each side, which is as many ops again as the
/// three melodies, and the sound has to run live.
/// </para>
/// </remarks>
internal sealed class OverworldPreset : PresetBench
{
    public const string Name = "Overworld";

    /// <summary>The three plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    private const string Mastering = "flyback.mastering";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string NoiseType = NodeCatalog.NoiseTypeId;

    private const string BoxType = "flyback.picture.box";

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    private const string CellsType = "flyback.picture.cells";

    private const string PosteriseType = "flyback.picture.posterise";

    private const string EqType = "flyback.mastering.eq";

    private const string WidthType = "flyback.mastering.width";

    private const string LimiterType = "flyback.mastering.limiter";

    /// <summary>A Filter's third output: what is over the cutoff. A Noise's held value.</summary>
    private const int High = 2;

    private const int Held = 2;

    /// <summary>A sequencer's second output: up while a step sounds.</summary>
    private const int Gate = 1;

    /// <summary>E natural minor, which every melody is written in and the harmony is snapped to.</summary>
    private static readonly int[] Scale = [4, 6, 7, 9, 11, 0, 2];

    /// <summary>
    /// How many rows of pixels the frame is. Everything drawn reads coordinates that have
    /// been rounded to the middle of one of these, so every shape comes out in blocks.
    /// </summary>
    private const float Rows = 45f;

    /// <summary>A tile of the ground, eight pixels, in the picture's own units.</summary>
    private const float Tile = 8f / Rows;

    /// <summary>Where the grass meets the air, and where the hero's feet are.</summary>
    private const float GroundTop = -0.6f;

    /// <summary>How far from the left the hero runs.</summary>
    private const float HeroX = -0.95f;

    /// <summary>
    /// The first melody, for the verses: a note and how many eighths it lasts. It climbs the
    /// Em chord, falls through C, reaches for the high B over G, and ends on the D sharp that
    /// the B major under it needs to pull home.
    /// </summary>
    private static readonly (float Note, float Eighths)[] Verse =
    [
        (71, 2), (76, 2), (79, 2), (78, 1), (76, 1),
        (79, 3), (76, 1), (72, 2), (76, 2),
        (74, 2), (79, 2), (83, 3), (81, 1),
        (81, 2), (78, 2), (74, 4),
        (71, 2), (76, 2), (79, 2), (83, 2),
        (84, 3), (83, 1), (79, 2), (76, 2),
        (81, 3), (79, 1), (78, 2), (81, 2),
        (75, 3), (78, 1), (81, 2), (78, 2),
    ];

    /// <summary>
    /// The second, for the choruses: higher, on longer notes, peaking on the E over C and
    /// running up the B major at the end so the phrase comes round again.
    /// </summary>
    private static readonly (float Note, float Eighths)[] Chorus =
    [
        (76, 2), (79, 2), (84, 3), (83, 1),
        (83, 2), (81, 2), (78, 2), (81, 2),
        (83, 4), (81, 2), (79, 2),
        (83, 2), (86, 2), (83, 2), (79, 2),
        (88, 4), (86, 2), (84, 1), (83, 1),
        (86, 3), (84, 1), (83, 2), (81, 2),
        (87, 3), (83, 1), (78, 2), (83, 2),
        (83, 4), (78, 1), (81, 1), (83, 1), (87, 1),
    ];

    /// <summary>
    /// The third, for the night: slow, on the bridge's A minor, with an F major that is not
    /// in the key and is the one moment the level turns strange.
    /// </summary>
    private static readonly (float Note, float Eighths)[] Night =
    [
        (69, 4), (72, 2), (76, 2),
        (71, 6), (67, 2),
        (76, 4), (79, 2), (76, 2),
        (75, 6), (78, 2),
        (72, 4), (76, 2), (81, 2),
        (79, 6), (76, 2),
        (77, 4), (81, 2), (84, 2),
        (83, 4), (78, 2), (75, 2),
    ];

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

        return new OverworldPreset(modules).Assemble();
    }

    private OverworldPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    /// <summary>One number for each eight bars, read off the count of beats.</summary>
    private NodeInstance Lane(NodeInstance count, params float[] phrases)
    {
        var lane = b.Add("seq.values", (1, 1f / 32f));
        StepsExtra.Set(lane, phrases.Select(p => new Step(p)));
        b.Wire(count, 0, lane, 0);
        return lane;
    }

    /// <summary>One where <paramref name="lane"/> is the whole number <paramref name="value"/>, nought elsewhere.</summary>
    private NodeInstance Is(NodeInstance lane, float value) =>
        Formula(FormattableString.Invariant($"step({value - 0.5f}, a) * step(a, {value + 0.5f})"), lane);

    /// <summary>
    /// A melody as a Note Sequencer that counts in eighths. Only its pitch is read: the
    /// lead is struck by a change of pitch, so a melody's gate would be an uneven list's
    /// worth of ops for nothing.
    /// </summary>
    private NodeInstance Melody(NodeInstance beats, (float Note, float Eighths)[] notes)
    {
        var melody = b.Add("seq.notes", (1, 2f));
        StepsExtra.Set(melody, notes.Select(n => new Step(n.Note, n.Eighths)));
        b.Wire(beats, 0, melody, 0);
        return melody;
    }

    /// <summary>A Sequencer of ordinary values at <paramref name="rate"/> steps a beat.</summary>
    private NodeInstance Pattern(NodeInstance beats, float rate, params float[] values)
    {
        var pattern = b.Add("seq.values", (1, rate), (2, 0.75f), (3, 0.02f));
        StepsExtra.Set(pattern, values.Select(v => new Step(v)));
        b.Wire(beats, 0, pattern, 0);
        return pattern;
    }

    /// <summary>
    /// A pulse at a frequency, with its width a number or a wire, and centered on nought.
    /// </summary>
    /// <remarks>
    /// A pulse an eighth wide sits three quarters of the way down on average, and an
    /// envelope on it turns that offset into a thump at the rate of the envelope. The
    /// bias that takes it out is twice the width less one.
    /// </remarks>
    private NodeInstance Pulse(NodeInstance hz, float width, NodeInstance? swept = null)
    {
        var pulse = b.Add("osc.pulse", (3, width), (5, 2f * width - 1f));
        b.Wire(hz, 0, pulse, 1);
        if (swept is not null) b.Wire(swept, 0, pulse, 3).Wire(Formula("a * 2 + -1", swept), 0, pulse, 5);
        return pulse;
    }

    /// <summary>A Filter with its cutoff a number.</summary>
    private NodeInstance Filtered(NodeInstance a, float cutoff, float resonance)
    {
        var filter = b.Add(FilterType, (1, cutoff), (2, resonance));
        b.Wire(a, 0, filter, 0);
        return filter;
    }

    /// <summary>
    /// A solid rectangle in the pixel grid: a Box <paramref name="halfWidth"/> by
    /// <paramref name="halfHeight"/> from its middle at (<paramref name="x"/>, <paramref name="y"/>),
    /// filled with an edge too hard to fall between two pixels.
    /// </summary>
    private NodeInstance Block(NodeInstance px, NodeInstance py, float x, float y, float halfWidth, float halfHeight)
    {
        var box = b.Add(BoxType, (2, halfWidth), (3, halfHeight));
        var fill = b.Add(FillType, (1, 0.0005f));
        b.Wire(Plus(px, -x), 0, box, 0).Wire(Plus(py, -y), 0, box, 1).Wire(box, 0, fill, 0);
        return fill;
    }

    /// <summary>A Blend of two colors by a signal.</summary>
    private NodeInstance Between(NodeInstance a, NodeInstance c, NodeInstance t)
    {
        var mix = b.Add("color.mix");
        b.Wire(a, 0, mix, 0).Wire(c, 0, mix, 1).Wire(t, 0, mix, 2);
        return mix;
    }

    private NodeInstance Rgb(float red, float green, float blue) =>
        b.Add("color.rgb", (0, red), (1, green), (2, blue));

    /// <summary>
    /// A color for each time of day — morning, sunset and night — chosen by how far into
    /// the night the level has got.
    /// </summary>
    private NodeInstance ByDay(
        NodeInstance dusk, NodeInstance dark,
        (float R, float G, float B) day, (float R, float G, float B) evening, (float R, float G, float B) night) =>
        Between(Between(Rgb(day.R, day.G, day.B), Rgb(evening.R, evening.G, evening.B), dusk),
            Rgb(night.R, night.G, night.B), dark);

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        // A hundred and fifty a minute. Every part reads the count of beats, so the
        // tempo is this one knob.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 150f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        // Eight bars is one step of the arrangement, and how far into it is what
        // the harmony and the builds are read from.
        var phraseGone = Formula("fract(a * (1 / 32))", beats);

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // How much track there is, a phrase at a time: the title screen and the level
        // starting, two verses, a build, two choruses, the night, a build out of it, a
        // solo, two choruses a tone up, and the credits. Builds are at six tenths.
        var song = Lane(beats, 0.2f, 0.4f, 0.7f, 0.75f, 0.6f, 0.9f, 1f, 0.3f, 0.6f, 0.95f, 1f, 1f, 0.25f);

        // Which progression: the verse's, the chorus's or the bridge's.
        var progression = Lane(beats, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 2f, 2f, 1f, 1f, 1f, 0f);

        // What the lead plays: nothing, the verse, the chorus, the night or the solo.
        // A quarter over the whole number is the second pulse playing a third under
        // it, which is saved for the second time through anything.
        var lead = Lane(beats, 0f, 0f, 1f, 1.25f, 0f, 2f, 2.25f, 3f, 3f, 4f, 2.25f, 2.25f, 1f);
        var twinned = Formula("step(0.1, fract(a))", lead);

        // The key change, from the last two choruses to the end: a whole tone, added to
        // every pitch there is. It is read off which phrase the song is on.
        var key = Formula("step(9.5 / 13, a) * 2", new Read(song, 2));

        // The same arrangement as the ear hears it turned: four seconds up and one down.
        var swell = b.Add(SlewType, (1, 0.6f), (2, 0f));

        // A build's second half is a ramp for the roll and the riser to climb, and its
        // last two beats are nothing at all, so the drop lands out of silence.
        var building = Formula("step(0.57, a) * step(a, 0.63)", song);
        var ramp = Formula("smoothstep(0.5, 1, a) * b", phraseGone, building);
        var hush = Formula("1 - step(0.9375, a) * b", phraseGone, building);

        // The chorus is anything from nine tenths up.
        var chorus = Rises(song, 0.85f, 0.9f);

        b.Wire(song, 0, swell, 0);

        Box("Arrangement");

        // --- the harmony -----------------------------------------------------

        // Three progressions of eight bars on one list: the verse's Em C G D Em C D B,
        // the chorus's C D Em G C D B B, and the bridge's Am Em C B Am Em F B. The list
        // is read at the bar, and a progression over the first starts it thirty-two
        // beats further along.
        var bar = Formula("a * 32 + b * 32", phraseGone, progression);
        var roots = b.Add("seq.notes", (1, 0.25f));
        StepsExtra.Set(roots,
        [
            new Step(40f), new Step(36f), new Step(43f), new Step(38f),
            new Step(40f), new Step(36f), new Step(38f), new Step(35f),
            new Step(36f), new Step(38f), new Step(40f), new Step(43f),
            new Step(36f), new Step(38f), new Step(35f), new Step(35f),
            new Step(45f), new Step(40f), new Step(36f), new Step(35f),
            new Step(45f), new Step(40f), new Step(41f), new Step(35f),
        ]);

        // How far over each root its third is: three for a minor chord, four for a
        // major one. The fifth is always seven, since nothing here is diminished.
        var thirds = b.Add("seq.values", (1, 0.25f));
        StepsExtra.Set(thirds,
        [
            new Step(3f), new Step(4f), new Step(4f), new Step(4f),
            new Step(3f), new Step(4f), new Step(4f), new Step(4f),
            new Step(4f), new Step(4f), new Step(3f), new Step(4f),
            new Step(4f), new Step(4f), new Step(4f), new Step(4f),
            new Step(3f), new Step(3f), new Step(4f), new Step(4f),
            new Step(3f), new Step(3f), new Step(4f), new Step(4f),
        ]);

        var root = Sum(roots, key);

        // The same root folded into the octave over C3, for the parts that play
        // chords and should not leap about to follow the bass.
        var chordRoot = Formula("a % 12 + 48", root);

        b.Wire(bar, 0, roots, 0)
         .Wire(bar, 0, thirds, 0);

        Box("Harmony");

        // --- the kick --------------------------------------------------------

        // One, the and of two, and three, with two and four filled in for the chorus.
        // Each hit is an eighth long, and its level is the pattern's value rather than
        // its gate, so it is struck at the top of the step.
        var kickPattern = Pattern(beats, 2f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 0f);
        var kickMore = Pattern(beats, 2f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f);
        var kickStroke = Enters(
            Product(Formula("a * (b + c * d)", Stroke(beats, 2f, 4f), kickPattern, kickMore, chorus), hush),
            song, 0.35f, 0.4f);
        var kick = Drum(kickStroke, 48f, 170f, 5f, 2.5f);

        Box("Kick");

        // --- the snare -------------------------------------------------------

        // The chip's noise is a random value held for a sample of a slower clock:
        // at six thousand four hundred a second it is the crunch rather than a hiss.
        var crunch = b.Add(NoiseType, (1, 64f), (2, 3f));

        // Two and four once the verse is in, and in a build, sixteenths that grow
        // louder and thirty-seconds in the last bar.
        var backbeat = Enters(Stroke(beats, 0.5f, 5f, 0.5f), song, 0.64f, 0.68f);
        var fill = Formula("a * (step(0.875, b) * c) * d", Stroke(beats, 8f, 4f), phraseGone, building, hush);
        var snareStroke = Formula("a + (b * c * 0.8 + d)", backbeat, Stroke(beats, 4f, 4f), ramp, fill);

        var rattle = Filtered(Product(snareStroke, crunch, Held), 1200f, 0.1f);
        var snare = Formula("a * 2 + b * 1.2", new Read(rattle, High), Drum(snareStroke, 185f, 120f, 6f, 1f));

        b.Wire(Times(clock, 100f), 0, crunch, 0);

        Box("Snare");

        // --- the hats --------------------------------------------------------

        // The same noise held for a sample of a much faster clock, which is the
        // chip's hat. Sixteenths with the off-beat leant on, an open one on the
        // off-beat in the chorus, and a crash at the top of any phrase with a verse
        // or more in it.
        var fizz = b.Add(NoiseType, (1, 64f), (2, 7f));
        var accent = Pattern(beats, 4f, 0.45f, 0.25f, 1f, 0.25f);
        var closed = Enters(Product(Stroke(beats, 4f, 9f), accent), song, 0.38f, 0.42f);
        var open = Product(Stroke(beats, 1f, 3f, 0.5f), chorus);
        var metal = Formula("a + b * 0.6 + c * smoothstep(0.68, 0.7, d) * 0.9",
            closed, open, Stroke(beats, 1f / 32f, 9f), song);
        var hats = Filtered(Product(metal, fizz, Held), 5500f, 0.1f);

        b.Wire(Times(clock, 300f), 0, fizz, 0);

        Box("Hats");

        // --- the bass --------------------------------------------------------

        // Root and octave in eighths, with the fifth for a pickup: what every bass on
        // the chip played, on its triangle.
        var bassLine = Pattern(beats, 2f, 0f, 12f, 0f, 12f, 0f, 12f, 7f, 12f);
        var bassHz = Through("audio.note", Sum(root, bassLine));
        var triangle = b.Add("osc.triangle");

        // Sixteen steps from bottom to top. The chip's triangle was a counter, and the
        // steps are the buzz a real one has over a pure one. And a sine an octave under
        // it once the chorus is in, which no console had.
        var sub = b.Add("osc.sine");
        var bass = Enters(
            Formula("((floor(a * 7.5) + 0.5) * (1 / 7.5) + b * c * 0.6) * d",
                triangle, sub, chorus, Product(hush, bassLine, Gate)),
            song, 0.27f, 0.3f);

        b.Wire(bassHz, 0, triangle, 1)
         .Wire(Times(bassHz, 0.5f), 0, sub, 1);

        Box("Bass");

        // --- the arp ---------------------------------------------------------

        // The chord a note at a time, root, third, fifth and octave at the
        // thirty-second, on the thinnest pulse. Pumped a little on the eighth, and
        // opened by the arrangement, so the title screen hears it through a wall.
        var arpStep = Pattern(beats, 8f, 0f, 0f, 7f, 12f);
        var arpThird = Pattern(beats, 8f, 0f, 1f, 0f, 0f);
        var arpNote = Formula("a + b + c * d", chordRoot, arpStep, arpThird, thirds);
        var arpOsc = Pulse(Through("audio.note", arpNote), 0.125f);
        var arp = b.Add(FilterType, (2, 0.15f));

        b.Wire(Formula("a * (b * 0.55 + 0.45)", arpOsc, Stroke(beats, 2f, 1.5f)), 0, arp, 0)
         .Wire(Span(swell, 0f, 1f, 500f, 6500f), 0, arp, 1);

        Box("Arp");

        // --- the lead --------------------------------------------------------

        // Three melodies, one of them at a time.
        var verse = Melody(beats, Verse);
        var chorusLine = Melody(beats, Chorus);
        var night = Melody(beats, Night);

        var isVerse = Is(lead, 1f);
        var isChorus = Is(lead, 2f);
        var isNight = Is(lead, 3f);
        var isSolo = Formula("step(3.5, a)", lead);

        // The solo is the arp itself an octave up, jumping a further octave every
        // other beat, which is how a chip showed off.
        var soloNote = Formula("a + step(0.5, fract(b * 0.5)) * 12 + 12", arpNote, beats);

        // What the melodies play before the key change, which the harmony is
        // worked out from, and what the lead plays after it.
        var written = Formula("a + b * c", Formula("a * b + c * d", isVerse, verse, isChorus, chorusLine), isNight, night);
        var leadNote = Formula("a + b + c * d", written, key, isSolo, soloNote);

        // A chip voice does not know where a note ends, only that its pitch has
        // changed. So the lead is on for the whole of a phrase that has one, and a
        // new pitch is what strikes it: the note against itself a few milliseconds
        // late is the edge, and a Decay on the edge is the accent every note starts
        // with.
        var held = b.Add(SlewType, (1, -2.3f), (2, -2.3f));
        var late = b.Add(SlewType, (1, -2.5f), (2, -2.5f));
        var strike = b.Add("flyback.voice.decay", (1, -3f), (2, -0.6f), (3, 0.5f));
        var leadGate = Formula("a * (b * 0.5 + 0.5)", held, strike);

        // A vibrato that waits for the strike to fade, so only the long notes sing.
        var vibrato = b.Add("osc.sine", (1, 6f));
        var leadHz = Formula("a * (b * (c * (1 - d)) * 0.012 + 1)",
            Through("audio.note", leadNote), vibrato, held, strike);

        // A quarter pulse for the verse, nearly a square for the chorus, the hollow
        // square for the night and the thin eighth for the solo, with a slow sweep
        // on top so no part sits still.
        var width = Formula("a * 0.15 + b * 0.25 + c * -0.125 + 0.25 + d",
            isChorus, isNight, isSolo, b.Add("osc.sine", (1, 0.3f), (3, 0.04f)));
        var leadOsc = Pulse(leadHz, 0.25f, width);

        // Quieter for the night, which is a song under the breath.
        var leadDry = Filtered(Formula("a * b * (c * -0.35 + 1)", leadOsc, leadGate, isNight), 5500f, 0.1f);

        b.Wire(Formula("step(0.5, a)", lead), 0, held, 0)
         .Wire(leadNote, 0, late, 0)
         .Wire(Formula("step(0.2, abs(a - b))", leadNote, late), 0, strike, 0);

        Box("Lead");

        // --- the harmony pulse -----------------------------------------------

        // A diatonic third under what is written: a little under four semitones down,
        // snapped to the key, then moved by the key change like everything else.
        var twinNote = Wired("math.add", InKey(written, Scale, -3.6f), key, 1);
        var twinOsc = Pulse(Through("audio.note", twinNote), 0.5f);
        var twin = Filtered(Formula("a * (b * c)", twinOsc, leadGate, twinned), 4000f, 0.1f);

        Box("Harmony pulse");

        // --- the power-up ----------------------------------------------------

        // A build ends the way a level does when something is picked up: a pulse
        // running up two octaves a semitone at a time, and noise rising under it.
        var run = Pulse(Through("audio.note", Formula("a + b * 24", chordRoot, ramp)), 0.125f);
        var powerUp = Filtered(Formula("a * (b * b)", run, ramp), 5000f, 0.1f);
        var riser = Hiss(ramp, 300f, 0.5f, "band", 1.2f);

        b.Wire(Span(ramp, 0f, 1f, 300f, 8000f), 0, riser, HissCutoff);

        Box("Power-up");

        // --- the space -------------------------------------------------------

        // A dotted eighth on the lead, crossing to the other side, and a small hall on
        // a send for the lead, the harmony, the arp and the snare.
        var taps = Echo(Filtered(leadDry, 3000f, 0f), beat, 3f, 3f, 0.4f, 1f);
        var roomSend = b.Add("math.mixer", (1, 0.6f), (3, 0.5f), (5, 0.3f), (7, 0.35f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.6f), (2, 0.55f), (3, 1f));

        b.Wire(leadDry, 0, roomSend, 0)
         .Wire(twin, 0, roomSend, 2)
         .Wire(arp, 0, roomSend, 4)
         .Wire(snare, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        // The music and its space on two Desks chained into one, which the kick ducks.
        // The arp leans left and the harmony right.
        var music = b.Add(DeskType);
        var space = b.Add(DeskType, (DeskTrim, 1f));

        Channel(music, 1, 0.4f, bass);
        Channel(music, 2, 0.55f, arp, Times(arp, 0.55f));
        Channel(music, 3, 0.45f, leadDry);
        Channel(music, 4, 0.4f, Times(twin, 0.55f), twin);

        Channel(space, 1, 0.3f, taps, taps, rightFrom: EchoRight);
        Channel(space, 2, 0.4f, room, room, rightFrom: 1);
        Channel(space, 3, 0.25f, riser);
        Channel(space, 4, 0.12f, powerUp);

        Chained(music, space);

        // The sidechain: everything pitched ducked by the kick alone, fast to go down
        // and a sixth of a second to come back.
        var ducked = b.Add(NodeCatalog.DuckTypeId, (3, 0.4f), (5, -2.5f), (6, -0.8f));

        b.Wire(space, 0, ducked, 0)
         .Wire(space, 1, ducked, 1)
         .Wire(kick, 0, ducked, 2);

        // The drums added back after it, on a Desk of their own chained into the
        // master, which is the only one with a trim.
        var drums = b.Add(DeskType);
        var master = b.Add(DeskType, (DeskTrim, 0.55f));

        Channel(drums, 1, 1f, kick);
        Channel(drums, 2, 0.6f, snare);
        Channel(drums, 3, 0.9f, hats, from: High);

        // Two decibels over unity, which is where the music sits against the drums.
        Channel(master, 1, 1.2589254f, ducked, ducked, rightFrom: 1);

        Chained(drums, master);

        Box("Desk");

        // --- the master ------------------------------------------------------

        // Nothing under thirty hertz, the square waves' honk taken out of the upper
        // middle, and air on top; then wider, with the bass kept in the middle; then
        // held a decibel under full scale.
        var tone = b.Add(EqType, (2, 30f), (5, 2800f), (6, -2f), (7, 0.8f), (8, 10000f), (9, 1.5f));
        var wide = b.Add(WidthType, (2, 1.35f), (3, 160f));
        var loud = b.Add(LimiterType, (2, -1f), (3, -1.3f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));

        b.Wire(master, 0, tone, 0)
         .Wire(master, 1, tone, 1)
         .Wire(tone, 0, wide, 0)
         .Wire(tone, 1, wide, 1)
         .Wire(wide, 0, loud, 0)
         .Wire(wide, 1, loud, 1)
         .Wire(loud, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(loud, 1, output, NodeCatalog.OutputRightPort);

        Box("Master", output);

        // --- the picture: screen ---------------------------------------------

        // A television's glass bulges, so the frame is read a little further out the
        // further from the middle it is. Then every coordinate is rounded to the middle
        // of its pixel, and the rest of the picture never sees anything finer.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var bulge = Formula("a * a * 0.035 + 1", new Read(coord, 2));
        var bentX = Product(coord, bulge);
        var bentY = Wired("math.mul", coord, bulge, 1);
        var column = Formula("floor(a * 45)", bentX);
        var row = Formula("floor(a * 45)", bentY);
        var px = Formula("(a + 0.5) * (1 / 45)", column);
        var py = Formula("(a + 0.5) * (1 / 45)", row);

        // What is off the edge of the glass is the dark of the set around it.
        var onGlass = Formula("step(abs(a), b) * step(abs(c), 0.99)",
            bentX, new Read(coord, NodeCatalog.CoordAspectPort), bentY);

        // Every other pixel, for dithering.
        var dither = b.Add("pattern.checker", (2, 1f));

        b.Wire(column, 0, dither, 0)
         .Wire(row, 0, dither, 1);

        Box("Picture: Screen");


        // --- the picture: sky ------------------------------------------------

        // The time of day, a phrase at a time: morning, day, dusk for the choruses,
        // night for the bridge, and back towards gold for the last of it. The same list
        // read a phrase ahead is the next phrase's, and the last quarter of each phrase
        // blends into it, which is a slew the picture can have.
        float[] times = [0.15f, 0f, 0f, 0f, 0.3f, 0.5f, 0.5f, 1f, 0.85f, 0.65f, 0.4f, 0.45f, 0.2f];
        var today = Formula("mix(a, b, smoothstep(0.75, 1, c))",
            Lane(beats, times), Lane(Plus(beats, 32f), times), phraseGone);
        var dusk = Rises(today, 0f, 0.5f);
        var dark = Rises(today, 0.5f, 1f);

        // Two colors for each time of day, one at the top of the sky and one at the
        // horizon, and seven bands between them with a checkerboard where they meet.
        var zenith = ByDay(dusk, dark, (0.2f, 0.4f, 0.95f), (0.3f, 0.12f, 0.5f), (0.02f, 0.02f, 0.12f));
        var horizon = ByDay(dusk, dark, (0.55f, 0.82f, 1f), (1f, 0.5f, 0.3f), (0.12f, 0.1f, 0.32f));
        var band = Formula("floor(smoothstep(-0.6, 0.95, a) * 7 + b * 0.5) * (1 / 7)", py, dither);
        var sky = Between(horizon, zenith, band);

        // Stars are Cells laid over the pixels themselves, one point to every seven
        // pixels square, and a star wherever a point falls on a pixel. Only at night,
        // only high up, and each twinkling on its own cell's number.
        var cells = b.Add(CellsType, (2, 0f), (3, 1f / 7f), (4, 0.9f));
        var twinkle = Formula("remap(sin(a * 40 + b * 3), -1, 1, 0.2, 1)", new Read(cells, 2), clock);
        var stars = Formula(
            "(1 - smoothstep(0.08, 0.09, a)) * b * (smoothstep(0.6, 0.9, c) * smoothstep(0, 0.4, d))",
            cells, twinkle, today, py);

        b.Wire(Plus(column, 0.5f), 0, cells, 0)
         .Wire(Plus(row, 0.5f), 0, cells, 1);

        Box("Picture: Sky");

        // --- the picture: sun and moon ---------------------------------------

        // One disc that is the sun by day, sinks and reddens at dusk, and comes back up
        // pale as the moon. It swells on the kick.
        var disc = b.Add(CircleType);
        var discFill = b.Add(FillType, (1, 0.0005f));
        var discColor = ByDay(dusk, dark, (1f, 0.9f, 0.35f), (1f, 0.4f, 0.2f), (0.92f, 0.92f, 0.8f));

        b.Wire(Plus(px, -1.05f), 0, disc, 0)
         .Wire(Formula("a - (remap(b, 0, 1, 0.55, 0.25) + c * 0.35)", py, dusk, dark), 0, disc, 1)
         .Wire(Span(kickStroke, 0f, 1f, 0.16f, 0.185f), 0, disc, 2)
         .Wire(disc, 0, discFill, 0);

        var withDisc = Between(Ink(sky, stars, 1f, 1f, 0.9f), discColor, discFill);

        Box("Picture: Sun");

        // --- the picture: clouds ---------------------------------------------

        // A Clouds field squashed flat and cut at two heights: the lower cut is the
        // cloud and the higher its bright middle. They drift at a twelfth of the
        // ground's speed, and only in the upper sky.
        var puff = b.Add("pattern.clouds", (3, 1f));
        var cloudLight = Formula(
            "(step(0.63, a) * 0.55 + step(0.71, a) * 0.4) * (smoothstep(0.05, 0.35, b) * (1 - c * 0.65))",
            puff, py, dark);

        b.Wire(Formula("(a + b * 0.02) * 2.2", px, beats), 0, puff, 0)
         .Wire(Times(py, 3.2f), 0, puff, 1)
         .Wire(Times(clock, 0.02f), 0, puff, 2);

        var withClouds = Ink(withDisc, cloudLight, 0.85f, 0.85f, 0.9f);

        Box("Picture: Clouds");

        // --- the picture: mountains ------------------------------------------

        // Far off and slow: one row of a Clouds field is the height of every column,
        // colored by the horizon behind it pulled towards slate, the way distance does,
        // with snow where the ridge is high.
        var rock = b.Add("pattern.clouds", (1, 3.7f), (2, 0f), (3, 1.6f));
        var ridge = Span(rock, 0.25f, 0.75f, -0.2f, 0.3f);
        var underRidge = Wired("math.step", py, ridge);
        var farBlue = b.Add("color.mix", (2, 0.55f));
        var snow = Formula(
            "smoothstep(0.17, 0.19, a) * (1 - smoothstep(0.04, 0.045, a - b)) * (1 - c * 0.6)",
            ridge, py, dark);

        b.Wire(Formula("a + b * 0.04", px, beats), 0, rock, 0)
         .Wire(horizon, 0, farBlue, 0)
         .Wire(Rgb(0.18f, 0.2f, 0.42f), 0, farBlue, 1);

        var withMountains = Between(withClouds, Ink(farBlue, snow, 0.95f, 0.95f, 1f), underRidge);

        Box("Picture: Mountains");

        // --- the picture: hills ----------------------------------------------

        // Nearer, and two and a half times as fast: two sines for a line of hills, a
        // lighter band along the top of them, and a darker checker down their faces,
        // laid on the pixels and moved with the hills.
        var hillX = Formula("a + b * 0.1", px, beats);
        var hillTop = Formula("sin(a * 3) * 0.1 + sin(a * 7.3 + 1) * 0.05 - 0.32", hillX);
        var underHill = Wired("math.step", py, hillTop);
        var weave = b.Add("pattern.checker", (2, 0.25f));
        var grass = ByDay(dusk, dark, (0.2f, 0.62f, 0.28f), (0.35f, 0.4f, 0.25f), (0.08f, 0.2f, 0.18f));
        var hillColor = b.Add("color.gain");

        // How far under the hilltop the pixel is decides both: the band is the top of
        // that, and the checker only shows once it is well down the face.
        var hillShade = Formula(
            "(1 - smoothstep(0.025, 0.03, a - b)) * 0.35 - c * (smoothstep(0.1, 0.12, a - b) * 0.2) + 1",
            hillTop, py, weave);

        b.Wire(Formula("floor(a + b * (0.1 * 45))", column, beats), 0, weave, 0)
         .Wire(row, 0, weave, 1)
         .Wire(grass, 0, hillColor, 0)
         .Wire(hillShade, 0, hillColor, 1);

        var withHills = Between(withMountains, hillColor, underHill);

        Box("Picture: Hills");

        // --- the picture: ground ---------------------------------------------

        // Bricks, a tile to the beat. Across is the pixel's place along the ground and
        // down is its depth under the grass, in bricks of eight pixels by four, every
        // other row set half a brick over. The mortar is the first pixel of each brick
        // either way. A tile is eight pixels of forty-five, which is how the formulas
        // write it.
        var along = Formula("a + b * 0.25", px, beats);
        var depth = Formula("-0.6 - a", py);
        var courses = Formula("a * (1 / (8 / 45 * 0.5))", depth);
        var mortar = Formula(
            "1 - max(1 - step(1 / 8, fract(a * (1 / (8 / 45)) + floor(b) * 0.5)), 1 - step(1 / 4, fract(b))) * 0.55",
            along, courses);
        var brick = ByDay(dusk, dark, (0.78f, 0.42f, 0.2f), (0.7f, 0.32f, 0.22f), (0.3f, 0.16f, 0.14f));
        var bricks = b.Add("color.gain");
        var turf = ByDay(dusk, dark, (0.35f, 0.85f, 0.3f), (0.5f, 0.6f, 0.25f), (0.1f, 0.25f, 0.15f));
        var underTurf = Rises(depth, 0.05f, 0.052f);
        var underGround = b.Add("math.step", (1, GroundTop));

        b.Wire(brick, 0, bricks, 0)
         .Wire(mortar, 0, bricks, 1)
         .Wire(py, 0, underGround, 0);

        var withGround = Between(withHills, Between(turf, bricks, underTurf), underGround);

        Box("Picture: Ground");

        // --- the picture: blocks ---------------------------------------------

        // A row of the blocks that hold something, one every four tiles, over the
        // hero's head: a dark rim, a gold face that flashes with the lead, and a dot.
        // The snare knocks them up, and they only hang there once the verse is in.
        var blockAt = Formula("(fract(a * (1 / (4 * (8 / 45)))) - 0.5) * (4 * (8 / 45))", along);
        var blockY = Formula("a - (b * 0.03 + 0.02)", py, snareStroke);
        var shown = Rises(song, 0.65f, 0.7f);
        var rim = Rgb(0.45f, 0.22f, 0.05f);
        var face = b.Add("color.gain");
        var pixel = 1f / Rows;

        b.Wire(Rgb(1f, 0.72f, 0.18f), 0, face, 0)
         .Wire(Formula("a * 0.35 + 0.75", Stroke(beats, 1f, 3f)), 0, face, 1);

        var withBlocks = Between(
            Between(
                Between(withGround, rim, Product(Block(blockAt, blockY, 0f, 0f, Tile * 0.5f, Tile * 0.5f), shown)),
                face, Product(Block(blockAt, blockY, 0f, 0f, Tile * 0.5f - pixel, Tile * 0.5f - pixel), shown)),
            rim, Product(Block(blockAt, blockY, 0f, 0f, pixel, pixel), shown));

        Box("Picture: Blocks");

        // --- the picture: coins ----------------------------------------------

        // Between the blocks and higher, a coin turning on the beat: a disc read
        // across a plane narrowed by the size of a sine. Only in the chorus.
        var coin = b.Add(CircleType, (2, 0.045f));
        var coinFill = b.Add(FillType, (1, 0.0005f));

        b.Wire(
             Formula(
                 "(fract(a * (1 / (4 * (8 / 45))) + 0.5) - 0.5) * (4 * (8 / 45)) / (abs(sin(b * pi)) * 0.85 + 0.15)",
                 along, beats),
             0, coin, 0)
         .Wire(Formula("a - (sin(b * (pi * 0.5)) * 0.02 + 0.22)", py, beats), 0, coin, 1)
         .Wire(coin, 0, coinFill, 0);

        var withCoins = Ink(withBlocks, Product(coinFill, chorus), 1f, 0.85f, 0.25f);

        Box("Picture: Coins");

        // --- the picture: hero -----------------------------------------------

        // Somebody running: a hop every two beats, which is a parabola on the phase
        // of them and which the night leaves out, and legs that swap on the eighth.
        // In a build the suit cycles through every color, which is what being
        // unstoppable looks like.
        var heroX = Plus(px, -HeroX);
        var heroY = Formula(
            "a + 0.6 - fract(b * 0.5) * (1 - fract(b * 0.5)) * (4 * 0.28) * smoothstep(0.5, 0.6, c)",
            py, beats, song);
        var eighth = Formula("fract(a * 2)", beats);
        var stride = Formula("smoothstep(0.5, 0.501, a) * 0.022", eighth);

        var legs = Wired("math.max",
            Block(Sum(heroX, stride), heroY, -0.011f, 0.03f, 0.012f, 0.03f),
            Block(Less(heroX, stride), heroY, 0.011f, 0.03f, 0.012f, 0.03f));
        var body = Block(heroX, heroY, 0f, 0.1f, 0.05f, 0.042f);
        var head = Block(heroX, heroY, 0.005f, 0.185f, 0.045f, 0.045f);
        var cap = Wired("math.max",
            Block(heroX, heroY, 0.005f, 0.235f, 0.05f, 0.018f),
            Block(heroX, heroY, 0.05f, 0.222f, 0.03f, 0.008f));
        var eye = Block(heroX, heroY, 0.03f, 0.19f, 0.008f, 0.014f);

        var rainbow = b.Add("color.hsv", (1, 0.8f), (2, 0.95f));
        var suit = Between(Rgb(0.88f, 0.14f, 0.12f), rainbow, ramp);

        b.Wire(eighth, 0, rainbow, 0);

        var withHero = Between(
            Between(
                Between(
                    Between(Ink(withCoins, legs, 0.2f, 0.25f, 0.7f), suit, body),
                    Rgb(1f, 0.76f, 0.56f), head),
                suit, cap),
            Rgb(0.05f, 0.03f, 0.08f), eye);

        Box("Picture: Hero");

        // --- the picture: HUD ------------------------------------------------

        // A dark strip along the top with how far through the level it is: which
        // phrase, and how far into it, as a bar that fills from the left.
        var inBar = Formula(
            "(1 - smoothstep(0.019, 0.021, abs(b - 0.925))) * step(a, (c + d * (1 / 13)) * 2.8 - 1.4) * smoothstep(-1.42, -1.4, a)",
            px, py, new Read(song, 2), phraseGone);
        var dimmed = b.Add("color.gain");

        b.Wire(withHero, 0, dimmed, 0)
         .Wire(Formula("1 - step(0.86, a) * 0.75", py), 0, dimmed, 1);

        var withHud = Ink(dimmed, inBar, 1f, 0.85f, 0.3f);

        Box("Picture: HUD");

        // --- the picture: tube -----------------------------------------------

        // The drop is a white flash, the colors are held to the few a chip had, the
        // rows of the tube show between the rows of pixels, and the kick lifts all of
        // it a little. The glass's edge is last but for the phosphor, which glows a
        // moment after and reads whatever reached the Output.
        var flash = Formula("a * b * 0.5", Stroke(beats, 1f / 32f, 20f), chorus);
        var chip = b.Add(PosteriseType, (1, 6f));
        var lines = b.Add("color.gain");
        var shaded = Vignette(lines, 0.6f, 2.2f, 0.4f);
        var glass = b.Add("color.gain");
        var glow = b.Add(TrailsType, (TrailsPersist, 0.35f));

        b.Wire(Ink(withHud, flash, 1f, 1f, 1f), 0, chip, 0)
         .Wire(chip, 0, lines, 0)
         .Wire(
             Formula("remap(sin(a * (45 * 2 * pi)), -1, 1, 0.78, 1) * remap(b, 0, 1, 1, 1.12)", bentY, kickStroke),
             0, lines, 1)
         .Wire(shaded, 0, glass, 0)
         .Wire(onGlass, 0, glass, 1)
         .Wire(glass, 0, glow, 0)
         .Wire(glow, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Tube");

        return b.Build();
    }
}
