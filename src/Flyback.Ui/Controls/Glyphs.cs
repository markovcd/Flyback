using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// The icons that are drawn rather than typed: the toolbar's, and the ones on the
/// panel's action buttons.
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
    /// A loudspeaker with two waves coming off it — what a preset that is heard
    /// and never seen shows where its picture would be.
    /// </summary>
    public static Control Speaker() => Stroked(
        "M2.5,6 L5,6 L8.5,3 L8.5,13 L5,10 L2.5,10 Z "
        + "M10.5,6 Q11.8,8 10.5,10 "
        + "M12.5,4 Q15,8 12.5,12");

    /// <summary>The loudspeaker with its waves crossed out: the same cone, turned down.</summary>
    public static Control Muted() => Stroked(
        "M2.5,6 L5,6 L8.5,3 L8.5,13 L5,10 L2.5,10 Z "
        + "M10.5,6 L14.5,10 M14.5,6 L10.5,10");

    /// <summary>Two bars, filled — what stops a picture on its frame.</summary>
    public static Control Pause() =>
        Filled(Geometry.Parse("M4,3 L7,3 L7,13 L4,13 Z M9,3 L12,3 L12,13 L9,13 Z"));

    /// <summary>A triangle pointing right, filled — what starts it again.</summary>
    public static Control Play() => Filled(Geometry.Parse("M4.5,3 L13,8 L4.5,13 Z"));

    /// <summary>Three dots in a row: the place a hidden toolbar is, waiting to be reached for.</summary>
    public static Control Dots() => Filled(Geometry.Parse(
        "M2,6.5 A1.5,1.5 0 1 1 2,9.5 A1.5,1.5 0 1 1 2,6.5 Z "
        + "M6.5,6.5 A1.5,1.5 0 1 1 6.5,9.5 A1.5,1.5 0 1 1 6.5,6.5 Z "
        + "M11,6.5 A1.5,1.5 0 1 1 11,9.5 A1.5,1.5 0 1 1 11,6.5 Z"));

    /// <summary>
    /// Two modules inside a frame — what grouping makes, in the canvas's own
    /// terms. Corners rather than a whole box, which at this size fills in.
    /// </summary>
    public static Control Group() => Stroked(
        "M2,5 L2,2 L5,2 M11,2 L14,2 L14,5 M14,11 L14,14 L11,14 M5,14 L2,14 L2,11 "
        + "M4,6 L7,6 L7,10 L4,10 Z "
        + "M9,6 L12,6 L12,10 L9,10 Z");

    /// <summary>The same two modules with the frame off them, adrift.</summary>
    public static Control Ungroup() => Stroked(
        "M2.5,2.5 L6.5,2.5 L6.5,6.5 L2.5,6.5 Z "
        + "M9.5,9.5 L13.5,9.5 L13.5,13.5 L9.5,13.5 Z");

    /// <summary>Arrows pushing apart along the diagonal: a box showing what is inside.</summary>
    public static Control OpenBox() => Stroked(
        "M7,3.5 L3.5,3.5 L3.5,7 M3.5,3.5 L7.5,7.5 "
        + "M9,12.5 L12.5,12.5 L12.5,9 M12.5,12.5 L8.5,8.5");

    /// <summary>The same arrows drawn inward: the modules gathered back into the box.</summary>
    public static Control ShutBox() => Stroked(
        "M3.5,7 L7,7 L7,3.5 M7,7 L3.5,3.5 "
        + "M12.5,9 L9,9 L9,12.5 M9,9 L12.5,12.5");

    /// <summary>A bin with a lid: the one button on the panel that takes something away.</summary>
    public static Control Delete() => Stroked(
        "M2.5,4.5 L13.5,4.5 "
        + "M6,4.5 L6,2.5 L10,2.5 L10,4.5 "
        + "M4,4.5 L4.8,13.5 L11.2,13.5 L12,4.5 "
        + "M6.5,7 L6.5,11 M9.5,7 L9.5,11");

    /// <summary>
    /// The power mark: a ring broken at the top with a stroke standing in the
    /// gap, which is what a switch looks like on every piece of equipment a
    /// patch would be played through.
    /// </summary>
    public static Control Switch() => Stroked(
        "M4.8,4.6 A5,5 0 1 0 11.2,4.6 "
        + "M8,2 L8,7.5");

    /// <summary>A bookmark — a group put by, to be added again later.</summary>
    public static Control Keep() => Stroked("M4.5,2.5 L11.5,2.5 L11.5,13.5 L8,10.5 L4.5,13.5 Z");

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
