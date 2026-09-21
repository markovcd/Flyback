using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

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
    // --- surfaces, darkest first --------------------------------------------

    /// <summary>Between one region and the next, and around a node.</summary>
    public static Color Edge { get; } = Color.FromRgb(0x10, 0x11, 0x14);

    /// <summary>The line around a node and around a socket.</summary>
    public static Color Outline { get; } = Color.FromRgb(0x14, 0x15, 0x18);

    /// <summary>The window behind everything.</summary>
    public static Color Window { get; } = Color.FromRgb(0x16, 0x18, 0x1B);

    /// <summary>The patch canvas.</summary>
    public static Color Canvas { get; } = Color.FromRgb(0x1A, 0x1C, 0x20);

    /// <summary>The inspector, the status bar and the assistant.</summary>
    public static Color Panel { get; } = Color.FromRgb(0x1C, 0x1E, 0x22);

    public static Color Toolbar { get; } = Color.FromRgb(0x22, 0x25, 0x2A);

    /// <summary>
    /// Over the whole window while a dialog is up, and the only thing that says the
    /// rest of the program is not listening. The one color here with an alpha,
    /// because a solid one would be a second window and the point of dimming is
    /// that the patch is still there behind the question. Dark rather than merely
    /// translucent: over a dark window a pale scrim reads as a fault in the display.
    /// </summary>
    public static Color Scrim { get; } = Color.FromArgb(0xAA, 0x0A, 0x0B, 0x0D);

    /// <summary>
    /// The shadow a dialog casts on the scrim. Nearly black and nearly opaque,
    /// because the scrim is already dark: anything lighter would not show on it.
    /// </summary>
    public static Color DialogShadow { get; } = Color.FromArgb(0xE6, 0x00, 0x00, 0x00);

    /// <summary>The canvas grid, and the brighter line every tenth of it.</summary>
    public static Color Grid { get; } = Color.FromRgb(0x24, 0x27, 0x2C);

    /// <summary>Also the groove a level sits in — the same recess, drawn twice.</summary>
    public static Color GridMajor { get; } = Color.FromRgb(0x2C, 0x30, 0x36);

    public static Color Node { get; } = Color.FromRgb(0x2A, 0x2D, 0x34);

    public static Color NodeSelected { get; } = Color.FromRgb(0x32, 0x36, 0x3E);

    /// <summary>The rule between groups of toolbar buttons.</summary>
    public static Color Separator { get; } = Color.FromRgb(0x3A, 0x3E, 0x46);

    // --- text ---------------------------------------------------------------

    /// <summary>A socket's name on a node.</summary>
    public static Color Label { get; } = Color.FromRgb(0xC8, 0xCC, 0xD4);

    /// <summary>The number beside it, quieter than the name it belongs to.</summary>
    public static Color Value { get; } = Color.FromRgb(0x8A, 0x92, 0xA0);

    /// <summary>
    /// The module driving a socket nothing is patched into, where the number
    /// would otherwise be. Quieter again than a number, because it is not a
    /// setting: nobody chose it and nothing about the patch changes it, and a
    /// module name at the weight of a value would read as one more thing to
    /// check down a column of them.
    /// </summary>
    public static Color Normalled { get; } = Color.FromRgb(0x6C, 0x74, 0x82);

    /// <summary>
    /// Text that is there to be read once and then ignored — a drag handle, a
    /// line of provenance.
    /// </summary>
    public static Color Muted { get; } = Color.FromRgb(0x8A, 0x90, 0x9A);

    /// <summary>A level turned all the way down, which still has to be visible.</summary>
    public static Color Inactive { get; } = Color.FromRgb(0x5A, 0x60, 0x6A);

    // --- the one color that means "look here" ------------------------------

    /// <summary>
    /// Selection, the wire being dragged, and whatever the compiler wants to
    /// say. One color for all three on purpose: they are the same request.
    /// </summary>
    public static Color Attention { get; } = Color.FromRgb(0xFF, 0xB0, 0x40);

    // --- module accents -----------------------------------------------------

    public static Color Sink { get; } = Color.FromRgb(0xE0, 0x5A, 0x5A);
    public static Color Source { get; } = Color.FromRgb(0x4A, 0x9E, 0xDE);
    public static Color Oscillator { get; } = Color.FromRgb(0x4F, 0xC3, 0x87);
    public static Color Sequencer { get; } = Color.FromRgb(0xD8, 0xB0, 0x4A);
    public static Color Maths { get; } = Color.FromRgb(0x7E, 0x86, 0x94);
    public static Color Space { get; } = Color.FromRgb(0xB4, 0x84, 0xE0);
    public static Color Pattern { get; } = Color.FromRgb(0xE0, 0xA8, 0x4A);
    public static Color Tint { get; } = Color.FromRgb(0xE0, 0x6A, 0xB8);
    public static Color Feedback { get; } = Color.FromRgb(0x3F, 0xC8, 0xC8);

    /// <summary>
    /// Shapes with edges. A periwinkle between the Source's blue and the
    /// Geometry violet, because a form is a field like the one and is read
    /// through the other.
    /// </summary>
    public static Color Form { get; } = Color.FromRgb(0x7A, 0x8C, 0xE8);

    /// <summary>
    /// The modules that know what a note is. Lime, next along from the Timing
    /// yellow: a pitch and a rhythm are the two halves of the same subject and
    /// should read as neighbors rather than as strangers.
    /// </summary>
    public static Color Note { get; } = Color.FromRgb(0xA8, 0xCE, 0x52);

    /// <summary>
    /// What is done to a waveform. Burnt orange, between the Pattern orange and
    /// the Output red, because shaping is the last thing before the sink.
    /// </summary>
    public static Color Shaping { get; } = Color.FromRgb(0xD8, 0x7A, 0x48);

    /// <summary>
    /// The delay-line effects. A darker teal than Feedback's cyan and related to
    /// it on purpose: both are a patch reading something that already happened.
    /// </summary>
    public static Color Echo { get; } = Color.FromRgb(0x3E, 0xA0, 0xB0);

    /// <summary>
    /// The charts and the meter. Slate, and deliberately the quietest accent
    /// after Maths: a module that looks at the patch should not be louder than
    /// the patch.
    /// </summary>
    public static Color Reading { get; } = Color.FromRgb(0x92, 0xA8, 0xC8);

    /// <summary>
    /// The mixers and the bus. A muted clay, quiet beside the families that make
    /// a signal, because routing carries theirs rather than one of its own.
    /// </summary>
    public static Color Routing { get; } = Color.FromRgb(0xC0, 0x9C, 0x88);

    /// <summary>A category nothing here knows, which a plugin may well introduce.</summary>
    public static Color Unknown { get; } = Color.FromRgb(0x88, 0x88, 0x88);

    /// <summary>
    /// What color a module's category is drawn in — its header on the canvas and its
    /// heading in the palette. Named against <see cref="ModuleCategories"/> rather
    /// than loose strings, so a category renamed there is a compile error here
    /// rather than a section that quietly turns grey.
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

        var accent = Of(palette.Accent);

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
    public static Color Blend(Color ground, Color over, double amount) => Color.FromRgb(
        Part(ground.R, over.R, amount),
        Part(ground.G, over.G, amount),
        Part(ground.B, over.B, amount));

    /// <summary>The same color with the light taken out of it.</summary>
    public static Color Shade(Color color, double by) => Color.FromArgb(
        color.A, Part(0, color.R, by), Part(0, color.G, by), Part(0, color.B, by));

    /// <summary>The same color, this much of the way to invisible.</summary>
    public static Color Faded(Color color, double alpha) =>
        Color.FromArgb(Part(0, 255, alpha), color.R, color.G, color.B);

    private static byte Part(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);

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
    /// mid-grey inverts to itself, and anything near one inverts to something
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

    public static Color ColorPort { get; } = Color.FromRgb(0xE8, 0xC8, 0x60);
    public static Color AnyPort { get; } = Color.FromRgb(0x9E, 0xC8, 0x9E);
    public static Color ScalarPort { get; } = Color.FromRgb(0xB8, 0xBC, 0xC4);

    /// <summary>What flows down a wire, said in color.</summary>
    public static Color PortColor(PortKind kind) => kind switch
    {
        PortKind.Color => ColorPort,
        PortKind.Any => AnyPort,
        _ => ScalarPort,
    };

    // --- the mark -----------------------------------------------------------

    /// <summary>The hot centre of the beam, the only near-white in the palette.</summary>
    public static Color BeamCore { get; } = Color.FromRgb(0xFF, 0xF3, 0xDC);

    /// <summary>The sweep behind the mark, which is three of the module accents.</summary>
    public static (Color Start, Color Middle, Color End) Sweep { get; } =
        (Source, Feedback, Oscillator);
}
