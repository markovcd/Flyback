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
    // --- what a drag looks like ---------------------------------------------

    /// <summary>
    /// A source wired across the canvas to the Output, with a third module
    /// sitting on top of the wire. The obstacle is added last, so the ordinary
    /// painting order puts it over the wire — which is the thing dragging has to
    /// overturn.
    /// </summary>
    private static Patch Crossing(out NodeInstance source, out NodeInstance obstacle)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        source = builder.Add("value", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 420, 200);
        obstacle = builder.Add("math.add", 230, 100);

        builder.Wire(source, 0, sink, NodeCatalog.OutputColorPort);

        return builder.Patch;
    }

    /// <summary>
    /// Where the wire runs over open canvas rather than over a module: past the
    /// source's right edge and short of the obstacle's left one.
    /// </summary>
    private const double OpenColumn = 213;

    /// <summary>
    /// Pressed and not released, which is a module mid-drag. It has not been
    /// moved, so every wire is where it was and the only thing that can differ
    /// between the two frames is how they are drawn.
    /// </summary>
    private static void HoldDown(NodeEditor editor, Window window, NodeInstance node)
    {
        window.MouseDown(Screen(editor, window, Body(node)), MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void A_wire_is_hidden_by_a_module_it_passes_behind()
    {
        var patch = Crossing(out _, out var obstacle);
        var (editor, window) = Editing(patch);

        WirePixelsOver(editor, window, obstacle).ShouldBe(0);
    }

    /// <summary>
    /// And is not, once the module it belongs to is being moved. Which wire goes
    /// where is the whole question a drag asks, and it cannot be answered by a
    /// wire that disappears behind the third module along.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_module_brings_its_own_wires_in_front_of_the_others()
    {
        var patch = Crossing(out var source, out var obstacle);
        var (editor, window) = Editing(patch);

        HoldDown(editor, window, source);

        WirePixelsOver(editor, window, obstacle).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Drawn heavier as well as in front, measured where nothing else is: the
    /// same wire, unmoved, covering more of the column it crosses than it did at
    /// rest.
    /// </summary>
    [AvaloniaFact]
    public void And_draws_them_heavier_than_they_rest_at()
    {
        var patch = Crossing(out var source, out _);
        var (editor, window) = Editing(patch);

        var resting = WireWidth(editor, window, OpenColumn);
        resting.ShouldBeGreaterThan(0, "the wire has to be visible at rest as well");

        HoldDown(editor, window, source);

        WireWidth(editor, window, OpenColumn).ShouldBeGreaterThan(resting);
    }

    /// <summary>
    /// A picked module casts a shadow on the canvas under it, read as the strip
    /// below its bottom edge getting darker than it was at rest.
    /// </summary>
    [AvaloniaFact]
    public void A_picked_module_casts_a_shadow_under_it()
    {
        var patch = Crossing(out _, out var obstacle);
        var (editor, window) = Editing(patch);

        var under = Geometry.Bounds(obstacle, NodeCatalog.BuiltIn.Require(obstacle.TypeId));
        var resting = BrightnessBelow(editor, window, under);

        HoldDown(editor, window, obstacle);

        BrightnessBelow(editor, window, under).ShouldBeLessThan(resting);
    }

    /// <summary>A shut box is held up the same way: shadow under it once picked.</summary>
    [AvaloniaFact]
    public void A_picked_box_casts_a_shadow_under_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var first = builder.Add("osc.sine", 100, 100);
        var second = builder.Add("math.mul", 100, 300);

        var group = builder.Patch.Group([first.Id, second.Id]).ShouldNotBeNull();
        group.Collapsed = true;

        var (editor, window) = Editing(builder.Patch);

        var box = editor.Selection.Scene.Boxes().Single().Bounds;
        var resting = BrightnessBelow(editor, window, box);

        var header = Screen(editor, window, box.TopLeft + new Vector(40, 8));
        window.MouseDown(header, MouseButton.Left);
        Settle(window);

        BrightnessBelow(editor, window, box).ShouldBeLessThan(resting);
    }

    /// <summary>And rises a little: its top edge is drawn above where it sits.</summary>
    [AvaloniaFact]
    public void A_picked_module_is_drawn_a_little_above_where_it_sits()
    {
        var patch = Crossing(out _, out var obstacle);
        var (editor, window) = Editing(patch);

        var above = Screen(editor, window, new Point(obstacle.X + NodeGeometry.Width / 2, obstacle.Y - 1.5));
        var resting = Frame(window)[(int)above.X, (int)above.Y];

        HoldDown(editor, window, obstacle);

        Near(Frame(window)[(int)above.X, (int)above.Y], resting, 2).ShouldBeFalse();
    }

    /// <summary>A module carried across a shut box is drawn over it, not under it.</summary>
    [AvaloniaFact]
    public void A_module_carried_over_a_shut_box_is_drawn_over_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var first = builder.Add("osc.sine", 100, 100);
        var second = builder.Add("math.mul", 100, 300);
        var carried = builder.Add("osc.saw", 500, 100);

        var group = builder.Patch.Group([first.Id, second.Id]).ShouldNotBeNull();
        group.Collapsed = true;

        var (editor, window) = Editing(builder.Patch);

        var box = editor.Selection.Scene.Boxes().Single().Bounds;
        var onto = new Vector(box.X - carried.X, box.Y + 4 - carried.Y);

        // Right of the title and inside the box, where only a header band is drawn.
        var spot = Screen(editor, window, new Point(box.X + NodeGeometry.Width - 25, box.Y + 13));
        var under = Frame(window)[(int)spot.X, (int)spot.Y];

        window.MouseDown(Screen(editor, window, Body(carried)), MouseButton.Left);
        window.MouseMove(Screen(editor, window, Body(carried) + onto));
        Settle(window);

        Near(Frame(window)[(int)spot.X, (int)spot.Y], under, 6).ShouldBeFalse();
    }

    /// <summary>A shut box carried across another is drawn over it, whichever of them is earlier in the patch.</summary>
    [AvaloniaFact]
    public void A_shut_box_carried_over_another_is_drawn_over_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var a1 = builder.Add("osc.sine", 100, 100);
        var a2 = builder.Add("math.mul", 100, 300);
        var b1 = builder.Add("osc.saw", 500, 100);
        var b2 = builder.Add("math.add", 500, 300);

        // Made first, so the ordinary order draws it under the other.
        builder.Patch.Group([a1.Id, a2.Id]).ShouldNotBeNull().Collapsed = true;
        builder.Patch.Group([b1.Id, b2.Id]).ShouldNotBeNull().Collapsed = true;

        var (editor, window) = Editing(builder.Patch);

        var boxes = editor.Selection.Scene.Boxes().Select(box => box.Bounds).ToList();
        var carried = boxes[0];
        var other = boxes[1];
        var onto = new Vector(other.X - carried.X, other.Y - 20 - carried.Y);

        // Across the carried box's bottom edge, which lies inside the other box once it is there.
        var edge = other.Y - 20 + carried.Height - 3;
        var from = Screen(editor, window, new Point(other.X + 40, edge - 4));
        var to = Screen(editor, window, new Point(other.X + 60, edge + 4));

        var before = Frame(window);
        var grabAt = carried.TopLeft + new Vector(40, 8);

        window.MouseDown(Screen(editor, window, grabAt), MouseButton.Left);
        window.MouseMove(Screen(editor, window, grabAt + onto));
        Settle(window);

        var after = Frame(window);
        var changed = 0;

        for (var y = (int)from.Y; y < (int)to.Y; y++)
            for (var x = (int)from.X; x < (int)to.X; x++)
                if (!Near(after[x, y], before[x, y], 6)) changed++;

        changed.ShouldBeGreaterThan(0);
    }

    /// <summary>The summed brightness of a strip of canvas just under a module or box.</summary>
    private static int BrightnessBelow(NodeEditor editor, Window window, Rect bounds)
    {
        var from = Screen(editor, window, new Point(bounds.X + 8, bounds.Bottom + 3));
        var to = Screen(editor, window, new Point(bounds.Right - 8, bounds.Bottom + 8));

        var pixels = Frame(window);
        var sum = 0;

        for (var y = (int)from.Y; y < (int)to.Y; y++)
            for (var x = (int)from.X; x < (int)to.X; x++)
                sum += pixels[x, y].R + pixels[x, y].G + pixels[x, y].B;

        return sum;
    }
}
