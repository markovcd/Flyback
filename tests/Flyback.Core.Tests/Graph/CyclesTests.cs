using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// <see cref="Cycles.Backwards"/> is the one answer to "which wire carries the
/// evaluation before" — the compiler puts a plane on it, the canvas dashes it,
/// the layout leaves it out of the layers and the language writes it as a
/// back-wire. They agree because they all ask this.
/// </summary>
public class CyclesTests
{
    private readonly PatchBuilder b = new();

    [Fact]
    public void A_patch_with_no_loop_has_no_wire_running_backwards()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var third = b.Add("math.sub", 400, 0);

        b.Wire(first, 0, second, 0).Wire(second, 0, third, 0).Wire(first, 0, third, 1);

        Cycles.Backwards(b.Patch).ShouldBeEmpty();
    }

    /// <summary>
    /// The wire that closes the loop, and only it: everything else in the ring
    /// carries this evaluation, or the loop would be delayed twice over.
    /// </summary>
    /// <remarks>
    /// And it is the wire that reaches furthest back, rather than any of the ones
    /// carrying the chain forward: walking out from the Output, the return is
    /// what arrives at a module the walk is still inside.
    /// </remarks>
    [Fact]
    public void One_wire_of_a_loop_runs_backwards()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var third = b.Add("math.sub", 400, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(first, 0, second, 0)
         .Wire(second, 0, third, 0)
         .Wire(third, 0, first, 1)
         .Wire(third, 0, sink, NodeCatalog.OutputLeftPort);

        var backwards = Cycles.Backwards(b.Patch);

        backwards.Count.ShouldBe(1);
        backwards.Single().ShouldBe(new Connection(third.Id, 0, first.Id, 1));
    }

    /// <summary>
    /// The shortest loop there is: a module wired to itself. It was refused while
    /// a cycle was an error — it is the smallest example of one — and there is
    /// nothing else about it that needs saying no to.
    /// </summary>
    [Fact]
    public void A_module_may_be_wired_to_itself()
    {
        var add = b.Add("math.add", 0, 0, (1, 0.25f));
        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(add, 0, add, 0).Wire(add, 0, sink, NodeCatalog.OutputLeftPort);

        b.Patch.IncomingTo(add.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(add.Id);

        var backwards = Cycles.Backwards(b.Patch);

        backwards.ShouldHaveSingleItem().ShouldBe(new Connection(add.Id, 0, add.Id, 0));

        var result = b.Patch.CompileForAudio();

        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));
        result.Program.PlaneCount.ShouldBe(1);
    }

    /// <summary>Two loops that share nothing are two wires, one each.</summary>
    [Fact]
    public void Each_loop_gives_up_one_wire()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var third = b.Add("math.sub", 400, 0);
        var fourth = b.Add("math.min", 600, 0);

        b.Wire(first, 0, second, 0).Wire(second, 0, first, 1);
        b.Wire(third, 0, fourth, 0).Wire(fourth, 0, third, 1);

        Cycles.Backwards(b.Patch).Count.ShouldBe(2);
    }

    /// <summary>
    /// The same patch always cuts in the same place, whatever order its modules
    /// are listed in. The canvas moves a module to the end of the list to draw it
    /// in front, so a walk that started from the list would let picking a box up
    /// move a loop's delay onto another wire — which is a patch that sounds
    /// different for having been looked at.
    /// </summary>
    [Fact]
    public void Which_wire_is_cut_does_not_follow_the_order_the_modules_are_listed_in()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);
        var third = b.Add("math.sub", 400, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(first, 0, second, 0)
         .Wire(second, 0, third, 0)
         .Wire(third, 0, first, 1)
         .Wire(third, 0, sink, NodeCatalog.OutputLeftPort);

        var before = Cycles.Backwards(b.Patch);

        before.ShouldHaveSingleItem().ShouldBe(new Connection(third.Id, 0, first.Id, 1));

        // Every module brought to the front in turn, which is what dragging each
        // one does to the list.
        foreach (var node in b.Patch.Nodes.ToList())
        {
            b.Patch.Nodes.Remove(node);
            b.Patch.Nodes.Add(node);

            Cycles.Backwards(b.Patch).ShouldBe(before, $"after {node.TypeId} was brought to the front");
        }
    }

    /// <summary>
    /// A loop nothing else reaches is still a loop. The walk starts at every
    /// node rather than at the sink, so a ring off to one side of the patch is
    /// cut like any other — which is what keeps the canvas and the compiler
    /// agreeing about a patch still being built.
    /// </summary>
    [Fact]
    public void A_loop_the_output_cannot_reach_is_found_too()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0);

        b.Wire(first, 0, second, 0).Wire(second, 0, first, 1);

        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        var lone = b.Add("time", 0, 200);

        b.Wire(lone, 0, sink, NodeCatalog.OutputLeftPort);

        Cycles.Backwards(b.Patch).Count.ShouldBe(1);
    }

    /// <summary>
    /// And the compiler takes the same view: a loop is a patch it compiles
    /// rather than one it complains about.
    /// </summary>
    [Fact]
    public void A_loop_compiles_rather_than_being_refused()
    {
        var first = b.Add("math.add", 0, 0);
        var second = b.Add("math.mul", 200, 0, (1, 0.5f));
        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0);

        b.Wire(first, 0, second, 0)
         .Wire(second, 0, first, 1)
         .Wire(first, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio();

        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));
        result.Program.PlaneCount.ShouldBe(1);
    }
}
