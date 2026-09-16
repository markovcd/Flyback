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
/// once the button comes back up.
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

    [AvaloniaFact]
    public void A_wire_survives_a_pan_taken_in_the_middle_of_the_drag()
    {
        var (editor, window, source, fed) = Editing();

        var from = NodeGeometry.OutputPort(source, 0);
        window.MouseDown(OnScreen(editor, from), MouseButton.Left);
        window.MouseMove(OnScreen(editor, from) + new Point(30, 20));
        Settle(window);

        // A pan taken mid-drag does not drop the wire: it is put on hold and
        // picked back up once the middle button comes back up.
        var panFrom = new Point(Wide / 2, Tall / 2);
        var panTo = panFrom - new Point(140, 0);

        window.MouseDown(panFrom, MouseButton.Middle);
        window.MouseMove(panTo);
        Settle(window);
        window.MouseUp(panTo, MouseButton.Middle);
        Settle(window);

        // Dropped where the target now sits on screen, after the pan.
        var to = NodeGeometry.InputPort(fed, NodeCatalog.BuiltIn.Require(fed.TypeId), 0);
        window.MouseMove(OnScreen(editor, to));
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
        window.MouseMove(OnScreen(editor, from) + new Point(30, 20));
        Settle(window);

        var before = editor.GraphToScreen.Invert().Transform(new Point(0, 0));

        var panFrom = new Point(Wide / 2, Tall / 2);
        var panTo = panFrom - new Point(140, 0);

        window.MouseDown(panFrom, MouseButton.Middle);
        window.MouseMove(panTo);
        Settle(window);

        var after = editor.GraphToScreen.Invert().Transform(new Point(0, 0));
        Math.Abs(after.X - before.X).ShouldBeGreaterThan(50, "the view should have panned");

        window.MouseUp(panTo, MouseButton.Middle);
        window.MouseUp(OnScreen(editor, from) + new Point(30, 20), MouseButton.Left);
        Settle(window);
    }
}
