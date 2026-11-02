using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What a module and a shut box share in how they are drawn: the title band, the
/// border, where a socket's name sits and how much room it has.
/// </summary>
internal static class ModuleFace
{
    /// <summary>How large a module's title is drawn.</summary>
    internal const double HeaderSize = 12.5;

    /// <summary>How wide a module's title may be drawn, leaving room for its tag.</summary>
    internal static double HeaderWidth(Rect bounds, bool tagged) => bounds.Width - 16 - (tagged ? UndescribedTags.TagRoom : 0);

    /// <summary>
    /// How strongly a module that is switched off is drawn, and its wires with
    /// it. Faint enough to read as out of the patch at any zoom, and not so faint
    /// that what it is wired to cannot be followed.
    /// </summary>
    internal const double OffOpacity = 0.38;

    /// <summary>What a title on a band is written in: white or black, whichever reads on the edge color every band is mostly made of.</summary>
    internal static Color HeaderInk { get; } = Colors.InkOn(Colors.Edge);

    internal static readonly IImmutableBrush HeaderTextBrush = new ImmutableSolidColorBrush(HeaderInk);

    /// <summary>What a module's name is written in when zoomed out, on a plate of the canvas.</summary>
    internal static readonly IImmutableBrush OverviewTextBrush = new ImmutableSolidColorBrush(Colors.InkOn(Colors.Canvas));

    /// <summary>The line through the name of a module that is switched off.</summary>
    internal static readonly IPen OffStrike = new ImmutablePen(HeaderTextBrush, 1.5);

    internal static readonly IPen SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention), 2);

    /// <summary>
    /// Selected, but not the one the inspector is showing. The same color at
    /// half strength rather than a second color: what these modules are is
    /// selected, and the difference between them and the bright one is which of
    /// them the panel on the right is currently about.
    /// </summary>
    private static readonly IPen SelectionPenSecondary =
        new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention, 0.5), 2);

    /// <summary>The line round a module or a box: plain, or the selection's, at full strength for the one the inspector shows.</summary>
    internal static IPen BorderFor(bool selected, bool focused) =>
        !selected ? NodeSkin.Edge : focused ? SelectionPen : SelectionPenSecondary;

    /// <summary>The band a module's or a box's title is on.</summary>
    internal static Rect HeaderOf(Rect bounds) => new(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);

    /// <summary>The band's shape: square at the bottom so it reads as a title bar.</summary>
    internal static RoundedRect HeaderShape(Rect header) =>
        new(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0);

    /// <summary>Where a title stands: its capitals centered in the band under the rule.</summary>
    internal static Point TitleAt(Rect bounds, FormattedText title) =>
        new(bounds.X + 9, bounds.Y + Baseline - title.Baseline);

    /// <summary>The band's middle under its 3 px rule, plus half a capital's height.</summary>
    private const double Baseline = (3 + NodeGeometry.HeaderHeight) / 2.0 + HeaderSize * 0.36;

    /// <summary>Writes a title, struck through with <paramref name="strike"/> where one is given.</summary>
    internal static void DrawTitle(DrawingContext context, FormattedText title, Point at, IPen? strike)
    {
        context.DrawText(title, at);

        if (strike is not null)
            context.DrawLine(
                strike,
                new Point(at.X, at.Y + title.Height / 2),
                new Point(at.X + title.Width, at.Y + title.Height / 2));
    }

    /// <summary>Whether an input is one its module's formula never reads, so nothing set on it does anything.</summary>
    internal static bool Idle(string? formula, int port) => formula is not null && !FormulaLayout.Reads(formula, port);

    /// <summary>Where a socket's name is drawn: inside the module, against the side its socket is on.</summary>
    internal static Point LabelAt(Rect bounds, Point center, FormattedText label, bool output) =>
        new(output ? bounds.Right - 14 - label.Width : bounds.X + 14, center.Y - label.Height / 2);

    /// <summary>How much of a box's width its sockets and their margins take from a label.</summary>
    private const double SocketLabelRoom = 26;

    /// <summary>How wide a box socket's label may be drawn: beside its value, on half a shared row, or across the box.</summary>
    internal static double BoxLabelRoom(Rect bounds, bool resting, bool compact) =>
        resting ? bounds.Width * 0.55 : compact ? HalfRow(bounds) : bounds.Width - SocketLabelRoom;

    /// <summary>How wide a socket's name may be drawn on a shared row: half the module, less the socket and margins.</summary>
    internal static double HalfRow(Rect bounds) => bounds.Width / 2 - 18;

    /// <summary>
    /// What a module's header says: its title, and the bus where it is a Send or a
    /// Receive. A module named after its own bus is called what it is instead.
    /// </summary>
    internal static string Heading(NodeInstance node, NodeDef def)
    {
        if (NodeCatalog.BusOf(node) is not { } bus) return node.Title(def);

        var title = string.Equals(node.Title(def), bus, StringComparison.OrdinalIgnoreCase) ? def.Name : node.Title(def);

        return $"{title} · {bus}";
    }
}
