using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The graph half of copy and paste: which modules and wires come out of a
/// patch, and what happens to them going into one.
/// </summary>
/// <remarks>
/// Nothing here touches a clipboard or a canvas. What travels is an ordinary
/// patch, so what is checked is the two things that make it one — that a
/// fragment holds only what was asked for, and that pasting it twice gives two
/// of something rather than one thing named twice.
/// </remarks>
public class PatchClipboardTests
{
    /// <summary>Time into a sine into the Output, with a spare module off to the side.</summary>
    private static Patch Chain(
        out NodeInstance time,
        out NodeInstance osc,
        out NodeInstance gain,
        out NodeInstance sink)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        time = b.Add("time", 0, 0);
        osc = b.Add("osc.sine", 300, 0, (1, 220f));
        gain = b.Add("math.mul", 600, 0, (1, 0.5f));
        sink = b.Add(NodeCatalog.OutputTypeId, 900, 0);

        b.Wire(time, 0, osc, 0)
         .Wire(osc, 0, gain, 0)
         .Wire(gain, 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    // --- what comes out -----------------------------------------------------

    [Fact]
    public void A_copy_holds_the_modules_asked_for_and_the_wires_between_them()
    {
        var patch = Chain(out var time, out var osc, out _, out _);

        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        fragment.Nodes.Select(n => n.TypeId).Order().ShouldBe(["osc.sine", "time"]);
        fragment.Connections.Count.ShouldBe(1, "the wire between the two comes with them");
    }

    /// <summary>
    /// A wire with one end outside the selection has nothing to plug into once
    /// it arrives, so it is left behind rather than guessed at.
    /// </summary>
    [Fact]
    public void A_wire_with_one_end_outside_the_selection_is_left_behind()
    {
        var patch = Chain(out _, out var osc, out _, out _);

        PatchClipboard.Copy(patch, [osc.Id]).Connections.ShouldBeEmpty();
    }

    /// <summary>
    /// The Output is never copied. A patch may hold exactly one (ADR-0037), so
    /// it is not a thing that can arrive anywhere.
    /// </summary>
    [Fact]
    public void The_output_never_comes()
    {
        var patch = Chain(out _, out _, out var gain, out var sink);

        var fragment = PatchClipboard.Copy(patch, [gain.Id, sink.Id]);

        fragment.Nodes.Select(n => n.TypeId).ShouldBe(["math.mul"]);
        fragment.Connections.ShouldBeEmpty("the wire into the Output went nowhere");
    }

    [Fact]
    public void Copying_the_output_alone_yields_nothing()
    {
        var patch = Chain(out _, out _, out _, out var sink);

        PatchClipboard.Copy(patch, [sink.Id]).Nodes.ShouldBeEmpty();
    }

    /// <summary>
    /// A fragment is a picture of the patch as it was. Sharing the settings
    /// arrays would let a knob turned afterwards change what a paste produces.
    /// </summary>
    [Fact]
    public void A_copy_does_not_follow_the_patch_it_came_from()
    {
        var patch = Chain(out _, out var osc, out _, out _);

        var fragment = PatchClipboard.Copy(patch, [osc.Id]);
        osc.InputValues[1] = 880f;

        fragment.Nodes[0].InputValues[1].ShouldBe(220f);
    }

    [Fact]
    public void Copying_does_not_touch_the_patch_it_copies_from()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var before = PatchIO.ToJson(patch, NodeCatalog.BuiltIn);

        PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        PatchIO.ToJson(patch, NodeCatalog.BuiltIn).ShouldBe(before);
    }

    // --- what goes in -------------------------------------------------------

    [Fact]
    public void Pasting_adds_the_modules_and_rebuilds_the_wires_between_them()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        var added = PatchClipboard.Paste(patch, fragment, 100, 40);

        added.Count.ShouldBe(2);
        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);

        var pasted = added.Single(n => n.TypeId == "osc.sine");
        var feeding = patch.IncomingTo(pasted.Id, 0).ShouldNotBeNull();

        feeding.SourceNode.ShouldBe(added.Single(n => n.TypeId == "time").Id);
    }

    /// <summary>
    /// Fresh ids, or a paste into the patch a fragment came from would name the
    /// modules already there rather than adding any.
    /// </summary>
    [Fact]
    public void Pasted_modules_are_new_modules()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        var once = PatchClipboard.Paste(patch, fragment);
        var twice = PatchClipboard.Paste(patch, fragment);

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(3, "the original and two pastes");
        patch.Nodes.Select(n => n.Id).Distinct().Count().ShouldBe(patch.Nodes.Count);

        once.Select(n => n.Id).ShouldNotBe(twice.Select(n => n.Id));
        patch.Nodes.ShouldContain(n => n.Id == osc.Id, "the module it was copied from is still there");
    }

    [Fact]
    public void Pasting_shifts_by_what_it_is_told_and_keeps_the_shape()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        var added = PatchClipboard.Paste(patch, fragment, 60, -20);

        added.Single(n => n.TypeId == "time").X.ShouldBe(time.X + 60);
        added.Single(n => n.TypeId == "time").Y.ShouldBe(time.Y - 20);
        added.Single(n => n.TypeId == "osc.sine").X.ShouldBe(osc.X + 60);
    }

    [Fact]
    public void Pasted_modules_carry_their_settings()
    {
        var patch = Chain(out _, out var osc, out _, out _);
        var fragment = PatchClipboard.Copy(patch, [osc.Id]);

        PatchClipboard.Paste(patch, fragment)[0].InputValues.ShouldBe(osc.InputValues);
    }

    /// <summary>
    /// The two things a node carries that are not knobs, and the two a copy
    /// could quietly leave behind — neither is a socket, so nothing else about
    /// the pasted module would show they had gone.
    /// </summary>
    [Fact]
    public void Pasted_modules_carry_what_is_not_a_knob()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        var sequencer = builder.Add("seq.notes", 0, 0);
        var quantiser = builder.Add(NodeCatalog.QuantiserTypeId, 0, 0);

        StepsExtra.Set(sequencer, [new Step(60f), new Step(64f, 2f)]);
        ScaleExtra.Set(quantiser, [0, 3, 7]);

        var fragment = PatchClipboard.Copy(builder.Patch, [sequencer.Id, quantiser.Id]);
        var pasted = PatchClipboard.Paste(builder.Patch, fragment);

        var pastedSequencer = pasted.Single(n => n.TypeId == "seq.notes");
        var pastedQuantiser = pasted.Single(n => n.TypeId == NodeCatalog.QuantiserTypeId);

        StepsExtra.Of(pastedSequencer).ShouldBe(StepsExtra.Of(sequencer));
        ScaleExtra.Of(pastedQuantiser).ShouldBe(ScaleExtra.Of(quantiser));

        // Its own copy, not the one it was pasted from: editing the new module's
        // scale must not reach back into the module it came from. Reached through
        // the store rather than through ScaleExtra, because the shape a kind
        // hands back is a copy either way — what is being pinned here is that the
        // trees underneath are two trees.
        pastedQuantiser.StateOf(ScaleExtra.Name).ShouldNotBeNull().AsArray().Clear();
        ScaleExtra.Of(quantiser).ShouldBe([0, 3, 7]);
    }

    /// <summary>
    /// A whole saved patch on the clipboard is a thing worth being able to
    /// paste, and it means everything in it but the sink.
    /// </summary>
    [Fact]
    public void Pasting_a_whole_patch_takes_everything_but_its_output()
    {
        var into = new PatchBuilder(NodeCatalog.BuiltIn);
        into.Add(NodeCatalog.OutputTypeId, 0, 0);

        var whole = Presets.Drone(NodeCatalog.BuiltIn);
        var added = PatchClipboard.Paste(into.Patch, whole);

        added.Count.ShouldBe(whole.Nodes.Count - 1);
        added.ShouldNotContain(n => NodeCatalog.IsSink(n.TypeId));
        into.Patch.Nodes.Count(n => NodeCatalog.IsSink(n.TypeId)).ShouldBe(1);
    }

    /// <summary>
    /// The round trip through text is how a fragment actually travels, so what
    /// survives it is what copy and paste really is.
    /// </summary>
    [Fact]
    public void A_fragment_survives_being_written_out_and_read_back()
    {
        var patch = Chain(out var time, out var osc, out var gain, out _);

        var text = PatchIO.ToJson(
            PatchClipboard.Copy(patch, [time.Id, osc.Id, gain.Id]), NodeCatalog.BuiltIn);

        var loaded = PatchIO.Read(text, NodeCatalog.BuiltIn);
        loaded.IsComplete.ShouldBeTrue();

        var fresh = new PatchBuilder(NodeCatalog.BuiltIn);
        var sink = fresh.Add(NodeCatalog.OutputTypeId, 900, 0);

        var added = PatchClipboard.Paste(fresh.Patch, loaded.Patch);
        added.Select(n => n.TypeId).Order().ShouldBe(["math.mul", "osc.sine", "time"]);

        // And what arrived is a working chain: wired to the sink it compiles to
        // the same instructions the original did.
        fresh.Wire(added.Single(n => n.TypeId == "math.mul"), 0, sink, NodeCatalog.OutputLeftPort);

        fresh.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Ops
            .ShouldBe(patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Ops);
    }

    /// <summary>
    /// Written out, a fragment names the plugins its modules came from, which is
    /// what lets a paste into a build without them be refused by name.
    /// </summary>
    [Fact]
    public void A_written_fragment_names_what_it_needs()
    {
        var patch = Chain(out var time, out _, out _, out _);

        var text = PatchIO.ToJson(PatchClipboard.Copy(patch, [time.Id]), NodeCatalog.BuiltIn);

        PatchIO.Read(text, NodeCatalog.BuiltIn).Patch.Version.ShouldBe(PatchIO.FormatVersion);
    }

    [Fact]
    public void Pasting_nothing_adds_nothing() =>
        PatchClipboard.Paste(new Patch(), new Patch()).ShouldBeEmpty();

    // --- the boxes ----------------------------------------------------------

    /// <summary>
    /// A box round what is being copied comes with it. Selecting a box on the
    /// canvas selects the modules inside it, so this is the whole of what
    /// copying a group is.
    /// </summary>
    [Fact]
    public void A_group_round_what_is_copied_comes_with_it()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var group = patch.Group([time.Id, osc.Id]).ShouldNotBeNull();

        group.Rename("Voice");

        var copied = PatchClipboard.Copy(patch, [time.Id, osc.Id]).Groups
            .ShouldNotBeNull()
            .ShouldHaveSingleItem();

        copied.Members.ShouldBe(group.Members);
        copied.Name.ShouldBe("Voice");
        copied.Collapsed.ShouldBeTrue("a box that was shut arrives shut");
    }

    /// <summary>
    /// Half a box is not a box. What arrived would have a different shape and a
    /// different set of sockets from the one on the canvas, which is the same
    /// objection as the wire with one end outside.
    /// </summary>
    [Fact]
    public void A_group_with_a_member_left_behind_does_not_come()
    {
        var patch = Chain(out var time, out var osc, out var gain, out _);
        patch.Group([time.Id, osc.Id, gain.Id]);

        PatchClipboard.Copy(patch, [time.Id, osc.Id]).Groups.ShouldBeNull();
    }

    [Fact]
    public void Pasting_a_group_draws_the_box_again_round_what_arrived()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        var group = patch.Group([time.Id, osc.Id]).ShouldNotBeNull();

        var added = PatchClipboard.Paste(
            patch, PatchClipboard.Copy(patch, [time.Id, osc.Id]), 100, 40);

        patch.Groups.ShouldNotBeNull().Count.ShouldBe(2);

        var pasted = patch.GroupOf(added[0].Id).ShouldNotBeNull();

        pasted.Id.ShouldNotBe(group.Id, "a box is a new box, the way a module is a new module");
        pasted.Members.Order().ShouldBe(added.Select(n => n.Id).Order());
        group.Members.ShouldBe([time.Id, osc.Id], "the box it was copied from is untouched");
    }

    [Fact]
    public void Pasting_a_group_twice_gives_two_boxes()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        patch.Group([time.Id, osc.Id]);

        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        var once = PatchClipboard.Paste(patch, fragment);
        var twice = PatchClipboard.Paste(patch, fragment);

        var groups = patch.Groups.ShouldNotBeNull();

        groups.Count.ShouldBe(3, "the original and two pastes");
        groups.Select(g => g.Id).Distinct().Count().ShouldBe(3);
        patch.GroupOf(once[0].Id).ShouldNotBe(patch.GroupOf(twice[0].Id));
    }

    /// <summary>
    /// A socket on the edge names a module and a port, so a pasted box has to
    /// point at the modules that arrived — and it keeps the ones with nothing
    /// wired to them, which are the edge somebody arranged rather than the edge
    /// the wires happen to imply.
    /// </summary>
    [Fact]
    public void A_pasted_box_keeps_the_edge_it_was_given()
    {
        var patch = Chain(out _, out var osc, out var gain, out _);
        var group = patch.Group([osc.Id, gain.Id]).ShouldNotBeNull();

        group.Exposed.Count.ShouldBe(2, "one wire crosses in and one leaves");

        // Unplugged, so one of them is a socket with nothing on it — which the
        // box keeps, and which a paste must therefore carry.
        patch.Disconnect(osc.Id, 0);

        var added = PatchClipboard.Paste(
            patch, PatchClipboard.Copy(patch, [osc.Id, gain.Id]), 0, 300);

        var pasted = patch.GroupOf(added[0].Id).ShouldNotBeNull();

        pasted.Exposed.ShouldBe(
            [
                new GroupSocket(added.Single(n => n.TypeId == "osc.sine").Id, 0, IsOutput: false),
                new GroupSocket(added.Single(n => n.TypeId == "math.mul").Id, 0, IsOutput: true),
            ],
            ignoreOrder: true);

        pasted.Exposed.ShouldNotContain(
            s => s.Node == osc.Id || s.Node == gain.Id,
            "a socket left pointing at the module it was copied from is a socket onto the wrong box");
    }

    /// <summary>
    /// The round trip through text is how a fragment actually travels, so a box
    /// that does not survive it is a box that cannot be pasted into the next
    /// window along.
    /// </summary>
    [Fact]
    public void A_box_survives_being_written_out_and_read_back()
    {
        var patch = Chain(out var time, out var osc, out _, out _);

        patch.Group([time.Id, osc.Id]).ShouldNotBeNull().Rename("Voice");

        var text = PatchIO.ToJson(
            PatchClipboard.Copy(patch, [time.Id, osc.Id]), NodeCatalog.BuiltIn);

        var loaded = PatchIO.Read(text, NodeCatalog.BuiltIn);
        loaded.IsComplete.ShouldBeTrue();

        var fresh = new PatchBuilder(NodeCatalog.BuiltIn);
        fresh.Add(NodeCatalog.OutputTypeId, 900, 0);

        var added = PatchClipboard.Paste(fresh.Patch, loaded.Patch);
        var pasted = fresh.Patch.GroupOf(added[0].Id).ShouldNotBeNull();

        pasted.Title().ShouldBe("Voice");
        pasted.Members.Order().ShouldBe(added.Select(n => n.Id).Order());
    }

    /// <summary>
    /// The rule against a box round one module holds on the way in as well:
    /// a fragment somebody hand-edited cannot paste the picture Ctrl+G declines
    /// to draw.
    /// </summary>
    [Fact]
    public void A_box_worn_down_to_one_member_is_not_pasted()
    {
        var patch = Chain(out var time, out var osc, out _, out _);
        patch.Group([time.Id, osc.Id]);

        var fragment = PatchClipboard.Copy(patch, [time.Id, osc.Id]);
        fragment.Nodes.RemoveAll(n => n.TypeId == "time");

        var fresh = new PatchBuilder(NodeCatalog.BuiltIn);

        PatchClipboard.Paste(fresh.Patch, fragment).Count.ShouldBe(1);
        fresh.Patch.Groups.ShouldBeNull();
    }

    /// <summary>
    /// A group says nothing about what the patch computes, so a fragment holding
    /// one compiles to exactly what the same modules compile to without it.
    /// </summary>
    [Fact]
    public void Pasting_a_group_changes_nothing_about_the_sound()
    {
        var patch = Chain(out var time, out var osc, out var gain, out _);
        patch.Group([time.Id, osc.Id]);

        var fresh = new PatchBuilder(NodeCatalog.BuiltIn);
        var sink = fresh.Add(NodeCatalog.OutputTypeId, 900, 0);

        var added = PatchClipboard.Paste(
            fresh.Patch, PatchClipboard.Copy(patch, [time.Id, osc.Id, gain.Id]));

        fresh.Wire(added.Single(n => n.TypeId == "math.mul"), 0, sink, NodeCatalog.OutputLeftPort);

        fresh.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Ops
            .ShouldBe(patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Ops);
    }
}
