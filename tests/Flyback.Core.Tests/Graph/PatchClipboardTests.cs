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
        var before = PatchIo.ToJson(patch, NodeCatalog.BuiltIn);

        PatchClipboard.Copy(patch, [time.Id, osc.Id]);

        PatchIo.ToJson(patch, NodeCatalog.BuiltIn).ShouldBe(before);
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

        var text = PatchIo.ToJson(
            PatchClipboard.Copy(patch, [time.Id, osc.Id, gain.Id]), NodeCatalog.BuiltIn);

        var loaded = PatchIo.Read(text, NodeCatalog.BuiltIn);
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

        var text = PatchIo.ToJson(PatchClipboard.Copy(patch, [time.Id]), NodeCatalog.BuiltIn);

        PatchIo.Read(text, NodeCatalog.BuiltIn).Patch.Version.ShouldBe(PatchIo.FormatVersion);
    }

    [Fact]
    public void Pasting_nothing_adds_nothing() =>
        PatchClipboard.Paste(new Patch(), new Patch()).ShouldBeEmpty();
}
