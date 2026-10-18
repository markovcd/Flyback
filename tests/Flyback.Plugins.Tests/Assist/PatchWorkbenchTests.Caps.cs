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
    // --- caps ---------------------------------------------------------------

    /// <summary>
    /// The budget is a turn's, so a long conversation never runs dry for good, and
    /// propose is what running out asks for, so it is never refused for the count.
    /// </summary>
    [Fact]
    public async Task The_tool_call_budget_is_a_turns_and_never_stops_a_proposal()
    {
        var bench = await Lit(0.25f);
        var limited = new PatchWorkbench(NodeCatalog.BuiltIn, bench.Snapshot(), limits: new WorkbenchLimits(MaxToolCalls: 1));

        await Call(limited, "describe_patch");
        (await Call(limited, "describe_patch")).Ok.ShouldBeFalse();
        (await Call(limited, "propose", """{"summary":"a lit screen"}""")).Ok.ShouldBeTrue();

        limited.Reopen();

        (await Call(limited, "describe_patch")).Ok.ShouldBeTrue("a new turn has its own budget");
    }

    [Fact]
    public async Task A_turn_counts_only_its_own_edits()
    {
        var bench = Bench();

        await Write(bench, "let knob1 = value()\nknob1 |> out.color");
        bench.Edits.ShouldBe(1);

        bench.Reopen();

        bench.Edits.ShouldBe(0, "a turn that only talks has changed nothing");
    }

    /// <summary>A module whose knobs are refused is not placed, so the retry does not find its name taken.</summary>
    [Fact]
    public async Task A_module_refused_for_its_knobs_is_not_placed()
    {
        var bench = Bench();

        var refused = await Write(bench, "let knob1 = value(nonesuch: 1)\nknob1 |> out.color");

        refused.Ok.ShouldBeFalse();
        bench.Snapshot().Nodes.ShouldNotContain(node => node.TypeId == "value");

        (await Write(bench, "let knob1 = value(value: 1)\nknob1 |> out.color")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task A_knob_list_with_one_bad_entry_changes_nothing()
    {
        var bench = await Lit(0.5f);

        var set = await Call(bench, "edit_patch", """
            {"edits":[{"find":"value: 0.5","replace":"value: 0.9"},{"find":"knob1 |> out.color","replace":"knob1 |> out.colour"}]}
            """);

        set.Ok.ShouldBeFalse();
        bench.Snapshot().Nodes.Single(node => node.TypeId == "value").InputValues[0].ShouldBe(0.5f);
    }

    /// <summary>
    /// A written patch answers to the names it was written with, so the reply need
    /// not print it back for the model to find out what everything is called.
    /// </summary>
    [Fact]
    public async Task A_written_patch_answers_to_its_let_names_and_is_not_printed_back()
    {
        var bench = Bench();

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new
        {
            source = "let tone = sine(freq: 220)\ntone |> out.left",
        }));

        written.Ok.ShouldBeTrue(written.Text);
        written.Text.ShouldNotContain("freq");

        (await Call(bench, "measure", """{"handles":["tone"],"seconds":0.25}""")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task A_mistyped_handle_in_a_large_patch_names_the_nearest_rather_than_all()
    {
        var bench = Bench();

        (await Write(bench, string.Join("\n", Enumerable.Range(1, 12).Select(i => $"let value{i} = value()")))).Ok.ShouldBeTrue();

        var missed = await Call(bench, "measure", """{"handles":["valu3"]}""");

        missed.Ok.ShouldBeFalse();
        missed.Text.ShouldContain("value3");
        missed.Text.ShouldNotContain("value12");
    }

    [Fact]
    public async Task Running_out_of_tool_calls_is_said_rather_than_thrown()
    {
        var bench = Bench(new WorkbenchLimits(MaxToolCalls: 2));

        await Call(bench, "describe_patch");
        await Call(bench, "describe_patch");

        var third = await Call(bench, "describe_patch");
        third.Ok.ShouldBeFalse();
        third.Text.ShouldContain("tool calls");
    }
}
