using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;

namespace Flyback.Ui.Controls;

/// <summary>
/// Every color the shell uses, in one place — the theme file.
/// </summary>
/// <remarks>
/// Colors rather than brushes: a brush is a resource with a lifetime and the
/// controls that need one cache their own, where half the uses here are not fills
/// at all — a pen, a gradient stop, the same hue at four fifths opacity. Named
/// Colors rather than Theme because every Avalonia StyledElement already has a
/// Theme property, which would shadow a static class of that name.
/// </remarks>
internal static class Colors
{
    /// <summary>The scheme the grounds, the text and the accent are read from.</summary>
    /// <remarks>
    /// Set once, before the first window: brushes made from these colors are cached
    /// for the life of the program, so a scheme chosen later takes effect at the next start.
    /// </remarks>
    public static Scheme Current { get; private set; } = Scheme.Default;

    /// <summary>Draws everything built from here on in <paramref name="scheme"/>.</summary>
    public static void Use(Scheme scheme) => Current = scheme;

    /// <summary>Whether the scheme is dark text on pale grounds.</summary>
    public static bool LightGrounds => Current.Light;

    // --- relief -------------------------------------------------------------

    /// <summary>
    /// A wash, glint or edge that raises something off its ground, at <paramref name="alpha"/>:
    /// white on a dark scheme, black on a light one.
    /// </summary>
    public static Color Lift(double alpha) => Faded(Current.Light ? Black : White, alpha);

    /// <summary>
    /// A shadow or seam that sinks something into its ground, at <paramref name="alpha"/>:
    /// black, and softer on a light scheme, where a full-strength one reads as a smudge.
    /// </summary>
    public static Color Press(double alpha) => Faded(Black, Current.Light ? alpha * LightPress : alpha);

    /// <summary>How much of a shadow's strength a light scheme keeps.</summary>
    private const double LightPress = 0.5;

    // --- surfaces, darkest first --------------------------------------------

    /// <summary>Between one region and the next, and around a node.</summary>
    public static Color Edge => Current.Edge;

    /// <summary>The line around a node and around a socket.</summary>
    public static Color Outline => Current.Outline;

    /// <summary>The window behind everything.</summary>
    public static Color Window => Current.Window;

    /// <summary>The patch canvas.</summary>
    public static Color Canvas => Current.Canvas;

    /// <summary>The inspector, the status bar and the assistant.</summary>
    public static Color Panel => Current.Panel;

    public static Color Toolbar => Current.Toolbar;

    /// <summary>
    /// Over the whole window while a dialog is up, and the only thing that says the
    /// rest of the program is not listening. The one color here with an alpha,
    /// because a solid one would be a second window and the point of dimming is
    /// that the patch is still there behind the question. Dark rather than merely
    /// translucent: over a dark window a pale scrim reads as a fault in the display.
    /// </summary>
    public static Color Scrim => Color.FromArgb(Current.Light ? (byte)0x66 : (byte)0xAA, 0x0A, 0x0B, 0x0D);

    /// <summary>
    /// The shadow a dialog casts on the scrim. Nearly black and nearly opaque,
    /// because the scrim is already dark: anything lighter would not show on it.
    /// </summary>
    public static Color DialogShadow => Press(0.9);

    /// <summary>Behind a picture, where it does not reach: black in every scheme, as a screen is.</summary>
    public static IImmutableSolidColorBrush Letterbox { get; } = new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black);

    /// <summary>The canvas grid, and the brighter line every tenth of it.</summary>
    public static Color Grid => Current.Grid;

    /// <summary>Also the groove a level sits in — the same recess, drawn twice.</summary>
    public static Color GridMajor => Current.GridMajor;

    public static Color Node => Current.Node;

    public static Color NodeSelected => Current.NodeSelected;

    /// <summary>The rule between groups of toolbar buttons.</summary>
    public static Color Separator => Current.Separator;

    // --- text ---------------------------------------------------------------

    /// <summary>A socket's name on a node.</summary>
    public static Color Label => Current.Label;

    /// <summary>The number beside it, quieter than the name it belongs to.</summary>
    public static Color Value => Current.Value;

    /// <summary>
    /// The module driving a socket nothing is patched into, where the number
    /// would otherwise be. Quieter again than a number, because it is not a
    /// setting: nobody chose it and nothing about the patch changes it, and a
    /// module name at the weight of a value would read as one more thing to
    /// check down a column of them.
    /// </summary>
    public static Color Normalled => Current.Normalled;

    /// <summary>
    /// Text that is there to be read once and then ignored — a drag handle, a
    /// line of provenance.
    /// </summary>
    public static Color Muted => Current.Muted;

    /// <summary>A level turned all the way down, which still has to be visible.</summary>
    public static Color Inactive => Current.Inactive;

    /// <summary>Text at its brightest: a value being turned or typed, a heading, a selection's outline.</summary>
    public static Color Bright => Current.Bright;

    /// <summary>Something wrong: a fault, a refusal, a problem to read. The same in every scheme.</summary>
    /// <remarks>A red apart from the Output family's, so a fault never reads as a module.</remarks>
    public static Color Fault { get; } = Color.FromRgb(0xF0, 0x52, 0x4F);

    // --- the one color that means "look here" ------------------------------

    /// <summary>
    /// Selection, the wire being dragged, and whatever the compiler wants to
    /// say. One color for all three on purpose: they are the same request.
    /// </summary>
    /// <remarks>
    /// In Phosphor, P31 green, as the Beam module draws its trace at
    /// its default hue, taken where the trace begins to whiten (ADR-0219).
    /// </remarks>
    public static Color Attention => Current.Attention;

    // --- module accents -----------------------------------------------------
    //
    // Each is drawn through Deepened, so on a light scheme it keeps its hue and stands out
    // from the canvas.

    public static Color Sink => Deepened(Color.FromRgb(0xE0, 0x5A, 0x5A));
    public static Color Source => Deepened(Color.FromRgb(0x4A, 0x9E, 0xDE));
    /// <summary>A dark sea green, kept off the P31 green of <see cref="Attention"/> so a selected oscillator still shows its ring.</summary>
    public static Color Oscillator => Deepened(Color.FromRgb(0x3A, 0xA1, 0x87));
    public static Color Sequencer => Deepened(Color.FromRgb(0xD8, 0xB0, 0x4A));
    public static Color Maths => Deepened(Color.FromRgb(0x7E, 0x86, 0x94));
    public static Color Space => Deepened(Color.FromRgb(0xB4, 0x84, 0xE0));
    public static Color Pattern => Deepened(Color.FromRgb(0xE0, 0xA8, 0x4A));
    public static Color Tint => Deepened(Color.FromRgb(0xE0, 0x6A, 0xB8));
    public static Color Feedback => Deepened(Color.FromRgb(0x3F, 0xC8, 0xC8));

    /// <summary>
    /// Shapes with edges. A periwinkle between the Source's blue and the
    /// Geometry violet, because a form is a field like the one and is read
    /// through the other.
    /// </summary>
    public static Color Form => Deepened(Color.FromRgb(0x7A, 0x8C, 0xE8));

    /// <summary>
    /// The modules that know what a note is. Lime, next along from the Timing
    /// yellow: a pitch and a rhythm are the two halves of the same subject and
    /// should read as neighbors rather than as strangers.
    /// </summary>
    public static Color Note => Deepened(Color.FromRgb(0xA8, 0xCE, 0x52));

    /// <summary>
    /// What is done to a waveform. Burnt orange, between the Pattern orange and
    /// the Output red, because shaping is the last thing before the sink.
    /// </summary>
    public static Color Shaping => Deepened(Color.FromRgb(0xD8, 0x7A, 0x48));

    /// <summary>
    /// The delay-line effects. A darker teal than Feedback's cyan and related to
    /// it on purpose: both are a patch reading something that already happened.
    /// </summary>
    public static Color Echo => Deepened(Color.FromRgb(0x3E, 0xA0, 0xB0));

    /// <summary>
    /// The charts and the meter. Slate, and deliberately the quietest accent
    /// after Maths: a module that looks at the patch should not be louder than
    /// the patch.
    /// </summary>
    public static Color Reading => Deepened(Color.FromRgb(0x92, 0xA8, 0xC8));

    /// <summary>
    /// The mixers and the bus. A muted clay, quiet beside the families that make
    /// a signal, because routing carries theirs rather than one of its own.
    /// </summary>
    public static Color Routing => Deepened(Color.FromRgb(0xC0, 0x9C, 0x88));

    /// <summary>A category nothing here knows, which a plugin may well introduce.</summary>
    public static Color Unknown => Deepened(Color.FromRgb(0x88, 0x88, 0x88));

    /// <summary>
    /// What color a module's category is drawn in — its header on the canvas and its
    /// heading in the palette. Named against <see cref="ModuleCategories"/> rather
    /// than loose strings, so a category renamed there is a compile error here
    /// rather than a section that quietly turns gray.
    /// </summary>
    public static Color Accent(string category) => category switch
    {
        ModuleCategories.Output => Sink,
        ModuleCategories.Sources => Source,
        ModuleCategories.Oscillators => Oscillator,
        ModuleCategories.Timing => Sequencer,
        ModuleCategories.Maths => Maths,
        ModuleCategories.Geometry => Space,
        ModuleCategories.Patterns => Pattern,
        ModuleCategories.Color => Tint,
        ModuleCategories.Feedback => Feedback,
        ModuleCategories.Forms => Form,
        ModuleCategories.Pitch => Note,
        ModuleCategories.Shaping => Shaping,
        ModuleCategories.TimeEffects => Echo,
        ModuleCategories.Measurement => Reading,
        ModuleCategories.Routing => Routing,
        _ => Unknown,
    };

    /// <summary>A color a plugin handed over, in the toolkit's own terms.</summary>
    public static Color Of(Swatch swatch) => Color.FromRgb(swatch.Red, swatch.Green, swatch.Blue);

    /// <summary>
    /// The two colors a module's background is worked out from: the palette its
    /// author gave, or its category's accent standing as both.
    /// </summary>
    public static (Color Accent, Color Floor) Palette(NodeDef def)
    {
        if (ModuleSkins.Of(def) is not ModuleSkin.Palette palette)
        {
            var category = Accent(def.Category);

            return (category, category);
        }

        var accent = Deepened(Of(palette.Accent));

        return (accent, palette.Floor is { } floor ? Of(floor) : accent);
    }

    /// <summary>
    /// What color a preset's kind is drawn in where it heads that kind's run of
    /// the preset list.
    /// </summary>
    /// <remarks>
    /// The three that are patches run cool to warm as they grow, from one idea
    /// to a whole piece. The blank canvas takes the Output's own red, that being
    /// the whole of what is in it — and the one of the four that is a category
    /// color meaning its category, since the module it heads a list of is the
    /// Output.
    /// </remarks>
    public static Color PresetAccent(PresetKind kind) => kind switch
    {
        PresetKind.Idea => Source,
        PresetKind.Interplay => Space,
        PresetKind.Showcase => Pattern,
        _ => Sink,
    };

    // --- mixing -------------------------------------------------------------

    /// <summary>
    /// <paramref name="amount"/> of <paramref name="over"/> laid on
    /// <paramref name="ground"/>, opaque.
    /// </summary>
    /// <remarks>
    /// Mixed here rather than drawn as a translucent second rectangle: what these
    /// make are gradient stops and cached brushes, and a stop has one color.
    /// </remarks>
    public static Color Blend(Color ground, Color over, double amount) => ColorMix.Blend(ground, over, amount);

    /// <summary>The same color with the light taken out of it.</summary>
    public static Color Shade(Color color, double by) => Color.FromArgb(
        color.A, Part(0, color.R, by), Part(0, color.G, by), Part(0, color.B, by));

    /// <summary>The same color, this much of the way to invisible.</summary>
    public static Color Faded(Color color, double alpha) =>
        Color.FromArgb(Part(0, 255, alpha), color.R, color.G, color.B);

    private static byte Part(byte from, byte to, double amount) => ColorMix.Part(from, to, amount);

    // --- on a light ground --------------------------------------------------

    /// <summary>
    /// A family, socket or wire color as the scheme draws it: itself on a dark scheme, and
    /// on a light one darkened, hue kept, until it stands <see cref="ColorOnGround"/> to
    /// one against the canvas.
    /// </summary>
    public static Color Deepened(Color color) =>
        Current.Light ? Deepenings.GetOrAdd((color, Current.Canvas), key => Deepened(key.Item1, key.Item2)) : color;

    /// <summary><paramref name="color"/> darkened, hue kept, until it stands <see cref="ColorOnGround"/> to one against <paramref name="ground"/>.</summary>
    public static Color Deepened(Color color, Color ground)
    {
        var made = color;

        for (var light = 1.0; light > 0 && ContrastRatio(made, ground) < ColorOnGround; light -= 0.01)
            made = Shade(color, light);

        return made;
    }

    /// <summary>The contrast a color drawn as a line or a fill keeps against a light canvas: WCAG's for graphics.</summary>
    public const double ColorOnGround = 3.0;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Color, Color), Color> Deepenings = new();

    /// <summary>WCAG's contrast ratio between two opaque colors, from one to twenty-one.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var (lighter, darker) = (Luminance(a), Luminance(b)) is var (x, y) && x > y ? (x, y) : (y, x);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>WCAG's relative luminance: the light in a color as the eye weighs it, from nought to one.</summary>
    public static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>White or black, whichever reads on <paramref name="band"/>: the ink for a title on a band.</summary>
    public static Color InkOn(Color band) => ContrastRatio(White, band) >= ContrastRatio(Black, band) ? White : Black;

    // --- writing on a color the palette did not choose -----------------------

    /// <summary>How much of the light there is in a color, from nought to one.</summary>
    public static double Luma(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;

    /// <summary>Whether text over this is better off dark than light.</summary>
    public static bool Light(Color color) => Luma(color) >= 0.5;

    /// <summary>
    /// What to write on <paramref name="background"/> so it can be read: the
    /// inverse of the color, driven toward the pole the background is not until
    /// the two are <see cref="Separation"/> apart in light.
    /// </summary>
    /// <remarks>
    /// The inverse alone is the obvious rule and it fails in the middle: a
    /// mid-gray inverts to itself, and anything near one inverts to something
    /// barely off it. So the inverse is the hue and the drive is the contrast,
    /// and the drive is solved for rather than picked — <c>Luma</c> is linear in
    /// each channel, so the amount that buys exactly the separation wanted is an
    /// equation, and text over a gradient stays continuous instead of stepping
    /// where a threshold would have been.
    /// <para>
    /// <paramref name="lift"/> is decided once for a whole run of text rather
    /// than per stop, because the two answers are equally readable and picking
    /// them independently is what splits a word down the middle.
    /// </para>
    /// </remarks>
    public static Color Contrast(Color background, bool lift)
    {
        var inverse = Color.FromRgb(
            (byte)(255 - background.R),
            (byte)(255 - background.G),
            (byte)(255 - background.B));

        var ground = Luma(background);
        var ink = Luma(inverse);

        var drive = lift
            ? (ground + Separation - ink) / Math.Max(1 - ink, Floor)
            : 1 - (ground - Separation) / Math.Max(ink, Floor);

        return Blend(inverse, lift ? White : Black, Math.Clamp(drive, 0, 1));
    }

    /// <summary>
    /// How far apart in light <see cref="Contrast"/> holds text and its
    /// background. Half the range: the most any pair can be held to, since a
    /// background exactly in the middle is half from either pole.
    /// </summary>
    private const double Separation = 0.5;

    /// <summary>Keeps the drive off a division by nought at either pole.</summary>
    private const double Floor = 1.0 / 255;

    private static Color White { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);

    private static Color Black { get; } = Color.FromRgb(0x00, 0x00, 0x00);

    // --- sockets ------------------------------------------------------------

    public static Color ColorPort => Deepened(Color.FromRgb(0xE8, 0xC8, 0x60));
    public static Color AnyPort => Deepened(Color.FromRgb(0x9E, 0xC8, 0x9E));
    public static Color ScalarPort => Deepened(Color.FromRgb(0xB8, 0xBC, 0xC4));

    /// <summary>What flows down a wire, said in color.</summary>
    public static Color PortColor(PortKind kind) => kind switch
    {
        PortKind.Color => ColorPort,
        PortKind.Any => AnyPort,
        _ => ScalarPort,
    };

    // --- the mark -----------------------------------------------------------

    /// <summary>The hot center of the beam, the only near-white in the palette.</summary>
    public static Color BeamCore { get; } = Color.FromRgb(0xFF, 0xF3, 0xDC);

    /// <summary>The sweep behind the mark, which is three of the module accents.</summary>
    public static (Color Start, Color Middle, Color End) Sweep { get; } =
        (Color.FromRgb(0x4A, 0x9E, 0xDE), Color.FromRgb(0x3F, 0xC8, 0xC8), Color.FromRgb(0x3A, 0xA1, 0x87));
}
