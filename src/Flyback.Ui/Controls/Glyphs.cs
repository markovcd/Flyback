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
/// Every symbol is drawn rather than typed, so it looks the same on every platform
/// and in a page: a typed one Inter lacks falls back to whatever font the machine
/// has, or to none. Drawn on a sixteen-unit box, so the strokes land on whole pixels
/// at the toolbar's scale; a smaller size scales the shape and keeps the stroke.
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

    /// <summary>A mains plug, prongs up, with its cable trailing down: what a plugin is.</summary>
    public static Control Plug() => Stroked(
        "M6,2 L6,5 M10,2 L10,5 "
        + "M4,5 L12,5 L12,8 Q12,11 8,11 Q4,11 4,8 Z "
        + "M8,11 L8,14");

    /// <summary>An envelope, flap down: writing to whoever wrote the program.</summary>
    public static Control Letter() => Stroked(
        "M2,4.5 L14,4.5 L14,11.5 L2,11.5 Z "
        + "M2,4.5 L8,9 L14,4.5");

    /// <summary>A ring with an i in it: what this is, who wrote it.</summary>
    public static Control About() => Stroked(Ring(5.5) + " M8,7.5 L8,11 M8,5 L8,5.1");

    /// <summary>An arrow turning back to the left: take the last edit back.</summary>
    public static Control Undo() => Stroked("M4,6.5 L10,6.5 A3.5,3.5 0 0 1 10,13.5 L6,13.5 M6.5,4 L4,6.5 L6.5,9");

    /// <summary>The same arrow turning right: put it back.</summary>
    public static Control Redo() => Stroked("M12,6.5 L6,6.5 A3.5,3.5 0 0 0 6,13.5 L10,13.5 M9.5,4 L12,6.5 L9.5,9");

    /// <summary>A pair of braces: the patch as text.</summary>
    public static Control Code() => Stroked(
        "M6,2.5 Q4,2.5 4,4.5 L4,6.5 Q4,8 2.5,8 Q4,8 4,9.5 L4,11.5 Q4,13.5 6,13.5 "
        + "M10,2.5 Q12,2.5 12,4.5 L12,6.5 Q12,8 13.5,8 Q12,8 12,9.5 L12,11.5 Q12,13.5 10,13.5");

    /// <summary>A knob seen from the front, its pointer at two o'clock: the knob panel.</summary>
    public static Control Knob() => Stroked(Ring(5.5) + " M8,8 L10.8,5.2");

    /// <summary>A window with its right-hand column marked off: the column beside the canvas.</summary>
    public static Control Side() => Stroked("M2,3 L14,3 L14,13 L2,13 Z M10,3 L10,13");

    /// <summary>Two arrows passing each other: the picture and the canvas trading places.</summary>
    public static Control Swap() => Stroked(SwapPath);

    /// <summary>The arrows of <see cref="Swap"/>, for a mark drawn straight onto the canvas.</summary>
    public const string SwapPath = "M3,5.5 L13,5.5 M10.5,3 L13,5.5 L10.5,8 M13,10.5 L3,10.5 M5.5,8 L3,10.5 L5.5,13";

    /// <summary>A four-pointed star, filled: the assistant.</summary>
    public static Control Spark(double size = Box, IBrush? ink = null) => Filled(
        Geometry.Parse("M8,1.5 Q8.8,7.2 14.5,8 Q8.8,8.8 8,14.5 Q7.2,8.8 1.5,8 Q7.2,7.2 8,1.5 Z"), size, ink);

    /// <summary>A cog: a ring with a hub and eight teeth.</summary>
    public static Control Settings() => Stroked(
        Ring(4) + " " + Ring(1.6)
        + " M8,1.5 L8,4 M8,12 L8,14.5 M1.5,8 L4,8 M12,8 L14.5,8"
        + " M3.4,3.4 L5.2,5.2 M10.8,10.8 L12.6,12.6 M3.4,12.6 L5.2,10.8 M10.8,5.2 L12.6,3.4");

    /// <summary>A cross: close, remove, or no.</summary>
    public static Control Cross(double size = Box, IBrush? ink = null) =>
        Stroked("M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5", size, ink);

    /// <summary>A tick: yes.</summary>
    public static Control Tick(double size = Box, IBrush? ink = null) =>
        Stroked("M3.5,8.5 L6.5,11.5 L12.5,4.5", size, ink);

    /// <summary>A small triangle pointing down, filled: a list that opens, or a block that is open.</summary>
    public static Control Down(double size = Box, IBrush? ink = null) =>
        Filled(Geometry.Parse("M4.5,6 L11.5,6 L8,10.5 Z"), size, ink);

    /// <summary>The same triangle pointing right: a block that is folded.</summary>
    public static Control Right(double size = Box, IBrush? ink = null) =>
        Filled(Geometry.Parse("M6,4.5 L10.5,8 L6,11.5 Z"), size, ink);

    /// <summary>Three bars: something to take hold of and drag.</summary>
    public static Control Grip(double size = Box, IBrush? ink = null) =>
        Stroked("M3,5 L13,5 M3,8 L13,8 M3,11 L13,11", size, ink);

    /// <summary>A ring around a dot: a socket following a knob on the panel.</summary>
    public static Control Linked(double size = Box, IBrush? ink = null) =>
        Stroked(Ring(5) + " " + Ring(1.2), size, ink);

    /// <summary>A triangle with a mark in it: something went wrong.</summary>
    public static Control Warning(double size = Box, IBrush? ink = null) =>
        Stroked("M8,2.5 L14,13 L2,13 Z M8,6.5 L8,9.5 M8,11.3 L8,11.4", size, ink);

    /// <summary>Glyphs and text side by side, in the order given, for a label that carries a symbol.</summary>
    public static StackPanel Line(params Control[] parts)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        line.Children.AddRange(parts);

        return line;
    }

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

    /// <summary>A ring nearly closed, its arrow back at where it began: the clock coming round.</summary>
    public static Control Loop() => Stroked("M13,8 A5,5 0 1 1 11.5,4.5 M11.5,1.5 L11.5,4.5 L8.5,4.5");

    /// <summary>Three dots in a row: the place a hidden toolbar is, waiting to be reached for.</summary>
    public static Control Dots(double size = Box, IBrush? ink = null) => Filled(Geometry.Parse(
        "M2,6.5 A1.5,1.5 0 1 1 2,9.5 A1.5,1.5 0 1 1 2,6.5 Z "
        + "M6.5,6.5 A1.5,1.5 0 1 1 6.5,9.5 A1.5,1.5 0 1 1 6.5,6.5 Z "
        + "M11,6.5 A1.5,1.5 0 1 1 11,9.5 A1.5,1.5 0 1 1 11,6.5 Z"), size, ink);

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

    /// <summary>Two modules, one a step down and right of the other: a copy of what is selected.</summary>
    public static Control Duplicate() => Stroked(
        "M2.5,2.5 L9.5,2.5 L9.5,9.5 L2.5,9.5 Z "
        + "M6.5,12 L6.5,13.5 L13.5,13.5 L13.5,6.5 L12,6.5");

    /// <summary>A plus: something new on the canvas.</summary>
    public static Control Add() => Stroked("M8,2.5 L8,13.5 M2.5,8 L13.5,8");

    /// <summary>A frame's four corners around a module: the whole patch brought into view.</summary>
    public static Control Frame() => Stroked(
        "M2,5 L2,2 L5,2 M11,2 L14,2 L14,5 M14,11 L14,14 L11,14 M5,14 L2,14 L2,11 "
        + "M5.5,6 L10.5,6 L10.5,10 L5.5,10 Z");

    /// <summary>A bookmark — a group put by, to be added again later.</summary>
    public static Control Keep() => Stroked("M4.5,2.5 L11.5,2.5 L11.5,13.5 L8,10.5 L4.5,13.5 Z");

    /// <summary>A circle of <paramref name="radius"/> around the middle of the box, as path data.</summary>
    private static string Ring(double radius) => FormattableString.Invariant(
        $"M8,{8 - radius} A{radius},{radius} 0 1 1 8,{8 + radius} A{radius},{radius} 0 1 1 8,{8 - radius} Z");

    /// <summary>
    /// Outlined rather than filled, to sit at the weight of the glyphs beside
    /// it, and colored from whatever holds it so that hovering, pressing and
    /// gray-out all reach it without being handled here. <paramref name="ink"/>
    /// colors one that no button holds.
    /// </summary>
    private static Control Stroked(string data, double size = Box, IBrush? ink = null)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Sized(Geometry.Parse(data), size),
            StrokeThickness = 1.2,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (ink is not null) path.Stroke = ink;
        else path[!Avalonia.Controls.Shapes.Shape.StrokeProperty] = ForegroundBinding();

        return path;
    }

    /// <summary>The transport glyphs, which read better solid than outlined at this size.</summary>
    private static Control Filled(Geometry geometry, double size = Box, IBrush? ink = null)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Sized(geometry, size),
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (ink is not null) path.Fill = ink;
        else path[!Avalonia.Controls.Shapes.Shape.FillProperty] = ForegroundBinding();

        return path;
    }

    /// <summary>The shape scaled from the sixteen-unit box to <paramref name="size"/>, the stroke left as it is.</summary>
    private static Geometry Sized(Geometry geometry, double size)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (size != Box) geometry.Transform = new ScaleTransform(size / Box, size / Box);

        return geometry;
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
