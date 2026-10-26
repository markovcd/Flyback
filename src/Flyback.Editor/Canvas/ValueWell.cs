using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The box an unpatched input's value sits in at the right of its row, with a level
/// line under the number showing where it sits in its range. Dragging it turns the value.
/// </summary>
internal static class ValueWell
{
    public const double Width = 56;
    public const double Height = 18;

    /// <summary>How far the well keeps off the module's right edge.</summary>
    private const double Inset = 7;

    /// <summary>How far the number and the level line keep off the well's sides.</summary>
    private const double Padding = 6;

    private const double Corner = 4;

    /// <summary>The most steps a stepped value shows as ticks; more would run into a line.</summary>
    private const int MostTicks = 43;

    /// <summary>The well of the input whose row is centered on <paramref name="center"/>.</summary>
    public static Rect At(Rect bounds, Point center) =>
        new(bounds.Right - Inset - Width, center.Y - Height / 2, Width, Height);

    /// <summary>Whether an input has a well: unpatched, on a knob of its own, and read by the module.</summary>
    public static bool Has(Patch patch, NodeInstance node, NodeDef def, int port) =>
        port < def.Inputs.Count
        && port < node.InputValues.Length
        && SocketDial.HasOwnKnob(patch, node, port, def.Inputs[port])
        && (NodeCatalog.FormulaOf(node) is not { } formula || FormulaLayout.Reads(formula, port));

    /// <summary>The tint across a row whose well is hovered, turning or typed into, and the ring round one that has the keys.</summary>
    public static void DrawRow(DrawingContext context, Rect bounds, Point center, WellState state)
    {
        var row = new Rect(bounds.X, center.Y - NodeGeometry.RowHeight / 2, bounds.Width, NodeGeometry.RowHeight);

        var tint = state switch
        {
            WellState.Hover or WellState.Edit => HoverRow,
            WellState.Turn or WellState.Fine => TurnRow,
            _ => null,
        };

        if (tint is not null) context.FillRectangle(tint, row);

        if (state == WellState.Focus) context.DrawRectangle(null, FocusRing, row.Deflate(0.75));
    }

    /// <summary>The well, the value in it and the level line under it.</summary>
    /// <param name="share">Where the value sits along its range, 0 to 1.</param>
    /// <param name="steps">How many values a stepped input can take, or 0 for a smooth one.</param>
    /// <param name="ink">What the number is written in at rest.</param>
    public static void Draw(DrawingContext context, Rect well, string text, double share, int steps, WellState state, IBrush ink)
    {
        var hot = state is WellState.Turn or WellState.Fine;
        var editing = state == WellState.Edit;
        var lit = state != WellState.Rest;

        if (state == WellState.Fine)
            context.DrawRectangle(null, FineHalo, well.Inflate(2.5), Corner + 2.5, Corner + 2.5);

        if (editing)
            context.DrawRectangle(null, EditHalo, well.Inflate(1.5), Corner + 1.5, Corner + 1.5);

        if (lit)
            context.DrawRectangle(
                hot ? TurnFill : editing ? EditFill : HoverFill,
                hot ? TurnEdge : editing ? EditEdge : HoverEdge,
                well.Deflate(0.5),
                Corner,
                Corner);

        var number = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Numbers,
            CanvasText.RowSize,
            hot || editing ? Brushes.White : lit ? HoverInk : ink);

        // The chevrons give way to a number that needs their room.
        var room = well.Width - Padding * 2;
        var chevrons = lit && !editing && number.Width <= room - ChevronRoom;

        number.MaxTextWidth = room;
        number.MaxLineCount = 1;
        number.Trimming = TextTrimming.CharacterEllipsis;

        context.DrawText(number, new Point(well.Right - Padding - number.Width, well.Y + (well.Height - 2 - number.Height) / 2));

        if (chevrons) DrawChevrons(context, new Point(well.X + Padding + 3, well.Center.Y - 1), hot ? Attention : HoverInk);

        var fill = hot ? Attention : editing ? EditLevel : lit ? HoverLevel : RestLevel;
        var track = new Rect(well.X + Padding, well.Bottom - 4, well.Width - Padding * 2, 2);
        share = Math.Clamp(share, 0, 1);

        if (steps is > 1 and <= MostTicks)
        {
            DrawTicks(context, track, steps, share, fill);
            return;
        }

        context.FillRectangle(state == WellState.Rest ? RestTrack : LitTrack, track, 1);
        context.FillRectangle(fill, track.WithWidth(track.Width * share), 1);
    }

    /// <summary>
    /// An input's socket drawn as a ring in its kind's color, hollow, which is what says
    /// it rests on a value of its own; white while its well is under the pointer.
    /// </summary>
    public static void DrawSocket(DrawingContext context, Point center, PortKind kind, WellState state)
    {
        var radius = NodeGeometry.PortRadius - 1;
        var pen = state == WellState.Rest ? RingPen(kind) : LitRing;

        context.DrawEllipse(Hollow, Outline, center, NodeGeometry.PortRadius, NodeGeometry.PortRadius);
        context.DrawEllipse(null, pen, center, radius, radius);
    }

    /// <summary>One tick per step, those up to the value lit.</summary>
    private static void DrawTicks(DrawingContext context, Rect track, int steps, double share, IBrush fill)
    {
        var pitch = track.Width / (steps - 1);
        var width = pitch < 4 ? 1 : 2;
        var last = (int)Math.Round(share * (steps - 1));

        for (var i = 0; i < steps; i++)
            context.FillRectangle(i <= last ? fill : Tick, new Rect(track.X + i * pitch - width / 2.0, track.Y - 1, width, 4));
    }

    /// <summary>The ▴▾ that says the value goes up and down.</summary>
    private static void DrawChevrons(DrawingContext context, Point center, IBrush brush)
    {
        var pen = new ImmutablePen((IImmutableBrush)brush, 1.3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(center.X - 2.5, center.Y - 1.5), false);
            sink.LineTo(new Point(center.X, center.Y - 4.5));
            sink.LineTo(new Point(center.X + 2.5, center.Y - 1.5));
            sink.EndFigure(false);

            sink.BeginFigure(new Point(center.X - 2.5, center.Y + 1.5), false);
            sink.LineTo(new Point(center.X, center.Y + 4.5));
            sink.LineTo(new Point(center.X + 2.5, center.Y + 1.5));
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private const double ChevronRoom = 9;

    private static IPen RingPen(PortKind kind)
    {
        if (Rings.TryGetValue(kind, out var kept)) return kept;

        var made = new ImmutablePen(new ImmutableSolidColorBrush(Colors.PortColor(kind)), 2);

        Rings[kind] = made;

        return made;
    }

    private static readonly Dictionary<PortKind, IPen> Rings = [];

    private static readonly Typeface Numbers = new(Text.Mono);

    private static readonly IImmutableBrush Attention = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IImmutableBrush HoverInk = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0));
    private static readonly IBrush HoverFill = new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x22, 0x27));
    private static readonly IPen HoverEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x7C, 0x84, 0x94)));
    private static readonly IBrush TurnFill = new ImmutableSolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x13));
    private static readonly IPen TurnEdge = new ImmutablePen(Attention, 2);
    private static readonly IPen FineHalo = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention, 0.28), 3);
    private static readonly IBrush RestTrack = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5B));
    private static readonly IBrush LitTrack = new ImmutableSolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x85));
    private static readonly IBrush Tick = LitTrack;
    private static readonly IBrush RestLevel = new ImmutableSolidColorBrush(Color.FromRgb(0x9B, 0xA3, 0xB1));
    private static readonly IBrush HoverLevel = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush HoverRow = new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.06);
    private static readonly IBrush TurnRow = new ImmutableSolidColorBrush(Colors.Attention, 0.13);
    private static readonly IBrush Hollow = new ImmutableSolidColorBrush(Colors.Canvas);
    private static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.2);
    private static readonly IPen FocusRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0)), 1.5);
    private static readonly IBrush EditFill = new ImmutableSolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x0E));
    private static readonly IPen EditEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)));
    private static readonly IPen EditHalo = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8), 0.28), 2);
    private static readonly IBrush EditLevel = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly IPen LitRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)), 2);

    /// <summary>What the number is written in at rest, lighter than other values so it reads as something to take hold of.</summary>
    public static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0xAE, 0xB5, 0xC2));
}
