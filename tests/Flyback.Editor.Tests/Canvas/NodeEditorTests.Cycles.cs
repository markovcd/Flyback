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
    // --- cycles -------------------------------------------------------------

    /// <summary>
    /// An oscillator, something reading it, and the Output — everything needed to
    /// draw a loop by wiring the second back into the first.
    /// </summary>
    private static Patch Loop(out NodeInstance osc, out NodeInstance gain, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        osc = builder.Add("osc.sine", 0, 0);
        gain = builder.Add("math.mul", 300, 260);
        sink = builder.Add(NodeCatalog.OutputTypeId, 620, 0);

        builder.Wire(osc, 0, gain, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    /// <summary>
    /// Drawing a wire that runs backwards is the whole gesture, and nothing is put
    /// on it: the wire itself carries the evaluation before, so a cycle is patched
    /// the way a rack lets you patch one and the patch is legal as drawn.
    /// </summary>
    [AvaloniaFact]
    public void A_wire_that_closes_a_loop_is_drawn_as_it_was_asked_for()
    {
        var patch = Loop(out var osc, out var gain, out _);
        var (editor, window) = Editing(patch);

        var sine = NodeCatalog.BuiltIn.Require("osc.sine");

        // gain.out -> sine.phase, which closes sine -> gain -> sine.
        Drag(editor, window,
            NodeGeometry.OutputPort(gain, 0),
            Geometry.InputPort(osc, sine, 2));

        var closing = patch.IncomingTo(osc.Id, 2);

        closing.ShouldNotBeNull("the oscillator's phase should be fed");
        closing.SourceNode.ShouldBe(gain.Id, "straight from the gain, with nothing in between");

        patch.Nodes.Count.ShouldBe(3, "nothing should have been added to the canvas");

        // One wire of the ring carries the previous evaluation, and it is the one
        // leaving the oscillator — the module the Output reads — rather than
        // whichever happened to be drawn last.
        Cycles.Backwards(patch).ShouldHaveSingleItem().SourceNode.ShouldBe(osc.Id);

        // Which is the point of all of it: the patch is legal as drawn.
        patch.CompileForAudio(NodeCatalog.BuiltIn).HasErrors.ShouldBeFalse();
    }

    /// <summary>
    /// One gesture, so one press of undo — and with nothing placed alongside the
    /// wire, taking it back leaves the canvas as it was.
    /// </summary>
    [AvaloniaFact]
    public void Taking_back_that_wire_leaves_the_patch_as_it_was()
    {
        var patch = Loop(out var osc, out var gain, out _);
        var (editor, window) = Editing(patch);

        var sine = NodeCatalog.BuiltIn.Require("osc.sine");

        Drag(editor, window,
            NodeGeometry.OutputPort(gain, 0),
            Geometry.InputPort(osc, sine, 2));

        editor.History.Undo().ShouldBeTrue();

        editor.History.Patch.IncomingTo(osc.Id, 2).ShouldBeNull("the wire is gone");
        Cycles.Backwards(editor.History.Patch).ShouldBeEmpty("and with it the loop");
    }

    /// <summary>
    /// A module's own output dropped on its own input is the shortest loop there
    /// is, and is drawn like any other — the canvas slings it under the box so it
    /// is not hidden behind the module it belongs to.
    /// </summary>
    [AvaloniaFact]
    public void A_module_can_be_wired_to_itself()
    {
        var patch = Loop(out var osc, out _, out _);
        var (editor, window) = Editing(patch);

        var sine = NodeCatalog.BuiltIn.Require("osc.sine");

        Drag(editor, window,
            NodeGeometry.OutputPort(osc, 0),
            Geometry.InputPort(osc, sine, 2));

        var closing = patch.IncomingTo(osc.Id, 2);

        closing.ShouldNotBeNull("the wire should have been drawn");
        closing.SourceNode.ShouldBe(osc.Id);

        Cycles.Backwards(patch).ShouldHaveSingleItem().ShouldBe(closing);
        patch.CompileForAudio(NodeCatalog.BuiltIn).HasErrors.ShouldBeFalse();
    }

    /// <summary>
    /// A wire that runs forwards closes nothing, and is drawn solid because what
    /// it carries is this evaluation.
    /// </summary>
    [AvaloniaFact]
    public void An_ordinary_wire_runs_forwards()
    {
        var patch = Loop(out _, out var gain, out var sink);
        var (editor, window) = Editing(patch);

        Drag(editor, window,
            NodeGeometry.OutputPort(gain, 0),
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputRightPort));

        patch.IncomingTo(sink.Id, NodeCatalog.OutputRightPort)
            .ShouldNotBeNull()
            .SourceNode.ShouldBe(gain.Id, "the wire should be exactly what was drawn");

        Cycles.Backwards(patch).ShouldBeEmpty();
    }

    /// <summary>
    /// Re-drawing the wire that closes a loop leaves it one loop with one
    /// evaluation of delay. Nothing accumulates, because there is nothing to
    /// accumulate.
    /// </summary>
    [AvaloniaFact]
    public void Drawing_the_closing_wire_again_changes_nothing()
    {
        var patch = Loop(out var osc, out var gain, out _);
        var (editor, window) = Editing(patch);

        var sine = NodeCatalog.BuiltIn.Require("osc.sine");
        var phase = Geometry.InputPort(osc, sine, 2);

        Drag(editor, window, NodeGeometry.OutputPort(gain, 0), phase);

        patch.Disconnect(osc.Id, 2);
        editor.History.Record();

        Drag(editor, window, NodeGeometry.OutputPort(gain, 0), phase);

        patch.Nodes.Count.ShouldBe(3);
        Cycles.Backwards(patch).Count.ShouldBe(1, "still one loop, still one evaluation of delay");
    }
}
