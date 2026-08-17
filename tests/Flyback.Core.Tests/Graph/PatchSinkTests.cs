using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Every patch holds exactly one Output: it cannot be added and it cannot be
/// taken away. The rule lives on the graph rather than in the editor, because
/// the editor is not the only thing that places modules — an assistant does too,
/// and a file is a third way a patch can arrive.
/// </summary>
/// <remarks>Specified by ADR-0037.</remarks>
public class PatchSinkTests
{
    private static Patch PatchOf(params string[] typeIds)
    {
        var patch = new Patch();

        foreach (var typeId in typeIds)
            patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require(typeId), 0, 0));

        return patch;
    }

    [Fact]
    public void The_output_is_the_sink_and_nothing_else_is()
    {
        NodeCatalog.IsSink(NodeCatalog.OutputTypeId).ShouldBeTrue();

        // Sat in the Output category of the palette, and no such thing to the compiler.
        NodeCatalog.IsSink("audio.frequency").ShouldBeFalse();
        NodeCatalog.IsSink("osc.sine").ShouldBeFalse();
    }

    [Fact]
    public void The_output_can_never_be_added_because_there_is_always_one()
    {
        PatchOf().CanAdd(NodeCatalog.OutputTypeId).ShouldBeFalse();
        PatchOf(NodeCatalog.OutputTypeId).CanAdd(NodeCatalog.OutputTypeId).ShouldBeFalse();
    }

    [Fact]
    public void Everything_that_is_not_the_sink_may_be_placed_as_often_as_you_like()
    {
        var patch = PatchOf("osc.sine", "osc.sine", "value");

        patch.CanAdd("osc.sine").ShouldBeTrue();
        patch.CanAdd("value").ShouldBeTrue();
    }

    [Fact]
    public void Ensuring_the_output_puts_one_there()
    {
        var patch = PatchOf();

        var output = patch.EnsureOutput(NodeCatalog.BuiltIn);

        output.TypeId.ShouldBe(NodeCatalog.OutputTypeId);
        patch.Nodes.ShouldHaveSingleItem();
    }

    [Fact]
    public void Ensuring_it_twice_does_not_make_a_second()
    {
        var patch = PatchOf("value");

        var first = patch.EnsureOutput(NodeCatalog.BuiltIn);
        var again = patch.EnsureOutput(NodeCatalog.BuiltIn);

        again.ShouldBeSameAs(first);
        patch.Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId).ShouldBe(1);
    }

    /// <summary>
    /// The whole point of it being permanent. Everything the shell hangs off the
    /// Output — the preview size, the renderer, the exports — would have nowhere
    /// to live the moment somebody pressed Delete on it.
    /// </summary>
    [Fact]
    public void The_output_cannot_be_removed()
    {
        var patch = PatchOf("value", NodeCatalog.OutputTypeId);
        var output = patch.Output;

        patch.Remove(output.Id).ShouldBeFalse();

        patch.Nodes.ShouldContain(output);
    }

    [Fact]
    public void Everything_else_can_still_be_removed()
    {
        var patch = PatchOf("value", NodeCatalog.OutputTypeId);
        var knob = patch.Nodes[0];

        patch.Remove(knob.Id).ShouldBeTrue();

        patch.Nodes.ShouldNotContain(knob);
    }

    /// <summary>Removing something takes its wires with it, including those into the sink.</summary>
    [Fact]
    public void Removing_a_module_unplugs_it_from_the_output()
    {
        var patch = PatchOf("value", NodeCatalog.OutputTypeId);
        var knob = patch.Nodes[0];

        patch.Connect(knob.Id, 0, patch.Output.Id, NodeCatalog.OutputColourPort);
        patch.Remove(knob.Id);

        patch.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void The_sink_a_patch_holds_can_be_found_to_be_wired_into()
    {
        var patch = PatchOf("value", NodeCatalog.OutputTypeId);

        patch.Output.ShouldBe(patch.Nodes[1]);
        patch.FirstOf(NodeCatalog.OutputTypeId).ShouldBe(patch.Nodes[1]);
    }

    /// <summary>
    /// A graph assembled by hand is the one way to get a patch without a sink,
    /// and asking for it should say so rather than hand back something wrong.
    /// </summary>
    [Fact]
    public void A_patch_built_without_one_says_so_when_asked()
    {
        Should.Throw<InvalidOperationException>(() => PatchOf("value").Output);
    }

    [Fact]
    public void Every_preset_ships_with_its_output()
    {
        foreach (var preset in Presets.All)
        {
            var patch = preset.Build(NodeCatalog.BuiltIn);

            patch.Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId)
                .ShouldBe(1, $"the '{preset.Name}' preset");
        }
    }
}
