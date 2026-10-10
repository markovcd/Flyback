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
    // --- presets ------------------------------------------------------------

    private static PatchWorkbench WithPresets(params PatchPreset[] presets) =>
        new(NodeCatalog.BuiltIn, new Patch(), presets: presets);

    [Fact]
    public void The_briefing_lists_the_presets_but_not_the_blank_ones()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("# Presets");
        briefing.ShouldContain("Plasma | Three drifting sine fields");
        briefing.ShouldNotContain("Empty | The Output");
    }

    [Fact]
    public async Task A_preset_can_be_read_in_the_language_it_is_written_in()
    {
        var bench = Bench();

        var described = await Call(bench, "describe_preset", """{"name":"plasma"}""");
        described.Ok.ShouldBeTrue(described.Text);

        // The line naming it and the line counting it are prose about the patch.
        var lines = described.Text.Split(Environment.NewLine);
        lines[0].ShouldStartWith("Plasma: ");
        lines[^1].ShouldContain(" modules, ");

        PatchLanguage.Build(string.Join('\n', lines[1..^1]), NodeCatalog.BuiltIn).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Reading_a_preset_leaves_the_patch_on_the_bench_alone()
    {
        var bench = Bench();
        var before = bench.Snapshot().Nodes.Count;

        (await Call(bench, "describe_preset", """{"name":"Plasma"}""")).Ok.ShouldBeTrue();

        bench.Snapshot().Nodes.Count.ShouldBe(before);
        bench.Edits.ShouldBe(0);
    }

    [Fact]
    public async Task A_preset_that_is_not_there_is_refused_with_the_ones_that_are()
    {
        var refused = await Call(Bench(), "describe_preset", """{"name":"nonesuch"}""");

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("Plasma");
    }

    [Fact]
    public async Task A_preset_that_cannot_be_built_here_is_said_rather_than_thrown()
    {
        var bench = WithPresets(new PatchPreset("Broken", _ => throw new InvalidOperationException("a plugin is missing")));

        var refused = await Call(bench, "describe_preset", """{"name":"Broken"}""");

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("cannot be built here");
        refused.Text.ShouldContain("a plugin is missing");
    }

    /// <summary>
    /// Whoever saved a preset has no description to give it, and it is offered and
    /// read like any other.
    /// </summary>
    [Fact]
    public async Task A_preset_somebody_saved_is_listed_and_can_be_read()
    {
        var mine = new PatchPreset("My tone", modules => PatchLanguage.Build("sine(freq: 220) |> out.left", modules).Patch);
        var bench = WithPresets(mine);

        bench.Briefing.ShouldContain("\nMy tone\n");

        var described = await Call(bench, "describe_preset", """{"name":"My tone"}""");
        described.Ok.ShouldBeTrue(described.Text);
        described.Text.ShouldContain("sine");
    }

    [Fact]
    public void With_no_presets_there_is_neither_a_list_nor_a_tool()
    {
        var bench = WithPresets();

        bench.Briefing.ShouldNotContain("# Presets");
        bench.Tools.Select(t => t.Name).ShouldNotContain("describe_preset");
        Bench().Tools.Select(t => t.Name).ShouldContain("describe_preset");
    }

    [Fact]
    public async Task Asking_for_a_retired_maths_module_adds_its_expression()
    {
        var bench = Bench();

        var added = await Call(bench, "add_module", """{"type_id":"math.floor","handle":"f1"}""");

        added.Ok.ShouldBeTrue(added.Text);
        added.Text.ShouldContain("an Expression for math.floor: floor(a)");
        bench.Snapshot().FirstOf(NodeCatalog.ExpressionTypeId).ShouldNotBeNull();
        bench.Snapshot().FirstOf("math.floor").ShouldBeNull();
    }

    [Fact]
    public void The_briefing_carries_what_each_module_is_for()
    {
        // The descriptions are written for a person who does not know the synth,
        // which is the same thing a model needs. Putting them in verbatim is what
        // makes them load-bearing beyond the tooltip they were written for.
        Bench().Briefing.ShouldContain(NodeCatalog.BuiltIn.Require("osc.sine").Description);
    }

    /// <summary>
    /// The words the panel shows as a socket's tip are beside it where the module is
    /// looked up, and left out of the briefing.
    /// </summary>
    [Fact]
    public async Task A_lookup_says_what_each_socket_is_for_and_the_briefing_does_not()
    {
        var bench = Bench();
        var filter = NodeCatalog.BuiltIn.Require(NodeCatalog.FilterTypeId);

        var described = await Call(bench, "describe_module", $$"""{"type_id":"{{filter.TypeId}}"}""");

        described.Ok.ShouldBeTrue(described.Text);

        foreach (var port in filter.Inputs.Concat(filter.Outputs))
        {
            bench.Briefing.ShouldNotContain(port.Help);
            described.Text.ShouldContain(port.Help);
        }
    }

    /// <summary>A lookup says what each setting on the node is for; the briefing names the setting only.</summary>
    [Fact]
    public async Task A_lookup_says_what_each_setting_is_for_and_the_briefing_does_not()
    {
        var bench = Bench();
        var formula = NodeCatalog.BuiltIn.Require(NodeCatalog.ExpressionTypeId).Extras.Single().Fields.Single();

        var described = await Call(bench, "describe_module", $$"""{"type_id":"{{NodeCatalog.ExpressionTypeId}}"}""");

        formula.Help.ShouldNotBeEmpty();
        described.Text.ShouldContain(formula.Help);
        bench.Briefing.ShouldNotContain(formula.Help);
    }

    [Fact]
    public async Task A_module_is_found_by_a_word_it_is_known_by()
    {
        var found = await Call(Bench(), "find_modules", """{"query":"portamento"}""");

        found.Text.ShouldContain(NodeCatalog.SlewTypeId);
    }

    [Fact]
    public async Task A_module_is_found_by_what_one_of_its_sockets_is_for()
    {
        var found = await Call(Bench(), "find_modules", """{"query":"meant to be swept"}""");

        found.Text.ShouldContain(NodeCatalog.FilterTypeId);
    }

    [Fact]
    public void Rendering_is_offered_only_when_the_model_can_see()
    {
        Bench().Tools.Select(t => t.Name).ShouldContain("render");
        Bench(vision: false).Tools.Select(t => t.Name).ShouldNotContain("render");
    }
}
