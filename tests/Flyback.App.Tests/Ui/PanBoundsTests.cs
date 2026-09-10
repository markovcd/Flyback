using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The view cannot be scrolled off the canvas either. It stops with the edge of the
/// canvas at the edge of the window, whether it was dragged there or zoomed out into
/// a corner.
/// </summary>
/// <remarks>
/// The other half of holding a module inside the canvas: a bound on where things may
/// be put is worth little beside a view that can wander somewhere none of them are.
/// </remarks>
public class PanBoundsTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private const double ReachAcross = NodeEditor.ViewReachAcross;
    private const double ReachDown = NodeEditor.ViewReachDown;

    private static (NodeEditor Editor, Window Window) Editing()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("value", 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 520, 0);

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);

        return (editor, window);
    }

    /// <summary>
    /// What the window is showing, in graph units. Read back through the
    /// editor's own transform, so it is the view the painting uses rather than a
    /// second opinion about it.
    /// </summary>
    private static Rect View(NodeEditor editor)
    {
        var inverse = editor.GraphToScreen.Invert();

        return new Rect(
            inverse.Transform(new Point(0, 0)),
            inverse.Transform(new Point(Wide, Tall)));
    }

    /// <summary>Drags the view with the middle button, by a distance in pixels.</summary>
    private static void PanBy(Window window, Vector by)
    {
        var from = new Point(Wide / 2, Tall / 2);

        window.MouseDown(from, MouseButton.Middle);

        // In steps, the way a hand moves it: a pan held against an edge has to
        // stay there and then come away again cleanly, which one jump would
        // never ask.
        for (var i = 1; i <= 10; i++)
            window.MouseMove(from + by * i / 10);

        window.MouseUp(from + by, MouseButton.Middle);
        Settle(window);
    }

    private static void Wheel(Window window, Point at, double delta)
    {
        window.MouseWheel(at, new Vector(0, delta));
        Settle(window);
    }

    [AvaloniaFact]
    public void Dragging_the_view_off_the_right_stops_at_the_canvas_edge()
    {
        var (editor, window) = Editing();

        // Dragging the view leftwards walks it towards the right-hand edge.
        PanBy(window, new Vector(-200_000, 0));

        View(editor).Right.ShouldBe(ReachAcross, 0.001);
    }

    [AvaloniaFact]
    public void Dragging_the_view_off_the_left_stops_at_the_canvas_edge()
    {
        var (editor, window) = Editing();

        PanBy(window, new Vector(200_000, 0));

        View(editor).Left.ShouldBe(-ReachAcross, 0.001);
    }

    [AvaloniaFact]
    public void Dragging_the_view_off_the_bottom_stops_at_the_canvas_edge()
    {
        var (editor, window) = Editing();

        PanBy(window, new Vector(0, -200_000));

        View(editor).Bottom.ShouldBe(ReachDown, 0.001);
    }

    [AvaloniaFact]
    public void Dragging_the_view_off_the_top_stops_at_the_canvas_edge()
    {
        var (editor, window) = Editing();

        PanBy(window, new Vector(0, 200_000));

        View(editor).Top.ShouldBe(-ReachDown, 0.001);
    }

    /// <summary>
    /// One drag that pushes far past an edge and then comes back a little must
    /// move the view on that first pixel back, rather than spending it paying
    /// off however far it was pushed past.
    /// </summary>
    [AvaloniaFact]
    public void A_drag_comes_away_from_an_edge_immediately()
    {
        var (editor, window) = Editing();
        var from = new Point(Wide / 2, Tall / 2);

        window.MouseDown(from, MouseButton.Middle);

        window.MouseMove(from - new Point(200_000, 0));
        Settle(window);

        var against = View(editor).Right;
        var back = from - new Point(199_880, 0);

        window.MouseMove(back);
        window.MouseUp(back, MouseButton.Middle);
        Settle(window);

        View(editor).Right.ShouldBe(against - 120 / editor.GraphToScreen.M11, 0.001);
    }

    /// <summary>
    /// Zooming out walks the view outwards on both axes at once, which is the
    /// way past a guard that only watched the drag.
    /// </summary>
    [AvaloniaFact]
    public void Zooming_out_in_a_corner_keeps_the_view_inside()
    {
        var (editor, window) = Editing();

        PanBy(window, new Vector(-200_000, -200_000));

        for (var i = 0; i < 20; i++) Wheel(window, new Point(Wide - 1, Tall - 1), -1);

        var view = View(editor);

        view.Right.ShouldBeLessThanOrEqualTo(ReachAcross + 0.001);
        view.Bottom.ShouldBeLessThanOrEqualTo(ReachDown + 0.001);
        view.Left.ShouldBeGreaterThanOrEqualTo(-ReachAcross - 0.001);
        view.Top.ShouldBeGreaterThanOrEqualTo(-ReachDown - 0.001);
    }

    [AvaloniaFact]
    public void Panning_about_inside_the_canvas_is_untouched()
    {
        var (editor, window) = Editing();
        var before = View(editor);

        PanBy(window, new Vector(-150, -90));

        var after = View(editor);

        (after.Left - before.Left).ShouldBe(150 / editor.GraphToScreen.M11, 0.001);
        (after.Top - before.Top).ShouldBe(90 / editor.GraphToScreen.M11, 0.001);
    }

    /// <summary>
    /// A window wide enough to see the whole canvas at the furthest zoom out has
    /// nowhere to pan to, so the canvas sits in the middle of it and stays there.
    /// </summary>
    /// <remarks>
    /// Reachable rather than defensive: the zoom stops at about an eighth, which
    /// puts nearly eight windows' worth of graph units across the view, so
    /// anything past about two thousand pixels wide is already there. Panning at
    /// all in that state must not drag the canvas off centre.
    /// </remarks>
    [AvaloniaFact]
    public void A_window_wider_than_the_canvas_holds_it_in_the_middle()
    {
        const double veryWide = 2600;

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        var editor = new NodeEditor { Width = veryWide, Height = Tall };
        var window = Show(editor, veryWide);

        editor.Patch = builder.Patch;
        Settle(window);

        var at = new Point(veryWide / 2, Tall / 2);
        for (var i = 0; i < 30; i++) window.MouseWheel(at, new Vector(0, -1));
        Settle(window);

        var view = new Rect(
            editor.GraphToScreen.Invert().Transform(new Point(0, 0)),
            editor.GraphToScreen.Invert().Transform(new Point(veryWide, Tall)));

        view.Width.ShouldBeGreaterThan(
            NodeInstance.Across * 2, "the window should be seeing past both edges at once");

        window.MouseDown(at, MouseButton.Middle);
        window.MouseMove(at + new Point(900, 0));
        window.MouseUp(at + new Point(900, 0), MouseButton.Middle);
        Settle(window);

        var after = new Rect(
            editor.GraphToScreen.Invert().Transform(new Point(0, 0)),
            editor.GraphToScreen.Invert().Transform(new Point(veryWide, Tall)));

        after.Center.X.ShouldBe(0, 0.001, "the canvas should still be centred");
    }

    /// <summary>
    /// The view is allowed a little past the canvas, so that the edge can be
    /// seen with ground on the far side of it rather than sitting flush against
    /// the window frame where it would read as part of the window.
    /// </summary>
    [AvaloniaFact]
    public void The_view_reaches_a_little_past_the_canvas()
    {
        NodeEditor.ViewReachAcross.ShouldBeGreaterThan(NodeInstance.Across);
        NodeEditor.ViewReachDown.ShouldBeGreaterThan(NodeInstance.Down);

        var (editor, window) = Editing();

        PanBy(window, new Vector(-200_000, 0));

        View(editor).Right.ShouldBeGreaterThan(
            NodeInstance.Across, "some of the ground past the edge should be reachable");
    }

    /// <summary>
    /// A patch reaching both side edges can be framed whole, which is what the
    /// zoom floor is set by: a module dragged out there has to be findable, and
    /// framing is how it is found.
    /// </summary>
    [AvaloniaFact]
    public void A_patch_reaching_both_edges_can_be_framed()
    {
        const double veryWide = 2600;

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        builder.Add("value", -NodeInstance.Across, 0);
        builder.Add(NodeCatalog.OutputTypeId, NodeInstance.Across, 0);

        var editor = new NodeEditor { Width = veryWide, Height = Tall };
        var window = Show(editor, veryWide);

        editor.Patch = builder.Patch;
        Settle(window);

        editor.FrameAll();
        Settle(window);

        var inverse = editor.GraphToScreen.Invert();

        var view = new Rect(
            inverse.Transform(new Point(0, 0)),
            inverse.Transform(new Point(veryWide, Tall)));

        // The right-hand module is pulled in by its own width on the way in, so
        // the far edge of the canvas is the far edge of the patch.
        view.Left.ShouldBeLessThanOrEqualTo(-NodeInstance.Across);
        view.Right.ShouldBeGreaterThanOrEqualTo(NodeInstance.Across);
    }

    /// <summary>
    /// The cursor while the view is being dragged, and after.
    /// </summary>
    /// <remarks>
    /// A hand rather than the four-way arrow a module drag wears, because a pan
    /// moves no part of the patch — the sheet goes under the pointer and nothing
    /// on it has changed. Given back at the end from where the pointer is
    /// standing rather than merely reset, since a pan usually ends over
    /// something other than what it began over.
    /// </remarks>
    [AvaloniaFact]
    public void Dragging_the_view_shows_a_hand_and_gives_the_cursor_back()
    {
        var (editor, window) = Editing();

        var from = new Point(Wide / 2, Tall / 2);
        var to = from - new Point(160, 0);

        window.MouseMove(from);
        Settle(window);

        var resting = editor.Cursor;

        window.MouseDown(from, MouseButton.Middle);
        window.MouseMove(to);
        Settle(window);

        var panning = editor.Cursor.ShouldNotBeNull();
        panning.ShouldNotBeSameAs(resting, "a pan is not what standing still looks like");

        window.MouseUp(to, MouseButton.Middle);
        Settle(window);

        var after = editor.Cursor;
        after.ShouldNotBeSameAs(panning, "the hand goes when the drag does");

        // And what it went back to is what standing there means, whatever the
        // pan happened to leave under the pointer.
        window.MouseMove(to);
        Settle(window);

        editor.Cursor.ShouldBeSameAs(after);
    }
}
