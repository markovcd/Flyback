using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- editing what is there ----------------------------------------------

    private static Task<ToolOutcome> EditAll(PatchWorkbench bench, params (string Find, string Replace)[] edits) =>
        Call(bench, "edit_patch", JsonSerializer.Serialize(new { edits = edits.Select(e => new { find = e.Find, replace = e.Replace }) }));

    private static float Knob(PatchWorkbench bench) =>
        bench.Snapshot().Nodes.Single(n => n.TypeId == "value").InputValues[0];

    [Fact]
    public async Task A_knob_is_turned_by_editing_its_text()
    {
        var bench = await Lit(0.25f);

        var turned = await Edit(bench, "value: 0.25", "value: 0.75");

        turned.Ok.ShouldBeTrue(turned.Text);
        Knob(bench).ShouldBe(0.75f);
    }

    [Theory]
    [InlineData("value: 0.9")]
    [InlineData("knob1")]
    public async Task A_find_that_is_missing_or_found_twice_is_refused_and_changes_nothing(string find)
    {
        var bench = await Lit(0.25f);
        var before = bench.Text;

        var refused = await Edit(bench, find, "value: 0.5");

        refused.Ok.ShouldBeFalse();
        bench.Text.ShouldBe(before);
        Knob(bench).ShouldBe(0.25f);
    }

    [Fact]
    public async Task Edits_apply_in_order()
    {
        var bench = await Lit(0.25f);

        var both = await EditAll(bench, ("value: 0.25", "value: 0.5"), ("value: 0.5", "value: 0.6"));

        both.Ok.ShouldBeTrue(both.Text);
        Knob(bench).ShouldBe(0.6f);
    }

    [Fact]
    public async Task An_edit_that_does_not_read_is_refused_and_changes_nothing()
    {
        var bench = await Lit(0.25f);
        var before = bench.Text;

        var refused = await Edit(bench, "value(value: 0.25)", "value(value: 0.25");

        refused.Ok.ShouldBeFalse();
        bench.Text.ShouldBe(before);
        Knob(bench).ShouldBe(0.25f);
    }

    /// <summary>A document the text owns is edited in its own words, comments and all.</summary>
    [Fact]
    public async Task A_workbench_over_text_edits_that_text_and_keeps_its_comments()
    {
        const string text = "# the one knob\nlet knob1 = value(value: 0.25)\nknob1 |> out.color\n";
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, PatchLanguage.Build(text, NodeCatalog.BuiltIn).Patch, text: text);

        var turned = await Edit(bench, "value: 0.25", "value: 0.75");

        turned.Ok.ShouldBeTrue(turned.Text);
        bench.Text.ShouldBe(text.Replace("0.25", "0.75"));
        (await Call(bench, "describe_patch")).Text.ShouldContain("# the one knob");
        Knob(bench).ShouldBe(0.75f);
    }

    /// <summary>
    /// A module the edit leaves alone is the same module afterwards, so what the
    /// canvas holds of it, its place included, survives the assistant's turn.
    /// </summary>
    [Fact]
    public async Task A_module_left_alone_keeps_its_id_across_edits_and_writes()
    {
        const string drawn = "let knob1 = value(value: 0.25)\nlet tone1 = sine(freq: 220)\nknob1 |> out.color\ntone1 |> out.left";
        var patch = PatchLanguage.Build(drawn, NodeCatalog.BuiltIn).Patch;
        var tone = patch.Nodes.Single(n => n.TypeId == "osc.sine").Id;
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, patch);

        var edited = await Edit(bench, "value: 0.25", "value: 0.5");

        edited.Ok.ShouldBeTrue(edited.Text);
        bench.Snapshot().Nodes.Single(n => n.TypeId == "osc.sine").Id.ShouldBe(tone);

        var written = await Write(bench, drawn.Replace("0.25", "0.75"));

        written.Ok.ShouldBeTrue(written.Text);
        bench.Snapshot().Nodes.Single(n => n.TypeId == "osc.sine").Id.ShouldBe(tone);
    }

    [Fact]
    public async Task Reset_puts_the_starting_text_back()
    {
        const string text = "# the one knob\nlet knob1 = value(value: 0.25)\nknob1 |> out.color\n";
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, PatchLanguage.Build(text, NodeCatalog.BuiltIn).Patch, text: text);

        await Edit(bench, "value: 0.25", "value: 0.75");
        await Call(bench, "reset");

        bench.Text.ShouldBe(text);
        Knob(bench).ShouldBe(0.25f);
    }

    /// <summary>The time the step sugar binds has no let name, so describe says what it answers to.</summary>
    [Fact]
    public async Task Describe_lists_the_modules_with_no_let_name()
    {
        var described = (await Call(await Heard(), "describe_patch")).Text;

        described.ShouldContain("with no let name, answering to: time1");
    }
}
