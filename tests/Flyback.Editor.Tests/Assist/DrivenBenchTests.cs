using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>
/// A program's tool calls run on a copy of the canvas, as the column's do: only a proposal
/// goes on the canvas, as one change, and the handles it chose hold between calls.
/// </summary>
public sealed class DrivenBenchTests
{
    private const string Tone = "let tone = t |> sine(freq: 440)\\ntone |> out.left";

    private readonly FakeAssistantEditor editor = new(Presets.Empty(NodeCatalog.BuiltIn));

    private DrivenBench Bench() => new(editor, NodeCatalog.BuiltIn, () => ProsePolicy.Default, () => Presets.All);

    [Fact]
    public async Task An_edit_stays_off_the_canvas_until_it_is_proposed()
    {
        var bench = Bench();

        var outcome = await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Applied.ShouldBe(0);
        editor.Current.Nodes.ShouldNotContain(node => node.TypeId == "osc.sine");

        var proposed = await Call(bench, "propose", """{"summary": "a 440 Hz tone"}""");

        proposed.Ok.ShouldBeTrue(proposed.Text);
        proposed.Text.ShouldContain("on the person's canvas");
        bench.Proposed.ShouldBe("a 440 Hz tone");
        editor.Applied.ShouldBe(1);
        editor.Current.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
    }

    [Fact]
    public void The_briefing_describes_the_modules_the_editor_has()
    {
        Bench().Briefing.ShouldContain("osc.sine");
        editor.Applied.ShouldBe(0);
    }

    [Fact]
    public async Task A_call_that_only_reads_puts_nothing_on_the_canvas()
    {
        (await Call(Bench(), "describe_patch", "{}")).Ok.ShouldBeTrue();

        editor.Applied.ShouldBe(0);
    }

    [Fact]
    public async Task A_handle_chosen_in_one_call_names_the_module_in_the_next()
    {
        var bench = Bench();
        await Call(bench, "write_patch", """{"source": "let lfo = sine()"}""");

        var outcome = await Call(bench, "measure", """{"handles": ["lfo"], "seconds": 0.25}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        outcome.Text.ShouldContain("lfo");
    }

    [Fact]
    public async Task A_canvas_edited_by_hand_while_nothing_waits_is_taken_up()
    {
        var bench = Bench();
        await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");
        (await Call(bench, "propose", """{"summary": "a tone"}""")).Ok.ShouldBeTrue();

        var byHand = PatchIO.Read(PatchIO.ToJson(editor.Current, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;
        byHand.Nodes.Add(new PatchBuilder(NodeCatalog.BuiltIn).Add("osc.saw", 300, 300));
        editor.Current = byHand;

        (await Call(bench, "describe_patch", "{}")).Text.ShouldContain("saw");
        (await Call(bench, "measure", """{"handles": ["tone"], "seconds": 0.25}""")).Ok.ShouldBeTrue();
    }

    /// <summary>A knob turned on the canvas while the program worked is kept, where the program left it alone.</summary>
    [Fact]
    public async Task A_knob_turned_by_hand_while_edits_wait_is_carried_onto_the_proposal()
    {
        var (lfo, freq) = Drawn();
        var bench = Bench();
        var printed = (await Call(bench, "describe_patch", "{}")).Text;
        var line = printed.Split('\n').First(l => l.Contains("saw(", StringComparison.Ordinal));

        (await Call(bench, "edit_patch", Replacing(line, line.Replace("saw(", "square(", StringComparison.Ordinal)))).Ok.ShouldBeTrue();

        var turned = PatchIO.Read(PatchIO.ToJson(editor.Current, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;
        turned.Find(lfo.Id).ShouldNotBeNull().InputValues[freq] = 5f;
        editor.Current = turned;

        var proposed = await Call(bench, "propose", """{"summary": "a square in place of the saw", "starts_silent": true}""");

        proposed.Ok.ShouldBeTrue(proposed.Text);
        proposed.Text.ShouldContain("keeps 1 setting");
        editor.Current.Nodes.ShouldContain(node => node.TypeId == "osc.square");
        editor.Current.Find(lfo.Id).ShouldNotBeNull().InputValues[freq].ShouldBe(5f);
    }

    /// <summary>Modules changed on the canvas while the program worked are replaced, and the answer says so.</summary>
    [Fact]
    public async Task Modules_changed_by_hand_while_edits_wait_are_replaced_and_said_to_be()
    {
        var bench = Bench();
        await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");

        var byHand = PatchIO.Read(PatchIO.ToJson(editor.Current, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;
        byHand.Nodes.Add(new PatchBuilder(NodeCatalog.BuiltIn).Add("osc.saw", 300, 300));
        editor.Current = byHand;

        var proposed = await Call(bench, "propose", """{"summary": "a tone"}""");

        proposed.Ok.ShouldBeTrue(proposed.Text);
        proposed.Text.ShouldContain("Ctrl+Z puts theirs back");
        editor.Current.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
        editor.Current.Nodes.ShouldNotContain(node => node.TypeId == "osc.saw");
    }

    /// <summary>
    /// Where the text owns the patch the edit goes into the person's own text, so what
    /// the canvas is handed back keeps everything they wrote around the change.
    /// </summary>
    [Fact]
    public async Task A_proposal_of_a_text_document_hands_back_the_text_with_its_comments()
    {
        const string written = """
            // a steady tone
            let tone = t |> sine(freq: 440)
            tone |> out.left
            """;

        editor.Current = PatchLanguage.Build(written, NodeCatalog.BuiltIn).Patch;
        editor.Text = written;

        var bench = Bench();
        (await Call(bench, "edit_patch", """{"edits": [{"find": "freq: 440", "replace": "freq: 330"}]}""")).Ok.ShouldBeTrue();
        (await Call(bench, "propose", """{"summary": "a 330 Hz tone"}""")).Ok.ShouldBeTrue();

        editor.Applied.ShouldBe(1);
        editor.Text.ShouldBe(written.Replace("freq: 440", "freq: 330", StringComparison.Ordinal));
        editor.Text.ShouldContain("// a steady tone");
    }

    /// <summary>
    /// Where the canvas owns the patch the edit is made on a printing, and the modules
    /// it did not touch are the same modules afterwards.
    /// </summary>
    [Fact]
    public async Task A_proposal_of_a_drawn_patch_keeps_the_modules_it_did_not_touch()
    {
        var (lfo, _) = Drawn();
        var output = editor.Current.Output;
        var saw = editor.Current.Nodes.First(node => node.TypeId == "osc.saw");

        var bench = Bench();
        var printed = (await Call(bench, "describe_patch", "{}")).Text;
        var line = printed.Split('\n').First(l => l.Contains("saw(", StringComparison.Ordinal));

        (await Call(bench, "edit_patch", Replacing(line, line.Replace("saw(", "square(", StringComparison.Ordinal)))).Ok.ShouldBeTrue();
        (await Call(bench, "propose", """{"summary": "a square in place of the saw", "starts_silent": true}""")).Ok.ShouldBeTrue();

        editor.Text.ShouldNotBeNull("the bench's text after an edit is handed over with the patch");
        editor.Text.ShouldContain("square(");
        editor.Current.Find(lfo.Id).ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
        editor.Current.Find(output.Id).ShouldNotBeNull();
        editor.Current.Find(saw.Id).ShouldBeNull("the saw it replaced is gone");
    }

    /// <summary>
    /// A patch the person opened, or changed by hand, is as far back as a reset goes: never
    /// the patch that was open when the program first called.
    /// </summary>
    [Fact]
    public async Task A_reset_goes_back_no_further_than_the_patch_the_person_last_opened()
    {
        var bench = Bench();
        await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");
        (await Call(bench, "propose", """{"summary": "a tone"}""")).Ok.ShouldBeTrue();

        const string opened = "let drone = t |> saw(freq: 55)\ndrone |> out.left";
        editor.Current = PatchLanguage.Build(opened, NodeCatalog.BuiltIn).Patch;
        editor.Text = opened;

        (await Call(bench, "edit_patch", """{"edits": [{"find": "freq: 55", "replace": "freq: 110"}]}""")).Ok.ShouldBeTrue();

        var reset = await Call(bench, "reset", "{}");

        reset.Ok.ShouldBeTrue(reset.Text);
        reset.Text.ShouldContain("freq: 55");
        reset.Text.ShouldNotContain("sine");
    }

    [Fact]
    public async Task A_reset_goes_back_no_further_than_the_last_proposal()
    {
        var bench = Bench();
        await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");
        (await Call(bench, "propose", """{"summary": "a 440 Hz tone"}""")).Ok.ShouldBeTrue();

        (await Call(bench, "edit_patch", """{"edits": [{"find": "freq: 440", "replace": "freq: 330"}]}""")).Ok.ShouldBeTrue();

        var reset = await Call(bench, "reset", "{}");

        reset.Ok.ShouldBeTrue(reset.Text);
        reset.Text.ShouldContain("freq: 440");
    }

    [Fact]
    public async Task A_proposal_after_an_edit_that_missed_is_refused_and_leaves_the_canvas_alone()
    {
        var bench = Bench();
        await Call(bench, "write_patch", $$"""{"source": "{{Tone}}"}""");

        (await Call(bench, "edit_patch", """{"edits": [{"find": "freq: 220", "replace": "freq: 330"}]}""")).Ok.ShouldBeFalse();

        var offered = await Call(bench, "propose", """{"summary": "a 330 Hz tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("last edit did not go through");
        editor.Applied.ShouldBe(0);
        bench.Proposed.ShouldBeNull();
    }

    /// <summary>A drawn patch on the canvas: a sine and a saw into the Output, and where the sine's freq knob is.</summary>
    private (NodeInstance Lfo, int Freq) Drawn()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId, 600, 0);
        var lfo = b.Add("osc.sine", 0, 0);
        var saw = b.Add("osc.saw", 0, 200);
        b.Wire(lfo, 0, output, NodeCatalog.OutputLeftPort);
        b.Wire(saw, 0, output, NodeCatalog.OutputLeftPort + 1);
        editor.Current = b.Patch;

        var freq = NodeCatalog.BuiltIn.All.First(def => def.TypeId == "osc.sine").Inputs.ToList().FindIndex(port => port.Name == "freq");

        return (lfo, freq);
    }

    private static string Replacing(string find, string replace) =>
        JsonSerializer.Serialize(new { edits = new[] { new { find, replace } } });

    private static async Task<ToolOutcome> Call(DrivenBench bench, string tool, string arguments)
    {
        using var given = JsonDocument.Parse(arguments);

        return await bench.Call(tool, given.RootElement.Clone(), CancellationToken.None);
    }
}
