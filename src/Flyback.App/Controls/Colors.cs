using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Every color the shell uses, in one place — the theme file.
/// </summary>
/// <remarks>
/// Colors rather than brushes, deliberately. A brush is a resource with a
/// lifetime and the controls that need one already cache their own; a color is
/// a value, and half the uses here are not fills at all — a pen, a gradient
/// stop, the same hue at four fifths opacity. Keeping the palette to values
/// means there is exactly one definition of each and no question about who owns
/// what.
/// <para>
/// Named Colors rather than Theme because every Avalonia StyledElement
/// already has a Theme property, and a static class of that name would be
/// shadowed inside every control that wanted it. This is the part of a theme
/// that XAML would have given for free, without the
/// binding layer that comes with it — see ADR-0016 for why the markup itself is
/// still declined. Before this, thirty-one colors were spread across six files
/// and two of them had already drifted a shade apart.
/// </para>
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
    /// Text that is there to be read once and then ignored — a drag handle, a
    /// line of provenance. Two of these had drifted a shade apart before the
    /// palette was one thing; they are the same grey now.
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

    /// <summary>A category nothing here knows, which a plugin may well introduce.</summary>
    public static Color Unknown { get; } = Color.FromRgb(0x88, 0x88, 0x88);

    /// <summary>
    /// What color a module's category is drawn in — its header on the canvas
    /// and its heading in the palette.
    /// </summary>
    /// <remarks>
    /// Here rather than on <c>NodeGeometry</c>, which is about where the parts
    /// of a node sit and had no business also deciding what color they are.
    /// </remarks>
    public static Color Accent(string category) => category switch
    {
        "Output" => Sink,
        "Source" => Source,
        "Oscillator" => Oscillator,
        "Sequencer" => Sequencer,
        "Maths" => Maths,
        "Space" => Space,
        "Pattern" => Pattern,
        "Color" => Tint,
        "Feedback" => Feedback,
        _ => Unknown,
    };

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
