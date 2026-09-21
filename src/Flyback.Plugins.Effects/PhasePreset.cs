using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Phase music, after Steve Reich: two players on one twelve-note pattern, the second
/// pulling ahead a note at a time until it has been through every canon the pattern
/// has and is back in unison — and a picture that is the diagram of it.
/// </summary>
/// <remarks>
/// The piece is a process rather than a composition, and the process is one signal:
/// how many notes the second player is ahead. It holds a whole number for most of a
/// stage and eases to the next over the end of it, and everything else reads it. Added
/// to the count it is the second player's place in the pattern, so that player speeds
/// up to get there and nobody has to be told to. Rounded, it is how far a second
/// clapping pattern is rotated against the first. On the screen it is how far one dial
/// of notes has turned inside the other, and how far apart two sets of rings are.
/// <para>
/// A Sequencer has no memory and follows whatever is on its 'in', which is what makes
/// this a wire and not a program. Twelve notes ahead is the pattern itself, so the last
/// stage is the first and the piece comes round with nothing to reset.
/// </para>
/// </remarks>
internal sealed class PhasePreset : PresetBench
{
    public const string Name = "Phase";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string EuclidType = "flyback.voice.euclid";

    private const string CircleType = "flyback.picture.circle";

    private const string BoxType = "flyback.picture.box";

    private const string FillType = "flyback.picture.fill";

    private const string PaletteType = "flyback.picture.palette";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>Notes a second. There is no beat slower than the note, so this is the tempo.</summary>
    private const float Pace = 6.5f;

    /// <summary>Notes in the pattern, and in one stage of the process: eight times round it.</summary>
    private const float Round = 12f;

    private const float Stage = 96f;

    /// <summary>
    /// The pattern: D, A, E, G, C, A, D, G, E, C, A, G. Five pitches and no semitone
    /// between any two of them, so every one of the twelve canons is consonant and what
    /// changes from stage to stage is the rhythm the pitches make, not the harmony.
    /// </summary>
    private static readonly float[] Pattern = [62f, 69f, 64f, 67f, 72f, 69f, 62f, 67f, 64f, 72f, 69f, 67f];

    /// <summary>The five pitches, as the scale the pulsing chords are snapped to.</summary>
    private static readonly int[] Scale = [2, 4, 7, 9, 0];

    private static Step[] Notes() => [.. Pattern.Select(note => new Step(note))];

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        return new PhasePreset(modules).Assemble();
    }

    private PhasePreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the process -----------------------------------------------------

        // The count of notes, and which stage of sixteen it has reached.
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var count = Times(clock, Pace);
        var stage = Times(count, 1f / Stage);
        var stageGone = Fraction(stage);

        // How far ahead the second player is. One less than the stage, because the
        // first stage is one player alone and the second is the two in unison; the
        // last third of a stage eased on top, which is the pulling ahead; and held
        // between nothing and twelve, which is the unison again at the far end.
        var ahead = b.Add("math.clamp", (1, 0f), (2, Round));
        var countB = Sum(count, ahead);

        // What is left of each player's note, squared. The second player's notes are
        // shorter while it is pulling ahead, and so is this.
        var noteA = Stroke(count, 1f, 2f);
        var noteB = Stroke(countB, 1f, 2f);

        // Which note of the twelve the first player is on. The dials and the clapping
        // rows both light by it.
        var playing = Floor(Knobbed("math.mod", count, Round));

        b.Wire(Sum(Plus(Floor(stage), -1f), Rises(stageGone, 0.7f, 1f)), 0, ahead, 0);

        Box("Process");

        // --- the arrangement -------------------------------------------------

        // One number a stage saying how much ensemble there is. It only ever grows
        // until the unison comes back, because a process that is thinning out cannot
        // be heard getting anywhere.
        var song = b.Add("seq.values", (1, 1f / Stage));
        StepsExtra.Set(song,
        [
            new Step(0.1f), new Step(0.2f), new Step(0.3f), new Step(0.4f),
            new Step(0.5f), new Step(0.55f), new Step(0.6f), new Step(0.7f),
            new Step(0.75f), new Step(0.8f), new Step(0.9f), new Step(0.95f),
            new Step(1f), new Step(1f), new Step(0.5f), new Step(0.25f),
        ]);

        // Each part below is brought in by a Fade off this number. The second player's
        // entry is a Smoothstep of its own, because the picture wants it as well.
        var secondIn = Rises(song, 0.12f, 0.18f);

        // A knock on every note, for the claps, from a little under halfway. Here
        // rather than with them because the rows of squares arrive when they do.
        var knock = Enters(Stroke(count, 1f, 12f), song, 0.45f, 0.5f);

        // What the pulses stand on: D, F, G and C, two stages each and twice round.
        var root = b.Add("seq.notes", (1, 1f / Stage));
        StepsExtra.Set(root,
        [
            new Step(38f), new Step(38f), new Step(41f), new Step(41f),
            new Step(43f), new Step(43f), new Step(36f), new Step(36f),
            new Step(38f), new Step(38f), new Step(41f), new Step(41f),
            new Step(43f), new Step(43f), new Step(38f), new Step(38f),
        ]);

        b.Wire(count, 0, song, 0)
         .Wire(count, 0, root, 0);

        Box("Arrangement");

        // --- the players -----------------------------------------------------

        // The same list and the same instrument twice. All that differs is what is on
        // 'in': the count, and the count with the lead added. A String's pitch is the
        // length of its delay line, but the pitch and the pluck change together here,
        // so nothing has to hold it.
        var (notesA, playerA) = Player(count);
        var (notesB, second) = Player(countB);
        var playerB = Product(second, secondIn);

        Box("Players");

        // --- the agreement ---------------------------------------------------

        // In any canon the two players now and then land on the same pitch at the same
        // moment, and which moments those are is different at every stage. A third
        // voice plays only there, an octave up: the pattern inside the pattern, which
        // a listener starts to pick out unaided and which is here said aloud.
        var differ = b.Add("math.step", (0, 0.5f));
        var agree = From(1f, differ);
        var agreeStroke = Enters(Product(agree, noteA), song, 0.55f, 0.6f);
        var chime = Bell(Through("audio.note", Plus(notesA, 12f)), agreeStroke, 4f, 0.25f);

        b.Wire(Size(Less(notesA, notesB)), 0, differ, 1);

        Box("Agreement");

        // --- the pulses ------------------------------------------------------

        // A pulse every second note, under everything. Each instrument swells and
        // dies away like a breath — half a sine over part of a stage — and the two
        // breathe at different lengths, so they overlap a different way each time.
        var pulse = Stroke(count, 0.5f, 1.5f);
        var lowBreath = Sine(Times(Fraction(Times(stage, 2f)), MathF.PI));
        var lowStroke = Enters(Product(pulse, lowBreath), song, 0.25f, 0.3f);
        var lowHz = Through("audio.note", root);
        var reed = b.Add("osc.triangle");
        var low = Sum(reed, Tone(Times(lowHz, 2f), Times(lowStroke, 0.4f)));

        // Two notes over the root, snapped to the five the pattern is made of.
        var highBreath = Sine(Times(Fraction(Times(stage, 3f)), MathF.PI));
        var organStroke = Enters(Product(pulse, highBreath), song, 0.65f, 0.7f);
        var organ = Sum(
            Tone(InKey(root, Scale, 31f), organStroke),
            Tone(InKey(root, Scale, 40f), Times(organStroke, 0.7f)));

        b.Wire(lowHz, 0, reed, 1)
         .Wire(lowStroke, 0, reed, 3);

        Box("Pulses");

        // --- the claps -------------------------------------------------------

        // The same process, stepped instead of eased. Seven in twelve on a block of
        // wood, twice: the second pattern is rotated by the lead, which a Euclid rounds
        // to whole steps, so it jumps where the melody slides. Both are struck in the
        // first player's time — it is the pattern that moves, not the hands.
        var clapsA = b.Add(EuclidType, (1, 1f), (2, Round), (3, 7f));
        var clapsB = b.Add(EuclidType, (1, 1f), (2, Round), (3, 7f));
        var strokeA = Product(knock, clapsA, 1);
        var strokeB = Product(knock, clapsB, 1);
        var wood = b.Add(NodeCatalog.ValueTypeId, (0, 1150f));
        var woodA = Tone(wood, strokeA);
        var woodB = Tone(wood, strokeB);

        b.Wire(count, 0, clapsA, 0)
         .Wire(count, 0, clapsB, 0)
         .Wire(ahead, 0, clapsB, 4);

        Box("Claps");

        // --- the shaker ------------------------------------------------------

        // Every note, quietly: the top of a Hiss, and what is left of the note to the
        // tenth power.
        var shaker = Hiss(Enters(Stroke(count, 1f, 10f), song, 0.35f, 0.4f), 7000f, 0.2f, "high");

        Box("Shaker");

        // --- the desk --------------------------------------------------------

        // A small dry room. The first player sits on the left and the second on the
        // right with a little of each in the other side, which is where the canons
        // are heard: as one line moving between two places.
        var roomSend = b.Add("math.mixer", (1, 0.4f), (3, 0.4f), (5, 0.6f), (7, 0.5f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.55f), (2, 0.55f), (3, 1f));

        // The sides are the first Desk. Its first channel is each player loud on their
        // own side and its second is the two of them crossed and quiet, which is a pan
        // on a desk that has none.
        var sides = b.Add(DeskType);

        // The middle is the second, and the master: a trim under unity, and rails that
        // should never be reached.
        var middle = b.Add(DeskType, (DeskTrim, 0.5f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        b.Wire(playerA, 0, roomSend, 0)
         .Wire(playerB, 0, roomSend, 2)
         .Wire(chime, 0, roomSend, 4)
         .Wire(organ, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Channel(sides, 1, 0.85f, playerA, playerB);
        Channel(sides, 2, 0.2f, playerB, playerA);
        Channel(sides, 3, 0.3f, woodA, woodB);
        Channel(sides, 4, 0.5f, room, room, rightFrom: 1);

        Channel(middle, 1, 0.45f, low);
        Channel(middle, 2, 0.28f, organ);
        Channel(middle, 3, 0.22f, chime);
        Channel(middle, 4, 0.2f, shaker);

        b.Wire(Chained(sides, middle), 0, output, NodeCatalog.OutputLeftPort)
         .Wire(middle, 1, output, NodeCatalog.OutputRightPort);

        Box("Desk", output);

        // --- the picture: dials ----------------------------------------------

        // The pattern as a dial: twelve beads round a ring, each as far out as its note
        // is high, read from a copy of the players' list at the angle instead of at the
        // count. There are two, one inside the other, and the inner one is turned by
        // the lead — so the two notes sounding are always the two beads on one spoke,
        // and the canon is how far the dials are out of line.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var around = b.Add("space.polar");
        var sector = Plus(Times(around, Round / MathF.Tau, 1), Round / 2f);
        var dialA = Dial(sector, playing, 0.64f, noteA);
        var dialB = Product(
            Dial(Sum(sector, ahead), Floor(Knobbed("math.mod", countB, Round)), 0.4f, noteB),
            Span(secondIn, 0f, 1f, 0.3f, 1f));

        // The spoke. How far round from the middle of the bead being played a pixel
        // is, the short way, drawn thin between the middle and the rim.
        var gap = Size(Less(sector, Plus(playing, 0.5f)));
        var spokeBand = b.Add(CircleType, (2, 0.42f));
        var spokeReach = b.Add(FillType, (1, 0.01f), (2, 0.62f));
        var spoke = Product(
            From(1f, Rises(Wired("math.min", gap, From(Round, gap)), 0f, 0.05f)), spokeReach, 1);

        // And a ring between the dials that flashes when the two players agree.
        var between = b.Add(CircleType, (2, 0.52f));
        var betweenLine = b.Add(FillType, (1, 0.004f), (2, 0.008f));
        var flash = Product(Span(agreeStroke, 0f, 1f, 0.12f, 1.6f), betweenLine, 1);

        b.Wire(spokeBand, 0, spokeReach, 0)
         .Wire(between, 0, betweenLine, 0);

        Box("Picture: Dials");

        // --- the picture: rows -----------------------------------------------

        // The clapping patterns as two rows of twelve squares, filled where there is a
        // clap and empty where there is a rest, the way the score of such a piece is
        // printed. Across the frame is cut into twelve cells; which cell a pixel is in
        // is asked of a copy of each Euclid, and the lower row's is rotated by the
        // lead. The column being played lights in both.
        var along = Plus(Times(coord, 3.6f), Round / 2f);
        var cell = Floor(along);
        var within = Plus(Fraction(along), -0.5f);

        // Only twelve of the cells are the row: the frame is a little wider than that.
        var fromFirst = b.Add("math.step", (0, -0.5f));
        var pastLast = b.Add("math.step", (0, Round - 0.5f));
        var shown = Product(Product(fromFirst, From(1f, pastLast)), knock, FadeGate);

        // The column being played. Both numbers are whole, so under a half apart is
        // the same.
        var offColumn = b.Add("math.step", (0, 0.5f));
        var column = From(1f, offColumn);
        var rowA = Row(0.86f, null, strokeA);
        var rowB = Row(-0.86f, ahead, strokeB);

        b.Wire(cell, 0, fromFirst, 1)
         .Wire(cell, 0, pastLast, 1)
         .Wire(Size(Less(cell, playing)), 0, offColumn, 1);

        Box("Picture: Rows");

        // --- the picture: rings ----------------------------------------------

        // Two sets of rings, one a player, pushed apart by the lead. Each is cut to
        // black and white and the two are compared, so what shows is where they
        // differ: nothing at all in unison, and a moiré that tightens as the second
        // player gets further ahead. The pulses breathe light into it.
        var apart = Times(ahead, 0.03f);
        var pushedA = b.Add("space.translate");
        var pushedB = b.Add("space.translate");
        var ringsA = b.Add("pattern.rings", (2, 12f));
        var ringsB = b.Add("pattern.rings", (2, 12f));
        var cutA = b.Add("math.step", (0, 0f));
        var cutB = b.Add("math.step", (0, 0f));
        var flow = Times(count, 1f / Round);
        var moire = Product(
            Size(Less(cutA, cutB)), Plus(Times(Sum(lowStroke, organStroke), 0.3f), 0.1f));

        // Tinted by where in the sixteen stages the piece is, in tints of one color.
        var tint = b.Add(PaletteType, (2, 0.12f), (3, 0.6f), (4, 0.4f));
        var moireLit = b.Add("color.gain");

        b.Wire(apart, 0, pushedA, 2)
         .Wire(Times(apart, -1f), 0, pushedB, 2)
         .Wire(pushedA, 0, ringsA, 0)
         .Wire(pushedA, 1, ringsA, 1)
         .Wire(flow, 0, ringsA, 3)
         .Wire(pushedB, 0, ringsB, 0)
         .Wire(pushedB, 1, ringsB, 1)
         .Wire(flow, 0, ringsB, 3)
         .Wire(ringsA, 0, cutA, 1)
         .Wire(ringsB, 0, cutB, 1)
         .Wire(song, 2, tint, 0)
         .Wire(tint, 0, moireLit, 0)
         .Wire(moire, 0, moireLit, 1);

        Box("Picture: Rings");

        // --- the picture: print ----------------------------------------------

        // Three Inks: the first player's red with the second's blue-green laid on it,
        // and cream over the rings for what belongs to both — the spoke and the flash.
        var inkA = Ink(null, Sum(dialA, rowA), 0.95f, 0.33f, 0.2f);
        var inkB = Ink(inkA, Sum(dialB, rowB), 0.15f, 0.72f, 0.75f);
        var inkBoth = Ink(moireLit, Sum(Times(spoke, 0.3f), flash), 0.96f, 0.92f, 0.82f);

        // Each note leaves the dial as a fading copy of itself, a little larger: the
        // last frame read from nearer the middle. Maximum rather than a blend, so an
        // echo brighter than the new frame keeps its brightness.
        var printed = b.Add(TrailsType, (TrailsZoom, 0.985f), (TrailsPersist, 0.84f));

        // Darkened at the corners, and graded by the stage last.
        var shaded = Vignette(printed, 0.5f, 2f, 0.35f);
        var graded = b.Add(GradeType, (2, 1.08f));

        b.Wire(Sum(inkB, inkBoth), 0, printed, 0)
         .Wire(shaded, 0, graded, 0)
         .Wire(Span(song, 0f, 1f, 0.85f, 1.3f), 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Print");

        return b.Build();

        // One player: the pattern read at a position, and a String plucked by its gate.
        // Four tenths of a second of ring, in decades of a second.
        (NodeInstance Notes, NodeInstance Sound) Player(NodeInstance position)
        {
            var notes = b.Add("seq.notes", (1, 1f), (2, 0.5f));
            StepsExtra.Set(notes, Notes());
            var plucked = b.Add(NodeCatalog.StringTypeId, (3, -0.4f), (4, 0.55f));

            b.Wire(position, 0, notes, 0)
             .Wire(notes, 1, plucked, 1)
             .Wire(Through("audio.note", notes), 0, plucked, 2);

            return (notes, plucked);
        }

        // One dial. 'round' is the angle in twelfths of a turn; 'lit' is the note its
        // player is on. Inside one twelfth the plane is flat enough to draw on: across
        // is the fraction through it scaled to the arc it spans, and up is how far the
        // radius is from where this bead sits.
        NodeInstance Dial(NodeInstance round, NodeInstance lit, float radius, NodeInstance stroke)
        {
            var bead = Floor(Knobbed("math.mod", round, Round));
            var score = b.Add("seq.notes", (1, 1f));
            StepsExtra.Set(score, Notes());
            var shape = b.Add(CircleType, (2, 0.032f));
            var fill = b.Add(FillType, (1, 0.004f));
            var elsewhere = b.Add("math.step", (0, 0.5f));

            b.Wire(bead, 0, score, 0)
             .Wire(Times(Plus(Fraction(round), -0.5f), MathF.Tau * radius / Round), 0, shape, 0)
             .Wire(Less(around, Span(score, 62f, 72f, radius - 0.06f, radius + 0.06f)), 0, shape, 1)
             .Wire(shape, 0, fill, 0)
             .Wire(Size(Less(bead, lit)), 0, elsewhere, 1);

            return Product(fill, Span(Product(From(1f, elsewhere), stroke), 0f, 1f, 0.4f, 1.7f));
        }

        // One row of squares, at a height. 'turned' is what its Euclid is rotated by,
        // if anything is.
        NodeInstance Row(float height, NodeInstance? turned, NodeInstance stroke)
        {
            var pattern = b.Add(EuclidType, (1, 1f), (2, Round), (3, 7f));
            var square = b.Add(BoxType, (2, 0.34f), (3, 0.34f), (4, 0.05f));
            var ink = b.Add(FillType, (1, 0.02f), (2, 0.05f));

            b.Wire(cell, 0, pattern, 0)
             .Wire(within, 0, square, 0)
             .Wire(Times(Plus(coord, -height, 1), 3.6f), 0, square, 1)
             .Wire(square, 0, ink, 0);

            if (turned is not null) b.Wire(turned, 0, pattern, 4);

            // An outline everywhere, and a fill where the pattern claps, brighter in
            // the column being played when it is this row that sounds.
            var filled = Product(
                Product(ink, pattern, 1), Span(Product(column, stroke), 0f, 1f, 0.45f, 1.6f));

            return Product(Sum(Times(ink, 0.4f, 1), filled), shown);
        }
    }
}
