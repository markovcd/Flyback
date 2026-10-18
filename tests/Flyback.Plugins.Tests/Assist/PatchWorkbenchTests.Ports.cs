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
    /// The point of naming sockets: a model says "value" and the wire lands on
    /// input 2, without anyone counting sockets.
    /// </summary>
    [Fact]
    public async Task A_port_named_in_a_wire_lands_on_the_right_index()
    {
        var bench = Bench();

        var wired = await Write(bench, "let sine1 = sine()\nlet tint1 = color.hsv(value: sine1)\ntint1 |> out.color");
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

        var wired = await Write(bench, "let sine1 = sine()\nlet tint1 = color.hsv(brightness: sine1)\ntint1 |> out.color");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("hue");
        wired.Text.ShouldContain("saturation");
        wired.Text.ShouldContain("value");
    }

    [Fact]
    public async Task A_source_with_one_output_needs_no_port_named()
    {
        var wired = await Write(Bench(), "let sine1 = sine()\nsine1 |> out.color");
        wired.Ok.ShouldBeTrue(wired.Text);
    }
}
