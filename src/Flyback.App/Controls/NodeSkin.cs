using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What a module is painted with, worked out from one accent: the wash down its
/// body, the band across its header, the color its mark is drawn in, and — for a
/// module that asked for it — the ink its text is written in.
/// </summary>
/// <remarks>
/// Built once per accent and kept, because painting asks for these per module
/// per frame and a gradient brush is not free to make. The canvas is one control
/// on one thread (ADR-0017), so the caches need no guard.
/// <para>
/// The tint is deliberately slight. A body in the accent at full strength is a
/// patch of fourteen colored rectangles with the labels lost in them; what is
/// wanted is the difference being legible at a glance and at a zoom where no
/// label is, which a few percent of hue over the node grey gives.
/// </para>
/// </remarks>
internal static class NodeSkin
{
    /// <summary>How much accent is in the body, under the header and at the floor.</summary>
    private const double TopTint = 0.24, FloorTint = 0.06;

    /// <summary>How much of the header band is accent, the rest being the body under it.</summary>
    private const double HeaderStrength = 0.95;

    /// <summary>How much light the band has left by the time it meets the body.</summary>
    private const double HeaderFall = 0.72;

    private static readonly Dictionary<(Color Accent, Color Floor, bool Selected), IBrush> bodies = [];
    private static readonly Dictionary<(Color Accent, Color Floor, bool Selected), IBrush> headers = [];
    private static readonly Dictionary<Color, IPen> marks = [];
    private static readonly Dictionary<(Color From, Color To, bool Lift, int Fade), IBrush> inks = [];

    /// <summary>The wash down a module's body, tinted by what the module does.</summary>
    public static IBrush Body(Color accent, Color floor, bool selected)
    {
        if (bodies.TryGetValue((accent, floor, selected), out var kept)) return kept;

        var made = Down(BodyTop(accent, selected), BodyFloor(floor, selected));

        bodies[(accent, floor, selected)] = made;

        return made;
    }

    /// <summary>The header band. Lit at the top and falling away, so it has a face.</summary>
    public static IBrush Header(Color accent, Color floor, bool selected)
    {
        if (headers.TryGetValue((accent, floor, selected), out var kept)) return kept;

        var made = Down(HeaderTop(accent, selected), HeaderFloor(floor));

        headers[(accent, floor, selected)] = made;

        return made;
    }

    public static Color BodyTop(Color accent, bool selected) =>
        Colors.Blend(Ground(selected), accent, TopTint);

    public static Color BodyFloor(Color accent, bool selected) =>
        Colors.Blend(Ground(selected), accent, FloorTint);

    /// <summary>
    /// The lit edge of the band — the accent, with the body showing through the
    /// last few percent of it.
    /// </summary>
    public static Color HeaderTop(Color accent, bool selected) =>
        Colors.Blend(BodyTop(accent, selected), accent, HeaderStrength);

    public static Color HeaderFloor(Color accent) => Colors.Shade(accent, HeaderFall);

    private static Color Ground(bool selected) => selected ? Colors.NodeSelected : Colors.Node;

    /// <summary>
    /// The plain node color, with no accent mixed in — what shows through a
    /// transparent picture, since ADR-0118 counts transparency as the node grey.
    /// </summary>
    public static IBrush GroundFill(bool selected) => selected ? groundSelected : ground;

    private static readonly IBrush ground = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush groundSelected = new ImmutableSolidColorBrush(Colors.NodeSelected);

    /// <summary>
    /// What the mark across the body is stroked with — see <see cref="ModuleGlyphs"/>.
    /// Thickness in the glyph's own units, so it grows with whatever the mark is
    /// scaled to.
    /// </summary>
    public static IPen Mark(Color accent)
    {
        if (marks.TryGetValue(accent, out var kept)) return kept;

        var made = new ImmutablePen(
            new ImmutableSolidColorBrush(accent, MarkOpacity),
            ModuleGlyphs.Thickness,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        marks[accent] = made;

        return made;
    }

    /// <summary>
    /// How strongly the mark shows. Far enough under the labels drawn over it
    /// that reading one is never a matter of looking past the other.
    /// </summary>
    private const double MarkOpacity = 0.17;

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

        if (inks.TryGetValue((from, to, lift, back), out var kept)) return kept;

        var made = Down(Written(from, back, lift), Written(to, back, lift));

        inks[(from, to, lift, back)] = made;

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
        if (cuts.TryGetValue((cut, over), out var kept)) return kept;

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

        cuts[(cut, over)] = made;

        return made;
    }

    private static readonly Dictionary<(GrainCut Cut, Color Over), IBrush> cuts = [];

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
    /// How strongly a cut shows. Under the mark, because a texture covers the
    /// whole body where a mark is one shape in the corner of it: at the mark's
    /// own weight the labels would be read over hatching everywhere.
    /// </summary>
    private const double CutOpacity = 0.10;

    // --- the line round a block, and a box, which belongs to no category -----

    /// <summary>The line round a block and round a socket on one.</summary>
    public static IPen Edge { get; } =
        new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.5);

    /// <summary>
    /// A box's body, which is the node grey lifted a little rather than tinted: the
    /// same statement its header makes, that a box belongs to no category.
    /// </summary>
    public static IBrush Box(bool selected) => selected ? boxSelected : box;

    /// <summary>The two colors a box's body is washed between.</summary>
    public static (Color Top, Color Floor) BoxWash { get; } =
        (Colors.Blend(Colors.Node, Colors.Separator, 0.3), Colors.Node);

    private static readonly IBrush box = Down(BoxWash.Top, BoxWash.Floor);

    private static readonly IBrush boxSelected =
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
    public static IBrush BoxHeaderOf(bool selected) => selected ? boxHeaderSelected : BoxHeader;

    private static readonly IBrush boxHeaderSelected =
        Down(Colors.Blend(Colors.Outline, Colors.Attention, 0.55), Colors.Outline);

    /// <summary>
    /// The mark across a box, which is the same picture the toolbar's group button
    /// carries — modules inside a frame.
    /// </summary>
    public static IPen BoxMark { get; } = new ImmutablePen(
        new ImmutableSolidColorBrush(Colors.Separator, 0.5),
        ModuleGlyphs.Thickness,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);

    // --- the face of a header band ------------------------------------------

    /// <summary>
    /// The two lines that give a header band a face: light along its top edge, and a
    /// seam where it meets the body.
    /// </summary>
    /// <remarks>
    /// The light is held off the corners, where a straight line across a rounded one
    /// reads as an overhang rather than as an edge catching the light. Both are white
    /// and black rather than palette colors, because what they are is a light and a
    /// shadow on whatever color the band happens to be.
    /// </remarks>
    public static void Relief(DrawingContext context, Rect header)
    {
        var inset = NodeGeometry.CornerRadius;

        context.DrawLine(
            gloss,
            new Point(header.X + inset, header.Y + 0.75),
            new Point(header.Right - inset, header.Y + 0.75));

        context.DrawLine(
            seam,
            new Point(header.X, header.Bottom - 0.5),
            new Point(header.Right, header.Bottom - 0.5));
    }

    private static readonly IPen gloss = new ImmutablePen(
        new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.16), 1.2);

    private static readonly IPen seam = new ImmutablePen(
        new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black, 0.3));

    // --- sockets and marks ---------------------------------------------------

    public static void DrawPort(DrawingContext context, Point centre, PortKind kind) =>
        context.DrawEllipse(
            PortFill(kind),
            PortOutline,
            centre,
            NodeGeometry.PortRadius,
            NodeGeometry.PortRadius);

    /// <summary>
    /// A socket filled clockwise from the top in <paramref name="fill"/> as far as
    /// <paramref name="share"/> of the way round, and in its kind's color beyond.
    /// </summary>
    public static void DrawPort(DrawingContext context, Point centre, PortKind kind, double share, IBrush fill)
    {
        var radius = NodeGeometry.PortRadius;

        context.DrawEllipse(PortFill(kind), null, centre, radius, radius);

        if (share >= 0.999)
        {
            context.DrawEllipse(fill, null, centre, radius, radius);
        }
        else if (share > 0.001)
        {
            var angle = share * 2 * Math.PI;
            var geometry = new StreamGeometry();

            using (var sink = geometry.Open())
            {
                sink.BeginFigure(centre, true);
                sink.LineTo(new Point(centre.X, centre.Y - radius));
                sink.ArcTo(
                    new Point(centre.X + radius * Math.Sin(angle), centre.Y - radius * Math.Cos(angle)),
                    new Size(radius, radius),
                    0,
                    share > 0.5,
                    SweepDirection.Clockwise);
                sink.EndFigure(true);
            }

            context.DrawGeometry(fill, null, geometry);
        }

        context.DrawEllipse(null, PortOutline, centre, radius, radius);
    }

    /// <summary>Cached per kind, since a socket is drawn several times a frame.</summary>
    private static IBrush PortFill(PortKind kind)
    {
        if (portFills.TryGetValue(kind, out var kept)) return kept;

        var made = new ImmutableSolidColorBrush(Colors.PortColor(kind));

        portFills[kind] = made;

        return made;
    }

    private static readonly Dictionary<PortKind, IBrush> portFills = [];

    private static readonly IPen PortOutline = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.2);

    /// <summary>
    /// Sets a module's mark in the body, right of the labels and under the
    /// header, which is drawn after it.
    /// </summary>
    /// <remarks>
    /// Sized to the body rather than fixed, so a module of one row gets a small
    /// whole mark instead of the bottom third of a large one, and capped so a
    /// tall module's does not become the module. Drawn under the text on purpose
    /// and held faint enough that nothing has to be read past it.
    /// </remarks>
    public static void DrawMark(DrawingContext context, RoundedRect body, Geometry? glyph, IPen pen)
    {
        if (glyph is null) return;

        var bounds = body.Rect;
        var room = bounds.Height - NodeGeometry.HeaderHeight;

        // Too little room for a mark to be anything but a smudge.
        if (room - MarkInset * 2 < MarkLeast) return;

        var size = Math.Min(room - MarkInset * 2, MarkMost);
        var scale = size / ModuleGlyphs.Box;

        var at = new Point(
            bounds.Right - MarkInset - size,
            bounds.Y + NodeGeometry.HeaderHeight + (room - size) / 2);

        using (context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            context.DrawGeometry(null, pen, glyph);
        }
    }

    /// <summary>How far a mark keeps off the sides of the body it is set in.</summary>
    private const double MarkInset = 4;

    /// <summary>The sizes a mark is held between — see <see cref="DrawMark"/>.</summary>
    private const double MarkLeast = 18, MarkMost = 52;

    /// <summary>
    /// How a block's background gives way on the panel: full strength in the top
    /// right corner and gone by about the middle of it, as an opacity mask so a
    /// wash, a grain, a picture and a mark all disappear the same way.
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

        if (fades.TryGetValue(key, out var kept)) return kept;

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

        fades[key] = made;

        return made;
    }

    /// <summary>
    /// How far the fade is driven down against how far it is driven left. Past the
    /// corner-to-corner diagonal on purpose: the fill belongs to the head of the
    /// panel, and a shallower one reads as a stripe across it.
    /// </summary>
    private const double Steep = 1.6;

    private static readonly Dictionary<(int Width, int Height), IBrush> fades = [];

    /// <summary>A gradient from the top of whatever it fills to the bottom.</summary>
    public static IBrush Down(Color top, Color bottom) => new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
}
