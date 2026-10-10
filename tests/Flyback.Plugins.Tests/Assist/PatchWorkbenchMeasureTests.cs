using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary><c>measure</c> tells the assistant what an output carries, in numbers, wired or not.</summary>
public class PatchWorkbenchMeasureTests
{
    private static PatchWorkbench Bench() => new(NodeCatalog.BuiltIn, new Patch(), vision: false, Listener.None);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    [Fact]
    public async Task An_unwired_oscillator_is_measured_at_its_pitch()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"lfo1","knobs":[{"port":"freq","value":3}]}""");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"]}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("lfo1.out");
        measured.Text.ShouldContain("3 Hz");
    }

    [Fact]
    public async Task A_measurement_can_start_anywhere_on_the_timeline()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"lfo1","knobs":[{"port":"freq","value":3}]}""");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"],"from":3000}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("from 3000s");
        measured.Text.ShouldContain("3 Hz");
    }

    [Fact]
    public async Task A_measurement_can_run_for_a_minute()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"lfo1","knobs":[{"port":"freq","value":0.1}]}""");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"],"seconds":60}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("Measured 60s");
        measured.Text.ShouldContain("0.1 Hz");
    }

    [Fact]
    public async Task A_handle_nobody_has_is_refused()
    {
        var measured = await Call(Bench(), "measure", """{"handles":["nonesuch"]}""");

        measured.Ok.ShouldBeFalse();
        measured.Text.ShouldContain("nonesuch");
    }

    [Fact]
    public void Measure_is_offered_to_a_model_that_can_neither_see_nor_hear()
    {
        Bench().Tools.ShouldContain(tool => tool.Name == "measure");
    }
}
