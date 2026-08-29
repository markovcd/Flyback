using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Letting a wire go over bare canvas: the module list opens, and what is picked
/// arrives already plugged in.
/// </summary>
/// <remarks>
/// Which socket it lands on is the whole of what is interesting here. Nothing is
/// refused for being the wrong kind, because nothing is — the compiler
/// broadcasts a scalar to three channels and takes luma from a color, so every
/// socket accepts every wire. The question is only which one was meant, and the
/// answer is the port the module is <em>about</em> before the port that matches.
/// </remarks>
public class WireDropTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // A patch of one module, so the canvas is nearly all bare and the
        // sockets under test are the only ones a drop could land on.
        Editor(window).Patch = new Patch();
        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static ModulePalette? Palette(MainWindow window) =>
        All<ModulePalette>(window).FirstOrDefault();

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>
    /// A point on the canvas, given in the editor's own coordinates rather than
    /// the patch's — so it is on screen, which a point in graph space need not
    /// be. A press that never reaches the control looks exactly like a gesture
    /// that does not work.
    /// </summary>
    private static Point Canvas(MainWindow window, double x, double y)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(new Point(x, y), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>The bottom-left corner of the canvas, which nothing here puts a module on.</summary>
    private static Point Bare(MainWindow window) =>
        Canvas(window, 40, Editor(window).Bounds.Height - 40);

    /// <summary>
    /// Adds a module at a point near the top left of the canvas, well clear of
    /// the Output in the middle and of the corner a wire gets dropped on.
    /// </summary>
    private static NodeInstance Place(MainWindow window, string typeId)
    {
        var editor = Editor(window);
        var at = editor.GraphToScreen.Invert().Transform(new Point(120, 90));

        var node = editor.AddNode(typeId, at).ShouldNotBeNull();
        Settle(window);
        return node;
    }

    private static void DragFrom(MainWindow window, Point socket, Point toWindow)
    {
        window.MouseDown(OnWindow(window, socket), MouseButton.Left);
        window.MouseMove(toWindow);
        window.MouseUp(toWindow, MouseButton.Left);
        Settle(window);
    }

    private static void Pick(MainWindow window, string name)
    {
        var palette = Palette(window).ShouldNotBeNull("the list should be up");
        var button = All<Button>(palette).First(b => b.Content as string == name);

        button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    // --- the gesture --------------------------------------------------------

    [AvaloniaFact]
    public void Dropping_a_wire_on_bare_canvas_opens_the_list()
    {
        var window = Open();
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));

        Palette(window).ShouldNotBeNull();
    }

    /// <summary>
    /// Dropped on a module's body it is a miss — the sockets are where a wire
    /// means something — rather than a request for another module.
    /// </summary>
    [AvaloniaFact]
    public void Dropping_a_wire_on_a_module_opens_nothing()
    {
        var window = Open();
        var time = Place(window, "time");
        var sink = Editor(window).Patch.Output;

        var body = OnWindow(window, new Point(
            sink.X + NodeGeometry.Width / 2,
            sink.Y + NodeGeometry.HeaderHeight / 2));

        DragFrom(window, NodeGeometry.OutputPort(time, 0), body);

        Palette(window).ShouldBeNull();
    }

    [AvaloniaFact]
    public void What_is_picked_arrives_plugged_in_and_where_it_was_dropped()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        var dropped = Bare(window);
        DragFrom(window, NodeGeometry.OutputPort(time, 0), dropped);
        Pick(window, "Sine");

        var added = editor.Patch.Nodes.Last(n => n.TypeId == "osc.sine");
        var wire = editor.Patch.IncomingTo(added.Id, 0).ShouldNotBeNull();

        wire.SourceNode.ShouldBe(time.Id);
        wire.SourcePort.ShouldBe(0);

        // Centred where it was let go, the same as a module picked after a
        // right-click.
        var def = NodeCatalog.BuiltIn.Require("osc.sine");
        var at = editor.GraphToScreen.Invert().Transform(
            window.TranslatePoint(dropped, editor) ?? dropped);

        (added.X + NodeGeometry.Width / 2).ShouldBe(at.X, 1);
        (added.Y + NodeGeometry.Height(def) / 2).ShouldBe(at.Y, 1);
    }

    [AvaloniaFact]
    public void The_module_and_its_wire_are_one_undo()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Sine");

        editor.Undo().ShouldBeTrue();

        editor.Patch.Nodes.ShouldNotContain(n => n.TypeId == "osc.sine");
        editor.Patch.Connections.ShouldBeEmpty("the wire went with it");
    }

    // --- which socket -------------------------------------------------------

    /// <summary>
    /// The port a module is about comes first. An oscillator's <c>in</c> is
    /// marked as the axis it is read across, and it is what dragging Time at one
    /// means — never its <c>freq</c>, which is next along and just as scalar.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_lands_on_the_port_the_module_is_read_across()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Sine");

        var added = editor.Patch.Nodes.Last(n => n.TypeId == "osc.sine");

        editor.Patch.IncomingTo(added.Id, 0).ShouldNotBeNull("'in' is the domain port");
        editor.Patch.IncomingTo(added.Id, 1).ShouldBeNull("'freq' is not");
    }

    /// <summary>
    /// A Probe's socket is swept rather than domain, and means the same thing:
    /// the signal it is for. Without that rule the first exactly-scalar socket
    /// would be its timebase, which is emphatically not what was dragged.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_lands_on_a_swept_port_rather_than_the_first_scalar_one()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Probe");

        var added = editor.Patch.Nodes.Last(n => n.TypeId == NodeCatalog.ProbeTypeId);

        editor.Patch.IncomingTo(added.Id, 0).ShouldNotBeNull("'in' is the swept port");
        editor.Patch.IncomingTo(added.Id, 1).ShouldBeNull("'window' is the timebase");
    }

    /// <summary>
    /// With no port to be read across, an exact match of kind decides — so a
    /// scalar goes to a Blend's <c>t</c> rather than being broadcast to grey
    /// down its <c>a</c>.
    /// </summary>
    [AvaloniaFact]
    public void A_scalar_lands_on_a_scalar_socket_rather_than_the_first_one()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Blend");

        var added = editor.Patch.Nodes.Last(n => n.TypeId == "color.mix");
        var def = NodeCatalog.BuiltIn.Require("color.mix");

        var landed = Enumerable.Range(0, def.Inputs.Count)
            .Single(p => editor.Patch.IncomingTo(added.Id, p) is not null);

        def.Inputs[landed].Kind.ShouldBe(PortKind.Scalar);
        def.Inputs[landed].Name.ShouldBe("t");
    }

    /// <summary>
    /// And the same rule the other way about: a wire pulled out of a color
    /// socket takes a Scan's <c>view</c>, which is its only color output, over
    /// the <c>out</c> that comes first.
    /// </summary>
    [AvaloniaFact]
    public void A_color_input_takes_the_color_output_rather_than_the_first_one()
    {
        var window = Open();
        var editor = Editor(window);
        var sink = editor.Patch.Output;
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.OutputTypeId);

        // Dragged backwards, out of the Output's color socket.
        DragFrom(
            window,
            NodeGeometry.InputPort(sink, def, NodeCatalog.OutputColorPort),
            Bare(window));

        Pick(window, "Scan");

        var added = editor.Patch.Nodes.Last(n => n.TypeId == NodeCatalog.ScanTypeId);
        var wire = editor.Patch.IncomingTo(sink.Id, NodeCatalog.OutputColorPort).ShouldNotBeNull();

        wire.SourceNode.ShouldBe(added.Id);
        wire.SourcePort.ShouldBe(1, "'view' is the color one; 'out' is a scalar");
    }

    /// <summary>
    /// A module with nothing on the side the wire needs still arrives — it is a
    /// module somebody asked for. Coordinates has no inputs at all.
    /// </summary>
    [AvaloniaFact]
    public void A_module_with_no_socket_for_it_still_arrives_unwired()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Coordinates");

        editor.Patch.Nodes.ShouldContain(n => n.TypeId == "coord");
        editor.Patch.Connections.ShouldBeEmpty("there was no input to plug into");
    }

    /// <summary>
    /// The list opened by a right-click afterwards adds an unwired module — the
    /// wire that was dropped last time is not still waiting for one.
    /// </summary>
    [AvaloniaFact]
    public void A_later_right_click_does_not_wire_to_the_last_dropped_wire()
    {
        var window = Open();
        var editor = Editor(window);
        var time = Place(window, "time");

        DragFrom(window, NodeGeometry.OutputPort(time, 0), Bare(window));
        Pick(window, "Sine");

        var wires = editor.Patch.Connections.Count;

        // The other corner: the module just added is standing on the one the
        // wire was dropped in, and a right-click there would be over it.
        var at = Canvas(window, Editor(window).Bounds.Width - 60, Editor(window).Bounds.Height - 40);

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Settle(window);

        Pick(window, "Rings");

        editor.Patch.Connections.Count.ShouldBe(wires, "nothing new should be plugged in");
    }
}
