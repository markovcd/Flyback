using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A patch holds at most one Video Output and one Audio Output. The rule lives
/// on the graph rather than in the editor, because the editor is not the only
/// thing that places modules — an assistant does too.
/// </summary>
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
    public void The_two_sinks_are_the_sinks_and_nothing_else_is()
    {
        NodeCatalog.IsSink(NodeCatalog.VideoOutputTypeId).ShouldBeTrue();
        NodeCatalog.IsSink(NodeCatalog.AudioOutputTypeId).ShouldBeTrue();

        // Named 'Output' in the palette and no such thing to the compiler.
        NodeCatalog.IsSink("audio.frequency").ShouldBeFalse();
        NodeCatalog.IsSink("osc.sine").ShouldBeFalse();
    }

    [Fact]
    public void An_empty_patch_takes_either_sink()
    {
        var patch = PatchOf();

        patch.CanAdd(NodeCatalog.VideoOutputTypeId).ShouldBeTrue();
        patch.CanAdd(NodeCatalog.AudioOutputTypeId).ShouldBeTrue();
    }

    [Fact]
    public void A_patch_with_a_screen_takes_no_second_screen()
    {
        var patch = PatchOf(NodeCatalog.VideoOutputTypeId);

        patch.CanAdd(NodeCatalog.VideoOutputTypeId).ShouldBeFalse();
    }

    [Fact]
    public void A_patch_with_a_screen_still_takes_speakers()
    {
        // The pair is the point of ADR-0022: one of each, not one in total.
        var patch = PatchOf(NodeCatalog.VideoOutputTypeId);

        patch.CanAdd(NodeCatalog.AudioOutputTypeId).ShouldBeTrue();
    }

    [Fact]
    public void A_patch_with_both_takes_neither_again()
    {
        var patch = PatchOf(NodeCatalog.VideoOutputTypeId, NodeCatalog.AudioOutputTypeId);

        patch.CanAdd(NodeCatalog.VideoOutputTypeId).ShouldBeFalse();
        patch.CanAdd(NodeCatalog.AudioOutputTypeId).ShouldBeFalse();
    }

    [Fact]
    public void Deleting_the_sink_lets_another_be_placed()
    {
        var patch = PatchOf(NodeCatalog.VideoOutputTypeId);

        patch.Remove(patch.Nodes[0].Id);

        patch.CanAdd(NodeCatalog.VideoOutputTypeId).ShouldBeTrue();
    }

    [Fact]
    public void Everything_that_is_not_a_sink_may_be_placed_as_often_as_you_like()
    {
        var patch = PatchOf("osc.sine", "osc.sine", "value");

        patch.CanAdd("osc.sine").ShouldBeTrue();
        patch.CanAdd("value").ShouldBeTrue();
    }

    [Fact]
    public void The_sink_a_patch_holds_can_be_found_to_be_wired_into()
    {
        var patch = PatchOf("value", NodeCatalog.VideoOutputTypeId);

        patch.FirstOf(NodeCatalog.VideoOutputTypeId).ShouldBe(patch.Nodes[1]);
        patch.FirstOf(NodeCatalog.AudioOutputTypeId).ShouldBeNull();
    }
}
