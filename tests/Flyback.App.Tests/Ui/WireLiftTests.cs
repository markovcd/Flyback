using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Re-sourcing a wire: Ctrl+drag an output carrying one wire and the wire comes
/// off <em>that</em> socket, staying in the input at its far end, to be dropped
/// on a different output.
/// </summary>
/// <remarks>
/// The mirror of dragging a connected input, which takes the plug out of the
/// input and keeps the source. This takes the plug out of the output and keeps
/// the target, so what is being chosen is where a signal comes from rather than
/// where it goes — the question nothing here could ask before.
/// <para>
/// It needs a modifier where an input does not, because an input holds one wire
/// and an output holds any number: dragging from an output already means "start
/// another", which is the common thing to want and cannot be given up.
/// </para>
/// </remarks>
public class WireLiftTests : UiTest
{
    private const string Sine = "osc.sine";
    private const string Saw = "osc.saw";
    private const string Add = "math.add";

    /// <summary>
    /// Two oscillators to be the source, and two Adds to be fed, with the Output
    /// already there.
    /// </summary>
    private static Board Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var source = b.Add(Sine, 40, 40);
        var other = b.Add(Saw, 40, 300);
        var fed = b.Add(Add, 400, 40);
        var spare = b.Add(Add, 400, 300);

        Editor(window).Patch = b.Patch;
        Settle(window);

        return new Board(window, source, other, fed, spare);
    }

    private sealed record Board(
        MainWindow Window,
        NodeInstance Source,
        NodeInstance Other,
        NodeInstance Fed,
        NodeInstance Spare)
    {
        public NodeEditor Editor => WireLiftTests.Editor(Window);

        public Patch Patch => Editor.Patch;

        /// <summary>Wires the Sine into the first Add and records it, so a drag is the next edit.</summary>
        public Board Wired()
        {
            Patch.Connect(Source.Id, 0, Fed.Id, 0);
            Editor.NotifyPatchChanged();
            return this;
        }
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    private static Point Input(NodeInstance node, int index) =>
        NodeGeometry.InputPort(node, NodeCatalog.BuiltIn.Require(node.TypeId), index);

    private static Point Output(NodeInstance node, int index = 0) =>
        NodeGeometry.OutputPort(node, index);

    private static void Drag(
        MainWindow window,
        Point fromGraph,
        Point toGraph,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(OnWindow(window, fromGraph), MouseButton.Left, modifiers);
        window.MouseMove(OnWindow(window, toGraph));
        window.MouseUp(OnWindow(window, toGraph), MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void Control_dragging_an_output_moves_the_wire_to_another_output()
    {
        var board = Open().Wired();

        Drag(board.Window, Output(board.Source), Output(board.Other), RawInputModifiers.Control);

        // The input it was feeding still is, and by the same socket — what
        // changed is only where the signal comes from.
        var wire = board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull(
            "the wire should have stayed in the input");

        wire.SourceNode.ShouldBe(board.Other.Id);
        wire.SourcePort.ShouldBe(0);

        // Moved rather than copied: nothing leaves the Sine any more.
        board.Patch.Connections.Count(c => c.SourceNode == board.Source.Id).ShouldBe(0);
        board.Patch.Connections.Count.ShouldBe(1);
    }

    /// <summary>Command as well as Control, so the gesture is the machine's own.</summary>
    [AvaloniaFact]
    public void Command_re_sources_a_wire_the_same_way()
    {
        var board = Open().Wired();

        Drag(board.Window, Output(board.Source), Output(board.Other), RawInputModifiers.Meta);

        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(board.Other.Id);
    }

    /// <summary>
    /// The end in your hand is the one that was grabbed, so this only lands on
    /// an output. Dropped on an input it is a miss, the same as any wire let go
    /// over the wrong kind of socket.
    /// </summary>
    [AvaloniaFact]
    public void A_re_sourced_wire_dropped_on_an_input_goes_nowhere()
    {
        var board = Open().Wired();

        Drag(board.Window, Output(board.Source), Input(board.Spare, 0), RawInputModifiers.Control);

        board.Patch.IncomingTo(board.Spare.Id, 0).ShouldBeNull();
        board.Patch.Connections.ShouldBeEmpty();
    }

    /// <summary>
    /// Without the modifier the gesture is what it always was, which is the part
    /// that cannot be given up: starting another wire is the common thing to
    /// want from an output.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_without_the_modifier_still_starts_a_second_wire()
    {
        var board = Open().Wired();

        Drag(board.Window, Output(board.Source), Input(board.Spare, 0));

        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull("the first wire should still be there");
        board.Patch.IncomingTo(board.Spare.Id, 0).ShouldNotBeNull();
        board.Patch.Connections.Count(c => c.SourceNode == board.Source.Id).ShouldBe(2);
    }

    /// <summary>
    /// Several wires and there is no telling which one was meant, so the
    /// modifier does nothing rather than picking one. A gesture that guesses is
    /// worse than one that declines.
    /// </summary>
    [AvaloniaFact]
    public void An_output_with_two_wires_on_it_is_left_alone()
    {
        var board = Open().Wired();

        board.Patch.Connect(board.Source.Id, 0, board.Spare.Id, 0);
        board.Editor.NotifyPatchChanged();

        Drag(board.Window, Output(board.Source), Input(board.Spare, 1), RawInputModifiers.Control);

        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull();
        board.Patch.IncomingTo(board.Spare.Id, 0).ShouldNotBeNull();
        board.Patch.Connections.Count(c => c.SourceNode == board.Source.Id).ShouldBe(3);
    }

    /// <summary>An output with nothing on it has nothing to lift, and draws a new wire.</summary>
    [AvaloniaFact]
    public void An_output_with_no_wire_on_it_starts_one()
    {
        var board = Open();

        Drag(board.Window, Output(board.Source), Input(board.Fed, 0), RawInputModifiers.Control);

        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(board.Source.Id);
    }

    /// <summary>
    /// Let go over bare canvas it asks for something to plug in, the same as any
    /// other wire dropped there — and what arrives feeds the input the wire is
    /// still in, because that is the end this gesture kept.
    /// </summary>
    [AvaloniaFact]
    public void A_re_sourced_wire_dropped_on_bare_canvas_asks_for_a_source()
    {
        var board = Open().Wired();
        var editor = board.Editor;
        var bare = editor.GraphToScreen.Invert().Transform(
            new Point(40, editor.Bounds.Height - 40));

        Drag(board.Window, Output(board.Source), bare, RawInputModifiers.Control);

        var palette = All<ModulePalette>(board.Window).FirstOrDefault()
            .ShouldNotBeNull("the list should be up");

        var button = All<Button>(palette).First(b => b.Content as string == "Saw");

        button.Focus();
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(board.Window);

        var added = board.Patch.Nodes.Last(n => n.TypeId == Saw);
        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(added.Id);
    }

    /// <summary>
    /// Unplugging and plugging in again is two edits and one gesture, so it goes
    /// back in one press — the same grouping an input's drag already has.
    /// </summary>
    [AvaloniaFact]
    public void Re_sourcing_a_wire_is_one_undo()
    {
        var board = Open().Wired();
        var editor = board.Editor;

        Drag(board.Window, Output(board.Source), Output(board.Other), RawInputModifiers.Control);

        // Stated before the undo, so this cannot pass on a build where the lift
        // never happened.
        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(board.Other.Id);

        editor.Undo();
        Settle(board.Window);

        editor.Patch.IncomingTo(board.Fed.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(board.Source.Id);

        // And the press before that goes back past the wire set up here, which
        // says the gesture did not fold into the edit before it.
        editor.Undo();
        Settle(board.Window);

        editor.Patch.Connections.ShouldBeEmpty();
    }

    /// <summary>
    /// An input still needs no modifier, and Ctrl on one changes nothing: it
    /// keeps the source and looks for a new target, held or not.
    /// </summary>
    [AvaloniaFact]
    public void An_input_is_unchanged_by_the_modifier()
    {
        var board = Open().Wired();

        Drag(board.Window, Input(board.Fed, 0), Input(board.Spare, 0), RawInputModifiers.Control);

        board.Patch.IncomingTo(board.Fed.Id, 0).ShouldBeNull();
        board.Patch.IncomingTo(board.Spare.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(board.Source.Id);
    }
}
