using System.Text.Json;
using Flyback.Assist;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary><c>measure</c> tells the assistant what an output carries, in numbers, wired or not.</summary>
public class PatchWorkbenchMeasureTests
{
    private static PatchWorkbench Bench() => new(NodeCatalog.BuiltIn, new Patch(), vision: false);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static async Task Write(PatchWorkbench bench, string source) =>
        (await Call(bench, "write_patch", JsonSerializer.Serialize(new { source }))).Ok.ShouldBeTrue(source);

    [Fact]
    public async Task An_unwired_oscillator_is_measured_at_its_pitch()
    {
        var bench = Bench();

        await Write(bench, "let lfo1 = sine(freq: 3)");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"]}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("lfo1.out");
        measured.Text.ShouldContain("3 Hz");
    }

    [Fact]
    public async Task A_measurement_can_start_anywhere_on_the_timeline()
    {
        var bench = Bench();

        await Write(bench, "let lfo1 = sine(freq: 3)");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"],"from":3000}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("from 3000s");
        measured.Text.ShouldContain("3 Hz");
    }

    [Fact]
    public async Task A_measurement_can_run_for_a_minute()
    {
        var bench = Bench();

        await Write(bench, "let lfo1 = sine(freq: 0.1)");

        var measured = await Call(bench, "measure", """{"handles":["lfo1"],"seconds":60}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("Measured 60s");
        measured.Text.ShouldContain("0.1 Hz");
    }

    /// <summary>Every output of a big patch over a long window would take minutes, so the window is cut to fit and said to be.</summary>
    [Fact]
    public async Task A_window_over_more_outputs_than_the_cap_allows_is_shortened_and_says_so()
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false, limits: new WorkbenchLimits(MeasuredOutputSeconds: 8d));

        await Write(bench, "let a = sine(freq: 3)\nlet b = sine(freq: 5)\nlet c = sine(freq: 7)\nlet d = sine(freq: 9)");

        var measured = await Call(bench, "measure", """{"seconds":60}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("Measured 2s");
        measured.Text.ShouldContain("Shortened from the 60s asked: watching 4 outputs");
        measured.Text.ShouldContain("a.out");
    }

    [Fact]
    public async Task A_window_over_outputs_named_within_the_cap_runs_as_long_as_asked()
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false, limits: new WorkbenchLimits(MeasuredOutputSeconds: 8d));

        await Write(bench, "let a = sine(freq: 3)\nlet b = sine(freq: 5)\nlet c = sine(freq: 7)\nlet d = sine(freq: 9)");

        var measured = await Call(bench, "measure", """{"seconds":8,"handles":["a.out"]}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("Measured 8s");
        measured.Text.ShouldNotContain("Shortened");
    }

    /// <summary>A socket whose name has a space is named as the language writes it, with an underscore, both ways.</summary>
    [Fact]
    public async Task An_output_is_named_and_printed_as_the_language_writes_it()
    {
        var bench = Bench();

        await Write(bench, "let bus = desk(left_1: sine(freq: 3))");

        var measured = await Call(bench, "measure", """{"handles":["bus.bus_left"],"seconds":0.25}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("bus.bus_left");
        measured.Text.ShouldNotContain("bus left");

        var whole = await Call(bench, "measure", """{"handles":["bus"],"seconds":0.25}""");

        whole.Text.ShouldContain("bus.bus_right");
    }

    [Fact]
    public async Task A_handle_nobody_has_is_refused()
    {
        var measured = await Call(Bench(), "measure", """{"handles":["nonesuch"]}""");

        measured.Ok.ShouldBeFalse();
        measured.Text.ShouldContain("nonesuch");
    }

    [Fact]
    public async Task One_output_is_named_as_module_dot_output()
    {
        var bench = Bench();

        await Write(bench, "let riff = notes(rate: 4) [ C4 E4 G4 C5 ]");

        var measured = await Call(bench, "measure", """{"handles":["riff.gate"]}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("riff.gate");
        measured.Text.ShouldNotContain("riff.out");
        measured.Text.ShouldNotContain("riff.index");
    }

    [Fact]
    public async Task An_output_no_module_has_is_refused_with_the_ones_it_has()
    {
        var bench = Bench();

        await Write(bench, "let riff = notes(rate: 4) [ C4 E4 G4 C5 ]");

        var measured = await Call(bench, "measure", """{"handles":["riff.nonesuch"]}""");

        measured.Ok.ShouldBeFalse();
        measured.Text.ShouldContain("'riff' has no output called 'nonesuch'. It has out, gate, index.");
    }

    [Fact]
    public async Task An_output_of_a_module_nobody_has_is_refused_naming_the_module()
    {
        var measured = await Call(Bench(), "measure", """{"handles":["nonesuch.out"]}""");

        measured.Ok.ShouldBeFalse();
        measured.Text.ShouldContain("no module called 'nonesuch'");
    }

    [Fact]
    public async Task A_let_name_for_one_output_measures_that_output()
    {
        var bench = Bench();

        await Write(bench, "let shine = noise()\nlet hat = shine.pink");

        var measured = await Call(bench, "measure", """{"handles":["hat"]}""");

        measured.Ok.ShouldBeTrue(measured.Text);
        measured.Text.ShouldContain("shine.pink");
        measured.Text.ShouldNotContain("shine.white");
    }

    [Fact]
    public async Task A_note_sequencer_says_which_notes_it_holds_by_name()
    {
        var bench = Bench();

        await Write(bench, "let riff = notes(rate: 4) [ C4 E4 G4 C5 ]");

        var measured = await Call(bench, "measure", """{"handles":["riff.out"]}""");

        measured.Text.ShouldContain("holds C4 (60), E4 (64), G4 (67), C5 (72)");
    }

    [Fact]
    public async Task An_output_named_twice_is_measured_once()
    {
        var bench = Bench();

        await Write(bench, "let riff = notes(rate: 4) [ C4 E4 G4 C5 ]");

        var measured = await Call(bench, "measure", """{"handles":["riff.out","riff.out"]}""");

        measured.Text.Split("riff.out").Length.ShouldBe(2);
    }

    [Fact]
    public void Measure_is_offered_to_a_model_that_can_neither_see_nor_hear()
    {
        Bench().Tools.ShouldContain(tool => tool.Name == "measure");
    }
}
