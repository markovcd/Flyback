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
/// The middle button pans even with a wire already in hand: the drag is put
/// on hold rather than dropped, and picks back up exactly where it left off
/// once the button comes back up. A module in hand is held the same way.
/// </summary>
public class WirePanTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private static (NodeEditor Editor, Window Window, NodeInstance Source, NodeInstance Fed) Editing()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("osc.sine", 40, 40);
        var fed = builder.Add("math.add", 500, 300);
        builder.Add(NodeCatalog.OutputTypeId, 900, 40);

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);

        return (editor, window, source, fed);
    }

    private static Point OnScreen(NodeEditor editor, Point graph) => editor.GraphToScreen.Transform(graph);

    /// <summary>
    /// The left button held throughout, exactly as a real mouse reports it on
    /// every move while a finger stays down — headless <c>MouseMove</c> has no
    /// button of its own and reports none at all unless told.
    /// </summary>
    private const RawInputModifiers LeftHeld = RawInputModifiers.LeftMouseButton;

    private const RawInputModifiers MiddleHeld = RawInputModifiers.MiddleMouseButton;

    private const RawInputModifiers LeftAndMiddleHeld =
        RawInputModifiers.LeftMouseButton | RawInputModifiers.MiddleMouseButton;

    /// <summary>The middle of a module's title bar — somewhere no socket is.</summary>
    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    [AvaloniaFact]
    public void A_wire_survives_a_pan_taken_in_the_middle_of_the_drag()
    {
        var (editor, window, source, fed) = Editing();

        var from = NodeGeometry.OutputPort(source, 0);
        window.MouseDown(OnScreen(editor, from), MouseButton.Left);
        window.MouseMove(OnScreen(editor, from) + new Point(30, 20), LeftHeld);
        Settle(window);

        // A pan taken mid-drag does not drop the wire: it is put on hold and
        // picked back up once the middle button comes back up.
        var panFrom = new Point(Wide / 2, Tall / 2);
        var panTo = panFrom - new Point(140, 0);

        window.MouseDown(panFrom, MouseButton.Middle, LeftHeld);
        window.MouseMove(panTo, LeftAndMiddleHeld);
        Settle(window);
        window.MouseUp(panTo, MouseButton.Middle, LeftHeld);
        Settle(window);

        // Dropped where the target now sits on screen, after the pan.
        var to = NodeGeometry.InputPort(fed, NodeCatalog.BuiltIn.Require(fed.TypeId), 0);
        window.MouseMove(OnScreen(editor, to), LeftHeld);
        window.MouseUp(OnScreen(editor, to), MouseButton.Left);
        Settle(window);

        editor.Patch.IncomingTo(fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(source.Id);
    }

    [AvaloniaFact]
    public void A_pan_taken_mid_wire_drag_actually_moves_the_view()
    {
        var (editor, window, source, _) = Editing();

        var from = NodeGeometry.OutputPort(source, 0);
        window.MouseDown(OnScreen(editor, from), MouseButton.Left);
        window.MouseMove(OnScreen(editor, from) + new Point(30, 20), LeftHeld);
        Settle(window);

        var before = editor.GraphToScreen.Invert().Transform(new Point(0, 0));

        var panFrom = new Point(Wide / 2, Tall / 2);
        var panTo = panFrom - new Point(140, 0);

        window.MouseDown(panFrom, MouseButton.Middle, LeftHeld);
        window.MouseMove(panTo, LeftAndMiddleHeld);
        Settle(window);

        var after = editor.GraphToScreen.Invert().Transform(new Point(0, 0));
        Math.Abs(after.X - before.X).ShouldBeGreaterThan(50, "the view should have panned");

        window.MouseUp(panTo, MouseButton.Middle, LeftHeld);
        window.MouseUp(OnScreen(editor, from) + new Point(30, 20), MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// A pan taken in the middle of moving a module does not cost the move its
    /// place in the history.
    /// </summary>
    /// <remarks>
    /// The drag is held in the canvas's coordinates, which a pan does not move,
    /// so coming back from the pan there is nothing to put right: the release
    /// measures from where the modules were picked up, finds them moved, and
    /// records the step.
    /// </remarks>
    [AvaloniaFact]
    public void A_move_with_a_pan_in_the_middle_of_it_can_be_undone()
    {
        var (editor, window, source, _) = Editing();
        var start = new Point(source.X, source.Y);

        var grab = OnScreen(editor, Body(source));
        var carried = grab + new Point(120, 60);

        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(carried, LeftHeld);
        Settle(window);

        var panTo = carried - new Point(140, 0);

        window.MouseDown(carried, MouseButton.Middle, LeftHeld);
        window.MouseMove(panTo, LeftAndMiddleHeld);
        Settle(window);
        window.MouseUp(panTo, MouseButton.Middle, LeftHeld);
        window.MouseUp(panTo, MouseButton.Left);
        Settle(window);

        new Point(source.X, source.Y).ShouldNotBe(start, "the module was moved");

        editor.CanUndo.ShouldBeTrue("a module that moved is a step to take back");
        editor.IsModified.ShouldBeTrue("and work that has not been saved");
    }

    /// <summary>
    /// The same, with the buttons let go the other way round: left first, the
    /// pan's own button last. The pan's release is then the last there will be,
    /// and the move on hold ends there as a step.
    /// </summary>
    [AvaloniaFact]
    public void A_move_is_recorded_when_the_pan_outlasts_it()
    {
        var (editor, window, source, _) = Editing();
        var start = new Point(source.X, source.Y);

        var grab = OnScreen(editor, Body(source));
        var carried = grab + new Point(120, 60);

        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(carried, LeftHeld);
        Settle(window);

        var panTo = carried - new Point(140, 0);

        window.MouseDown(carried, MouseButton.Middle, LeftHeld);
        window.MouseMove(panTo, LeftAndMiddleHeld);
        Settle(window);
        window.MouseUp(panTo, MouseButton.Left, MiddleHeld);
        window.MouseUp(panTo, MouseButton.Middle);
        Settle(window);

        new Point(source.X, source.Y).ShouldNotBe(start, "the module was moved");

        editor.CanUndo.ShouldBeTrue("a module that moved is a step to take back");
    }
}
