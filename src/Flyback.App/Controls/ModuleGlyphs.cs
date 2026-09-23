using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The mark drawn faintly across a module's body: its own where the module is one
/// you would name, and its category's otherwise.
/// </summary>
/// <remarks>
/// Zoomed out far enough that no label can be read, the tint and the mark are the
/// whole of what tells one module from another — which is why the mark is large
/// and set in the body rather than small and set in the header. A module earns a
/// drawing of its own by being the only one of itself: the four waveforms, the
/// sink, the clock, the plane, the loop. Everything else is one of a family and
/// is drawn as the family.
/// <para>
/// Laid out on a twenty-four unit box and stroked, so one scale takes a path to
/// whatever size it is wanted at, and a category nothing here knows draws nothing
/// rather than a shape invented for it.
/// </para>
/// </remarks>
internal static class ModuleGlyphs
{
    /// <summary>The side of the box every path below is drawn on.</summary>
    public const double Box = 24;

    /// <summary>How thick the strokes are at that size.</summary>
    public const double Thickness = 1.7;

    /// <summary>What to draw across <paramref name="def"/>, or null where nothing is known.</summary>
    public static Geometry? For(NodeDef def) =>
        ModuleSkins.Of(def) is ModuleSkin.Palette { Glyph: { } given } ? Given(given)
        : Own.TryGetValue(def.TypeId, out var mine) ? mine
        : OfCategory(def.Category);

    /// <summary>
    /// A plugin's own path data on the same twenty-four unit box, read once and
    /// kept — the failure too, so a path that will not parse is not parsed again
    /// every frame. Nothing is drawn for one, which is what a mark does when it
    /// is not there.
    /// </summary>
    private static Geometry? Given(string data)
    {
        if (Parsed.TryGetValue(data, out var kept)) return kept;

        Geometry? read;

        try
        {
            read = Path(data);
        }
        catch (Exception)
        {
            read = null;
        }

        Parsed[data] = read;

        return read;
    }

    private static readonly Dictionary<string, Geometry?> Parsed = [];

    /// <summary>What a module of <paramref name="category"/> is drawn as by default.</summary>
    public static Geometry? OfCategory(string category) =>
        ByCategory.TryGetValue(category, out var family) ? family : null;

    /// <summary>The type ids that have a mark of their own.</summary>
    public static IEnumerable<string> Named => Own.Keys;

    /// <summary>Several modules drawn as one: two blocks inside a frame of corners.</summary>
    public static Geometry Group { get; } = Path(
        "M3,8 L3,3 L8,3 M16,3 L21,3 L21,8 M21,16 L21,21 L16,21 M8,21 L3,21 L3,16 "
        + "M6,9 L11,9 L11,15 L6,15 Z M13,9 L18,9 L18,15 L13,15 Z");

    /// <summary>
    /// The modules that are the only one of themselves, keyed by type id.
    /// </summary>
    /// <remarks>
    /// A type id that is not in the catalog costs nothing: the lookup falls to
    /// the category, which is where a module whose drawing was dropped belongs
    /// anyway.
    /// </remarks>
    private static Dictionary<string, Geometry> Own { get; } = new()
    {
        // The four fixed waveforms and the fifth with a duty cycle, each drawn
        // as the wave it is — the one case where the picture is the definition.
        [NodeCatalog.SineTypeId] = Path("M2,12 C3.6,3.5 6.4,3.5 8,12 C9.6,20.5 12.4,20.5 14,12 C15.6,3.5 18.4,3.5 20,12"),
        [NodeCatalog.SawTypeId] = Path("M2,18 L8,6 L8,18 L14,6 L14,18 L20,6 L20,18"),
        [NodeCatalog.TriangleTypeId] = Path("M2,18 L7,6 L12,18 L17,6 L22,18"),
        [NodeCatalog.SquareTypeId] = Path("M2,18 L2,6 L9,6 L9,18 L16,18 L16,6 L22,6"),
        [NodeCatalog.PulseTypeId] = Path("M2,18 L5,18 L5,6 L8,6 L8,18 L15,18 L15,6 L18,6 L18,18 L22,18"),

        // A wave that dies away rather than repeating: the one oscillator that
        // is struck rather than run.
        [NodeCatalog.StringTypeId] = Path(
            "M2,12 C3.6,4 6.4,4 8,12 C9.4,17 11.4,17 12.6,12 C13.6,8.5 14.8,8.5 15.6,12 L21,12"),

        // A clock, and the plane it is read across: the two sockets everything
        // else here is normalled to.
        [NodeCatalog.TimeTypeId] = Path(
            "M3,12 A9,9 0 1 1 21,12 A9,9 0 1 1 3,12 M12,6 L12,12.5 L16.5,14.5"),
        [NodeCatalog.CoordTypeId] = Path(
            "M12,3 L12,21 M3,12 L21,12 "
            + "M18.6,7.4 A2.3,2.3 0 1 1 14,7.4 A2.3,2.3 0 1 1 18.6,7.4"),

        // The wire that runs backwards, drawn as the turn it is.
        [NodeCatalog.FeedbackTypeId] = Path(
            "M12,4 A8,8 0 0 1 20,12 A8,8 0 0 1 12,20 A8,8 0 0 1 4,12 M1.4,14.7 L4,11.6 L6.6,14.7"),

        // Waves leaving a point and arriving at one: the two ends of a bus.
        [NodeCatalog.SendTypeId] = Path(
            "M4,12 A2,2 0 1 1 8,12 A2,2 0 1 1 4,12 M9.5,8.5 A5,5 0 0 1 9.5,15.5 "
            + "M12.4,5.6 A9,9 0 0 1 12.4,18.4 M15.2,2.8 A13,13 0 0 1 15.2,21.2"),
        [NodeCatalog.ReceiveTypeId] = Path(
            "M16,12 A2,2 0 1 1 20,12 A2,2 0 1 1 16,12 M14.5,8.5 A5,5 0 0 0 14.5,15.5 "
            + "M11.6,5.6 A9,9 0 0 0 11.6,18.4 M8.8,2.8 A13,13 0 0 0 8.8,21.2"),

        // Braces: a module that is whatever is written inside it.
        [NodeCatalog.ExpressionTypeId] = Path(
            "M9.5,4 C6.5,4 8,10.5 4.5,12 C8,13.5 6.5,20 9.5,20 "
            + "M14.5,4 C17.5,4 16,10.5 19.5,12 C16,13.5 17.5,20 14.5,20"),

        // Keys, a file of samples, a picture, a dial: the four sources that are
        // not a signal the patch computed.
        [NodeCatalog.MidiTypeId] = Path(
            "M2,5 L22,5 L22,19 L2,19 Z M9,5 L9,19 M16,5 L16,19 "
            + "M6,5 L6,13 L11,13 L11,5 M13,5 L13,13 L18,13 L18,5"),
        [NodeCatalog.SampleTypeId] = Path("M4,9 L4,15 M8,5 L8,19 M12,8 L12,16 M16,3 L16,21 M20,10 L20,14"),
        [NodeCatalog.PictureTypeId] = Path(
            "M3,5 L21,5 L21,19 L3,19 Z M3,16 L9,10 L13.5,14.5 L16.5,11.5 L21,16 "
            + "M14.9,9.4 A1.8,1.8 0 1 1 18.5,9.4 A1.8,1.8 0 1 1 14.9,9.4"),
        [NodeCatalog.ValueTypeId] = Path("M4,12 A8,8 0 1 1 20,12 A8,8 0 1 1 4,12 M12,12 L12,5"),

        // A tempo swung like a metronome, and a level held flat until the next
        // trigger: the two ways Timing counts out when rather than what.
        [NodeCatalog.TempoTypeId] = Path(
            "M5,21 L12,3 L19,21 Z M12,7 L16,18 M17.3,18 A1.3,1.3 0 1 1 14.7,18 A1.3,1.3 0 1 1 17.3,18"),
        [NodeCatalog.HoldTypeId] = Path(
            "M2,17 L6,17 L6,10 L10,10 L10,14 L14,14 L14,6 L18,6 L18,12 L22,12"),

        // Four lines meeting in one: what a mixer does to its inputs.
        [NodeCatalog.MixerTypeId] = Path(
            "M3,4 L12,12 M3,9.3 L12,12 M3,14.7 L12,12 M3,20 L12,12 M12,12 L21,12"),

        // A mixing desk: four rails and the slider each is resting at.
        [NodeCatalog.DeskTypeId] = Path(
            "M5,4 L5,20 M10,4 L10,20 M15,4 L15,20 M20,4 L20,20 "
            + "M3,8 L7,8 M8,14 L12,14 M13,6 L17,6 M18,16 L22,16"),

        // A sweep snapped to even steps, which is what a Quantiser does to
        // whatever arrives.
        [NodeCatalog.QuantiserTypeId] = Path(
            "M3,19 L21,5 M3,19 L3,15 L7,15 L7,11 L11,11 L11,9 L15,9 L15,7 L19,7 L19,5"),

        // A field with no pattern to it.
        [NodeCatalog.NoiseTypeId] = Path("M2,12 L4,6 L6,16 L8,4 L10,14 L12,7 L14,18 L16,9 L18,15 L20,5 L22,12"),

        // The key hitting, and the level it ducked growing back from nothing
        // rather than swinging at one height throughout.
        [NodeCatalog.DuckTypeId] = Path(
            "M3,20 L3,3 M5.5,12 C6.7,10.5 9.3,10.5 10.5,12 "
            + "C11.7,8 15.3,8 16.5,12 C17.2,5.5 20.8,5.5 22,12"),

        // Values scattered with no order to them.
        [NodeCatalog.RandomTypeId] = Path(
            "M3.6,7 A1.4,1.4 0 1 1 6.4,7 A1.4,1.4 0 1 1 3.6,7 "
            + "M10.6,4.5 A1.4,1.4 0 1 1 13.4,4.5 A1.4,1.4 0 1 1 10.6,4.5 "
            + "M16.6,10 A1.4,1.4 0 1 1 19.4,10 A1.4,1.4 0 1 1 16.6,10 "
            + "M6.6,16 A1.4,1.4 0 1 1 9.4,16 A1.4,1.4 0 1 1 6.6,16 "
            + "M15.6,19 A1.4,1.4 0 1 1 18.4,19 A1.4,1.4 0 1 1 15.6,19"),

        // A resonant peak on the cutoff, and a wave with its tops clipped flat.
        [NodeCatalog.FilterTypeId] = Path("M2,14 L9,14 C11,14 11,6 13,6 C15,6 14,10 16,10 C18,10 19,17 22,17"),
        [NodeCatalog.DriveTypeId] = Path(
            "M2,12 C3,6 4.5,6 6,6 L9,6 C10.5,6 11,9 12,12 C13,15 13.5,18 15,18 L18,18 C19.5,18 21,18 22,12"),

        // Two repeats, the second quieter, and a room with sound bouncing in it.
        [NodeCatalog.DelayTypeId] = Path("M3,19 L6,4 L9,19 M13,19 L15.5,10 L18,19"),
        [NodeCatalog.ReverbTypeId] = Path(
            "M3,4 L21,4 L21,20 L3,20 Z M6,12 A2,2 0 1 1 10,12 A2,2 0 1 1 6,12 "
            + "M13.6,8 A1.4,1.4 0 1 1 16.4,8 A1.4,1.4 0 1 1 13.6,8 "
            + "M14.6,16 A1.4,1.4 0 1 1 17.4,16 A1.4,1.4 0 1 1 14.6,16"),

        // Measurement's five ways of looking at a signal: two charts, one
        // ruled where it is now and one biased into the past, that same past
        // turned into a spectrum, a level read as a number instead of drawn,
        // and a loop read as a waveform.
        [NodeCatalog.ProbeTypeId] = Path(
            "M3,5 L21,5 L21,19 L3,19 Z M12,5 L12,19 "
            + "M4,12 C6,7 8,7 10,12 C11,14 13,14 14,12 C16,7 18,7 20,12"),
        [NodeCatalog.ScopeTypeId] = Path(
            "M3,5 L21,5 L21,19 L3,19 Z M5.5,15 C7.5,15 7.5,9 10,9 C12.5,9 12.5,15 15,15 C17,15 17,10 18.5,10"),
        [NodeCatalog.AnalyzerTypeId] = Path("M4,20 L4,13 M8,20 L8,6 M12,20 L12,10 M16,20 L16,4 M20,20 L20,15 M2,21.5 L22,21.5"),
        [NodeCatalog.MeterTypeId] = Path("M8,4 L13,4 L13,20 L8,20 Z M15.5,6 L19.5,6 M15.5,11 L19.5,11 M15.5,16 L19.5,16"),
        [NodeCatalog.ScanTypeId] = Path(
            "M3,5 L21,5 L21,19 L3,19 Z M5,12 L19,12 "
            + "M10,10.3 L12,8.3 L14,10.3 M10,13.7 L12,15.7 L14,13.7"),
    };

    /// <summary>
    /// What a module is drawn as where it has no drawing of its own. Named against
    /// <see cref="ModuleCategories"/> for the reason <see cref="Colors.Accent"/>
    /// is: a category renamed there should not quietly lose its mark.
    /// </summary>
    private static Dictionary<string, Geometry> ByCategory { get; } = new()
    {
        // A point with rays coming off it.
        [ModuleCategories.Sources] = Path(
            "M6,12 A2.6,2.6 0 1 1 11.2,12 A2.6,2.6 0 1 1 6,12 "
            + "M13.4,12 L21,12 M12.8,8.4 L19.4,4.6 M12.8,15.6 L19.4,19.4"),

        // The wave in a ring, which is what an oscillator is drawn as everywhere.
        [ModuleCategories.Oscillators] = Path(
            "M3,12 A9,9 0 1 1 21,12 A9,9 0 1 1 3,12 "
            + "M7,12 C8.3,7.6 10.7,7.6 12,12 C13.3,16.4 15.7,16.4 17,12"),

        // Two squares of a check, in the frame the field fills.
        [ModuleCategories.Patterns] = Path(
            "M4,4 L20,4 L20,20 L4,20 Z M4,4 L12,4 L12,12 L4,12 Z M12,12 L20,12 L20,20 L12,20 Z"),

        // A circle over a square: an edge is an edge whichever shape has it.
        [ModuleCategories.Forms] = Path(
            "M3,9 L14,9 L14,20 L3,20 Z M7.5,11 A6.5,6.5 0 1 1 20.5,11 A6.5,6.5 0 1 1 7.5,11"),

        // A square and the turn being applied to it.
        [ModuleCategories.Geometry] = Path(
            "M7,11 L18,11 L18,21 L7,21 Z M3.5,10 C3.5,3 11,1.2 15.2,5.2 M12.3,3.6 L15.6,5.5 L13.6,8.7"),

        // Three lights over one another, which is how a color is made.
        [ModuleCategories.Color] = Path(
            "M3.6,10 A5.6,5.6 0 1 1 14.8,10 A5.6,5.6 0 1 1 3.6,10 "
            + "M9.2,10 A5.6,5.6 0 1 1 20.4,10 A5.6,5.6 0 1 1 9.2,10 "
            + "M6.4,15.6 A5.6,5.6 0 1 1 17.6,15.6 A5.6,5.6 0 1 1 6.4,15.6"),

        // The operator star: a plus and a times over one another.
        [ModuleCategories.Maths] = Path(
            "M12,3 L12,21 M3,12 L21,12 M5.6,5.6 L18.4,18.4 M18.4,5.6 L5.6,18.4"),

        // A note, because a pitch is the one number here that has a name.
        [ModuleCategories.Pitch] = Path(
            "M5.2,17 A3.4,2.7 0 1 1 12,17 A3.4,2.7 0 1 1 5.2,17 M12,17 L12,4 L19,6.2 L19,9.4"),

        // An envelope: attack, decay, sustain, release, which is what timing
        // draws whatever shape it is drawing.
        [ModuleCategories.Timing] = Path("M3,20 L8,5 L12,12 L16,12 L21,20"),

        // A transfer curve in its axes: in along the bottom, out up the side.
        [ModuleCategories.Shaping] = Path(
            "M4,4 L4,20 L20,20 M4.5,19 C9.5,19 8.5,12 12,12 C15.5,12 14.5,5 19.5,5"),

        // Repeats, each quieter than the last.
        [ModuleCategories.TimeEffects] = Path("M4,4 L4,20 M9.5,7 L9.5,17 M15,9.5 L15,14.5 M20,11 L20,13"),

        // The same block again a step behind itself.
        [ModuleCategories.Feedback] = Path(
            "M3,7 L11,7 L11,15 L3,15 Z M8.5,9 L16.5,9 L16.5,17 L8.5,17 Z M14,11 L22,11 L22,19 L14,19 Z"),

        // A gauge with a needle: a module that looks rather than makes.
        [ModuleCategories.Measurement] = Path(
            "M2.5,18.5 A9.5,9.5 0 0 1 21.5,18.5 M12,18.5 L17.2,10.4 "
            + "M10.5,18.5 A1.5,1.5 0 1 1 13.5,18.5 A1.5,1.5 0 1 1 10.5,18.5"),

        // A patch cable between two jacks.
        [ModuleCategories.Routing] = Path(
            "M3,17 A2.5,2.5 0 1 1 8,17 A2.5,2.5 0 1 1 3,17 "
            + "M16,7 A2.5,2.5 0 1 1 21,7 A2.5,2.5 0 1 1 16,7 M5.5,14.5 C5.5,4 18.5,20 18.5,9.5"),

        // A screen on a stand: the sink, which is the whole of this category.
        [ModuleCategories.Output] = Path("M3,5 L21,5 L21,16 L3,16 Z M12,16 L12,20 M8.5,20 L15.5,20"),
    };

    /// <summary>
    /// Parsed once, because the canvas asks for these per module per frame.
    /// </summary>
    private static Geometry Path(string data) => Geometry.Parse(data);
}
