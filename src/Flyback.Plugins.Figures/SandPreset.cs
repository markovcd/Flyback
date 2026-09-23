using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// A song in D minor played on the three Figures: the plate is the melody and its
/// sand dances to it, the harmonograph draws each chord as it sounds it, and the
/// sand is read back as the pad under both. A second plate is the kick.
/// </summary>
/// <remarks>
/// Four chords a bar each, Dm, B♭, F and C, and a melody of thirty-two eighths over
/// them. The harmonograph's two pendulums are two notes of the chord, so its ratio
/// changes with the chord and every bar draws a different figure. The drums come in
/// on the second time round and the hat on the third.
/// <para>
/// Overtones reads the plate's swing once per partial, so it reads a twin of the
/// melody plate ringing its four lowest modes off the same strikes: the same figure,
/// coarser, at under half the cost a partial. Eight panel knobs, one per encoder on a
/// Syntakt's page. Engine modules only besides the three, so the preset needs nothing
/// but Figures.
/// </para>
/// </remarks>
internal static class SandPreset
{
    public const string Name = "Sand";

    private const int Beats = 96;

    /// <summary>A rest: a step with no gate, so nothing is struck.</summary>
    private static Step Rest => new(0f, Volume: 0f);

    private static readonly Step[] Melody =
    [
        // Dm
        new(69f), Rest, new(72f), new(74f), Rest, new(77f), new(76f), new(74f),
        // B♭
        new(74f), Rest, new(70f), new(74f), Rest, new(77f), new(81f), new(77f),
        // F
        new(72f), Rest, new(77f), new(81f), Rest, new(79f), new(77f), new(72f),
        // C
        new(76f), Rest, new(79f), new(76f), new(72f), Rest, new(74f), Rest,
    ];

    /// <summary>How hard each eighth of a bar is struck: the downbeat, then the backbeat.</summary>
    private static readonly Step[] Accents =
        [.. new[] { 1f, 0.45f, 0.7f, 0.5f, 0.85f, 0.45f, 0.7f, 0.55f }.Select(v => new Step(v))];

    private static readonly Step[] Roots = [new(38f), new(34f), new(41f), new(36f)];

    /// <summary>The second pendulum against the first: a minor third, a major third, a fifth, a fourth.</summary>
    private static readonly Step[] Ratios = [new(1.2f), new(1.25f), new(1.5f), new(4f / 3f)];

    /// <summary>Two bars of kick in eighths, how hard in the value.</summary>
    private static readonly Step[] Kicks =
    [
        new(1f), Rest, Rest, Rest, new(0.8f), Rest, Rest, new(0.6f),
        new(1f), Rest, Rest, Rest, new(0.8f), Rest, new(0.7f), Rest,
    ];

    /// <summary>A beat of hat in sixteenths, the offbeat open.</summary>
    private static readonly Step[] Hats =
        [new(1f, Volume: 0.2f), new(1f, Volume: 0.35f), new(1f, Volume: 0.9f), new(1f, Volume: 0.35f)];

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var tone = b.Patch.AddControl("tone", 0.3f);
        var shape = b.Patch.AddControl("shape", 0.3f);
        var strike = b.Patch.AddControl("strike", 0.25f);
        var ring = b.Patch.AddControl("ring", 0.35f);
        var twist = b.Patch.AddControl("twist", 0.15f);
        var row = b.Patch.AddControl("row", 0.5f);
        var drums = b.Patch.AddControl("drums", 0.8f);
        var space = b.Patch.AddControl("space", 0.5f);

        // --- the clock ----------------------------------------------------------

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, Beats));

        // One step a progression, four bars: what comes in when.
        var drumsIn = b.Add("seq.values", (1, 1f / 16f));
        StepsExtra.Set(drumsIn, [new Step(0f), new Step(1f), new Step(1f), new Step(1f)]);

        var hatsIn = b.Add("seq.values", (1, 1f / 16f));
        StepsExtra.Set(hatsIn, [new Step(0f), new Step(0f), new Step(1f), new Step(1f)]);

        b.Wire(tempo, 1, drumsIn, 0)
         .Wire(tempo, 1, hatsIn, 0);

        b.Group("Song", tempo, drumsIn, hatsIn);

        // --- the melody ---------------------------------------------------------

        var notes = b.Add("seq.notes", (1, 2f), (2, 0.6f));
        StepsExtra.Set(notes, Melody);

        var accents = b.Add("seq.values", (1, 2f));
        StepsExtra.Set(accents, Accents);

        // The pitch held from strike to strike, so a note ringing on is not retuned under it.
        var pitch = b.Add("audio.note");
        var held = b.Add(NodeCatalog.HoldTypeId);

        // Where down the plate it is hit drifts on a slow sine, so no two strikes are alike.
        var strikeY = b.Add(NodeCatalog.SineTypeId, (1, 0.023f), (3, 0.3f), (4, 0.5f));

        var plate = b.Add(PlateModule.TypeId, (PlateModule.FreqPort, 440f));
        Follows(plate, PlateModule.BrightnessPort, tone, 0f, 1f);
        Follows(plate, PlateModule.AspectPort, shape, 1f, 2f);
        Follows(plate, PlateModule.StrikeXPort, strike, 0.15f, 0.85f);
        Follows(plate, PlateModule.DecayPort, ring, 0.3f, 4f);

        b.Wire(tempo, 1, notes, 0)
         .Wire(tempo, 1, accents, 0)
         .Wire(notes, 0, pitch, 0)
         .Wire(pitch, 0, held, 0)
         .Wire(notes, 1, held, 1)
         .Wire(held, 0, plate, PlateModule.FreqPort)
         .Wire(notes, 1, plate, PlateModule.TriggerPort)
         .Wire(accents, 0, plate, PlateModule.VelocityPort)
         .Wire(strikeY, 0, plate, PlateModule.StrikeYPort);

        // The same plate at four modes, for Overtones to read its swing off.
        var heard = PlateModule.WithModes(b.Add(PlateModule.TypeId), 2);
        Follows(heard, PlateModule.BrightnessPort, tone, 0f, 1f);
        Follows(heard, PlateModule.AspectPort, shape, 1f, 2f);
        Follows(heard, PlateModule.StrikeXPort, strike, 0.15f, 0.85f);
        Follows(heard, PlateModule.DecayPort, ring, 0.3f, 4f);

        b.Wire(notes, 1, heard, PlateModule.TriggerPort)
         .Wire(accents, 0, heard, PlateModule.VelocityPort)
         .Wire(strikeY, 0, heard, PlateModule.StrikeYPort);

        b.Group("Melody", notes, accents, pitch, held, strikeY, plate, heard);

        // --- the chords ---------------------------------------------------------

        // A bar a chord. The root and the ratio are held from one chord's strike to the next.
        var roots = b.Add("seq.notes", (1, 0.25f), (2, 0.95f));
        StepsExtra.Set(roots, Roots);

        var ratios = b.Add("seq.values", (1, 0.25f));
        StepsExtra.Set(ratios, Ratios);

        // Two octaves up for the harmonograph, one down for the pad.
        var chordPitch = b.Add("audio.note", (1, 2f));
        var chordHeld = b.Add(NodeCatalog.HoldTypeId);
        var ratioHeld = b.Add(NodeCatalog.HoldTypeId);
        var padPitch = b.Add("audio.note");

        var harmonograph = b.Add(
            HarmonographModule.TypeId,
            (HarmonographModule.VelocityPort, 0.7f),
            (HarmonographModule.SpeedPort, 0.8f),
            (HarmonographModule.DampingPort, 3f),
            (HarmonographModule.PersistPort, 0.6f),
            (HarmonographModule.SizePort, 0.85f));
        Follows(harmonograph, HarmonographModule.TwistPort, twist, 0f, 1f);

        b.Wire(tempo, 1, roots, 0)
         .Wire(tempo, 1, ratios, 0)
         .Wire(roots, 0, chordPitch, 0)
         .Wire(chordPitch, 0, chordHeld, 0)
         .Wire(roots, 1, chordHeld, 1)
         .Wire(ratios, 0, ratioHeld, 0)
         .Wire(roots, 1, ratioHeld, 1)
         .Wire(roots, 0, padPitch, 0)
         .Wire(chordHeld, 0, harmonograph, HarmonographModule.PitchPort)
         .Wire(ratioHeld, 0, harmonograph, HarmonographModule.RatioPort)
         .Wire(roots, 1, harmonograph, HarmonographModule.TriggerPort);

        b.Group("Chords", roots, ratios, chordPitch, chordHeld, ratioHeld, padPitch, harmonograph);

        // --- the pad ------------------------------------------------------------

        // The row read slides up and down the figure around 'row'.
        var reading = b.Add(NodeCatalog.SineTypeId, (1, 0.04f), (3, 0.25f));
        Follows(reading, 4, row, 0.1f, 0.9f);

        // The bars keep to a band along the bottom of the screen.
        var band = b.Add("math.remap", (1, -1f), (2, -0.6f), (3, -1f), (4, 1f));
        var coord = b.Add(NodeCatalog.CoordTypeId);

        var overtones = b.Add(OvertonesModule.TypeId, (OvertonesModule.TiltPort, -4f), (OvertonesModule.AmpPort, 0.8f));

        b.Wire(padPitch, 0, overtones, OvertonesModule.FreqPort)
         .Wire(reading, 0, overtones, OvertonesModule.RowPort)
         .Wire(heard, PlateModule.MotionPort, overtones, OvertonesModule.SpectrumPort)
         .Wire(coord, NodeCatalog.CoordYPort, band, 0)
         .Wire(band, 0, overtones, 7);

        b.Group("Pad", reading, band, coord, overtones);

        // --- the drums ----------------------------------------------------------

        // A plate struck dead center rings its lowest mode alone: a round, low thump.
        var kicks = b.Add("seq.values", (1, 2f), (2, 0.3f));
        StepsExtra.Set(kicks, Kicks);

        var kickGate = b.Add("math.mul");
        var kick = PlateModule.WithModes(b.Add(
            PlateModule.TypeId,
            (PlateModule.FreqPort, 50f),
            (PlateModule.AspectPort, 1f),
            (PlateModule.DecayPort, 0.3f),
            (PlateModule.BrightnessPort, 0f),
            (PlateModule.StrikeXPort, 0.5f),
            (PlateModule.StrikeYPort, 0.5f)), 2);

        // White noise above 7 kHz, opened for a moment on each sixteenth.
        var hats = b.Add("seq.values", (1, 4f), (2, 0.25f), (3, 0.12f));
        StepsExtra.Set(hats, Hats);

        var hiss = b.Add(NodeCatalog.NoiseTypeId);
        var bright = b.Add(NodeCatalog.FilterTypeId, (1, 7000f), (2, 0.1f));
        var hatOpen = b.Add("math.mul");
        var hat = b.Add("math.mul", (1, 0.25f));
        var hatLevel = b.Add("math.mul");

        var kit = b.Add("math.add");
        var drumLevel = b.Add("math.mul");
        Follows(drumLevel, 1, drums, 0f, 1f);

        b.Wire(tempo, 1, kicks, 0)
         .Wire(kicks, 1, kickGate, 0)
         .Wire(drumsIn, 0, kickGate, 1)
         .Wire(kickGate, 0, kick, PlateModule.TriggerPort)
         .Wire(kicks, 0, kick, PlateModule.VelocityPort)
         .Wire(tempo, 1, hats, 0)
         .Wire(hiss, 0, bright, 0)
         .Wire(bright, 2, hatOpen, 0)
         .Wire(hats, 1, hatOpen, 1)
         .Wire(hatOpen, 0, hat, 0)
         .Wire(hat, 0, hatLevel, 0)
         .Wire(hatsIn, 0, hatLevel, 1)
         .Wire(kick, PlateModule.OutPort, kit, 0)
         .Wire(hatLevel, 0, kit, 1)
         .Wire(kit, 0, drumLevel, 0);

        b.Group("Drums", kicks, kickGate, kick, hats, hiss, bright, hatOpen, hat, hatLevel, kit, drumLevel);

        // --- the picture --------------------------------------------------------

        // The plate's swing as a warm glow, and the sand laid over it in pale grains
        // wherever the plate is still enough to hold it. The edges never move and so
        // are always sanded; a vignette keeps that from being a frame round the picture.
        var lit = b.Add("math.mul", (1, 2.2f));
        var glow = b.Add("color.hsv", (0, 0.07f), (1, 0.7f));
        var grains = b.Add("math.mul", (1, 0.55f));
        var sand = b.Add("color.ink", (2, 0.93f), (3, 0.85f), (4, 0.62f));
        var edges = b.Add("color.vignette", (3, 0.75f), (4, 1.35f), (5, 0.85f));

        // The pen's line over it, in ink.
        var ink = b.Add("color.ink", (2, 0.55f), (3, 0.75f), (4, 1f));

        // The readings along the bottom, faint, in mint.
        var faint = b.Add("math.mul", (1, 0.35f));
        var bars = b.Add("color.ink", (2, 0.56f), (3, 0.84f), (4, 0.7f));

        b.Wire(plate, PlateModule.MotionPort, lit, 0)
         .Wire(lit, 0, glow, 2)
         .Wire(glow, 0, sand, 0)
         .Wire(plate, PlateModule.FigurePort, grains, 0)
         .Wire(grains, 0, sand, 1)
         .Wire(sand, 0, edges, 0)
         .Wire(edges, 0, ink, 0)
         .Wire(harmonograph, HarmonographModule.FigurePort, ink, 1)
         .Wire(ink, 0, bars, 0)
         .Wire(overtones, OvertonesModule.BarsPort, faint, 0)
         .Wire(faint, 0, bars, 1);

        b.Group("Picture", lit, glow, grains, sand, edges, ink, faint, bars);

        // --- the sound ----------------------------------------------------------

        // The melody dry at the front, the drums under it, and everything but the
        // drums in a room whose share is 'space'. The pad echoes on the dotted eighth.
        var echo = b.Add(NodeCatalog.DelayTypeId, (1, 60f / Beats * 0.75f), (2, 0.35f), (3, 0.3f));
        var sent = b.Add("math.add");
        var room = b.Add("math.add");
        var reverb = b.Add(NodeCatalog.ReverbTypeId, (1, 0.75f), (2, 0.7f), (3, 1f));

        var desk = b.Add(NodeCatalog.DeskTypeId, (2, 0.5f), (5, 0.45f), (8, 0.35f), (14, 1.8f));
        Follows(desk, 11, space, 0f, 0.6f);

        b.Wire(plate, PlateModule.OutPort, desk, 0)
         .Wire(overtones, OvertonesModule.OutPort, echo, 0)
         .Wire(echo, 0, desk, 3)
         .Wire(harmonograph, HarmonographModule.LeftPort, desk, 6)
         .Wire(harmonograph, HarmonographModule.RightPort, desk, 7)
         .Wire(reverb, 0, desk, 9)
         .Wire(reverb, 1, desk, 10)
         .Wire(drumLevel, 0, desk, 12)
         .Wire(drumLevel, 0, desk, 13)
         .Wire(plate, PlateModule.OutPort, sent, 0)
         .Wire(harmonograph, HarmonographModule.LeftPort, sent, 1)
         .Wire(sent, 0, room, 0)
         .Wire(echo, 0, room, 1)
         .Wire(room, 0, reverb, 0);

        b.Group("Mix", echo, sent, room, reverb, desk);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));

        b.Wire(bars, 0, output, NodeCatalog.OutputColorPort)
         .Wire(desk, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(desk, 1, output, NodeCatalog.OutputRightPort);

        b.Patch.Describe(
            "A song in D minor on the three Figures. The plate plays the melody and its sand dances "
            + "to it, the harmonograph draws each chord as it sounds it, and the sand is read back as "
            + "the pad under both; a second plate is the kick. 'tone', 'shape', 'strike' and 'ring' "
            + "are the plate, 'twist' the drawing, 'row' where the pad reads the sand, and 'drums' "
            + "and 'space' the mix.");

        return b.Build();
    }

    /// <summary>A socket that follows a panel knob from <paramref name="low"/> to <paramref name="high"/>, resting where the knob rests.</summary>
    private static void Follows(NodeInstance node, int port, PatchControl knob, float low, float high)
    {
        var link = new ControlLink(knob.Id, low, high);

        node.InputValues[port] = link.At(knob.Value);
        ControlMap.Link(node, port, link);
    }
}
