using System.Globalization;
using System.Text.Json;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Shouldly;

namespace Flyback.Assist.Tests;

/// <summary>
/// What the workbench tests build their benches with. The workbench is the whole of
/// what an assistant can do to a patch, and none of it involves a network or a
/// provider, so all of it is tested off disk and off the wire.
/// </summary>
/// <remarks>
/// Built against <see cref="NodeCatalog.BuiltIn"/> rather than
/// <see cref="NodeCatalog.Current"/>, so these say the same thing whatever
/// plugins happen to be installed in the test host.
/// </remarks>
internal static class Workbenches
{
    internal static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    internal static readonly NodeDef Doubler = new(
        "test.extras.double", "Double", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    internal static PatchWorkbench Bench(
        WorkbenchLimits? limits = null,
        bool vision = true,
        Listener hearing = Listener.Another) =>
        new(NodeCatalog.BuiltIn, new Patch(), vision, hearing, limits);

    internal static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    internal static Task<ToolOutcome> Write(PatchWorkbench bench, string source) =>
        Call(bench, "write_patch", JsonSerializer.Serialize(new { source }));

    internal static Task<ToolOutcome> Edit(PatchWorkbench bench, string find, string replace) =>
        Call(bench, "edit_patch", JsonSerializer.Serialize(new { edits = new[] { new { find, replace } } }));

    /// <summary>Builds the smallest patch that renders: one knob into the screen.</summary>
    internal static async Task<PatchWorkbench> Lit(float value = 0f)
    {
        var bench = Bench();

        var written = await Write(bench, string.Create(CultureInfo.InvariantCulture, $"""
            let knob1 = value(value: {value})
            knob1 |> out.color
            """));

        written.Ok.ShouldBeTrue(written.Text);

        return bench;
    }

    /// <summary>
    /// Builds the smallest patch that actually sounds, and has no screen at
    /// all. The clock is not decoration: an oscillator accumulates how far its
    /// 'in' moved, so one without it is silent however its freq is set.
    /// </summary>
    internal static async Task<PatchWorkbench> Heard()
    {
        var bench = Bench();

        var written = await Write(bench, """
            let tone1 = t |> sine(freq: 440)
            tone1 |> out.left
            """);

        written.Ok.ShouldBeTrue(written.Text);

        return bench;
    }

    /// <summary>The scale the bench's one quantizer holds.</summary>
    internal static IReadOnlyList<int> Scale(PatchWorkbench bench) =>
        ScaleExtra.Of(bench.Snapshot().Nodes
            .Single(n => n.TypeId == NodeCatalog.QuantiserTypeId));
}
