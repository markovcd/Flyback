using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Escape part way through a gesture, which puts back what the gesture had
/// already changed.
/// </summary>
/// <remarks>
/// Two of the three have changed something before the hand has chosen anything,
/// which is what makes this more than stopping: a wire lifted off an input is off
/// it from the press, and a rubber band has replaced the selection on its first
/// move. The release that follows must then complete nothing, or the abort would
/// only have been a pause.
/// </remarks>
public class AbortGestureTests : UiTest
{
    private const string Sine = "osc.sine";
    private const string Add = "math.add";

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    private static Point Input(NodeInstance node, int index) =>
        Geometry.InputPort(node, NodeCatalog.BuiltIn.Require(node.TypeId), index);

    private static Point Output(NodeInstance node, int index = 0) =>
        NodeGeometry.OutputPort(node, index);

    private static void Escape(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
    }

    /// <summary>An oscillator, something to feed, and the Output they need.</summary>
    private (MainWindow Window, NodeInstance Source, NodeInstance Fed) Open()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var source = b.Add(Sine, 40, 40);
        var fed = b.Add(Add, 400, 40);

        Editor(window).History.Open(b.Patch);
        Settle(window);

        return (window, source, fed);
    }

    /// <summary>
    /// The case the key is really for. Lifting a wire off an input disconnects it
    /// at the press, so backing out has to put it back — and backing out used to
    /// cost the module list being dismissed and then a Ctrl+Z.
    /// </summary>
    [AvaloniaFact]
    public void A_lifted_wire_goes_back_on_the_socket_it_came_off()
    {
        var (window, source, fed) = Open();
        var editor = Editor(window);

        editor.History.Patch.Connect(source.Id, 0, fed.Id, 0);
        editor.History.Record();
        Settle(window);

        window.MouseDown(OnWindow(window, Input(fed, 0)), MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(fed.X + 120, fed.Y + 200)));
        Settle(window);

        editor.History.Patch.Connections.ShouldBeEmpty("the wire is in the hand, not on the socket");

        Escape(window);

        var back = editor.History.Patch.Connections.ShouldHaveSingleItem();
        back.SourceNode.ShouldBe(source.Id);
        back.TargetNode.ShouldBe(fed.Id);
        back.TargetPort.ShouldBe(0);

        editor.Gestures.Gesturing.ShouldBeFalse();

        // Letting go over another socket now patches nothing: the gesture the
        // button began is over, so this is a release and not a choice.
        var elsewhere = OnWindow(window, Input(fed, 1));

        window.MouseMove(elsewhere);
        window.MouseUp(elsewhere, MouseButton.Left);
        Settle(window);

        editor.History.Patch.Connections.ShouldHaveSingleItem().TargetPort.ShouldBe(0);

        // And the whole lift cost no step, so one undo reaches past it to the
        // wire being made in the first place.
        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Connections.ShouldBeEmpty();
    }

    /// <summary>A wire drawn new has nothing to put back, and is simply dropped.</summary>
    [AvaloniaFact]
    public void A_wire_being_drawn_is_dropped()
    {
        var (window, source, fed) = Open();
        var editor = Editor(window);

        window.MouseDown(OnWindow(window, Output(source)), MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(source.X + 200, source.Y + 40)));
        Settle(window);

        editor.Gestures.Gesturing.ShouldBeTrue("a wire is being drawn");

        Escape(window);

        editor.Gestures.Gesturing.ShouldBeFalse();

        var socket = OnWindow(window, Input(fed, 0));

        window.MouseMove(socket);
        window.MouseUp(socket, MouseButton.Left);
        Settle(window);

        editor.History.Patch.Connections.ShouldBeEmpty("the wire was let go of, not plugged in");
        editor.History.CanUndo.ShouldBeFalse("and nothing about the patch changed");
    }

    /// <summary>
    /// A module drag has moved the modules and recorded nothing, so ending it
    /// without putting them back would leave them wherever the hand had got to.
    /// </summary>
    [AvaloniaFact]
    public void A_module_dragged_and_backed_out_of_goes_back_where_it_was()
    {
        var (window, source, _) = Open();
        var editor = Editor(window);

        var was = (source.X, source.Y);

        window.MouseDown(OnWindow(window, Body(source)), MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(source.X + 160, source.Y + 90)));
        Settle(window);

        var dragged = editor.History.Patch.Find(source.Id).ShouldNotBeNull();
        (dragged.X, dragged.Y).ShouldNotBe(was, "the module is in the hand");

        Escape(window);

        (dragged.X, dragged.Y).ShouldBe(was);
        editor.Gestures.Gesturing.ShouldBeFalse();

        // Moving on with the button still down carries nothing, and the release
        // records no step.
        window.MouseMove(OnWindow(window, new Point(source.X + 400, source.Y + 200)));
        window.MouseUp(OnWindow(window, new Point(source.X + 400, source.Y + 200)), MouseButton.Left);
        Settle(window);

        (dragged.X, dragged.Y).ShouldBe(was);
        editor.History.CanUndo.ShouldBeFalse("a drag that was backed out of is not a step");
    }

    /// <summary>
    /// A rubber band replaces the selection as it moves, so backing out of one
    /// means giving back the selection it swept away.
    /// </summary>
    [AvaloniaFact]
    public void A_rubber_band_backed_out_of_gives_the_selection_back()
    {
        var (window, source, fed) = Open();
        var editor = Editor(window);

        editor.Selection.Select(source.Id);
        Settle(window);

        var empty = OnWindow(window, new Point(fed.X - 80, fed.Y - 30));

        window.MouseDown(empty, MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(fed.X + NodeGeometry.Width, fed.Y + 40)));
        Settle(window);

        editor.Selection.Nodes.ShouldHaveSingleItem().Id.ShouldBe(fed.Id, "the band swept the other module");

        Escape(window);

        editor.Selection.Nodes.ShouldHaveSingleItem().Id.ShouldBe(source.Id);
        editor.Gestures.Gesturing.ShouldBeFalse();
    }

    /// <summary>
    /// With nothing under way the canvas leaves the key alone, so it goes on
    /// reaching whatever else here answers it.
    /// </summary>
    [AvaloniaFact]
    public void Escape_with_no_gesture_is_left_for_the_window()
    {
        var (window, source, _) = Open();
        var editor = Editor(window);

        // A click, so the canvas has the focus and would see the key first.
        var at = OnWindow(window, Body(source));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        editor.Gestures.Gesturing.ShouldBeFalse();

        var handled = false;

        window.AddHandler(
            InputElement.KeyDownEvent,
            (object? _, KeyEventArgs e) => handled = e.Handled,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        Escape(window);

        handled.ShouldBeFalse();
    }
}
