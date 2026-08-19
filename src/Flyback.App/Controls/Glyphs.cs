using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// The toolbar icons that are drawn rather than typed.
/// </summary>
/// <remarks>
/// A folder and a floppy disk are what open and save look like everywhere, and
/// neither is a character any font here can be relied on to have: the code
/// points exist, but on Windows they resolve to the color emoji font, which
/// puts two full-color pictures in a bar of thin grey strokes.
/// <para>
/// Drawn on a sixteen-unit box and left at that size, so the strokes land on
/// whole pixels at the scale the toolbar actually uses. The same reasoning as
/// <see cref="LogoMark"/>, which draws the mark rather than rasterising the SVG
/// beside it — a shape this small is less work than the dependency would be.
/// </para>
/// </remarks>
internal static class Glyphs
{
    private const double Box = 16;

    /// <summary>A folder, seen from the front, with the tab on the left.</summary>
    public static Control Open() => Stroked("M2,4.5 L6.5,4.5 L8,6.5 L14,6.5 L14,12.5 L2,12.5 Z");

    /// <summary>
    /// A three-and-a-half inch disk: the clipped corner, the shutter at the top
    /// and the label at the bottom.
    /// </summary>
    public static Control Save() => Stroked(
        "M2.5,2.5 L11.5,2.5 L13.5,4.5 L13.5,13.5 L2.5,13.5 Z "
        + "M5.5,2.5 L10.5,2.5 L10.5,6 L5.5,6 Z "
        + "M4.5,9.5 L11.5,9.5 L11.5,13.5 L4.5,13.5 Z");

    /// <summary>
    /// A patch in miniature: two modules on the left feeding one on the right,
    /// which is what the button does to the canvas said in the canvas's own
    /// terms. Drawn rather than typed for the same reason the other two are —
    /// no character here means "lay this out", and the ones that come close
    /// resolve to the emoji font on Windows.
    /// </summary>
    public static Control Tidy() => Stroked(
        "M2,2.5 L6,2.5 L6,6.5 L2,6.5 Z "
        + "M2,9.5 L6,9.5 L6,13.5 L2,13.5 Z "
        + "M10,6 L14,6 L14,10 L10,10 Z "
        + "M6,4.5 L8,4.5 L8,11.5 L6,11.5 "
        + "M8,8 L10,8");

    /// <summary>
    /// Outlined rather than filled, to sit at the weight of the glyphs beside
    /// it, and colored from whatever holds it so that hovering, pressing and
    /// grey-out all reach it without being handled here.
    /// </summary>
    private static Control Stroked(string data)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.2,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Width = Box,
            Height = Box,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        path[!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) },
        };

        return path;
    }
}
