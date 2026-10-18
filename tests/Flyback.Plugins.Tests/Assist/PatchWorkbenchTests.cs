using System.Globalization;
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

/// <summary>
/// The workbench is the whole of what an assistant can do to a patch, and none
/// of it involves a network or a provider — so all of it is tested here, off
/// disk and off the wire.
/// </summary>
/// <remarks>
/// Built against <see cref="NodeCatalog.BuiltIn"/> rather than
/// <see cref="NodeCatalog.Current"/>, so these say the same thing whatever
/// plugins happen to be installed in the test host.
/// </remarks>
public partial class PatchWorkbenchTests
{
    private static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    private static readonly NodeDef Doubler = new(
        "test.extras.double", "Double", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    private static PatchWorkbench Bench(
        WorkbenchLimits? limits = null,
        bool vision = true,
        Listener hearing = Listener.Another) =>
        new(NodeCatalog.BuiltIn, new Patch(), vision, hearing, limits);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static Task<ToolOutcome> Write(PatchWorkbench bench, string source) =>
        Call(bench, "write_patch", JsonSerializer.Serialize(new { source }));

    private static Task<ToolOutcome> Edit(PatchWorkbench bench, string find, string replace) =>
        Call(bench, "edit_patch", JsonSerializer.Serialize(new { edits = new[] { new { find, replace } } }));

    /// <summary>Builds the smallest patch that renders: one knob into the screen.</summary>
    private static async Task<PatchWorkbench> Lit(float value = 0f)
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
    private static async Task<PatchWorkbench> Heard()
    {
        var bench = Bench();

        var written = await Write(bench, """
            let tone1 = t |> sine(freq: 440)
            tone1 |> out.left
            """);

        written.Ok.ShouldBeTrue(written.Text);

        return bench;
    }
}
