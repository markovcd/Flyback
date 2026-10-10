using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Shouldly;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tests.Canvas;

public partial class NodeEditorTests
{
    // --- reading the pixels -------------------------------------------------

    /// <summary>
    /// How many pixels of wire are visible inside a module's body. Inset off its
    /// own outline, which is drawn in a color of its own and would otherwise be
    /// counted as part of what is on top of it. Matched almost exactly, because
    /// antialiased label text passes through nearby grays.
    /// </summary>
    private static int WirePixelsOver(NodeEditor editor, Window window, NodeInstance node)
    {
        var def = NodeCatalog.BuiltIn.Require(node.TypeId);
        var bounds = Geometry.Bounds(node, def).Deflate(6);

        var topLeft = Screen(editor, window, bounds.TopLeft);
        var bottomRight = Screen(editor, window, bounds.BottomRight);

        var pixels = Frame(window);
        var count = 0;

        for (var y = (int)Math.Ceiling(topLeft.Y); y < (int)bottomRight.Y; y++)
            for (var x = (int)Math.Ceiling(topLeft.X); x < (int)bottomRight.X; x++)
                if (Within(pixels, x, y) && Near(pixels[x, y], Colors.ScalarPort, 2))
                    count++;

        return count;
    }

    /// <summary>
    /// How many pixels down one column of the canvas the wire covers — its
    /// thickness, read off the picture rather than off the pen that drew it.
    /// </summary>
    /// <remarks>
    /// A column with no module on it, so everything light in it is wire: the
    /// canvas and both weights of grid line are far darker than this, and the
    /// wire clears it at either opacity.
    /// </remarks>
    private static int WireWidth(NodeEditor editor, Window window, double graphX)
    {
        const byte lit = 120;

        var column = (int)Math.Round(Screen(editor, window, new Point(graphX, 0)).X);
        var pixels = Frame(window);
        var count = 0;

        for (var y = 0; y < pixels.GetLength(1); y++)
            if (Within(pixels, column, y) && pixels[column, y].R >= lit)
                count++;

        return count;
    }

    private static bool Within(Color[,] pixels, int x, int y) =>
        x >= 0 && y >= 0 && x < pixels.GetLength(0) && y < pixels.GetLength(1);

    /// <summary>
    /// Close enough to be that color. A tolerance rather than equality because
    /// a stroke is antialiased even down its middle, and wide enough only to
    /// cover that — a socket label is three times this far from a wire.
    /// </summary>
    private static bool Near(Color pixel, Color wanted, int tolerance = 8) =>
        Math.Abs(pixel.R - wanted.R) <= tolerance
        && Math.Abs(pixel.G - wanted.G) <= tolerance
        && Math.Abs(pixel.B - wanted.B) <= tolerance;

    /// <summary>
    /// What the window actually drew. Skia is under the headless platform for
    /// this: a draw order is not a thing any property exposes, so the only place
    /// to read it is off the frame.
    /// </summary>
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
}
