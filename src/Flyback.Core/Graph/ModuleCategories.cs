namespace Flyback.Core.Graph;

/// <summary>
/// The sections the palette has, and the order they appear in.
/// </summary>
/// <remarks>
/// A curated set rather than whatever strings happen to be in the catalogue.
/// Two things went wrong while a category was only a word a module wrote down.
/// Two providers meant different things by one of them — the engine's Rotate and
/// a plugin's Reverb both said "Space", and the palette drew them as one section
/// — and two meant nearly the same thing by different ones, because "Shape" was
/// taken by the waveshapers and the shapes had to be called something else.
/// Neither is a mistake a reader of either file could have caught: the word is
/// right where it stands, and only wrong beside a word in another assembly.
/// <para>
/// So the names live here, together, where picking one is a choice between the
/// ones that already exist. <see cref="All"/> is also the display order, which
/// keeps a plugin's installation from moving any section around.
/// </para>
/// <para>
/// A plugin may still name a category of its own — nothing refuses an unknown
/// string, and a plugin that adds a genuinely new kind of module should not have
/// to wait for the engine to admit it. Those sort after the ones named here. What
/// the engine's own modules may say is checked by a test.
/// </para>
/// </remarks>
public static class ModuleCategories
{
    /// <summary>Where a signal comes from before anything is done to it.</summary>
    public const string Sources = "Sources";

    /// <summary>The fixed-shape waveforms, and the one stacked oscillator.</summary>
    public const string Oscillators = "Oscillators";

    /// <summary>Fields over the plane: noise, checks, rings and the fractals.</summary>
    public const string Patterns = "Patterns";

    /// <summary>Shapes with edges, as distance fields to fill and combine.</summary>
    public const string Forms = "Forms";

    /// <summary>What bends the plane a field is read across.</summary>
    public const string Geometry = "Geometry";

    /// <summary>Building a color, taking one apart, and correcting one.</summary>
    public const string Color = "Color";

    /// <summary>Arithmetic, and the desk that sums four of anything.</summary>
    public const string Maths = "Maths";

    /// <summary>
    /// The modules that know a pitch is not an ordinary number — hertz, note
    /// numbers, and snapping to a scale.
    /// </summary>
    public const string Pitch = "Pitch";

    /// <summary>
    /// What decides when something happens: tempo, the two step sequencers, the
    /// envelope and the hold.
    /// </summary>
    public const string Timing = "Timing";

    /// <summary>What is done to a waveform's shape — folding, driving, filtering.</summary>
    public const string Shaping = "Shaping";

    /// <summary>
    /// The effects built on a delay line: repeats, rooms, and the three sweeps.
    /// Audio only, every one of them, because a delay line needs a memory.
    /// </summary>
    public const string TimeEffects = "Time effects";

    /// <summary>Reading back what has already been evaluated.</summary>
    public const string Feedback = "Feedback";

    /// <summary>
    /// What looks at a signal rather than making one: the two charts, the meter
    /// and the scan.
    /// </summary>
    public const string Measurement = "Measurement";

    /// <summary>The sink, which is one module and always exactly one.</summary>
    public const string Output = "Output";

    /// <summary>
    /// Every category the engine names, in the order the palette shows them.
    /// </summary>
    /// <remarks>
    /// Roughly the order a patch is built in — where a signal comes from, what
    /// makes one, what bends it, what colors it, what times it, what shapes it,
    /// and what it ends at. The sink is last because it is where the patch ends,
    /// and because it is the one section nobody goes looking in: a patch already
    /// has its Output and cannot be given a second.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        Sources,
        Oscillators,
        Patterns,
        Forms,
        Geometry,
        Color,
        Maths,
        Pitch,
        Timing,
        Shaping,
        TimeEffects,
        Feedback,
        Measurement,
        Output,
    ];

    /// <summary>
    /// Where a category sorts, with anything not named here after everything that
    /// is — so a plugin's own section appears at the bottom rather than wherever
    /// the load order happened to put it.
    /// </summary>
    public static int Order(string category) =>
        ranks.TryGetValue(category, out var rank) ? rank : All.Count;

    /// <summary>Built once, because the palette asks this per module per keystroke.</summary>
    private static readonly Dictionary<string, int> ranks =
        All.Select((name, at) => (name, at)).ToDictionary(pair => pair.name, pair => pair.at);
}
