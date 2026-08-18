using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// <see cref="Patch.WouldCycle"/> is what the editor asks before it draws a wire,
/// so it has to agree with what the compiler will do with the graph afterwards:
/// true exactly where <see cref="PatchCompiler"/> would complain, false everywhere
/// it would not.
/// </summary>
public class WouldCycleTests
{
    private readonly PatchBuilder b = new();

    /// <summary>
    /// Whether the compiler actually refuses the patch once the wire is drawn —
    /// the thing <see cref="Patch.WouldCycle"/> is a prediction of. Every case
    /// below checks the prediction against this rather than only against itself.
    /// </summary>
    private static bool Refused(Patch patch) =>
        patch.CompileForAudio().Issues.Any(i =>
            i.Severity == IssueSeverity.Error && i.Message.Contains("feeds back into itself"));

    [Fact]
    public void A_wire_that_runs_forwards_closes_nothing()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);

        b.Patch.WouldCycle(first.Id, second.Id).ShouldBeFalse();
    }

    [Fact]
    public void A_wire_straight_back_to_where_it_came_from_closes_a_loop()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);

        b.Wire(first, 0, second, 0);

        b.Patch.WouldCycle(second.Id, first.Id).ShouldBeTrue();

        // And the compiler agrees once it is actually drawn.
        b.Wire(second, 0, first, 0);
        Refused(WithSink(first)).ShouldBeTrue();
    }

    [Fact]
    public void A_wire_back_across_a_longer_chain_closes_a_loop()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var third = b.Add("math.sub", 400, 0);

        b.Wire(first, 0, second, 0).Wire(second, 0, third, 0);

        b.Patch.WouldCycle(third.Id, first.Id).ShouldBeTrue();

        b.Wire(third, 0, first, 0);
        Refused(WithSink(first)).ShouldBeTrue();
    }

    /// <summary>
    /// Two modules reading the same source are not a loop, however much the walk
    /// meets the same node twice getting there.
    /// </summary>
    [Fact]
    public void A_diamond_is_not_a_loop()
    {
        var source = b.Add("math.add", 0, 0);
        var left = b.Add("math.mul", 200, 0);
        var right = b.Add("math.sub", 200, 200);
        var join = b.Add("math.max", 400, 100);

        b.Wire(source, 0, left, 0)
         .Wire(source, 0, right, 0)
         .Wire(left, 0, join, 0)
         .Wire(right, 0, join, 1);

        // Nothing downstream of join reaches source, so wiring on is still forward.
        var tail = b.Add("math.min", 600, 100);
        b.Patch.WouldCycle(join.Id, tail.Id).ShouldBeFalse();
    }

    // --- what a breaker excuses ---------------------------------------------

    /// <summary>
    /// The point of the whole mechanism: a loop with a Unit Delay on it is legal,
    /// so a wire completing one must not be reported as a cycle — otherwise the
    /// editor would insert a second breaker into a loop that already has one.
    /// </summary>
    [Fact]
    public void A_loop_already_broken_by_a_unit_delay_is_not_reported()
    {
        var osc = b.Add("osc.sine", 0, 0);
        var unit = b.Add(NodeCatalog.UnitDelayTypeId, 200, 0);

        b.Wire(osc, 0, unit, 0);

        // unit.out -> osc.phase completes the loop, and the break is already in it.
        b.Patch.WouldCycle(unit.Id, osc.Id).ShouldBeFalse();

        b.Wire(unit, 0, osc, 2);
        Refused(WithSink(osc)).ShouldBeFalse();
    }

    /// <summary>
    /// The same thing the other way round the loop: the breaker is downstream of
    /// the new wire rather than at the end of it, so the walk has to stop when it
    /// arrives there instead of carrying on to the source.
    /// </summary>
    [Fact]
    public void A_breaker_partway_round_stops_the_walk()
    {
        var osc = b.Add("osc.sine", 0, 0);
        var gain = b.Add("math.mul", 200, 0);
        var unit = b.Add(NodeCatalog.UnitDelayTypeId, 400, 0);
        var after = b.Add("math.add", 600, 0);

        b.Wire(gain, 0, unit, 0).Wire(unit, 0, after, 0);

        // osc -> gain would complete osc -> gain -> unit -> after, and nothing
        // comes back past the unit, so there is no loop to break.
        b.Patch.WouldCycle(osc.Id, gain.Id).ShouldBeFalse();

        // But with 'after' wired back into the oscillator the loop is real — and
        // still broken, because the unit sits on it.
        b.Wire(after, 0, osc, 2);
        b.Patch.WouldCycle(osc.Id, gain.Id).ShouldBeFalse();

        b.Wire(osc, 0, gain, 0);
        Refused(WithSink(osc)).ShouldBeFalse();
    }

    /// <summary>
    /// A loop of plain maths behind a breaker is still refused, so a wire closing
    /// one must still be reported: the breaker excuses the loop it is on, not
    /// every loop in the patch.
    /// </summary>
    [Fact]
    public void A_breaker_elsewhere_excuses_nothing()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var unit = b.Add(NodeCatalog.UnitDelayTypeId, 400, 0);

        b.Wire(first, 0, second, 0).Wire(second, 0, unit, 0);

        // second -> first closes a loop of two maths modules. That the unit hangs
        // off second changes nothing about it.
        b.Patch.WouldCycle(second.Id, first.Id).ShouldBeTrue();

        b.Wire(second, 0, first, 0);
        Refused(WithSink(unit)).ShouldBeTrue();
    }

    // --- the edges ----------------------------------------------------------

    /// <summary>
    /// Connect refuses a wire from a node to itself, so this has to agree rather
    /// than report a loop the editor could not draw and would then try to fix.
    /// </summary>
    [Fact]
    public void A_node_wired_to_itself_is_left_to_Connect_to_refuse()
    {
        var only = b.Add("math.add", 0, 0);

        b.Patch.WouldCycle(only.Id, only.Id).ShouldBeFalse();

        // And Connect really does drop it, so nothing arrives for the compiler.
        b.Patch.Connect(only.Id, 0, only.Id, 0);
        b.Patch.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void A_node_that_is_not_in_the_patch_closes_nothing()
    {
        var only = b.Add("math.add", 0, 0);

        b.Patch.WouldCycle(Guid.NewGuid(), only.Id).ShouldBeFalse();
        b.Patch.WouldCycle(only.Id, Guid.NewGuid()).ShouldBeFalse();
    }

    /// <summary>Puts the sink on so the graph can be compiled and asked about.</summary>
    private Patch WithSink(NodeInstance heard)
    {
        var sink = b.Patch.EnsureOutput();
        b.Patch.Connect(heard.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }
}
