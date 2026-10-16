using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.Ui.Controls;

/// <summary>
/// A switch or a button over the picture: an outline, filled while it is on, light
/// over dark so it reads on a white frame and a black one alike.
/// </summary>
internal sealed class StageSwitch : PanelSwitch
{
    private static readonly IImmutableBrush Light = new ImmutableSolidColorBrush(Avalonia.Media.Colors.White);
    private static readonly IImmutableBrush Dark = new ImmutableSolidColorBrush(Avalonia.Media.Colors.Black, 0.65);

    private static readonly IPen Outline = new ImmutablePen(Light, 2.5);
    private static readonly IPen OutlineUnder = new ImmutablePen(Dark, 5.5);

    public StageSwitch()
    {
        Width = 40;
        Height = 40;
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (Momentary)
        {
            var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 6;
            context.DrawEllipse(null, OutlineUnder, center, radius, radius);
            context.DrawEllipse(IsOn ? Light : Brushes.Transparent, Outline, center, radius, radius);
            return;
        }

        var track = new Rect(center.X - 15, center.Y - 8, 30, 16);
        var thumb = new Point(IsOn ? track.Right - 8 : track.Left + 8, center.Y);

        context.DrawRectangle(null, OutlineUnder, track, 8, 8);
        context.DrawRectangle(Brushes.Transparent, Outline, track, 8, 8);
        context.DrawEllipse(IsOn ? Light : Dark, Outline, thumb, 5, 5);
    }
}
