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
/// A module cannot be dragged off the canvas. It stops at the edge, and a
/// selection dragged into one stops there in the shape it was picked up in.
/// </summary>
/// <remarks>
/// The coordinate clamps itself, so a module can never be lost altogether
/// whatever the drag did — see <c>NodeBoundsTests</c>. Two things here are what
/// that clamp cannot do on its own.
/// <para>
/// It stops the corner rather than the module: what a coordinate names is the
/// top left, and a module whose corner is on the edge stands entirely on the far
/// side of the line. So the drag measures the body, which only the view can.
/// </para>
/// <para>
/// And it stops each module separately: the gesture is cut back to what the one
/// nearest an edge can take, so the rest of the selection is cut back by the
/// same amount. Clamp them one at a time and a group holds together until it
/// meets the edge and then flattens against it, which is a selection nothing
/// puts back.
/// </para>
/// </remarks>
public class DragBoundsTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    /// <summary>
    /// The furthest a module's corner may go: the edge of the canvas, less the
    /// module itself, since what has to fit inside is the whole of it.
    /// </summary>
    private static readonly double Wall = NodeInstance.Extent - NodeGeometry.Width;

    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    /// <summary>The middle of a module's title bar — somewhere no socket is.</summary>
    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static void Click(
        NodeEditor editor, Window window, Point graph, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = Screen(editor, window, graph);

        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Settle(window);
    }

    /// <summary>
    /// Drags whatever is under <paramref name="fromGraph"/> by a vector given in
    /// graph units. Both ends go through the same transform, so the gesture is
    /// exactly that vector however the view happens to be zoomed.
    /// </summary>
    private static void DragBy(NodeEditor editor, Window window, Point fromGraph, Vector by)
    {
        var from = Screen(editor, window, fromGraph);
        var to = Screen(editor, window, fromGraph + by);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// Two modules near the right-hand edge and a sink beside them, so that
    /// framing the patch on opening puts all three on screen at a readable zoom
    /// rather than fitting the origin in as well.
    /// </summary>
    private static Patch AtTheEdge(out NodeInstance near, out NodeInstance behind)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        near = builder.Add("value", Wall - 60, 0);
        behind = builder.Add("time", Wall - 360, 0);
        builder.Add(NodeCatalog.OutputTypeId, Wall - 900, 0);

        return builder.Patch;
    }

    [AvaloniaFact]
    public void A_module_dragged_past_the_edge_stops_on_it()
    {
        var patch = AtTheEdge(out var near, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(near));
        DragBy(editor, window, Body(near), new Vector(500, 0));

        near.X.ShouldBe(Wall);
    }

    /// <summary>
    /// The whole of the module, not the corner it is measured from. Stopping the
    /// corner on the edge left the body out on the far side of the line, which is
    /// the one thing the line is drawn to say cannot happen.
    /// </summary>
    [AvaloniaFact]
    public void A_module_dragged_into_a_corner_ends_up_wholly_inside()
    {
        var patch = AtTheEdge(out var near, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(near));
        DragBy(editor, window, Body(near), new Vector(4000, 4000));

        var def = NodeCatalog.BuiltIn.Require(near.TypeId);
        var body = NodeGeometry.Bounds(near, def);

        body.Right.ShouldBeLessThanOrEqualTo(NodeInstance.Extent + 0.001);
        body.Bottom.ShouldBeLessThanOrEqualTo(NodeInstance.Extent + 0.001);
    }

    /// <summary>
    /// A patch that arrives with a module already hanging off an edge — an older
    /// file, a paste from a larger document — is put right as it is shown, rather
    /// than waiting for somebody to drag the module before it comes inside.
    /// </summary>
    [AvaloniaFact]
    public void A_module_arriving_outside_is_brought_in()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        // The corner on the edge, which is as far out as a coordinate can be:
        // the body is then a whole module past it.
        var hanging = builder.Add("value", NodeInstance.Extent, NodeInstance.Extent);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        Editing(builder.Patch);

        var def = NodeCatalog.BuiltIn.Require(hanging.TypeId);
        var body = NodeGeometry.Bounds(hanging, def);

        body.Right.ShouldBeLessThanOrEqualTo(NodeInstance.Extent + 0.001);
        body.Bottom.ShouldBeLessThanOrEqualTo(NodeInstance.Extent + 0.001);
    }

    /// <summary>
    /// The whole point of cutting the gesture rather than the positions: the two
    /// modules are 300 apart when picked up and 300 apart when put down, however
    /// hard the drag pushes at the edge.
    /// </summary>
    [AvaloniaFact]
    public void A_selection_dragged_into_the_edge_keeps_its_shape()
    {
        var patch = AtTheEdge(out var near, out var behind);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(near));
        Click(editor, window, Body(behind), RawInputModifiers.Control);

        editor.SelectedNodes.Count.ShouldBe(2, "both modules should be selected");

        DragBy(editor, window, Body(near), new Vector(500, 0));

        near.X.ShouldBe(Wall);
        behind.X.ShouldBe(Wall - 300);
    }

    /// <summary>
    /// Each axis is cut on its own, so a module already against one edge still
    /// slides along it rather than being stuck in place by the axis that cannot
    /// move.
    /// </summary>
    [AvaloniaFact]
    public void A_module_on_the_edge_still_slides_along_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var stuck = builder.Add("value", Wall, 0);
        builder.Add(NodeCatalog.OutputTypeId, Wall - 600, 0);

        var (editor, window) = Editing(builder.Patch);

        Click(editor, window, Body(stuck));
        DragBy(editor, window, Body(stuck), new Vector(200, 150));

        stuck.X.ShouldBe(Wall);
        stuck.Y.ShouldBe(150, 0.001);
    }

    [AvaloniaFact]
    public void A_module_dragged_back_off_the_edge_moves_normally()
    {
        var patch = AtTheEdge(out var near, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, Body(near));
        DragBy(editor, window, Body(near), new Vector(-400, 0));

        near.X.ShouldBe(Wall - 460, 0.001);
    }
}
