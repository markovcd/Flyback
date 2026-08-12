using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// The Flyback mark, drawn rather than loaded. Avalonia cannot rasterise an SVG
/// without a package for it, and this one is two sweeps, a retrace and a beam —
/// less work than the dependency would be.
/// </summary>
/// <remarks>
/// Every number here comes from <c>docs/logo.svg</c> and is meant to stay equal
/// to it: the coordinates are that file's 256-unit box, scaled to whatever size
/// this control is given. The badge and the glow are left out, because a
/// watermark wants the shape and not the icon.
/// </remarks>
public sealed class LogoMark : Control
{
    private const double Box = 256;
    private const double Thickness = 21;

    private static readonly IBrush Retrace = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));
    private static readonly IBrush Beam = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x4A));
    private static readonly IBrush Core = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xDC));

    /// <summary>
    /// Draws the largest square that fits, centred in whatever it is given. The
    /// control can then simply fill its parent and follow the splitter, rather
    /// than being pinned to a size that is only right at one panel width.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;

        var scale = side / Box;
        var left = (Bounds.Width - side) / 2;
        var top = (Bounds.Height - side) / 2;

        Point At(double x, double y) => new(left + x * scale, top + y * scale);

        var sweep = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(At(56, 184), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(At(200, 72), RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x4A, 0x9E, 0xDE), 0),
                new GradientStop(Color.FromRgb(0x3F, 0xC8, 0xC8), 0.5),
                new GradientStop(Color.FromRgb(0x4F, 0xC3, 0x87), 1),
            },
        };

        var stroke = Thickness * scale;

        // Retrace underneath, as in the file: the sweeps meet it at both ends and
        // should be the ones that overlap.
        context.DrawLine(new Pen(Retrace, stroke, lineCap: PenLineCap.Round), At(128, 72), At(128, 184));

        var pen = new Pen(sweep, stroke, lineCap: PenLineCap.Round);
        context.DrawLine(pen, At(56, 184), At(128, 72));
        context.DrawLine(pen, At(128, 184), At(200, 72));

        context.DrawEllipse(Beam, null, At(200, 72), 13 * scale, 13 * scale);
        context.DrawEllipse(Core, null, At(200, 72), 5.5 * scale, 5.5 * scale);
    }
}
