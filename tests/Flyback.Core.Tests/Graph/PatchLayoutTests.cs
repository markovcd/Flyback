using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The layout: where the modules go, so that a patch reads left to right and
/// its wires can be followed.
/// </summary>
/// <remarks>
/// Everything here is a property of the drawing rather than a set of expected
/// coordinates, because a coordinate is a thing that changes when a gap is
/// widened and a property is not. What the drawing has to be is: forward,
/// non-overlapping, unchanged as a program, and the same answer twice.
/// </remarks>
public class PatchLayoutTests
{
    private static readonly PatchLayout.Metrics Size = PatchLayout.Metrics.Default;

    private static Patch Preset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    public static TheoryData<string> EveryPreset =>
        [.. Presets.All.Where(p => p.Name != "Empty").Select(p => p.Name)];

    private static Patch Arranged(Patch patch)
    {
        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);
        return patch;
    }

    private static (double Left, double Top, double Right, double Bottom) Box(NodeInstance node)
    {
        var def = NodeCatalog.BuiltIn.Require(node.TypeId);
        return (node.X, node.Y, node.X + Size.Width, node.Y + Size.Height(def));
    }

    /// <summary>
    /// Every wire leaves a node to the left of the one it arrives at, with clear
    /// space in between. A wire that ran backwards would have to double round
    /// the module it came from, which is the one thing a signal chain must never
    /// look like.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Every_wire_runs_forwards(string name)
    {
        var patch = Arranged(Preset(name));

        foreach (var wire in patch.Connections)
        {
            var from = patch.Find(wire.SourceNode).ShouldNotBeNull();
            var to = patch.Find(wire.TargetNode).ShouldNotBeNull();

            // Unless it is the wire that closes a loop, which by construction
            // can only leave a cycle breaker and is the one wire meant to be
            // read as going back.
            if (NodeCatalog.BuiltIn.Require(from.TypeId).IsCycleBreaker) continue;

            Box(from).Right.ShouldBeLessThanOrEqualTo(
                to.X, $"{from.TypeId} feeds {to.TypeId} and should sit to its left");
        }
    }

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void No_two_modules_overlap(string name)
    {
        var placed = Arranged(Preset(name)).Nodes;

        for (var a = 0; a < placed.Count; a++)
        for (var b = a + 1; b < placed.Count; b++)
        {
            var (one, two) = (Box(placed[a]), Box(placed[b]));

            var apart = one.Right <= two.Left || two.Right <= one.Left
                || one.Bottom <= two.Top || two.Bottom <= one.Top;

            apart.ShouldBeTrue($"{placed[a].TypeId} and {placed[b].TypeId} overlap");
        }
    }

    /// <summary>
    /// The Output is the end of the patch and reads as the end. It is pinned
    /// there rather than left to the arithmetic, which would put a Probe hanging
    /// off a long chain further right than the sink it is watching.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void The_output_is_the_rightmost_module(string name)
    {
        var patch = Arranged(Preset(name));
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();

        foreach (var node in patch.Nodes)
            node.X.ShouldBeLessThanOrEqualTo(sink.X);
    }

    /// <summary>
    /// Nothing but coordinates changes, which is what makes the button safe to
    /// press: the patch compiles to the same instructions, so the picture and
    /// the sound are untouched.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Laying_out_a_patch_does_not_change_what_it_compiles_to(string name)
    {
        var before = Preset(name).CompileForVideo(NodeCatalog.BuiltIn).Program.Ops;
        var after = Arranged(Preset(name)).CompileForVideo(NodeCatalog.BuiltIn).Program.Ops;

        after.ShouldBe(before);
    }

    /// <summary>
    /// Laying out an already laid-out patch changes nothing. Pressing the button
    /// twice should not shuffle the canvas, and it is the property a relaxation
    /// could not have given — see ADR-0044.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Laying_out_twice_is_laying_out_once(string name)
    {
        var patch = Arranged(Preset(name));
        var settled = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(settled[node.Id], $"{node.TypeId} moved on the second pass");
    }

    /// <summary>
    /// A module wired to nothing goes before the first column rather than among
    /// the sources, which it is not one of.
    /// </summary>
    [Fact]
    public void A_module_wired_to_nothing_is_parked_ahead_of_the_patch()
    {
        var patch = Preset("Drone");
        var stray = NodeInstance.Create(NodeCatalog.BuiltIn.Require("pattern.noise"), 9999, 9999);
        patch.Nodes.Add(stray);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        foreach (var node in patch.Nodes.Where(n => n.Id != stray.Id))
            stray.X.ShouldBeLessThan(node.X);
    }

    /// <summary>
    /// A patch holding a loop lays out rather than hanging. The wire that closes
    /// it leaves a cycle breaker, and those are the edges the layout cuts before
    /// it counts anything.
    /// </summary>
    [Fact]
    public void A_patch_with_a_loop_in_it_still_lays_out()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0, (0, 1f));
        var osc = b.Add("osc.sine", 0, 0, (1, 220f));
        var mix = b.Add("math.add", 0, 0);
        var delay = b.Add(NodeCatalog.UnitDelayTypeId, 0, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 0, 0);

        b.Wire(time, 0, osc, 0)
         .Wire(osc, 0, mix, 0)
         .Wire(mix, 0, delay, 0)
         .Wire(delay, 0, mix, 1)
         .Wire(mix, 0, output, NodeCatalog.OutputLeftPort);

        PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        // The forward half of the loop still reads forwards; the wire back is
        // the only one allowed not to.
        b.Patch.Find(mix.Id)!.X.ShouldBeLessThan(b.Patch.Find(delay.Id)!.X);
        b.Patch.Nodes.Select(n => n.X).Distinct().Count().ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// Wires that have to cross are the whole reason the ordering pass exists,
    /// so it is worth pinning that it does something. Two chains fed in the
    /// order that crosses them lay out uncrossed.
    /// </summary>
    [Fact]
    public void Two_chains_wired_across_each_other_are_untangled()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0, (0, 1f));
        var top = b.Add("osc.sine", 0, 0, (1, 220f));
        var bottom = b.Add("osc.sine", 0, 0, (1, 330f));

        // Added in the order that puts the crossing in: the first multiplier
        // takes the second oscillator and the second takes the first.
        var first = b.Add("math.mul", 0, 0);
        var second = b.Add("math.mul", 0, 0);
        var sum = b.Add("math.add", 0, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 0, 0);

        b.Wire(time, 0, top, 0)
         .Wire(time, 0, bottom, 0)
         .Wire(bottom, 0, first, 0)
         .Wire(top, 0, second, 0)
         .Wire(first, 0, sum, 0)
         .Wire(second, 0, sum, 1)
         .Wire(sum, 0, output, NodeCatalog.OutputLeftPort);

        PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        // Whichever oscillator ends up on top, its multiplier is on top too.
        var topper = b.Patch.Find(top.Id)!.Y < b.Patch.Find(bottom.Id)!.Y ? second : first;
        var other = topper.Id == second.Id ? first : second;

        b.Patch.Find(topper.Id)!.Y.ShouldBeLessThan(b.Patch.Find(other.Id)!.Y);
    }

    /// <summary>
    /// A module whose plugin is not installed cannot be measured, so it is left
    /// exactly where the file put it rather than moved to a guessed size.
    /// </summary>
    [Fact]
    public void A_module_that_is_not_in_the_catalogue_is_left_alone()
    {
        var patch = Preset("Drone");
        var unknown = new NodeInstance { Id = Guid.NewGuid(), TypeId = "nobody.knows", X = 1234, Y = 5678 };
        patch.Nodes.Add(unknown);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        unknown.X.ShouldBe(1234);
        unknown.Y.ShouldBe(5678);
    }

    [Fact]
    public void An_empty_patch_lays_out_without_complaint() =>
        Should.NotThrow(() => PatchLayout.Arrange(new Patch(), NodeCatalog.BuiltIn));
}
