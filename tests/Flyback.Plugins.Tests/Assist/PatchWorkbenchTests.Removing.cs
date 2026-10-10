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
    // --- unwiring and removing ----------------------------------------------

    [Fact]
    public async Task Unwiring_an_input_puts_it_back_on_its_knob()
    {
        var bench = await Lit(0.25f);

        var cut = await Call(bench, "disconnect", """{"handle":"output1","port":"color"}""");

        cut.Ok.ShouldBeTrue(cut.Text);
        bench.Snapshot().Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_module_takes_its_wires_with_it()
    {
        var bench = await Lit();

        var gone = await Call(bench, "remove_module", """{"handle":"knob1"}""");

        gone.Ok.ShouldBeTrue(gone.Text);
        gone.Text.ShouldContain("One wire");

        var patch = bench.Snapshot();
        patch.Nodes.Count.ShouldBe(1);
        patch.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_the_output_is_refused()
    {
        var bench = await Lit();

        var removed = await Call(bench, "remove_module", """{"handle":"output1"}""");

        removed.Ok.ShouldBeFalse(removed.Text);
        bench.Snapshot().Connections.Count.ShouldBe(1);
        (await Call(bench, "connect", """{"from":"knob1","to":"output1","to_port":"left"}""")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Switching_a_module_off_leaves_it_in_the_patch()
    {
        var bench = await Lit();

        var off = await Call(bench, "switch_module", """{"handle":"knob1"}""");

        off.Ok.ShouldBeTrue(off.Text);

        var patch = bench.Snapshot();

        patch.Nodes.Count.ShouldBe(2);
        patch.Connections.ShouldHaveSingleItem();
        patch.Nodes.Single(n => n.TypeId == "value").Off.ShouldBeTrue();

        var on = await Call(bench, "switch_module", """{"handle":"knob1","off":false}""");

        on.Ok.ShouldBeTrue(on.Text);
        bench.Snapshot().Nodes.Single(n => n.TypeId == "value").Off.ShouldBeFalse();
    }

    [Fact]
    public async Task The_Output_cannot_be_switched_off()
    {
        var bench = await Lit();

        var refused = await Call(bench, "switch_module", """{"handle":"output1"}""");

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("volume");
        bench.Snapshot().Output.Off.ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_puts_back_the_patch_that_was_open()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        await Call(bench, "reset");

        // Back to what was open, which is never nothing: the Output survives a
        // reset because every patch has one.
        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }
}
