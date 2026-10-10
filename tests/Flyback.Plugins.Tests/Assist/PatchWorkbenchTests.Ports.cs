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
    // --- ports by name ------------------------------------------------------

    /// <summary>
    /// The point of the whole handle-and-name protocol: a model says "value" and
    /// the wire lands on input 2, without anyone counting sockets.
    /// </summary>
    [Fact]
    public async Task A_port_named_in_a_wire_lands_on_the_right_index()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"color.hsv","handle":"tint1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"tint1","to_port":"value"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var patch = bench.Snapshot();
        var tint = patch.Nodes.Single(n => n.TypeId == "color.hsv");

        // hue, saturation, value — "value" is the third.
        patch.IncomingTo(tint.Id, 2).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_port_that_does_not_exist_comes_back_with_the_ones_that_do()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"color.hsv","handle":"tint1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"tint1","to_port":"brightness"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("hue");
        wired.Text.ShouldContain("saturation");
        wired.Text.ShouldContain("value");
    }

    [Fact]
    public async Task A_source_with_one_output_needs_no_port_named()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"output1","to_port":"color"}""");
        wired.Ok.ShouldBeTrue(wired.Text);
    }

    [Fact]
    public async Task A_source_with_several_outputs_is_asked_which_one()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"coord","handle":"coords1"}""");

        var wired = await Call(bench, "connect", """{"from":"coords1","to":"output1","to_port":"color"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("radius");
    }
}
