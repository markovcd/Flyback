using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// An input socket with no knob of its own, drawn as a diamond in its kind's color:
/// filled where a panel knob sets it, hollow where only a wire can.
/// </summary>
internal static class SocketDiamond
{
    private const double Reach = 5.5;

    public static void DrawLinked(DrawingContext context, Point center, PortKind kind) =>
        context.DrawGeometry(new ImmutableSolidColorBrush(Colors.PortColor(kind)), Outline, Shape(center));

    public static void DrawWireOnly(DrawingContext context, Point center, PortKind kind) =>
        context.DrawGeometry(Hollow, new ImmutablePen(new ImmutableSolidColorBrush(Colors.PortColor(kind)), 1.5), Shape(center));

    private static StreamGeometry Shape(Point center)
    {
        var diamond = new StreamGeometry();

        using (var sink = diamond.Open())
        {
            sink.BeginFigure(new Point(center.X, center.Y - Reach), true);
            sink.LineTo(new Point(center.X + Reach, center.Y));
            sink.LineTo(new Point(center.X, center.Y + Reach));
            sink.LineTo(new Point(center.X - Reach, center.Y));
            sink.EndFigure(true);
        }

        return diamond;
    }

    private static readonly IBrush Hollow = new ImmutableSolidColorBrush(Colors.Canvas);
    private static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Outline), 1.2);
}
