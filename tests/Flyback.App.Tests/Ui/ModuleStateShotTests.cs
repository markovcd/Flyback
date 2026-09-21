using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Three ways a module is drawn differently from plain and unselected: switched
/// off (ADR-0117), selected, and carrying the tag the assistant panel puts on a
/// module it was not told about (<see cref="NodeEditor.Undescribed"/>).
/// </summary>
/// <remarks>
/// Each test draws the same one-node patch twice, changes one thing about it,
/// and reads the rendered pixels back rather than asserting only that painting
/// did not throw — the same reason <see cref="QrCodeTests"/> decodes what it
/// drew instead of trusting the encoder.
/// </remarks>
public class ModuleStateShotTests : UiTest
{
    private const double Across = 300, Down = 60;
    private const double Wide = 900, Tall = 500;

    [AvaloniaFact]
    public void A_switched_off_module_is_drawn_toward_the_canvas()
    {
        var (editor, window, node, def) = Built();
        var header = HeaderPoint(node, def);

        var on = At(window, editor, header);

        editor.Select(node.Id);
        editor.SwitchSelected();
        Settle(window);

        var off = At(window, editor, header);

        off.ShouldNotBe(on, "switching a module off should change how its header is drawn");

        // Faint means closer to what is behind it, the canvas — not a different
        // color, since the category accent is still how the canvas is read at a
        // glance (ADR-0116).
        Distance(off, Colors.Canvas).ShouldBeLessThan(
            Distance(on, Colors.Canvas),
            "a switched-off module's header should read closer to the canvas than an on one's");
    }

    [AvaloniaFact]
    public void A_selected_module_is_drawn_brighter()
    {
        var (editor, window, node, def) = Built();
        var floor = FloorPoint(node, def);

        var unselected = At(window, editor, floor);

        editor.Select(node.Id);
        Settle(window);

        var selected = At(window, editor, floor);

        selected.ShouldNotBe(unselected, "selecting a module should change its wash");
        Luma(selected).ShouldBeGreaterThan(Luma(unselected), "a selected module's wash should be brighter than an unselected one's");
    }

    [AvaloniaFact]
    public void A_module_the_assistant_was_not_told_about_carries_a_tag()
    {
        var (editor, window, node, def) = Built();

        // Where the tag sits: the right of the header, clear of the title on a
        // module named this short.
        var bounds = NodeGeometry.Bounds(node, def);
        var tagArea = new Rect(bounds.Right - 30, bounds.Y, 26, NodeGeometry.HeaderHeight);

        var before = Brightest(window, editor, tagArea);

        editor.Undescribed = new HashSet<string> { def.TypeId };
        Settle(window);

        var after = Brightest(window, editor, tagArea);

        after.ShouldBeGreaterThan(
            before, "a module the assistant was not told about should carry a visible tag in its header");
    }

    private (NodeEditor Editor, Window Window, NodeInstance Node, NodeDef Def) Built()
    {
        var catalog = NodeCatalog.BuiltIn;
        var builder = new PatchBuilder(catalog);
        var node = builder.Add("osc.sine", Across, Down);
        var def = catalog.Require("osc.sine");

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);

        // Again now the control has a size — see PatchShotTests.
        editor.FrameAll();
        Settle(window);

        return (editor, window, node, def);
    }

    /// <summary>The middle of the header band, away from the title and the tag.</summary>
    private static Point HeaderPoint(NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);

        return new Point(bounds.X + bounds.Width * 0.3, bounds.Y + NodeGeometry.HeaderHeight * 0.5);
    }

    /// <summary>Near the body's floor, where the wash is almost entirely the ground rather than the accent.</summary>
    private static Point FloorPoint(NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);

        return new Point(bounds.X + 10, bounds.Bottom - 6);
    }

    private static double Distance(Color a, Color b) =>
        Math.Sqrt(Sq(a.R - b.R) + Sq(a.G - b.G) + Sq(a.B - b.B));

    private static double Sq(int value) => value * (double)value;

    private static double Luma(Color color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    /// <summary>The color at one point in graph space, read back off the rendered frame.</summary>
    private static Color At(Window window, NodeEditor editor, Point graph)
    {
        var screen = graph.Transform(editor.GraphToScreen);

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var locked = frame.Lock();

        var scaleX = locked.Size.Width / window.Bounds.Width;
        var scaleY = locked.Size.Height / window.Bounds.Height;

        var x = Math.Clamp((int)(screen.X * scaleX), 0, locked.Size.Width - 1);
        var y = Math.Clamp((int)(screen.Y * scaleY), 0, locked.Size.Height - 1);

        return Pixel(locked, x, y);
    }

    /// <summary>The brightest pixel under one region of graph space — enough to notice a small pale tag.</summary>
    private static int Brightest(Window window, NodeEditor editor, Rect graph)
    {
        var screen = graph.TransformToAABB(editor.GraphToScreen);

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var locked = frame.Lock();

        var scaleX = locked.Size.Width / window.Bounds.Width;
        var scaleY = locked.Size.Height / window.Bounds.Height;

        var x0 = Math.Clamp((int)(screen.X * scaleX), 0, locked.Size.Width - 1);
        var y0 = Math.Clamp((int)(screen.Y * scaleY), 0, locked.Size.Height - 1);
        var x1 = Math.Clamp((int)(screen.Right * scaleX), 0, locked.Size.Width - 1);
        var y1 = Math.Clamp((int)(screen.Bottom * scaleY), 0, locked.Size.Height - 1);

        var brightest = 0;

        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var color = Pixel(locked, x, y);

            brightest = Math.Max(brightest, color.R + color.G + color.B);
        }

        return brightest;
    }

    private static Color Pixel(ILockedFramebuffer locked, int x, int y)
    {
        var bytes = new byte[4];
        Marshal.Copy(locked.Address + y * locked.RowBytes + x * 4, bytes, 0, 4);

        var blue = locked.Format == PixelFormat.Bgra8888 ? 0 : 2;

        return Color.FromRgb(bytes[2 - blue], bytes[1], bytes[blue]);
    }
}
