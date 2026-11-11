using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.Ui.Controls;

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

    /// <summary>How heavy a line is: square-ended and mitred, like a mark printed on a front panel.</summary>
    private const double Weight = 1.6;

    /// <summary>An arrow down into a tray: load.</summary>
    public static Control Open() => Stroked("M2,9 L2,14 L14,14 L14,9 M8,2 L8,10 M5,7 L8,10 L11,7");

    /// <summary>An arrow up out of a tray: store.</summary>
    public static Control Save(double size = Box, IBrush? ink = null) => Stroked(
        "M2,9 L2,14 L14,14 L14,9 M8,10 L8,2 M5,5 L8,2 L11,5", size, ink);

    /// <summary>Two modules on the left feeding one on the right: the patch laid out, in the canvas's own terms.</summary>
    public static Control Tidy() => Inked(
        "M6.5,4 L8,4 L8,12 L6.5,12 M8,8 L9.5,8",
        "M1.5,2 L6.5,2 L6.5,6 L1.5,6 Z M1.5,10 L6.5,10 L6.5,14 L1.5,14 Z M9.5,6 L14.5,6 L14.5,10 L9.5,10 Z");

    /// <summary>Three rows, each with a block at its head: a list of presets to pick from.</summary>
    public static Control Presets() => Inked(
        "M6,3.5 L14,3.5 M6,8 L14,8 M6,12.5 L14,12.5",
        "M2,2.5 L5,2.5 L5,4.5 L2,4.5 Z M2,7 L5,7 L5,9 L2,9 Z M2,11.5 L5,11.5 L5,13.5 L2,13.5 Z");

    /// <summary>A patch cable's jack, tip up: what a plugin is plugged into.</summary>
    public static Control Plug() => Inked(
        "M6,3 L10,3 L10,6 L6,6 Z M6.5,6 L6.5,10 L9.5,10 L9.5,6 M8,10 L8,15",
        "M7.2,1 L8.8,1 L8.8,3 L7.2,3 Z");

    /// <summary>An envelope, flap down: writing to whoever wrote the program.</summary>
    public static Control Letter() => Stroked(
        "M2,4.5 L14,4.5 L14,11.5 L2,11.5 Z "
        + "M2,4.5 L8,9 L14,4.5");

    /// <summary>An i in a box.</summary>
    public static Control About() => Inked(
        "M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z M8,7 L8,12",
        "M7,4 L9,4 L9,5.8 L7,5.8 Z");

    /// <summary>An arrow turned back on itself, squared off.</summary>
    public static Control Undo() => Stroked("M3,6 L11,6 L13,8 L13,12 L11,14 L7,14 M6,3 L3,6 L6,9");

    /// <summary>The same arrow, the other way.</summary>
    public static Control Redo() => Stroked("M13,6 L5,6 L3,8 L3,12 L5,14 L9,14 M10,3 L13,6 L10,9");

    /// <summary>An open book: the tutorials.</summary>
    public static Control Lesson() => Stroked(
        "M8,4.5 Q5,3 2,3.5 L2,12.5 Q5,12 8,13.5 Q11,12 14,12.5 L14,3.5 Q11,3 8,4.5 Z M8,4.5 L8,13.5");

    /// <summary>A pair of braces, drawn straight: the patch as text.</summary>
    public static Control Code() => Stroked(
        "M6,2 L4,2 L4,7 L2,8 L4,9 L4,14 L6,14 M10,2 L12,2 L12,7 L14,8 L12,9 L12,14 L10,14");

    /// <summary>A knob with its pointer at two o'clock and its scale ticked round it: the knob panel.</summary>
    public static Control Knob() => Stroked(
        Ring(4.5) + " M8,8 L10.6,5.4 M1,8 L2,8 M14,8 L15,8 M8,1 L8,2 M3,3 L3.7,3.7 M13,3 L12.3,3.7");

    /// <summary>An octave of a piano: four white keys with the black ones over their gaps.</summary>
    public static Control Keys() => Stroked(
        "M2,3 L14,3 L14,13 L2,13 Z M5,8.5 L5,13 M8,8.5 L8,13 M11,8.5 L11,13 "
        + "M4,3 L4,8.5 L6,8.5 L6,3 M7,3 L7,8.5 L9,8.5 L9,3 M10,3 L10,8.5 L12,8.5 L12,3");

    /// <summary>A minus, the other half of <see cref="Add"/>.</summary>
    public static Control Minus() => Stroked("M2.5,8 L13.5,8");

    /// <summary>A window with its right-hand column filled in: the column beside the canvas.</summary>
    public static Control Side() => Inked("M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z", "M10,2.5 L14.5,2.5 L14.5,13.5 L10,13.5 Z");

    /// <summary>The side column with its divider pushed to the top: the panel taking the picture's room.</summary>
    public static Control Spread() => Stroked("M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M2.5,4.5 L13.5,4.5 M8,12 L8,7 M6,9 L8,7 L10,9");

    /// <summary>The side column with its divider back in the middle: the picture given its room again.</summary>
    public static Control Gather() => Stroked("M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M2.5,8.5 L13.5,8.5 M8,4 L8,7 M6.5,5.5 L8,7 L9.5,5.5");

    /// <summary>A window with a row filled in along its foot: the transport row.</summary>
    public static Control Transport() => Inked("M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z", "M1.5,10 L14.5,10 L14.5,13.5 L1.5,13.5 Z");

    /// <summary>Two arrows passing each other: the picture and the canvas trading places.</summary>
    public static Control Swap() => Stroked(SwapPath);

    /// <summary>The arrows of <see cref="Swap"/>, for a mark drawn straight onto the canvas.</summary>
    public const string SwapPath = "M2,5 L13,5 M10,2 L13,5 L10,8 M14,11 L3,11 M6,8 L3,11 L6,14";

    /// <summary>A four-pointed star, filled: the assistant.</summary>
    public static Control Spark(double size = Box, IBrush? ink = null) => Filled(
        Geometry.Parse("M8,1 L9.4,6.6 L15,8 L9.4,9.4 L8,15 L6.6,9.4 L1,8 L6.6,6.6 Z"), size, ink);

    /// <summary>Three faders, each at its own level: the settings.</summary>
    public static Control Settings() => Inked(
        "M4,2 L4,14 M8,2 L8,14 M12,2 L12,14",
        "M2.5,9 L5.5,9 L5.5,11.5 L2.5,11.5 Z M6.5,4 L9.5,4 L9.5,6.5 L6.5,6.5 Z M10.5,7 L13.5,7 L13.5,9.5 L10.5,9.5 Z");

    /// <summary>A cross: close, remove, or no.</summary>
    public static Control Cross(double size = Box, IBrush? ink = null) =>
        Stroked("M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5", size, ink);

    /// <summary>A tick: yes.</summary>
    public static Control Tick(double size = Box, IBrush? ink = null) =>
        Stroked("M3.5,8.5 L6.5,11.5 L12.5,4.5", size, ink);

    /// <summary>An arrow pointing up: send what is written.</summary>
    public static Control Send(double size = Box, IBrush? ink = null) =>
        Stroked("M8,13 L8,3.5 M4,7.5 L8,3.5 L12,7.5", size, ink);

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

    /// <summary>A dot in a frame, filled — the record button on a deck's panel.</summary>
    public static Control Record() => Inked(
        "M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z", "M4.5,8 A3.5,3.5 0 1 1 11.5,8 A3.5,3.5 0 1 1 4.5,8 Z");

    /// <summary>A square in the frame — what the record button becomes once a take is running.</summary>
    public static Control Stop(double size = Box, IBrush? ink = null) => Inked(
        "M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z", "M5,5 L11,5 L11,11 L5,11 Z", size, ink);

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

    /// <summary>A sheet with an arrow leaving it: a copy sent to the clipboard.</summary>
    public static Control Copy() => Stroked(
        "M2.5,5.5 L2.5,13.5 L10.5,13.5 L10.5,9.5 "
        + "M6.5,9.5 L13.5,2.5 M9,2.5 L13.5,2.5 L13.5,7");

    /// <summary>Scissors: what is taken off the canvas and onto the clipboard.</summary>
    public static Control Cut() => Stroked(
        "M3.5,11 A1.5,1.5 0 1 1 3.5,14 A1.5,1.5 0 1 1 3.5,11 Z "
        + "M8.5,11 A1.5,1.5 0 1 1 8.5,14 A1.5,1.5 0 1 1 8.5,11 Z "
        + "M4.2,11.2 L11,2.5 M7.8,11.2 L1,2.5");

    /// <summary>A clipboard with an arrow going into it: the clipboard's modules added to the canvas.</summary>
    public static Control Paste() => Stroked(
        "M5.5,3.5 L3,3.5 L3,14 L13,14 L13,3.5 L10.5,3.5 "
        + "M5.5,2 L10.5,2 L10.5,5 L5.5,5 Z "
        + "M8,7 L8,12 M6,10 L8,12 L10,10");

    /// <summary>A dashed box with a module inside: every module picked out.</summary>
    public static Control SelectAll() => Stroked(
        "M2.5,2.5 L5,2.5 M7,2.5 L9,2.5 M11,2.5 L13.5,2.5 L13.5,5 M13.5,7 L13.5,9 "
        + "M13.5,11 L13.5,13.5 L11,13.5 M9,13.5 L7,13.5 M5,13.5 L2.5,13.5 L2.5,11 "
        + "M2.5,9 L2.5,7 M2.5,5 L2.5,2.5 "
        + "M5.5,5.5 L10.5,5.5 L10.5,10.5 L5.5,10.5 Z");

    /// <summary>A head and shoulders: a person.</summary>
    public static Control Person(double size = Box, IBrush? ink = null) =>
        Stroked("M8,2.5 A2.5,2.5 0 1 1 8,7.5 A2.5,2.5 0 1 1 8,2.5 Z M3,13.5 A5,4.5 0 0 1 13,13.5", size, ink);

    /// <summary>A pencil: change what is there.</summary>
    public static Control Pencil(double size = Box, IBrush? ink = null) =>
        Stroked("M10.5,2.8 L13.2,5.5 L5.3,13.4 L2.5,13.5 L2.6,10.7 Z M9,4.3 L11.7,7", size, ink);

    /// <summary>A plus: something new on the canvas.</summary>
    public static Control Add() => Stroked("M8,2.5 L8,13.5 M2.5,8 L13.5,8");

    /// <summary>A frame's four corners around a module: the whole patch brought into view.</summary>
    public static Control Frame() => Stroked(
        "M2,5 L2,2 L5,2 M11,2 L14,2 L14,5 M14,11 L14,14 L11,14 M5,14 L2,14 L2,11 "
        + "M5.5,6 L10.5,6 L10.5,10 L5.5,10 Z");

    /// <summary>A trace on a scope's screen: every output, measured.</summary>
    public static Control Measure() => Stroked(
        "M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z" + " M2,8 Q4.5,3 6.5,8 Q8.5,13 11,8 Q12.5,5 14,7");

    /// <summary>The trace with a cross at its corner: give up the measurement under way.</summary>
    public static Control MeasureCancel() => Stroked(
        "M1.5,7.5 L11.5,7.5 L11.5,14 L1.5,14 Z M2,11 Q4,8 6,11 Q8,14 11,11 M10,1.5 L14.5,6 M14.5,1.5 L10,6");

    /// <summary>The trace struck through: take the measurements down.</summary>
    public static Control MeasureHide() => Stroked(
        "M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z" + " M2,8 Q4.5,3 6.5,8 Q8.5,13 11,8 Q12.5,5 14,7 M2.5,14 L13.5,2");

    /// <summary>A box with an arrow leaving it: the patch taken to the viewer, in a tab of its own.</summary>
    public static Control Viewer() => Stroked("M9,2.5 L13.5,2.5 L13.5,7 M13.5,2.5 L7.5,8.5 M12,9.5 L12,13.5 L2.5,13.5 L2.5,4 L6.5,4");

    /// <summary>A screen with two modules wired inside it: the patch played over the picture.</summary>
    public static Control Live() => Stroked(
        "M2,3 L14,3 L14,13 L2,13 Z M4.5,5.5 L7,5.5 L7,8 L4.5,8 Z M9,8 L11.5,8 L11.5,10.5 L9,10.5 Z M7,6.75 L9,9.25");

    /// <summary>Square brackets round a dash: what a module carries besides its knobs.</summary>
    public static Control Extras() => Stroked("M6,2.5 L3.5,2.5 L3.5,13.5 L6,13.5 M10,2.5 L12.5,2.5 L12.5,13.5 L10,13.5 M6.5,8 L9.5,8");

    /// <summary>A bookmark — a group put by, to be added again later.</summary>
    public static Control Keep() => Stroked("M4.5,2.5 L11.5,2.5 L11.5,13.5 L8,10.5 L4.5,13.5 Z");

    /// <summary>A die showing three: the knob panel's randomize.</summary>
    public static Control Dice() => Stroked(
        "M3,2.5 L13,2.5 A0.5,0.5 0 0 1 13.5,3 L13.5,13 A0.5,0.5 0 0 1 13,13.5 L3,13.5 A0.5,0.5 0 0 1 2.5,13 L2.5,3 A0.5,0.5 0 0 1 3,2.5 Z "
        + "M5.5,5.5 L5.5,5.6 M8,8 L8,8.1 M10.5,10.5 L10.5,10.6");

    /// <summary>A clock with its hands and an arrow running back round it: back through the randomizes.</summary>
    public static Control History() => Stroked(
        "M2.5,8 A5.5,5.5 0 1 0 4.1,4.1 M4.1,4.1 L4.1,1.8 M4.1,4.1 L1.8,4.1 M8,5 L8,8 L10.5,9.5");

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
            StrokeThickness = Weight,
            StrokeJoin = PenLineJoin.Miter,
            StrokeLineCap = PenLineCap.Square,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (ink is not null) path.Stroke = ink;
        else path.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, Foreground(path));

        return path;
    }

    /// <summary>The transport glyphs, which read better solid than outlined at this size.</summary>
    /// <summary>A mark that is part line and part solid block, as a panel prints one.</summary>
    private static Control Inked(string stroke, string fill, double size = Box, IBrush? ink = null) => new Panel
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Stroked(stroke, size, ink), Filled(Geometry.Parse(fill), size, ink) },
    };

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
        else path.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, Foreground(path));

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
    /// The foreground the glyph inherits. A button's presenter passes its own on,
    /// which is where a disabled button dims; outside a button it is the window's.
    /// </summary>
    private static IObservable<IBrush?> Foreground(Control path) =>
        path.GetObservable(TextElement.ForegroundProperty);
}
