using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// <see cref="VoiceCounts"/>: how many voices each module runs, which the
/// compiler lowers by and the canvas draws by.
/// </summary>
public class VoiceCountsTests
{
    private static NodeInstance Voice(PatchBuilder b, int count)
    {
        var node = b.Add(NodeCatalog.VoiceTypeId, 0, 0);

        node.SetState("voices", new JsonObject { ["voices"] = (float)count });
        return node;
    }

    [Fact]
    public void A_count_follows_the_wires_and_stops_at_a_merge()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voice(b, 4);
        var add = b.Add("math.add", 0, 0);
        var merge = b.Add(NodeCatalog.MergeTypeId, 0, 0);
        var after = b.Add("math.mul", 0, 0);
        var aside = b.Add("math.sub", 0, 0);

        b.Wire(voice, 0, add, 0).Wire(add, 0, merge, 0).Wire(merge, 0, after, 0).Wire(aside, 0, add, 1);

        var counts = VoiceCounts.Of(b.Patch, NodeCatalog.BuiltIn);

        counts[voice.Id].ShouldBe(4);
        counts[add.Id].ShouldBe(4);
        counts.ShouldNotContainKey(merge.Id);
        counts.ShouldNotContainKey(after.Id);
        counts.ShouldNotContainKey(aside.Id);
    }

    [Fact]
    public void Where_two_counts_meet_the_larger_runs()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var two = Voice(b, 2);
        var six = Voice(b, 6);
        var add = b.Add("math.add", 0, 0);

        b.Wire(two, 0, add, 0).Wire(six, 0, add, 1);

        VoiceCounts.Of(b.Patch, NodeCatalog.BuiltIn)[add.Id].ShouldBe(6);
    }

    [Fact]
    public void A_merge_switched_off_passes_its_voices_on()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voice(b, 3);
        var merge = b.Add(NodeCatalog.MergeTypeId, 0, 0);
        var after = b.Add("math.mul", 0, 0);

        merge.Off = true;
        b.Wire(voice, 0, merge, 0).Wire(merge, 0, after, 0);

        VoiceCounts.Of(b.Patch, NodeCatalog.BuiltIn)[after.Id].ShouldBe(3);
    }

    [Fact]
    public void A_loop_carries_its_count_round_and_ends()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voice(b, 5);
        var add = b.Add("math.add", 0, 0);
        var mul = b.Add("math.mul", 0, 0);

        b.Wire(voice, 0, add, 0).Wire(add, 0, mul, 0).Wire(mul, 0, add, 1);

        var counts = VoiceCounts.Of(b.Patch, NodeCatalog.BuiltIn);

        counts[add.Id].ShouldBe(5);
        counts[mul.Id].ShouldBe(5);
    }

    [Fact]
    public void A_chart_hears_one_signal()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voice(b, 4);
        var scope = b.Add(NodeCatalog.ScopeTypeId, 0, 0);

        b.Wire(voice, 0, scope, 0);

        VoiceCounts.Of(b.Patch, NodeCatalog.BuiltIn).ShouldNotContainKey(scope.Id);
    }
}
