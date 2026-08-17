using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A patch file is the only thing here that outlives the process, so a
/// serialisation regression is the one class of bug that costs somebody work
/// they cannot redo. These render a patch to JSON and read it back, and check
/// the document is the same on the other side.
/// </summary>
/// <remarks>
/// <see cref="PatchProvenanceTests"/> covers the header that names the plugins
/// a file needs. This covers the body: the modules, where they sit, what their
/// knobs are set to, and the wires between them.
/// </remarks>
public class PatchIoTests
{
    /// <summary>
    /// A patch with something of everything in it: several modules, knobs moved
    /// off their defaults, positions, a fan-out from one output to two inputs,
    /// and a wire from a port that is not port 0.
    /// </summary>
    private static Patch Assorted()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var coords = builder.Add("coord", 40, 12);
        var tint = builder.Add("colour.hsv", -180.5, 260, (1, 0.25f), (2, 0.75f));
        var gain = builder.Add("colour.gain", 90, 300, (1, 1.5f), (2, -0.125f));
        var screen = builder.Add(NodeCatalog.OutputTypeId, 520, 140);

        builder
            .Wire(coords, 0, tint, 0)
            .Wire(coords, 3, gain, 1)
            .Wire(tint, 0, gain, 0)
            .Wire(gain, 0, screen, 0);

        return builder.Patch;
    }

    private static Patch RoundTrip(Patch patch)
    {
        var loaded = PatchIo.Read(PatchIo.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn);

        loaded.IsComplete.ShouldBeTrue(loaded.Summary);
        return loaded.Patch;
    }

    [Fact]
    public void Every_module_comes_back_with_its_identity_and_its_place()
    {
        var before = Assorted();

        var after = RoundTrip(before);

        after.Nodes.Count.ShouldBe(before.Nodes.Count);

        for (var i = 0; i < before.Nodes.Count; i++)
        {
            after.Nodes[i].Id.ShouldBe(before.Nodes[i].Id);
            after.Nodes[i].TypeId.ShouldBe(before.Nodes[i].TypeId);
            after.Nodes[i].X.ShouldBe(before.Nodes[i].X);
            after.Nodes[i].Y.ShouldBe(before.Nodes[i].Y);
        }
    }

    /// <summary>
    /// The knobs are most of what a patch is (ADR-0009), so losing them would
    /// lose the patch while leaving a file that still opens.
    /// </summary>
    [Fact]
    public void Every_knob_comes_back_at_the_value_it_was_left_at()
    {
        var before = Assorted();

        var after = RoundTrip(before);

        for (var i = 0; i < before.Nodes.Count; i++)
            after.Nodes[i].InputValues.ShouldBe(before.Nodes[i].InputValues, $"node {i} ({before.Nodes[i].TypeId})");
    }

    /// <summary>
    /// Both port indices have to survive, not just the pair of nodes: a wire
    /// that came back attached to port 0 of the right module would be a patch
    /// that opens looking correct and sounds wrong.
    /// </summary>
    [Fact]
    public void Every_wire_comes_back_between_the_same_two_sockets()
    {
        var before = Assorted();

        var after = RoundTrip(before);

        after.Connections.ShouldBe(before.Connections);
    }

    [Fact]
    public void A_patch_that_survived_the_trip_still_compiles_to_the_same_program()
    {
        var before = Assorted();

        var after = RoundTrip(before);

        var expected = before.CompileForVideo(NodeCatalog.BuiltIn);
        var actual = after.CompileForVideo(NodeCatalog.BuiltIn);

        actual.Issues.ShouldBeEmpty();
        actual.Program.Ops.ShouldBe(expected.Program.Ops);
        actual.Program.RegisterCount.ShouldBe(expected.Program.RegisterCount);
        actual.Program.OutputBase.ShouldBe(expected.Program.OutputBase);
    }

    /// <summary>
    /// A sequencer's tune is the one thing a node carries that is not a knob
    /// (ADR-0038), so it is the one thing a round trip could quietly drop.
    /// </summary>
    [Fact]
    public void A_sequencers_notes_survive_the_trip()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sequencer = builder.Add("seq.notes", 0, 0);

        sequencer.Steps = [new Step(60f), new Step(62f, 2.5f), new Step(64f, 1f, 0.25f)];

        var after = RoundTrip(builder.Patch).Nodes.Single(n => n.TypeId == "seq.notes");

        after.Steps.ShouldBe(sequencer.Steps);
    }

    [Fact]
    public void A_full_length_tune_survives_the_trip()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sequencer = builder.Add("seq.values", 0, 0);

        sequencer.Steps = [.. Enumerable.Range(0, NodeCatalog.MaxSteps).Select(s => new Step(s / 32f))];

        RoundTrip(builder.Patch).Nodes
            .Single(n => n.TypeId == "seq.values").Steps!.Count.ShouldBe(NodeCatalog.MaxSteps);
    }

    /// <summary>
    /// Written only where there is a tune to write, so every other module's JSON
    /// reads exactly as it always did.
    /// </summary>
    [Fact]
    public void A_module_with_no_notes_writes_none()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("osc.sine", 0, 0);

        PatchIo.ToJson(builder.Patch, NodeCatalog.BuiltIn).ShouldNotContain("Steps");
    }

    /// <summary>
    /// Empty means nothing patched, not nothing at all: reading gives a patch
    /// its Output if the file did not carry one, so the emptiest thing that can
    /// come back is still an instrument with somewhere to go.
    /// </summary>
    [Fact]
    public void An_empty_patch_round_trips_as_the_output_alone()
    {
        var after = RoundTrip(new Patch());

        after.Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
        after.Connections.ShouldBeEmpty();
    }

    /// <summary>
    /// ADR-0020's forwards compatibility, through the file rather than around
    /// it: a patch saved before a module gained an input has fewer stored values
    /// than the module now has sockets, and that shortfall has to survive the
    /// trip for the compiler's fallback to be the thing that fills it in.
    /// </summary>
    [Fact]
    public void A_node_with_fewer_stored_values_than_sockets_keeps_the_shortfall()
    {
        var patch = new PatchBuilder(NodeCatalog.BuiltIn).Patch;
        var osc = NodeInstance.Create(NodeCatalog.BuiltIn.Require("osc.sine"), 0, 0);

        osc.InputValues = [0.25f, 1f];
        patch.Nodes.Add(osc);

        RoundTrip(patch).Nodes
            .Single(n => n.TypeId == "osc.sine")
            .InputValues.ShouldBe([0.25f, 1f]);
    }

    /// <summary>
    /// Guids are the one part of a node that has to be stable in text: they are
    /// what the wires refer to, so a format that wrote them differently on the
    /// way out than it read them on the way in would drop every connection.
    /// </summary>
    [Fact]
    public void Wires_still_find_their_nodes_after_the_trip()
    {
        var after = RoundTrip(Assorted());

        after.Connections.ShouldNotBeEmpty();

        foreach (var wire in after.Connections)
        {
            after.Find(wire.SourceNode).ShouldNotBeNull($"source of {wire}");
            after.Find(wire.TargetNode).ShouldNotBeNull($"target of {wire}");
        }
    }
}
