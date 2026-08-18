using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Undo and redo. A step is the whole document rather than an inverse of an
/// edit, so what these check is not that any particular edit reverses — every
/// edit reverses, by construction — but the bookkeeping around that: what
/// counts as a step, what counts as a new document, and what a gesture held
/// down for a hundred frames counts as.
/// </summary>
public class PatchHistoryTests
{
    private static readonly ModuleCatalog Catalog = NodeCatalog.BuiltIn;

    /// <summary>A patch with one module wired into the Output, and room for more.</summary>
    private static Patch Wired(out NodeInstance source, out NodeInstance sink)
    {
        var builder = new PatchBuilder(Catalog);

        source = builder.Add("value", 0, 0, (0, 0.25f));
        sink = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        builder.Wire(source, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    private static PatchHistory Opened(Patch patch)
    {
        var history = new PatchHistory(Catalog);
        history.Opened(patch);
        return history;
    }

    [Fact]
    public void A_freshly_opened_patch_has_nothing_to_undo_or_redo()
    {
        var history = Opened(Wired(out _, out _));

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        history.Undo().ShouldBeNull();
        history.Redo().ShouldBeNull();
    }

    [Fact]
    public void A_removed_module_comes_back_with_the_wire_that_reached_it()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        patch.Remove(source.Id);
        history.Record(patch);

        var back = history.Undo().ShouldNotBeNull();

        back.Find(source.Id).ShouldNotBeNull();
        back.Connections.ShouldHaveSingleItem().SourceNode.ShouldBe(source.Id);
    }

    [Fact]
    public void An_added_module_goes_away_again()
    {
        var patch = Wired(out _, out _);
        var history = Opened(patch);

        var added = NodeInstance.Create(Catalog.Require("math.mixer"), 200, 200);
        patch.Nodes.Add(added);
        history.Record(patch);

        history.Undo().ShouldNotBeNull().Find(added.Id).ShouldBeNull();
        history.Redo().ShouldNotBeNull().Find(added.Id).ShouldNotBeNull();
    }

    [Fact]
    public void A_knob_goes_back_to_what_it_was()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.9f;
        history.Record(patch);

        history.Undo().ShouldNotBeNull()
            .Find(source.Id).ShouldNotBeNull()
            .InputValues[0].ShouldBe(0.25f);
    }

    [Fact]
    public void A_wire_goes_back_to_where_it_was_plugged()
    {
        var patch = Wired(out var source, out var sink);
        var history = Opened(patch);

        patch.Connect(source.Id, 0, sink.Id, NodeCatalog.OutputColourPort);
        history.Record(patch);

        var back = history.Undo().ShouldNotBeNull();

        back.Connections.ShouldHaveSingleItem().TargetPort.ShouldBe(NodeCatalog.OutputLeftPort);
    }

    /// <summary>
    /// Where a module sits is part of the document, so it undoes like the rest
    /// of it. Nothing in here knows this edit is any different from the others.
    /// </summary>
    [Fact]
    public void A_module_goes_back_to_where_it_sat()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.X = 640;
        source.Y = 480;
        history.Record(patch);

        var back = history.Undo().ShouldNotBeNull().Find(source.Id).ShouldNotBeNull();

        back.X.ShouldBe(0);
        back.Y.ShouldBe(0);
    }

    /// <summary>
    /// The point of recording from one place rather than at each of the things
    /// an editor can do: a caller that records whether or not anything happened
    /// does not fill the history with steps that go nowhere.
    /// </summary>
    [Fact]
    public void An_edit_that_changed_nothing_is_not_a_step()
    {
        var patch = Wired(out _, out _);
        var history = Opened(patch);

        history.Record(patch);
        history.Record(patch);

        history.CanUndo.ShouldBeFalse();
    }

    /// <summary>
    /// A slider makes an edit a frame while it is dragged, and putting a hundred
    /// of those in the history would make undo useless exactly where it is most
    /// wanted. They fold into one step, which ends where the drag began.
    /// </summary>
    [Fact]
    public void A_gesture_held_down_is_one_step_however_many_edits_it_makes()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        foreach (var frame in Enumerable.Range(1, 40))
        {
            source.InputValues[0] = frame / 40f;
            history.Record(patch, "the slider");
        }

        history.Undo().ShouldNotBeNull()
            .Find(source.Id).ShouldNotBeNull()
            .InputValues[0].ShouldBe(0.25f);

        history.CanUndo.ShouldBeFalse("the whole drag was the one step");
    }

    [Fact]
    public void Two_gestures_are_two_steps_however_close_together()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch, "the first drag");

        source.InputValues[0] = 0.75f;
        history.Record(patch, "the second drag");

        history.Undo().ShouldNotBeNull()
            .Find(source.Id).ShouldNotBeNull()
            .InputValues[0].ShouldBe(0.5f);

        history.Undo().ShouldNotBeNull()
            .Find(source.Id).ShouldNotBeNull()
            .InputValues[0].ShouldBe(0.25f);
    }

    /// <summary>
    /// Otherwise a second drag of the same slider would quietly rewrite the step
    /// an undo had just arrived at, and what came before it would be gone.
    /// </summary>
    [Fact]
    public void A_gesture_does_not_fold_into_one_that_has_been_undone_past()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch, "the slider");

        patch = history.Undo().ShouldNotBeNull();
        source = patch.Find(source.Id).ShouldNotBeNull();

        source.InputValues[0] = 0.8f;
        history.Record(patch, "the slider");

        history.Undo().ShouldNotBeNull()
            .Find(source.Id).ShouldNotBeNull()
            .InputValues[0].ShouldBe(0.25f);
    }

    [Fact]
    public void Editing_after_an_undo_discards_what_was_ahead()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);

        patch = history.Undo().ShouldNotBeNull();
        history.CanRedo.ShouldBeTrue();

        patch.Find(source.Id).ShouldNotBeNull().InputValues[0] = 0.75f;
        history.Record(patch);

        history.CanRedo.ShouldBeFalse();
    }

    /// <summary>
    /// A file, a preset or a patch from the assistant is a different document.
    /// Undoing back into whatever was open before it would not be undo; it would
    /// be losing the thing that was just opened.
    /// </summary>
    [Fact]
    public void Opening_a_patch_is_not_something_to_undo_into()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);
        history.CanUndo.ShouldBeTrue();

        history.Opened(Presets.Plasma(Catalog));

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }

    /// <summary>
    /// The oldest steps are dropped rather than the newest refused, so a long
    /// session keeps undoing — just not all the way back to where it began.
    /// </summary>
    [Fact]
    public void The_history_stops_growing_and_keeps_the_recent_end()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        for (var edit = 1; edit <= PatchHistory.Depth + 20; edit++)
        {
            source.InputValues[0] = edit;
            history.Record(patch);
        }

        var steps = 0;
        while (history.Undo() is not null) steps++;

        steps.ShouldBe(PatchHistory.Depth);
    }

    /// <summary>
    /// A restored patch is a fresh set of objects, so nothing can be carried
    /// across by reference. Ids are what the canvas re-selects by and what wires
    /// name, and a restore that minted new ones would break both.
    /// </summary>
    [Fact]
    public void A_restored_patch_is_its_own_objects_under_the_same_ids()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);

        var back = history.Undo().ShouldNotBeNull();

        back.ShouldNotBeSameAs(patch);
        back.Find(source.Id).ShouldNotBeNull().ShouldNotBeSameAs(source);
    }

    /// <summary>
    /// A sequencer's notes are the one thing a module carries that is not a
    /// knob, and they ride on the same snapshot as everything else.
    /// </summary>
    [Fact]
    public void Sequencer_notes_come_back_too()
    {
        var builder = new PatchBuilder(Catalog);
        var sequencer = builder.Add("seq.values", 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        var patch = builder.Patch;
        var history = Opened(patch);
        var before = sequencer.Steps!.Count;

        sequencer.Steps!.RemoveAt(0);
        history.Record(patch);

        history.Undo().ShouldNotBeNull()
            .Find(sequencer.Id).ShouldNotBeNull()
            .Steps.ShouldNotBeNull()
            .Count.ShouldBe(before);
    }

    // --- what there is to lose ----------------------------------------------

    [Fact]
    public void A_patch_nobody_has_touched_has_nothing_to_lose()
    {
        Opened(Wired(out _, out _)).IsModified.ShouldBeFalse();
    }

    [Fact]
    public void An_edit_is_something_to_lose()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);

        history.IsModified.ShouldBeTrue();
    }

    /// <summary>
    /// The whole reason this is a comparison rather than a flag. Undoing back to
    /// where it started leaves the same document that was opened, and prompting
    /// to save that would be asking about nothing.
    /// </summary>
    [Fact]
    public void Undoing_back_to_the_start_leaves_nothing_to_lose()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);

        history.Undo().ShouldNotBeNull();

        history.IsModified.ShouldBeFalse();
    }

    [Fact]
    public void Saving_settles_it_and_the_next_edit_unsettles_it_again()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);

        history.Saved(patch);
        history.IsModified.ShouldBeFalse();

        // And is no reason to stop being able to take the edit back.
        history.CanUndo.ShouldBeTrue();

        source.InputValues[0] = 0.75f;
        history.Record(patch);
        history.IsModified.ShouldBeTrue();
    }

    /// <summary>
    /// Undone past what was written out, there is unsaved work again — the file
    /// on disk and the patch on the canvas no longer say the same thing.
    /// </summary>
    [Fact]
    public void Undoing_past_a_save_is_something_to_lose_again()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);
        history.Saved(patch);

        history.Undo().ShouldNotBeNull();

        history.IsModified.ShouldBeTrue();
    }

    [Fact]
    public void Opening_something_else_is_a_patch_with_nothing_to_lose()
    {
        var patch = Wired(out var source, out _);
        var history = Opened(patch);

        source.InputValues[0] = 0.5f;
        history.Record(patch);
        history.IsModified.ShouldBeTrue();

        history.Opened(Presets.Plasma(Catalog));

        history.IsModified.ShouldBeFalse();
    }
}
