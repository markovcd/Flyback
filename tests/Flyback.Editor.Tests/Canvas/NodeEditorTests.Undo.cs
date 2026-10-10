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
    // --- undo and redo ------------------------------------------------------

    /// <summary>
    /// What is on the canvas now, looked up by id. A restore hands back a fresh
    /// set of objects, so nothing a test held before an undo means anything
    /// after one — which is the property worth writing the lookup out for.
    /// </summary>
    private static NodeInstance? Now(NodeEditor editor, NodeInstance node) => editor.History.Patch.Find(node.Id);

    [AvaloniaFact]
    public void There_is_nothing_to_undo_on_a_patch_nobody_has_edited()
    {
        var (editor, _) = Editing(Pair(out _, out _));

        editor.History.CanUndo.ShouldBeFalse();
        editor.History.CanRedo.ShouldBeFalse();
        editor.History.Undo().ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Undo_takes_an_added_module_back_out_and_redo_puts_it_back()
    {
        var (editor, window) = Editing(Pair(out _, out _));

        var added = editor.Edits.AddNode("math.mixer").ShouldNotBeNull();
        Settle(window);

        editor.History.Undo().ShouldBeTrue();
        Now(editor, added).ShouldBeNull();

        editor.History.Redo().ShouldBeTrue();
        Now(editor, added).ShouldNotBeNull();
    }

    /// <summary>
    /// A deleted module takes its wires with it, so putting it back has to put
    /// them back too. Nothing in the editor arranges that — the step is the
    /// whole document, and the wires were part of it.
    /// </summary>
    [AvaloniaFact]
    public void Undo_brings_a_deleted_module_back_with_its_wiring()
    {
        var patch = Pair(out var source, out var sink);
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(source));
        editor.Edits.DeleteSelected();
        Settle(window);

        editor.History.Patch.Connections.ShouldBeEmpty();

        editor.History.Undo().ShouldBeTrue();

        Now(editor, source).ShouldNotBeNull();
        editor.History.Patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Undo_unplugs_a_wire_that_was_just_patched()
    {
        var patch = Pair(out var source, out var sink);
        var (editor, window) = Editing(patch);

        Drag(editor, window,
            NodeGeometry.OutputPort(source, 0),
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

        patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort).ShouldNotBeNull();

        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Connections.ShouldBeEmpty();
    }

    /// <summary>
    /// Moving a wire from one socket to another unplugs it and plugs it in
    /// again, which is two edits and one thing somebody did. One press of undo
    /// puts it back where it was rather than leaving it dangling halfway.
    /// </summary>
    [AvaloniaFact]
    public void Undo_treats_a_re_patch_as_the_one_gesture_it_was()
    {
        var patch = Pair(out var source, out var sink);
        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColorPort);

        var (editor, window) = Editing(patch);

        Drag(editor, window,
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputColorPort),
            Geometry.InputPort(sink, Sink, NodeCatalog.OutputLeftPort));

        patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort).ShouldNotBeNull("the wire moved");

        editor.History.Undo().ShouldBeTrue();

        editor.History.Patch.IncomingTo(sink.Id, NodeCatalog.OutputColorPort)
            .ShouldNotBeNull("and goes back to the socket it came off, in one press");
    }

    /// <summary>
    /// A module put down in the wrong place is an edit like any other, and the
    /// one edit here that nothing downstream can hear — so it goes in the
    /// history without asking anything to recompile.
    /// </summary>
    [AvaloniaFact]
    public void Undo_puts_a_moved_module_back_without_rebuilding_the_program()
    {
        var patch = Pair(out var source, out _);
        var (editor, window) = Editing(patch);

        var recompiles = 0;
        editor.Reactions.Add<PatchChanged>(_ => recompiles++);

        var from = Body(source);
        Drag(editor, window, from, from + new Vector(180, 120));

        Now(editor, source).ShouldNotBeNull().X.ShouldBe(180, 1);
        recompiles.ShouldBe(0, "where a module sits is not in the program");
        editor.History.CanUndo.ShouldBeTrue();

        editor.History.Undo().ShouldBeTrue();
        Now(editor, source).ShouldNotBeNull().X.ShouldBe(0, 1);
    }

    /// <summary>
    /// Opening something else is not an edit to what was open. Undoing back into
    /// the patch somebody had before they loaded a file would lose them the file.
    /// </summary>
    [AvaloniaFact]
    public void A_patch_that_arrives_from_outside_is_not_something_to_undo_into()
    {
        var (editor, window) = Editing(Pair(out _, out _));

        editor.Edits.AddNode("math.mixer");
        Settle(window);
        editor.History.CanUndo.ShouldBeTrue();

        editor.History.Open(Presets.Plasma(NodeCatalog.BuiltIn));
        Settle(window);

        editor.History.CanUndo.ShouldBeFalse(
            "and the layout a preset arrives already placed by is not an edit either — ADR-0070");

        editor.History.CanRedo.ShouldBeFalse();
    }

    /// <summary>
    /// The selection is what the inspector is showing, so an undo that removes
    /// the selected module has to let go of it — and one that does not, must
    /// keep it, or every undo would close the panel somebody is working in.
    /// </summary>
    [AvaloniaFact]
    public void Undo_keeps_the_selection_where_what_it_named_is_still_there()
    {
        var patch = Pair(out var source, out _);
        var (editor, window) = Editing(patch);

        ClickAt(editor, window, Body(source));

        source.InputValues[0] = 0.5f;
        editor.History.Record();

        editor.History.Undo().ShouldBeTrue();
        editor.Selection.Focused.ShouldNotBeNull().Id.ShouldBe(source.Id);

        var added = editor.Edits.AddNode("math.mixer").ShouldNotBeNull();
        editor.Selection.Focused.ShouldNotBeNull().Id.ShouldBe(added.Id);

        editor.History.Undo().ShouldBeTrue();
        editor.Selection.Focused.ShouldBeNull("what was selected is no longer there");
    }

    /// <summary>
    /// What the shell asks before it lets a patch go. The logic is the history's
    /// and is tested there; what matters here is that the canvas reports it for
    /// the edits somebody actually makes on it.
    /// </summary>
    [AvaloniaFact]
    public void The_canvas_says_whether_there_is_unsaved_work_on_it()
    {
        var (editor, window) = Editing(Pair(out _, out _));

        editor.History.IsModified.ShouldBeFalse("nothing has been done to it yet");

        editor.Edits.AddNode("math.mixer");
        Settle(window);
        editor.History.IsModified.ShouldBeTrue();

        editor.History.Undo().ShouldBeTrue();
        editor.History.IsModified.ShouldBeFalse("undone back to the patch that was opened");

        editor.Edits.AddNode("math.mixer");
        Settle(window);

        editor.History.MarkSaved();
        editor.History.IsModified.ShouldBeFalse("written out is written out");
        editor.History.CanUndo.ShouldBeTrue("and saving is not a reason to stop being able to undo");

        editor.History.Undo().ShouldBeTrue();
        editor.History.IsModified.ShouldBeTrue("undone back past what was written out");
    }

    [AvaloniaFact]
    public void A_patch_that_arrives_from_outside_has_nothing_unsaved_in_it()
    {
        var (editor, window) = Editing(Pair(out _, out _));

        editor.Edits.AddNode("math.mixer");
        Settle(window);
        editor.History.IsModified.ShouldBeTrue();

        editor.History.Open(Presets.Plasma(NodeCatalog.BuiltIn));
        Settle(window);

        editor.History.IsModified.ShouldBeFalse();
    }

    /// <summary>
    /// The assistant's whole patch, which arrives as an edit rather than as a
    /// new document. Nothing about it is small, and that is exactly why it has
    /// to undo: it replaces everything on the canvas at once.
    /// </summary>
    [AvaloniaFact]
    public void A_patch_applied_as_an_edit_undoes_like_any_other()
    {
        var patch = Pair(out var source, out _);
        var (editor, window) = Editing(patch);

        editor.History.Apply(Presets.Plasma(NodeCatalog.BuiltIn));
        Settle(window);

        Now(editor, source).ShouldBeNull("the new patch is on the canvas");
        editor.History.CanUndo.ShouldBeTrue();
        editor.History.IsModified.ShouldBeTrue("and none of it has been saved");

        editor.History.Undo().ShouldBeTrue();
        Now(editor, source).ShouldNotBeNull("undo puts back what was there before it");

        editor.History.Redo().ShouldBeTrue();
        Now(editor, source).ShouldBeNull("and redo brings it round again");
    }
}
