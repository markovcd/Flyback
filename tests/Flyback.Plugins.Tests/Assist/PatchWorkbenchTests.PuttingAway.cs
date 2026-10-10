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
    // --- putting it away ----------------------------------------------------

    /// <summary>A workbench built the way a conversation carried on builds one.</summary>
    private static PatchWorkbench Restored(WorkbenchState state)
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, PatchIO.Read(state.Start, NodeCatalog.BuiltIn).Patch);

        bench.Restore(state);

        return bench;
    }

    /// <summary>
    /// A conversation carried on is written in the names it used, so a restored
    /// workbench has to answer to them — a handle the model chose included, which
    /// is not the one the type id would have given.
    /// </summary>
    [Fact]
    public async Task A_restored_workbench_answers_to_the_names_it_had()
    {
        var bench = await Lit(0.5f);
        var restored = Restored(bench.Save());

        var described = (await Call(restored, "describe_patch")).Text;

        described.ShouldContain("knob1");
        described.ShouldNotContain("value1");
        restored.Snapshot().Nodes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_restored_workbench_has_spent_what_it_had_spent()
    {
        var bench = await Lit();
        var restored = Restored(bench.Save());

        restored.Edits.ShouldBe(bench.Edits);
        restored.ToolCalls.ShouldBe(bench.ToolCalls);
    }

    /// <summary>
    /// Back to where the conversation began, not to the patch it was carried on
    /// from — the starting point travels with it.
    /// </summary>
    [Fact]
    public async Task Reset_after_restoring_goes_back_to_where_the_conversation_began()
    {
        var restored = Restored((await Lit()).Save());

        await Call(restored, "reset");

        restored.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    [Fact]
    public async Task A_handle_for_a_module_that_is_not_there_is_let_go()
    {
        var state = (await Lit()).Save() with
        {
            Handles = new Dictionary<string, Guid> { ["ghost1"] = Guid.NewGuid() },
        };

        var described = (await Call(Restored(state), "describe_patch")).Text;

        described.ShouldNotContain("ghost1");
        described.ShouldContain("value1", customMessage: "the knob, named from its type again");
    }

    /// <summary>
    /// Every module but the Maths modules an Expression stands for, which arrive
    /// as one when asked for and are named in its description as its functions.
    /// </summary>
    [Fact]
    public void The_briefing_names_every_module_there_is()
    {
        var briefing = Bench().Briefing;

        foreach (var def in NodeCatalog.BuiltIn.All)
        {
            if (ExpressionFusion.Retired(def)) briefing.ShouldNotContain(def.TypeId + " |");
            else briefing.ShouldContain(def.TypeId);
        }
    }

    /// <summary>
    /// A model copies the example it is shown, and the example is the one place the
    /// briefing cannot afford to contradict its own rule about names.
    /// </summary>
    [Fact]
    public void The_briefing_example_writes_the_ambiguous_names_in_full()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("|> color.hsv(");
        briefing.ShouldNotContain("|> hsv(");
    }

    [Fact]
    public void The_briefing_says_how_to_change_a_patch_that_is_there()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("Change what is there and leave the rest alone.");
        briefing.ShouldContain("Say what you did.");
        briefing.ShouldContain("Keep the sum out of clipping.");
    }
}
