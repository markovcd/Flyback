using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- unwiring and removing ----------------------------------------------

    [Fact]
    public async Task Unwiring_an_input_puts_it_back_on_its_knob()
    {
        var bench = await Lit(0.25f);

        var cut = await Edit(bench, "knob1 |> out.color", "");

        cut.Ok.ShouldBeTrue(cut.Text);
        bench.Snapshot().Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_module_takes_its_wires_with_it()
    {
        var bench = await Lit();

        var gone = await Edit(bench, "let knob1 = value(value: 0)\nknob1 |> out.color", "");

        gone.Ok.ShouldBeTrue(gone.Text);

        var patch = bench.Snapshot();
        patch.Nodes.Count.ShouldBe(1);
        patch.Connections.ShouldBeEmpty();
    }

    /// <summary>A patch written with no <c>out</c> in it still has its Output, which nothing removes.</summary>
    [Fact]
    public async Task Writing_the_output_out_of_the_text_leaves_it_in_place()
    {
        var bench = await Lit();

        (await Write(bench, "let knob1 = value()")).Ok.ShouldBeTrue();

        bench.Snapshot().Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId).ShouldBe(1);
        (await Edit(bench, "let knob1 = value()", "let knob1 = value()\nknob1 |> out.left")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Switching_a_module_off_leaves_it_in_the_patch()
    {
        var bench = await Lit();

        var off = await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\noff knob1");

        off.Ok.ShouldBeTrue(off.Text);

        var patch = bench.Snapshot();

        patch.Nodes.Count.ShouldBe(2);
        patch.Connections.ShouldHaveSingleItem();
        patch.Nodes.Single(n => n.TypeId == "value").Off.ShouldBeTrue();

        var on = await Edit(bench, "\noff knob1", "");

        on.Ok.ShouldBeTrue(on.Text);
        bench.Snapshot().Nodes.Single(n => n.TypeId == "value").Off.ShouldBeFalse();
    }

    [Fact]
    public async Task The_Output_cannot_be_switched_off()
    {
        var bench = await Lit();

        var refused = await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\noff out");

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("volume");
        bench.Snapshot().Output.Off.ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_puts_back_the_patch_that_was_open()
    {
        var bench = Bench();

        await Write(bench, "let tone = sine()");
        await Call(bench, "reset");

        // Back to what was open, which is never nothing: the Output survives a
        // reset because every patch has one.
        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }
}
