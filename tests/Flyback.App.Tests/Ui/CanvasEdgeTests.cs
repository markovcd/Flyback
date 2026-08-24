using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The canvas is drawn as the finite sheet it is: ruled where a module may
/// stand, darker and bare where none may, and a line between the two.
/// </summary>
/// <remarks>
/// Read off the rendered frame, because none of this is a property to ask — the
/// grid, the ground and the edge are three draw calls and what they add up to is
/// only visible in the pixels. Skia is under the headless platform for these,
/// the same as the tests of draw order in <see cref="NodeEditorTests"/>.
/// </remarks>
public class CanvasEdgeTests : UiTest
{
    private const double Wide = 900;
    private const double Tall = 700;

    private const double Edge = NodeInstance.Extent;

    /// <summary>
    /// An empty patch put down beside the right-hand edge, and the view walked
    /// over to it. Empty so that nothing but ground, grid and the edge itself is
    /// in the frame — a module in the way would be the one thing that could make
    /// a pixel test pass or fail for the wrong reason.
    /// </summary>
    private static (NodeEditor Editor, Window Window) AtTheRightEdge()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, Edge - 4000, 0);

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);

        var from = new Point(Wide / 2, Tall / 2);

        window.MouseDown(from, MouseButton.Middle);
        window.MouseMove(from - new Point(200_000, 0));
        window.MouseUp(from - new Point(200_000, 0), MouseButton.Middle);
        Settle(window);

        return (editor, window);
    }

    /// <summary>Where a graph point has ended up on the control.</summary>
    private static Point OnScreen(NodeEditor editor, Point graph) =>
        editor.GraphToScreen.Transform(graph);

    private static bool Near(Color pixel, Color wanted, int tolerance = 6) =>
        Math.Abs(pixel.R - wanted.R) <= tolerance
        && Math.Abs(pixel.G - wanted.G) <= tolerance
        && Math.Abs(pixel.B - wanted.B) <= tolerance;

    /// <summary>What the window actually drew.</summary>
    private static Color[,] Frame(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered nothing");

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var pixels = new Color[locked.Size.Width, locked.Size.Height];
        var bgra = locked.Format == PixelFormat.Bgra8888;

        for (var y = 0; y < locked.Size.Height; y++)
        for (var x = 0; x < locked.Size.Width; x++)
        {
            var at = y * locked.RowBytes + x * 4;

            pixels[x, y] = bgra
                ? Color.FromRgb(bytes[at + 2], bytes[at + 1], bytes[at + 0])
                : Color.FromRgb(bytes[at + 0], bytes[at + 1], bytes[at + 2]);
        }

        return pixels;
    }

    /// <summary>
    /// Every colour down one column of the frame, which is how a band of ground
    /// is told from a band of ground with lines ruled across it.
    /// </summary>
    private static IEnumerable<Color> Column(Color[,] pixels, int x)
    {
        for (var y = 0; y < pixels.GetLength(1); y++) yield return pixels[x, y];
    }

    [AvaloniaFact]
    public void Past_the_edge_the_ground_is_not_the_canvas()
    {
        var (editor, window) = AtTheRightEdge();

        var wall = (int)OnScreen(editor, new Point(Edge, 0)).X;
        var pixels = Frame(window);

        wall.ShouldBeInRange(4, (int)Wide - 40, "the edge should be on screen");

        // Well clear of the line itself, on both sides of it.
        Near(pixels[wall - 20, 10], Colors.Canvas).ShouldBeTrue("inside should be canvas");
        Near(pixels[wall + 20, 10], Colors.Edge).ShouldBeTrue("outside should be the ground past it");
    }

    [AvaloniaFact]
    public void The_grid_stops_at_the_edge()
    {
        var (editor, window) = AtTheRightEdge();

        var wall = (int)OnScreen(editor, new Point(Edge, 0)).X;
        var pixels = Frame(window);

        // Inside, a full-height column somewhere has grid lines crossing it.
        Column(pixels, wall - 20).Any(c => !Near(c, Colors.Canvas))
            .ShouldBeTrue("the canvas should still be ruled");

        // Outside, every column is bare ground from top to bottom.
        for (var x = wall + 8; x < (int)Wide - 2; x++)
            Column(pixels, x).ShouldAllBe(c => Near(c, Colors.Edge),
                $"column {x - wall} past the edge should have nothing drawn on it");
    }

    [AvaloniaFact]
    public void The_edge_is_ruled_brighter_than_the_grid()
    {
        var (editor, window) = AtTheRightEdge();

        var wall = (int)OnScreen(editor, new Point(Edge, 0)).X;
        var pixels = Frame(window);

        // The line is two wide and lands where it lands, so this looks in a
        // narrow band around the coordinate rather than at one column.
        var found = Enumerable.Range(wall - 2, 5)
            .SelectMany(x => Column(pixels, x))
            .Any(c => Near(c, Colors.Separator));

        found.ShouldBeTrue("the edge of the canvas should be drawn");
    }

    /// <summary>
    /// Away from the edges nothing changes: the canvas fills the view, and none
    /// of the ground past it is anywhere in the frame.
    /// </summary>
    /// <remarks>
    /// Read along two strips near the top and the bottom rather than down whole
    /// columns, to keep the sink out of it. A module's outline is within a few
    /// parts of the ground past the canvas — they are the two darkest things in
    /// the palette — so a column through one would answer this question with a
    /// node border.
    /// </remarks>
    [AvaloniaFact]
    public void In_the_middle_the_canvas_fills_the_view()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);

        var pixels = Frame(window);

        foreach (var y in (int[])[4, (int)Tall - 5])
        for (var x = 0; x < (int)Wide; x++)
            Near(pixels[x, y], Colors.Edge).ShouldBeFalse(
                $"({x}, {y}) should be canvas, not the ground past it");
    }
}
