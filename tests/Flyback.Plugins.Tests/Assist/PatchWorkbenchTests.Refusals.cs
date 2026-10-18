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

    /// <summary>
    /// A fault is answered by whatever the assistant does next, rather than
    /// waiting for it to ask — the call that finished the patch is where it is
    /// said.
    /// </summary>
    [Fact]
    public async Task A_fault_is_reported_by_the_next_thing_the_assistant_does()
    {
        var bench = Bench();

        await Write(bench, "let clip1 = sample(\"gone.wav\")");

        var wired = await Edit(bench, "let clip1 = sample(\"gone.wav\")", "let clip1 = sample(\"gone.wav\")\nclip1.out |> out.color");

        wired.Text.ShouldContain("gone.wav");
    }

    /// <summary>
    /// A cycle is not a fault: the wire that closes it carries the evaluation
    /// before, so the assistant may draw one and is told nothing is wrong.
    /// </summary>
    [Fact]
    public async Task A_cycle_the_assistant_draws_is_not_complained_about()
    {
        var closed = await Write(Bench(), "let sum = square(freq: 110) * 0.06 |> add(a: _)\nsum.b <- sum * 0.94\nsum |> out.left");

        closed.Ok.ShouldBeTrue(closed.Text);
        closed.Text.ShouldNotContain("feeds back into itself");
    }

    [Fact]
    public async Task A_patch_that_reaches_nothing_says_so_on_the_first_edit()
    {
        var added = await Write(Bench(), "let tone = sine()");

        added.Text.ShouldContain("Nothing is wired into the Output");
    }

    /// <summary>A patch has exactly one Output, so text that writes a second is refused.</summary>
    [Fact]
    public async Task Writing_a_second_output_is_refused()
    {
        var bench = await Lit();

        var again = await Write(bench, "let output2 = output()\nlet knob1 = value()\nknob1 |> out.color");

        again.Ok.ShouldBeFalse();
        bench.Snapshot().Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId).ShouldBe(1);
    }

    [Fact]
    public void Every_bench_starts_with_its_output_already_placed()
    {
        var bench = Bench();

        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    [Fact]
    public async Task A_refused_sink_leaves_the_patch_as_it_was()
    {
        var bench = await Lit();
        var edits = bench.Edits;

        await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\nlet output2 = output()");

        bench.Edits.ShouldBe(edits);
        bench.Snapshot().Nodes.Count.ShouldBe(2);
    }
}
