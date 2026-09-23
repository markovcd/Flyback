using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// The three Figures, each doing the thing it is for, on its own knobs: a plate
/// struck on the beat fills the screen with sand, a harmonograph draws a chord
/// over it once a phrase, and the sand is read back as the overtones of a drone.
/// </summary>
/// <remarks>
/// Every sound is one module heard on its own and every region of the picture
/// is one module's output, so what a knob does is seen and heard at once.
/// <para>
/// Overtones reads the plate's swing once per partial, so it reads a twin of the
/// plate ringing its four lowest modes off the same strike rather than the
/// nine-mode one on the screen: the same figure, coarser, at under half the cost
/// a partial. Engine modules only besides the three, so the preset needs nothing
/// but Figures.
/// </para>
/// </remarks>
internal static class SandPreset
{
    public const string Name = "Sand";

    private const int Beats = 84;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // --- the clock ----------------------------------------------------------

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, Beats));

        // A strike every two beats. How hard, and where down the plate, drift on two
        // slow sines, so no two strikes are alike.
        var strikeRate = b.Add("math.mul", (1, 0.5f));
        var strikes = b.Add(NodeCatalog.PulseTypeId, (3, 0.05f), (4, 0.5f), (5, 0.5f));
        var force = b.Add(NodeCatalog.SineTypeId, (1, 0.11f), (3, 0.2f), (4, 0.75f));
        var strikeY = b.Add(NodeCatalog.SineTypeId, (1, 0.023f), (3, 0.3f), (4, 0.5f));

        // A phrase is sixteen beats; the harmonograph is set swinging at the start of each.
        var phraseRate = b.Add("math.mul", (1, 1f / 16f));
        var phrases = b.Add("seq.values", (2, 0.05f));
        StepsExtra.Set(phrases, [new Step(1f), new Step(0.8f), new Step(1f), new Step(0.6f)]);

        b.Wire(tempo, 0, strikeRate, 0)
         .Wire(strikeRate, 0, strikes, 1)
         .Wire(tempo, 0, phraseRate, 0)
         .Wire(phraseRate, 0, phrases, 1);

        // --- the plate ----------------------------------------------------------

        var strikeX = b.Patch.AddControl("strike x", 0.25f);
        var aspect = b.Patch.AddControl("aspect", 0.3f);
        var brightness = b.Patch.AddControl("brightness", 0.5f);

        var plate = b.Add(PlateModule.TypeId, (PlateModule.FreqPort, 110f), (PlateModule.DecayPort, 3.5f));
        Follows(plate, PlateModule.StrikeXPort, strikeX, 0.15f, 0.85f);
        Follows(plate, PlateModule.AspectPort, aspect, 1f, 2f);
        Follows(plate, PlateModule.BrightnessPort, brightness, 0f, 1f);

        b.Wire(strikes, 0, plate, PlateModule.TriggerPort)
         .Wire(force, 0, plate, PlateModule.VelocityPort)
         .Wire(strikeY, 0, plate, PlateModule.StrikeYPort);

        // The same plate at four modes, for Overtones to read its swing off.
        var heard = PlateModule.WithModes(
            b.Add(PlateModule.TypeId, (PlateModule.FreqPort, 110f), (PlateModule.DecayPort, 3.5f)), 2);
        Follows(heard, PlateModule.StrikeXPort, strikeX, 0.15f, 0.85f);
        Follows(heard, PlateModule.AspectPort, aspect, 1f, 2f);
        Follows(heard, PlateModule.BrightnessPort, brightness, 0f, 1f);

        b.Wire(strikes, 0, heard, PlateModule.TriggerPort)
         .Wire(force, 0, heard, PlateModule.VelocityPort)
         .Wire(strikeY, 0, heard, PlateModule.StrikeYPort);

        b.Group("Plate", strikeRate, strikes, force, strikeY, plate, heard);

        // --- the harmonograph ---------------------------------------------------

        var ratio = b.Patch.AddControl("ratio", 0.5f);
        var twist = b.Patch.AddControl("twist", 0.15f);

        var harmonograph = b.Add(
            HarmonographModule.TypeId,
            (HarmonographModule.SpeedPort, 0.35f),
            (HarmonographModule.PitchPort, 220f),
            (HarmonographModule.DampingPort, 12f),
            (HarmonographModule.PersistPort, 0.85f),
            (HarmonographModule.SizePort, 0.85f));
        Follows(harmonograph, HarmonographModule.RatioPort, ratio, 1f, 2f);
        Follows(harmonograph, HarmonographModule.TwistPort, twist, 0f, 1f);

        b.Wire(phrases, 1, harmonograph, HarmonographModule.TriggerPort)
         .Wire(phrases, 0, harmonograph, HarmonographModule.VelocityPort);

        b.Group("Harmonograph", phraseRate, phrases, harmonograph);

        // --- the overtones ------------------------------------------------------

        var row = b.Patch.AddControl("row", 0.5f);
        var sweep = b.Patch.AddControl("sweep", 0.6f);

        // The row read slides up and down the figure; 'row' is its middle, 'sweep' how far it goes.
        var reading = b.Add(NodeCatalog.SineTypeId, (1, 0.04f));
        Follows(reading, 3, sweep, 0f, 0.4f);
        Follows(reading, 4, row, 0.1f, 0.9f);

        // The bars keep to a band along the bottom of the screen.
        var band = b.Add("math.remap", (1, -1f), (2, -0.6f), (3, -1f), (4, 1f));
        var coord = b.Add(NodeCatalog.CoordTypeId);

        // A fifth below the plate.
        var overtones = b.Add(OvertonesModule.TypeId, (OvertonesModule.FreqPort, 110f / 1.5f), (OvertonesModule.TiltPort, -3f));

        b.Wire(reading, 0, overtones, OvertonesModule.RowPort)
         .Wire(heard, PlateModule.MotionPort, overtones, OvertonesModule.SpectrumPort)
         .Wire(coord, NodeCatalog.CoordYPort, band, 0)
         .Wire(band, 0, overtones, 7);

        b.Group("Overtones", reading, band, coord, overtones);

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

        // The plate dry at the front; the drone and the chord in a room behind it.
        var room = b.Add("math.add");
        var reverb = b.Add(NodeCatalog.ReverbTypeId, (1, 0.7f), (2, 0.75f), (3, 1f));
        var echo = b.Add(NodeCatalog.DelayTypeId, (1, 60f / Beats * 1.5f), (2, 0.4f), (3, 0.35f));

        var desk = b.Add(NodeCatalog.DeskTypeId, (2, 0.6f), (5, 0.55f), (8, 0.3f), (11, 0.28f), (14, 0.9f));

        b.Wire(plate, PlateModule.OutPort, desk, 0)
         .Wire(echo, 0, desk, 3)
         .Wire(harmonograph, HarmonographModule.LeftPort, desk, 6)
         .Wire(harmonograph, HarmonographModule.RightPort, desk, 7)
         .Wire(reverb, 0, desk, 9)
         .Wire(reverb, 1, desk, 10)
         .Wire(overtones, OvertonesModule.OutPort, echo, 0)
         .Wire(harmonograph, HarmonographModule.LeftPort, room, 0)
         .Wire(overtones, OvertonesModule.OutPort, room, 1)
         .Wire(room, 0, reverb, 0);

        b.Group("Mix", room, reverb, echo, desk);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));

        b.Wire(bars, 0, output, NodeCatalog.OutputColorPort)
         .Wire(desk, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(desk, 1, output, NodeCatalog.OutputRightPort);

        b.Patch.Describe(
            "A plate struck every two beats fills the screen with sand, a harmonograph draws a chord over "
            + "it once a phrase, and the sand is read back as the overtones of a drone. Turn the knobs: "
            + "the strike point and the plate's shape change the figure and the ring together, the ratio "
            + "changes the drawing and the chord together, and the row is where the drone reads the sand.");

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
