using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Knobs;

/// <summary>A thin ring around the randomize die, lit as far round as the amount reaches.</summary>
internal sealed class AmountRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<AmountRing, double>(nameof(Value), coerce: (_, v) => Math.Clamp(v, 0d, 1d));

    private const double Start = 135;
    private const double Sweep = 270;

    private static readonly IPen Track = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator), 2, lineCap: PenLineCap.Round);
    private static readonly IPen Lit = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention), 2, lineCap: PenLineCap.Round);

    static AmountRing() => AffectsRender<AmountRing>(ValueProperty);

    public AmountRing() => IsHitTestVisible = false;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2;

        context.DrawGeometry(null, Track, Arc(center, radius, Sweep));
        if (Value > 0) context.DrawGeometry(null, Lit, Arc(center, radius, Math.Max(Sweep * Value, 1)));
    }

    private static StreamGeometry Arc(Point center, double radius, double sweep)
    {
        var geometry = new StreamGeometry();

        using var sink = geometry.Open();

        sink.BeginFigure(Along(center, radius, Start), false);
        sink.ArcTo(Along(center, radius, Start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
        sink.EndFigure(false);

        return geometry;
    }

    private static Point Along(Point center, double radius, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
}
