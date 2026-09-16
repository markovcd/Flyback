using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// The toolbar icons that are drawn rather than typed.
/// </summary>
/// <remarks>
/// A folder and a floppy disk are what open and save look like everywhere, and
/// neither is a character any font here can be relied on to have: on Windows the code
/// points resolve to the color emoji font, which puts two full-color pictures in a bar
/// of thin grey strokes. Drawn on a sixteen-unit box and left at that size, so the
/// strokes land on whole pixels at the scale the toolbar uses.
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
    /// Three rows of a list, the shortest at the bottom — what a preset is:
    /// one to point at, out of several.
    /// </summary>
    public static Control Presets() => Stroked(
        "M2,4 L14,4 M2,8 L14,8 M2,12 L10,12");

    /// <summary>A plain dot, filled — the record light on every deck and camera.</summary>
    public static Control Record() => Filled(new EllipseGeometry(new Rect(3, 3, 10, 10)));

    /// <summary>A plain square, filled — what the dot becomes once a take is running.</summary>
    public static Control Stop() => Filled(new RectangleGeometry(new Rect(3.5, 3.5, 9, 9)));

    /// <summary>A bar and a triangle pointing at it — skip to the start, on every deck.</summary>
    public static Control Rewind() =>
        Filled(Geometry.Parse("M3,3 L4.5,3 L4.5,13 L3,13 Z M13,3 L13,13 L5,8 Z"));

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

        path[!Avalonia.Controls.Shapes.Shape.StrokeProperty] = ForegroundBinding();

        return path;
    }

    /// <summary>The transport glyphs, which read better solid than outlined at this size.</summary>
    private static Control Filled(Geometry geometry)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Width = Box,
            Height = Box,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        path[!Avalonia.Controls.Shapes.Shape.FillProperty] = ForegroundBinding();

        return path;
    }

    /// <summary>
    /// The ancestor's ContentPresenter, not the Button itself: a disabled
    /// button dims by setting Foreground on the presenter its template draws
    /// through, and leaves the Button's own property untouched. Binding to
    /// the Button would read a color that never changes.
    /// </summary>
    private static Binding ForegroundBinding() => new("Foreground")
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
        {
            AncestorType = typeof(ContentPresenter),
        },
    };
}
