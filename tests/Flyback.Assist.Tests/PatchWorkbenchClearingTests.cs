using System.Text.Json;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary>An empty <c>write_patch</c> clears the patch, and the cleared patch can be proposed.</summary>
public class PatchWorkbenchClearingTests
{
    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments) =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    [Fact]
    public async Task An_empty_source_clears_the_patch_and_the_clearing_can_be_proposed()
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);
        (await Call(bench, "write_patch", """{"source": "t |> sine(freq: 220) |> out.left"}""")).Ok.ShouldBeTrue();

        var cleared = await Call(bench, "write_patch", """{"source": ""}""");

        cleared.Ok.ShouldBeTrue(cleared.Text);
        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);

        var proposed = await Call(bench, "propose", """{"summary": "a clean slate"}""");

        proposed.Ok.ShouldBeTrue(proposed.Text);
    }

    [Fact]
    public async Task A_missing_source_is_still_refused()
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);

        var outcome = await Call(bench, "write_patch", "{}");

        outcome.Ok.ShouldBeFalse();
        outcome.Text.ShouldContain("'source' is required");
    }

    [Fact]
    public async Task Modules_with_nothing_wired_to_the_output_are_still_not_proposed()
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);
        (await Call(bench, "write_patch", """{"source": "let lfo = sine()"}""")).Ok.ShouldBeTrue();

        var proposed = await Call(bench, "propose", """{"summary": "an LFO"}""");

        proposed.Ok.ShouldBeFalse();
        proposed.Text.ShouldContain("nothing is wired into the Output");
    }
}
