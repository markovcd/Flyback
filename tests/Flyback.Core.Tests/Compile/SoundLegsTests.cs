using Flyback.Core.Compile;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// Where a sound is cut for several cores: never through something one evaluation leaves
/// for the next, and through the knots of ops that are, once they are laid apart.
/// </summary>
public class SoundLegsTests
{
    private const int Length = 20;

    [Fact]
    public void Two_feedback_loops_written_in_turn_are_laid_apart_and_run_a_leg_each()
    {
        var ops = Interleaved(Loop(10), Loop(40));

        var legs = Cut(ops);

        legs.Count.ShouldBe(2);
        LegsOf(legs, ops, 10).ShouldBe([LegsOf(legs, ops, 10)[0]]);
        LegsOf(legs, ops, 40).ShouldBe([LegsOf(legs, ops, 40)[0]]);
        LegsOf(legs, ops, 10).ShouldNotBe(LegsOf(legs, ops, 40));
    }

    [Fact]
    public void A_cell_read_and_written_in_one_evaluation_stays_in_one_leg()
    {
        var ops = Interleaved(CellLoop(10, cell: 0), CellLoop(40, cell: 1));

        var legs = Cut(ops);

        legs.Count.ShouldBe(2);

        foreach (var cell in new[] { 0, 1 })
        {
            var touching = Enumerable.Range(0, ops.Length)
                .Where(i => ops[i].Code is OpCode.UnitRead or OpCode.UnitWrite && (int)ops[i].K == cell)
                .Select(i => LegOf(legs, i))
                .Distinct();

            touching.Count().ShouldBe(1, $"cell {cell}");
        }
    }

    [Fact]
    public void One_loop_through_the_whole_program_is_left_whole()
    {
        var ops = Loop(10).Append(Output(10 + Length - 1, 100)).Append(Output(10 + Length - 1, 101)).ToArray();

        SoundLegs.For(ops, (100, 102), new Repeats(ops, (100, 102)), most: 4, least: 0d).ShouldBeNull();
    }

    [Fact]
    public void A_program_too_cheap_to_be_worth_cutting_is_left_whole()
    {
        var ops = Interleaved(Loop(10), Loop(40));

        SoundLegs.For(ops, (100, 102), new Repeats(ops, (100, 102)), most: 4, least: 1e9).ShouldBeNull();
    }

    private static SoundLegs Cut(Op[] ops) =>
        SoundLegs.For(ops, (100, 102), new Repeats(ops, (100, 102)), most: 2, least: 0d).ShouldNotBeNull();

    /// <summary>A chain of sines whose first reads its last as the last evaluation left it.</summary>
    private static Op[] Loop(int first) =>
        [.. Enumerable.Range(0, Length).Select(k => new Op(OpCode.Sin, first + k, a: first + (k + Length - 1) % Length))];

    /// <summary>A chain of sines from a cell back into it.</summary>
    private static Op[] CellLoop(int first, int cell) =>
    [
        new Op(OpCode.UnitRead, first, k: cell),
        .. Enumerable.Range(1, Length - 1).Select(k => new Op(OpCode.Sin, first + k, a: first + k - 1)),
        new Op(OpCode.UnitWrite, -1, a: first + Length - 1, k: cell),
    ];

    private static Op Output(int from, int into) => new(OpCode.Copy, into, a: from);

    /// <summary>The two loops op by op in turn, each copied to an output.</summary>
    private static Op[] Interleaved(Op[] left, Op[] right) =>
    [
        .. left.Zip(right).SelectMany(pair => new[] { pair.First, pair.Second }),
        Output(left[^1].Out < 0 ? left[^2].Out : left[^1].Out, 100),
        Output(right[^1].Out < 0 ? right[^2].Out : right[^1].Out, 101),
    ];

    private static int LegOf(SoundLegs legs, int op)
    {
        var at = Array.IndexOf(legs.Order, op);
        for (var l = 0; l < legs.Count; l++)
            if (at >= legs.Cuts[l] && at < legs.Cuts[l + 1]) return l;

        throw new InvalidOperationException($"Op {op} is in no leg.");
    }

    /// <summary>The legs holding the sines that write registers from <paramref name="first"/>.</summary>
    private static int[] LegsOf(SoundLegs legs, Op[] ops, int first) =>
    [
        .. Enumerable.Range(0, ops.Length)
            .Where(i => ops[i].Code is OpCode.Sin && ops[i].Out >= first && ops[i].Out < first + Length)
            .Select(i => LegOf(legs, i))
            .Distinct(),
    ];
}
