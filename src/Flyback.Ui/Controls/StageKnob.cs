using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.App.Controls;

/// <summary>
/// A knob drawn as nothing but a ring, its travel and a pointer, on no face at all:
/// light strokes over dark ones, so it reads on a white frame and a black one alike.
/// </summary>
internal sealed class StageKnob : Knob
{
    /// <summary>How much wider the dark stroke under each light one is, either side together.</summary>
    private const double Halo = 3;

    private static readonly IImmutableBrush Light = new ImmutableSolidColorBrush(Avalonia.Media.Colors.White);
    private static readonly IImmutableBrush Dark = new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black, 0.65);
    private static readonly IImmutableBrush FaintLight = new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.45);
    private static readonly IImmutableBrush FaintDark = new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black, 0.4);

    private static readonly IPen Track = new ImmutablePen(FaintLight, 1.5, lineCap: PenLineCap.Round);
    private static readonly IPen TrackUnder = new ImmutablePen(FaintDark, 1.5 + Halo, lineCap: PenLineCap.Round);
    private static readonly IPen Arc = new ImmutablePen(Light, 2.5, lineCap: PenLineCap.Round);
    private static readonly IPen ArcUnder = new ImmutablePen(Dark, 2.5 + Halo, lineCap: PenLineCap.Round);

    public StageKnob()
    {
        Width = 40;
        Height = 40;
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 3;

        // Filled with nothing, so a press on the middle still lands on the knob.
        context.DrawEllipse(Brushes.Transparent, null, center, radius, radius);

        var track = ArcGeometry(center, radius, Start, Sweep);

        context.DrawGeometry(null, TrackUnder, track);
        context.DrawGeometry(null, Track, track);

        var angle = Radians(Start + Sweep * Value);
        var pointer = new LineGeometry(Along(center, radius * 0.2, angle), Along(center, radius, angle));
        var travel = Value > 0.001 ? ArcGeometry(center, radius, Start, Sweep * Value) : null;

        // Every dark stroke before any light one, so no halo cuts across a line.
        context.DrawGeometry(null, ArcUnder, pointer);
        if (travel is not null) context.DrawGeometry(null, ArcUnder, travel);

        if (travel is not null) context.DrawGeometry(null, Arc, travel);
        context.DrawGeometry(null, Arc, pointer);
    }
}