using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
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
        var outcome = await Call(Bench(), "add_module", """{"type_id": "osc.sine"}""");

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
        await Call(bench, "add_module", """{"type_id": "osc.sine", "handle": "lfo"}""");

        var outcome = await Call(bench, "set_knobs", """{"handle": "lfo", "knobs": [{"port": "freq", "value": 2}]}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Applied.ShouldBe(2);
    }

    [Fact]
    public async Task A_canvas_edited_by_hand_is_taken_up_and_the_handles_still_hold()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id": "osc.sine", "handle": "lfo"}""");

        var byHand = PatchIO.Read(PatchIO.ToJson(editor.Current, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;
        byHand.Nodes.Add(new PatchBuilder(NodeCatalog.BuiltIn).Add("osc.saw", 300, 300));
        editor.Current = byHand;

        var outcome = await Call(bench, "set_knobs", """{"handle": "lfo", "knobs": [{"port": "freq", "value": 2}]}""");

        outcome.Ok.ShouldBeTrue(outcome.Text);
        editor.Current.Nodes.ShouldContain(node => node.TypeId == "osc.saw");
    }

    private static async Task<ToolOutcome> Call(DrivenBench bench, string tool, string arguments)
    {
        using var given = JsonDocument.Parse(arguments);

        return await bench.Call(tool, given.RootElement.Clone(), CancellationToken.None);
    }
}
