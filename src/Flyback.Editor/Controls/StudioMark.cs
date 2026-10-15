using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.Editor.Controls;

/// <summary>
/// Potato Smasher's mark at icon size: the spud on its butter square, handle,
/// plate and eyes, as in <c>site/assets/potato-smasher.svg</c>.
/// </summary>
/// <remarks>
/// The colors are the studio's own, not Flyback's palette, so they stay put when
/// the editor's theme changes.
/// </remarks>
internal sealed class StudioMark : Control
{
    private const double Box = 64;

    private static readonly IBrush Butter = new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xC8, 0x47));
    private static readonly IBrush Starch = new ImmutableSolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xF0));
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x1B, 0x15, 0x10));

    // A geometry belongs to the thread that made it, so each control parses its own.
    private readonly Geometry spud = Geometry.Parse(
        "M52 84H148C176 84 190 100 190 122C190 150 170 168 140 168H62C30 168 10 150 10 124C10 100 26 84 52 84Z");

    private readonly Geometry eyes = Geometry.Parse("M60 104L82 118L60 132M140 104L118 118L140 132");

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;

        var scale = side / Box;
        var left = (Bounds.Width - side) / 2;
        var top = (Bounds.Height - side) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
        {
            context.DrawRectangle(Butter, null, new Rect(0, 0, Box, Box), 15, 15);

            // The spud's own 200 by 180 box, set inside the square.
            using (context.PushTransform(Matrix.CreateScale(0.24, 0.24) * Matrix.CreateTranslation(8, 10)))
            {
                context.DrawGeometry(Starch, null, spud);
                context.DrawRectangle(Ink, null, new Rect(40, 58, 120, 30), 15, 15);
                context.DrawRectangle(Ink, null, new Rect(86, 10, 28, 60), 12, 12);
                context.DrawGeometry(null, new Pen(Ink, 16, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), eyes);
            }
        }
    }
}
