using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What a module is painted with, worked out from one accent: the wash down its
/// body, the band across its header and the rule along it, and — for a
/// module that asked for it — the ink its text is written in.
/// </summary>
/// <remarks>
/// Built once per accent and kept, because painting asks for these per module
/// per frame and a gradient brush is not free to make. The canvas is one control
/// on one thread (ADR-0017), so the caches need no guard.
/// <para>
/// The body is flat and the tint slight, so a patch reads as panels of one
/// instrument; the rule along each header is what tells the kinds apart.
/// </para>
/// </remarks>
internal static class NodeSkin
{
    /// <summary>How much accent is in the body.</summary>
    private const double BodyTint = 0.09;

    /// <summary>How much accent is in the header band; the rule along its top carries the rest.</summary>
    private const double HeaderStrength = 0.14;

    private static readonly Dictionary<(Color Accent, Color Floor, bool Selected), IBrush> Bodies = [];
    private static readonly Dictionary<(Color Accent, Color Floor, bool Selected), IBrush> Headers = [];
    private static readonly Dictionary<(Color From, Color To, bool Lift, int Fade), IBrush> Inks = [];

    /// <summary>A module's body, tinted by what the module does.</summary>
    public static IBrush Body(Color accent, Color floor, bool selected)
    {
        if (Bodies.TryGetValue((accent, floor, selected), out var kept)) return kept;

        var made = Down(BodyTop(accent, selected), BodyFloor(floor, selected));

        Bodies[(accent, floor, selected)] = made;

        return made;
    }

    /// <summary>The header band behind a module's title.</summary>
    public static IBrush Header(Color accent, Color floor, bool selected)
    {
        if (Headers.TryGetValue((accent, floor, selected), out var kept)) return kept;

        var made = Down(HeaderTop(accent, selected), HeaderFloor(floor));

        Headers[(accent, floor, selected)] = made;

        return made;
    }

    public static Color BodyTop(Color accent, bool selected) =>
        Colors.Blend(Ground(selected), accent, BodyTint);

    public static Color BodyFloor(Color accent, bool selected) =>
        Colors.Blend(Ground(selected), accent, BodyTint);

    /// <summary>The band behind a module's title: dark, with only enough of the accent to tie it to its rule.</summary>
    public static Color HeaderTop(Color accent, bool selected) =>
        Colors.Blend(selected ? Colors.Outline : Colors.Edge, accent, HeaderStrength);

    public static Color HeaderFloor(Color accent) => Colors.Blend(Colors.Edge, accent, HeaderStrength);

    private static Color Ground(bool selected) => selected ? Colors.NodeSelected : Colors.Node;

    /// <summary>
    /// The plain node color, with no accent mixed in — what shows through a
    /// transparent picture, since ADR-0118 counts transparency as the node gray.
    /// </summary>
    public static IBrush GroundFill(bool selected) => selected ? SelectedGround : PlainGround;

    private static readonly IBrush PlainGround = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush SelectedGround = new ImmutableSolidColorBrush(Colors.NodeSelected);

    /// <summary>
    /// Text colored from the background it covers: the readable opposite of what
    /// is behind the top of the text and behind its bottom, as a gradient down
    /// the text, so what a pixel is drawn in follows the pixel behind it.
    /// </summary>
    /// <param name="lift">
    /// Which way the ink is driven, decided by the caller for a whole surface —
    /// see <see cref="Colors.Contrast"/>.
    /// </param>
    /// <param name="fade">
    /// How far back into the background the ink is pulled — nought for a name,
    /// more for the quieter columns, which is how they keep their place without
    /// being given a color of their own.
    /// </param>
    public static IBrush Ink(Color from, Color to, bool lift, double fade)
    {
        var back = Steps(fade);

        if (Inks.TryGetValue((from, to, lift, back), out var kept)) return kept;

        var made = Down(Written(from, back, lift), Written(to, back, lift));

        Inks[(from, to, lift, back)] = made;

        return made;
    }

    private static Color Written(Color under, int fade, bool lift) =>
        Colors.Blend(Colors.Contrast(under, lift), under, Fraction(fade));

    /// <summary>
    /// A fade counted in sixty-fourths, so an ink is keyed on something
    /// countable and a fade moved by a sixty-fourth is not a fade moved.
    /// </summary>
    private static int Steps(double fraction) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * Fades);

    private static double Fraction(int steps) => steps / (double)Fades;

    private const int Fades = 64;

    /// <summary>
    /// The texture cut across a module's body, tiled in graph units so it holds
    /// its pitch as the canvas is zoomed.
    /// </summary>
    /// <remarks>
    /// Drawn in the same color the module's text would be drawn in, at a fraction
    /// of its strength: derived from the background by the one rule, so a cut
    /// shows on a pale accent and on a dark one without either being named.
    /// </remarks>
    public static IBrush Cut(GrainCut cut, Color over)
    {
        var cutBrushes = CutBrushes ??= [];

        if (cutBrushes.TryGetValue((cut, over), out var kept)) return kept;

        var ink = Colors.Contrast(over, !Colors.Light(over));
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(ink, CutOpacity), CutWidth);

        var made = new DrawingBrush(
            cut == GrainCut.Beaded
                ? new GeometryDrawing
                {
                    Brush = new ImmutableSolidColorBrush(ink, CutOpacity),
                    Geometry = new EllipseGeometry(new Rect(Tile / 2 - 1, Tile / 2 - 1, 2, 2)),
                }
                : new GeometryDrawing { Pen = pen, Geometry = Geometry.Parse(Cuts[cut]) })
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            SourceRect = new RelativeRect(0, 0, Tile, Tile, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, Tile, Tile, RelativeUnit.Absolute),
        };

        cutBrushes[(cut, over)] = made;

        return made;
    }

    /// <summary>Kept per thread, because a drawing brush has no immutable form and belongs to the thread that made it.</summary>
    [ThreadStatic]
    private static Dictionary<(GrainCut Cut, Color Over), IBrush>? CutBrushes;

    /// <summary>
    /// The paths the tiling cuts are made of, on the tile's own square. The
    /// diagonal is drawn three times so it meets itself across the seam.
    /// </summary>
    private static readonly Dictionary<GrainCut, string> Cuts = new()
    {
        [GrainCut.Hatched] = "M0,8 L8,0 M-2,2 L2,-2 M6,10 L10,6",
        [GrainCut.Milled] = "M2,0 L2,8 M6,0 L6,8",
    };

    /// <summary>The square a cut repeats on, in graph units.</summary>
    private const double Tile = 8;

    private const double CutWidth = 1;

    /// <summary>
    /// How strongly a cut shows: faint, because it covers the whole body and the
    /// labels are read over it.
    /// </summary>
    private const double CutOpacity = 0.10;

    // --- the line round a block, and a box, which belongs to no category -----

    /// <summary>The line round a block and round a socket on one.</summary>
    public static IPen Edge { get; } =
        new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.5);

    /// <summary>
    /// A box's body, which is the node gray lifted a little rather than tinted: the
    /// same statement its header makes, that a box belongs to no category.
    /// </summary>
    public static IBrush Box(bool selected) => selected ? SelectedBox : PlainBox;

    /// <summary>The two colors a box's body is washed between.</summary>
    public static (Color Top, Color Floor) BoxWash { get; } =
        (Colors.Blend(Colors.Node, Colors.Separator, 0.3), Colors.Node);

    private static readonly IBrush PlainBox = Down(BoxWash.Top, BoxWash.Floor);

    private static readonly IBrush SelectedBox =
        Down(Colors.Blend(Colors.NodeSelected, Colors.Separator, 0.3), Colors.NodeSelected);

    /// <summary>
    /// A box's header, in the one color on the canvas that belongs to no category. A
    /// module's header is tinted by what it does; a box does nothing, so it is drawn
    /// in the outline color and reads as canvas furniture rather than as a module
    /// whose kind you have forgotten.
    /// </summary>
    public static IBrush BoxHeader { get; } =
        Down(Colors.Blend(Colors.Outline, Colors.Separator, 0.55), Colors.Outline);

    /// <summary>
    /// A box's header, selected or not — mirrors <see cref="Box"/>: the same band,
    /// with the selection color standing in for <see cref="Colors.Separator"/> the
    /// way a selected box's body stands its ground in <see cref="Colors.NodeSelected"/>.
    /// </summary>
    public static IBrush BoxHeaderOf(bool selected) => selected ? SelectedBoxHeader : BoxHeader;

    private static readonly IBrush SelectedBoxHeader =
        Down(Colors.Blend(Colors.Outline, Colors.Attention, 0.55), Colors.Outline);

    // --- the face of a header band ------------------------------------------

    /// <summary>
    /// The rule along the top of a header in its category's accent, and the seam
    /// where the band meets the body. The rule is what tells one kind of module
    /// from another at a glance.
    /// </summary>
    public static void Relief(DrawingContext context, Rect header, Color rule)
    {
        context.FillRectangle(Rules.TryGetValue(rule, out var kept) ? kept : Rules[rule] = new ImmutableSolidColorBrush(rule),
            new Rect(header.X, header.Y, header.Width, RuleHeight));

        context.DrawLine(
            Seam,
            new Point(header.X, header.Bottom - 0.5),
            new Point(header.Right, header.Bottom - 0.5));
    }

    /// <summary>How thick the accent rule along a header's top is.</summary>
    private const double RuleHeight = 3;

    private static readonly Dictionary<Color, IBrush> Rules = [];

    private static readonly IPen Seam = new ImmutablePen(
        new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black, 0.3));

    // --- sockets ---------------------------------------------------------

    public static void DrawPort(DrawingContext context, Point center, PortKind kind) =>
        context.DrawEllipse(
            PortFill(kind),
            PortOutline,
            center,
            NodeGeometry.PortRadius,
            NodeGeometry.PortRadius);

    /// <summary>
    /// A socket filled clockwise from the top in <paramref name="fill"/> as far as
    /// <paramref name="share"/> of the way round, and in its kind's color beyond.
    /// </summary>
    public static void DrawPort(DrawingContext context, Point center, PortKind kind, double share, IBrush fill)
    {
        var radius = NodeGeometry.PortRadius;

        context.DrawEllipse(PortFill(kind), null, center, radius, radius);

        if (share >= 0.999)
        {
            context.DrawEllipse(fill, null, center, radius, radius);
        }
        else if (share > 0.001)
        {
            var angle = share * 2 * Math.PI;
            var geometry = new StreamGeometry();

            using (var sink = geometry.Open())
            {
                sink.BeginFigure(center);
                sink.LineTo(new Point(center.X, center.Y - radius));
                sink.ArcTo(
                    new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                    new Size(radius, radius),
                    0,
                    share > 0.5,
                    SweepDirection.Clockwise);
                sink.EndFigure(true);
            }

            context.DrawGeometry(fill, null, geometry);
        }

        context.DrawEllipse(null, PortOutline, center, radius, radius);
    }

    /// <summary>Cached per kind, since a socket is drawn several times a frame.</summary>
    private static IBrush PortFill(PortKind kind)
    {
        if (PortFills.TryGetValue(kind, out var kept)) return kept;

        var made = new ImmutableSolidColorBrush(Colors.PortColor(kind));

        PortFills[kind] = made;

        return made;
    }

    private static readonly Dictionary<PortKind, IBrush> PortFills = [];

    private static readonly IPen PortOutline = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.2);

    /// <summary>
    /// How a block's background gives way on the panel: full strength in the top
    /// right corner and gone by about the middle of it, as an opacity mask so a
    /// wash, a grain and a picture all disappear the same way.
    /// </summary>
    /// <remarks>
    /// The panel is not a canvas: what ends the face is the reading running on
    /// underneath it rather than a line round it.
    /// <para>
    /// Measured in the panel's own pixels rather than in fractions of it, because a
    /// gradient given relative ends is skewed by the shape of what it fills — the
    /// angle would tilt every time the splitter moved. Fixed angle, and only how far
    /// it runs follows the panel.
    /// </para>
    /// </remarks>
    public static IBrush Fade(Rect bounds)
    {
        var key = ((int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));

        if (FadeBrushes.TryGetValue(key, out var kept)) return kept;

        var reach = new Vector(-1, Steep).Normalize()
            * new Vector(bounds.Width / 2, bounds.Height / 2).Length;

        var from = new Point(bounds.Right, bounds.Y);

        var made = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0, Avalonia.Media.Colors.White),
                new ImmutableGradientStop(1, Color.FromArgb(0, 255, 255, 255)),
            ],
            startPoint: new RelativePoint(from, RelativeUnit.Absolute),
            endPoint: new RelativePoint(from + reach, RelativeUnit.Absolute));

        FadeBrushes[key] = made;

        return made;
    }

    /// <summary>
    /// How far the fade is driven down against how far it is driven left. Past the
    /// corner-to-corner diagonal on purpose: the fill belongs to the head of the
    /// panel, and a shallower one reads as a stripe across it.
    /// </summary>
    private const double Steep = 1.6;

    private static readonly Dictionary<(int Width, int Height), IBrush> FadeBrushes = [];

    /// <summary>A gradient from the top of whatever it fills to the bottom.</summary>
    public static IBrush Down(Color top, Color bottom) => new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
}
