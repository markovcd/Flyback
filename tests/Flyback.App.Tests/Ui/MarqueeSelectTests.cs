using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The rubber band: dragging the left button across empty canvas selects what it
/// sweeps.
/// </summary>
/// <remarks>
/// Panning is the middle and right buttons alone — so half of what is below is
/// about the band doing what it should, and half is about the two buttons that
/// must not do it too.
/// </remarks>
public class MarqueeSelectTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    /// <summary>
    /// A row of three, spaced so a band can take any one, any two, or all of
    /// them — and the sink far enough off to be reached only on purpose.
    /// </summary>
    private static Patch Row(out NodeInstance a, out NodeInstance b, out NodeInstance c)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        a = builder.Add("value", 0, 0);
        b = builder.Add("time", 0, 200);
        c = builder.Add("coord", 0, 400);
        builder.Add(NodeCatalog.OutputTypeId, 700, 200);

        return builder.Patch;
    }

    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    private static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    /// <summary>Presses at one point in graph space, moves to another, and lets go.</summary>
    private static void Sweep(
        NodeEditor editor,
        Window window,
        Point fromGraph,
        Point toGraph,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(Screen(editor, window, fromGraph), button, modifiers);
        window.MouseMove(Screen(editor, window, toGraph), modifiers);
        window.MouseUp(Screen(editor, window, toGraph), button, modifiers);
        Settle(window);
    }

    private static void Click(NodeEditor editor, Window window, NodeInstance node)
    {
        var at = Screen(editor, window, new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static string[] Selected(NodeEditor editor) =>
        [.. editor.SelectedNodes.Select(n => n.TypeId).Order()];

    // --- what the band takes ------------------------------------------------

    [AvaloniaFact]
    public void Dragging_across_modules_selects_them()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        // From above and left of the first, to below and right of the second.
        Sweep(editor, window, new Point(-40, -40), new Point(240, 300));

        Selected(editor).ShouldBe(["time", "value"]);
    }

    /// <summary>
    /// Started from any of its four corners. Dragging up or left puts the second
    /// point above or before the first, which a rectangle built by subtracting
    /// one from the other reads as a negative width — and a negative width
    /// intersects nothing, so the band would look like it did nothing.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(-40, -40, 240, 300)]
    [InlineData(240, 300, -40, -40)]
    [InlineData(240, -40, -40, 300)]
    [InlineData(-40, 300, 240, -40)]
    public void The_band_works_from_every_corner(double x1, double y1, double x2, double y2)
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        Sweep(editor, window, new Point(x1, y1), new Point(x2, y2));

        Selected(editor).ShouldBe(["time", "value"]);
    }

    [AvaloniaFact]
    public void A_module_the_band_never_reaches_is_left_alone()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300));

        Selected(editor).ShouldNotContain("coord");
        Selected(editor).ShouldNotContain(NodeCatalog.OutputTypeId);
    }

    /// <summary>
    /// Touching is enough. Having to swallow a module whole would mean reaching
    /// past the ends of a row to take it, which is not what the gesture looks
    /// like it should do.
    /// </summary>
    [AvaloniaFact]
    public void Clipping_a_corner_is_enough_to_take_a_module()
    {
        var patch = Row(out var a, out _, out _);
        var (editor, window) = Editing(patch);

        // A small band overlapping only the bottom-right corner of the first.
        var corner = new Point(a.X + NodeGeometry.Width - 6, a.Y + 6);

        Sweep(editor, window, corner, corner + new Vector(80, 80));

        Selected(editor).ShouldBe(["value"]);
    }

    [AvaloniaFact]
    public void The_band_shows_what_it_has_before_the_button_comes_up()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        window.MouseDown(Screen(editor, window, new Point(-40, -40)), MouseButton.Left);
        window.MouseMove(Screen(editor, window, new Point(240, 300)));
        Settle(window);

        Selected(editor).ShouldBe(["time", "value"], "the selection follows the band as it is drawn");

        window.MouseUp(Screen(editor, window, new Point(240, 300)), MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// Sweeping back off a module takes it out again. It only can if what the
    /// band adds to is the selection as it was when the band started, rather
    /// than the selection as the last frame left it.
    /// </summary>
    [AvaloniaFact]
    public void Sweeping_back_off_a_module_gives_it_up()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        window.MouseDown(Screen(editor, window, new Point(-40, -40)), MouseButton.Left);

        window.MouseMove(Screen(editor, window, new Point(240, 300)));
        Settle(window);
        Selected(editor).ShouldBe(["time", "value"]);

        // Pulled back so only the first is under it.
        window.MouseMove(Screen(editor, window, new Point(240, 60)));
        Settle(window);
        Selected(editor).ShouldBe(["value"]);

        window.MouseUp(Screen(editor, window, new Point(240, 60)), MouseButton.Left);
        Settle(window);
    }

    // --- what it does to what was already selected ---------------------------

    [AvaloniaFact]
    public void A_plain_band_replaces_whatever_was_selected()
    {
        var patch = Row(out _, out _, out var c);
        var (editor, window) = Editing(patch);

        Click(editor, window, c);
        Selected(editor).ShouldBe(["coord"]);

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300));

        Selected(editor).ShouldBe(["time", "value"]);
    }

    [AvaloniaFact]
    public void A_band_with_the_modifier_held_adds_to_it()
    {
        var patch = Row(out _, out _, out var c);
        var (editor, window) = Editing(patch);

        Click(editor, window, c);

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), modifiers: RawInputModifiers.Control);

        Selected(editor).ShouldBe(["coord", "time", "value"]);
    }

    [AvaloniaFact]
    public void A_band_over_nothing_clears_the_selection()
    {
        var patch = Row(out var a, out _, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, a);
        Selected(editor).ShouldNotBeEmpty();

        // Well clear of every module, in the gap before the sink.
        Sweep(editor, window, new Point(400, 40), new Point(600, 160));

        Selected(editor).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Drawing_a_band_moves_nothing()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300));

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);

        editor.CanUndo.ShouldBeFalse("selecting is not an edit");
    }

    // --- the buttons that did not change --------------------------------------

    [AvaloniaFact]
    public void The_middle_button_still_pans_and_selects_nothing()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        var before = editor.GraphToScreen.Transform(new Point(0, 0));

        // The same drag that would draw a band with the left button.
        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Middle);

        editor.SelectedNodes.ShouldBeEmpty("the middle button pans, it does not select");
        editor.GraphToScreen.Transform(new Point(0, 0)).ShouldNotBe(before, "the view should have moved");
    }

    /// <summary>
    /// The right button opens the module list, so it cannot also stay silent
    /// for a click meant as a pan. Panning is the middle button and nothing
    /// else.
    /// </summary>
    [AvaloniaFact]
    public void The_right_button_no_longer_pans()
    {
        var patch = Row(out _, out _, out _);
        var (editor, window) = Editing(patch);

        var before = editor.GraphToScreen.Transform(new Point(0, 0));

        Sweep(editor, window, new Point(-40, -40), new Point(240, 300), MouseButton.Right);

        editor.GraphToScreen.Transform(new Point(0, 0)).ShouldBe(before, "the view should not have moved");
        editor.SelectedNodes.ShouldBeEmpty();
    }

    /// <summary>
    /// Pressing a module still drags the module. The band is for empty canvas,
    /// and taking that over would have taken dragging with it.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_a_module_still_drags_the_module()
    {
        var patch = Row(out var a, out _, out _);
        var (editor, window) = Editing(patch);

        var body = new Point(a.X + NodeGeometry.Width / 2, a.Y + NodeGeometry.HeaderHeight / 2);

        Sweep(editor, window, body, body + new Vector(90, 30));

        a.X.ShouldBe(90, 0.001);
        a.Y.ShouldBe(30, 0.001);
    }
}
