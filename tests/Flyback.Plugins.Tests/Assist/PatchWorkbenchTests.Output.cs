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
    // --- what comes out -----------------------------------------------------

    [Fact]
    public async Task A_snapshot_is_laid_out_left_to_right_with_the_screen_last()
    {
        var patch = (await Lit()).Snapshot();

        var knob = patch.Nodes.Single(n => n.TypeId == "value");
        var screen = patch.Nodes.Single(n => n.TypeId == NodeCatalog.OutputTypeId);

        screen.X.ShouldBeGreaterThan(knob.X);
    }

    [Fact]
    public async Task A_snapshot_is_a_copy_that_the_workbench_cannot_reach()
    {
        var bench = await Lit();
        var first = bench.Snapshot();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");

        first.Nodes.Count.ShouldBe(2);
        bench.Snapshot().Nodes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_snapshot_records_the_plugin_a_module_came_from()
    {
        var catalog = NodeCatalog.BuiltIn.With(Extras, [Doubler]).Catalog;
        var bench = new PatchWorkbench(catalog, new Patch());

        await Call(bench, "add_module", """{"type_id":"test.extras.double"}""");

        bench.Snapshot().Requires.ShouldHaveSingleItem().ShouldBe(Extras);
    }
}
