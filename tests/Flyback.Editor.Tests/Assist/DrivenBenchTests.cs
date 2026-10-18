using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A program's tool calls run on the canvas: each edit put on it once, its handles kept between calls.</summary>
public sealed class DrivenBenchTests
{
    private readonly FakeAssistantEditor editor = new(Presets.Empty(NodeCatalog.BuiltIn));

    private DrivenBench Bench() => new(editor, NodeCatalog.BuiltIn, () => ProsePolicy.Default, () => Presets.All);

    [Fact]
    public async Task An_edit_goes_on_the_canvas_as_one_change()
    {
        var outcome = await Call(Bench(), "write_patch", """{"source": "let lfo = sine()"}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Applied.ShouldBe(1);
        editor.Current.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
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
        editor.Applied.ShouldBe(1, "a measurement changes nothing");
    }

    [Fact]
    public async Task A_canvas_edited_by_hand_is_taken_up_and_the_handles_still_hold()
    {
        var bench = Bench();
        await Call(bench, "write_patch", """{"source": "let lfo = sine()"}""");

        var byHand = PatchIO.Read(PatchIO.ToJson(editor.Current, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;
        byHand.Nodes.Add(new PatchBuilder(NodeCatalog.BuiltIn).Add("osc.saw", 300, 300));
        editor.Current = byHand;

        var outcome = await Call(bench, "measure", """{"handles": ["lfo"], "seconds": 0.25}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        outcome.Text.ShouldContain("lfo");

        (await Call(bench, "describe_patch", "{}")).Text.ShouldContain("saw");
    }

    /// <summary>
    /// Where the text owns the patch the edit goes into the person's own text, so what
    /// the canvas is handed back keeps everything they wrote around the change.
    /// </summary>
    [Fact]
    public async Task An_edit_of_a_text_document_hands_back_the_text_with_its_comments()
    {
        const string written = """
            # a slow wobble
            let lfo = t |> sine(freq: 2)
            lfo |> out.left
            """;

        editor.Current = PatchLanguage.Build(written, NodeCatalog.BuiltIn).Patch;
        editor.Text = written;

        var outcome = await Call(Bench(), "edit_patch", """{"edits": [{"find": "freq: 2", "replace": "freq: 3"}]}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Applied.ShouldBe(1);
        editor.Text.ShouldBe(written.Replace("freq: 2", "freq: 3", StringComparison.Ordinal));
        editor.Text.ShouldContain("# a slow wobble");
    }

    /// <summary>
    /// Where the canvas owns the patch the edit is made on a printing, and the modules
    /// it did not touch are the same modules afterwards.
    /// </summary>
    [Fact]
    public async Task An_edit_of_a_drawn_patch_keeps_the_modules_it_did_not_touch()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId, 600, 0);
        var lfo = b.Add("osc.sine", 0, 0);
        var saw = b.Add("osc.saw", 0, 200);
        b.Wire(lfo, 0, output, NodeCatalog.OutputLeftPort);
        editor.Current = b.Patch;

        var bench = Bench();
        var printed = (await Call(bench, "describe_patch", "{}")).Text;
        var line = printed.Split('\n').First(l => l.Contains("saw(", StringComparison.Ordinal));

        var outcome = await Call(bench, "edit_patch",
            JsonSerializer.Serialize(new { edits = new[] { new { find = line, replace = line.Replace("saw(", "square(", StringComparison.Ordinal) } } }));

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Text.ShouldNotBeNull("the bench's text after an edit is handed over with the patch");
        editor.Text.ShouldContain("square(");
        editor.Current.Find(lfo.Id).ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
        editor.Current.Find(output.Id).ShouldNotBeNull();
        editor.Current.Find(saw.Id).ShouldBeNull("the saw it replaced is gone");
    }

    private static async Task<ToolOutcome> Call(DrivenBench bench, string tool, string arguments)
    {
        using var given = JsonDocument.Parse(arguments);

        return await bench.Call(tool, given.RootElement.Clone(), CancellationToken.None);
    }
}
