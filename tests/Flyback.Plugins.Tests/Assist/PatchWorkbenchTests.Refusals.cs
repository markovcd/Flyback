using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- what the graph will not allow --------------------------------------

    [Fact]
    public async Task A_second_wire_into_one_input_says_what_it_replaced()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.saw","handle":"saw1"}""");

        await Call(bench, "connect", """{"from":"sine1","to":"output1","to_port":"color"}""");
        var second = await Call(bench, "connect", """{"from":"saw1","to":"output1","to_port":"color"}""");

        second.Ok.ShouldBeTrue(second.Text);
        second.Text.ShouldContain("replacing sine1");
        bench.Snapshot().Connections.Count.ShouldBe(1);
    }

    /// <summary>
    /// <see cref="Patch.Connect"/> refuses a self-wire by quietly doing nothing.
    /// Quiet is wrong here: an assistant told nothing happened will try it again.
    /// </summary>
    [Fact]
    public async Task Wiring_a_module_to_itself_is_refused_out_loud()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add1"}""");
        var wired = await Call(bench, "connect", """{"from":"add1","to":"add1","to_port":"a"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("feedback");
    }

    /// <summary>
    /// A fault is answered by whatever the assistant does next, rather than
    /// waiting for it to ask — the call that finished the patch is where it is
    /// said.
    /// </summary>
    [Fact]
    public async Task A_fault_is_reported_by_the_next_thing_the_assistant_does()
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");

        var wired = await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"color"}""");

        wired.Text.ShouldContain("gone.wav");
    }

    /// <summary>
    /// A cycle is not a fault: the wire that closes it carries the evaluation
    /// before, so the assistant may draw one and is told nothing is wrong.
    /// </summary>
    [Fact]
    public async Task A_cycle_the_assistant_draws_is_not_complained_about()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add1"}""");
        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add2"}""");

        await Call(bench, "connect", """{"from":"add2","to":"output1","to_port":"color"}""");
        await Call(bench, "connect", """{"from":"add1","to":"add2","to_port":"a"}""");
        var closed = await Call(bench, "connect", """{"from":"add2","to":"add1","to_port":"a"}""");

        closed.Ok.ShouldBeTrue(closed.Text);
        closed.Text.ShouldNotContain("feeds back into itself");
    }

    [Fact]
    public async Task A_patch_that_reaches_nothing_says_so_on_the_first_edit()
    {
        var added = await Call(Bench(), "add_module", """{"type_id":"osc.sine"}""");

        added.Text.ShouldContain("Nothing is wired into the Output");
    }

    /// <summary>
    /// The refusal names the sink that is already there, because the useful next
    /// move is to wire into it — an assistant told only "no" removes the one it
    /// has and adds it again.
    /// </summary>
    [Fact]
    public async Task Adding_the_output_is_refused_and_the_one_already_there_named()
    {
        var bench = await Lit();

        var again = await Call(bench, "add_module", """{"type_id":"output","handle":"output2"}""");

        again.Ok.ShouldBeFalse();
        again.Text.ShouldContain("output1");
        bench.Snapshot().Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId).ShouldBe(1);
    }

    /// <summary>
    /// The refusal names both sockets, because an assistant that wanted "an
    /// audio output" needs telling that what it is after is a socket on the
    /// block already in front of it.
    /// </summary>
    [Fact]
    public async Task The_refusal_says_which_sockets_to_use_instead()
    {
        var again = await Call(await Lit(), "add_module", """{"type_id":"output"}""");

        again.Text.ShouldContain("color");
        again.Text.ShouldContain("left");
    }

    [Fact]
    public async Task Every_bench_starts_with_its_output_already_placed()
    {
        var bench = Bench();

        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    [Fact]
    public async Task A_refused_sink_leaves_the_patch_as_it_was()
    {
        var bench = await Lit();
        var edits = bench.Edits;

        await Call(bench, "add_module", """{"type_id":"output"}""");

        // A refusal that had half-placed the module would leave a handle
        // reserved for a node that is not there.
        bench.Edits.ShouldBe(edits);
        bench.Snapshot().Nodes.Count.ShouldBe(2);
    }
}
