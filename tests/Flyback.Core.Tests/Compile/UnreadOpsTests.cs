using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="Emitter.ToProgram(Slot)"/> — the program without the ops nothing
/// reads.
/// </summary>
/// <remarks>
/// A module is emitted whole, so what is pinned here is that an output no wire
/// takes costs nothing, and the two things that must survive it: whatever the
/// program remembers, and the order its memory is handed out in.
/// </remarks>
public class UnreadOpsTests
{
    [Fact]
    public void An_op_nothing_reads_is_left_out()
    {
        var em = new Emitter();
        var x = em.Load(OpCode.LoadX);
        var wanted = em.Unary(OpCode.Sin, x);

        em.Unary(OpCode.Cos, x);

        var program = em.ToProgram(wanted);

        program.Select(op => op.Code).ShouldBe([OpCode.LoadX, OpCode.Sin]);
    }

    /// <summary>What an unread op read goes with it, however long the chain.</summary>
    [Fact]
    public void Nor_is_anything_that_fed_only_that()
    {
        var em = new Emitter();
        var wanted = em.Load(OpCode.LoadX);
        var unread = em.Load(OpCode.LoadY);

        for (var i = 0; i < 5; i++) unread = em.Unary(OpCode.Sin, unread);

        em.ToProgram(wanted).Select(op => op.Code).ShouldBe([OpCode.LoadX]);
    }

    [Fact]
    public void The_answer_is_the_one_the_whole_program_gives()
    {
        var em = new Emitter();
        var x = em.Load(OpCode.LoadX);
        var wanted = em.Add(em.Mul(em.Unary(OpCode.Sin, x), 3f), 0.25f);

        em.Ternary(OpCode.Noise3, x, em.Constant(7f), em.Load(OpCode.LoadT));

        var whole = new CompiledPatch(em.ToProgram(), em.RegisterCount, wanted.Base, 1);
        var swept = new CompiledPatch(em.ToProgram(wanted), em.RegisterCount, wanted.Base, 1);

        swept.Ops.Length.ShouldBeLessThan(whole.Ops.Length);

        var a = whole.AllocateRegisters();
        var b = swept.AllocateRegisters();

        foreach (var at in new[] { -1d, -0.3, 0d, 0.5, 1d })
        {
            whole.Evaluate(at, 0, 2d, a, default);
            swept.Evaluate(at, 0, 2d, b, default);

            b[wanted.Base].ShouldBe(a[wanted.Base]);
        }
    }

    /// <summary>A color op is one op, so one channel read keeps all three.</summary>
    [Fact]
    public void One_channel_of_a_color_keeps_the_op_that_wrote_it()
    {
        var em = new Emitter();
        var x = em.Load(OpCode.LoadX);
        var color = em.Triple(OpCode.HsvToRgb, x, em.Constant(1f), em.Constant(1f));

        var program = em.ToProgram(Slot.Scalar(color.Base + 2));

        program.Count(op => op.Code == OpCode.HsvToRgb).ShouldBe(1);
    }

    /// <summary>
    /// A write is read by the next evaluation and by no register, so it is kept
    /// along with everything it is worked out from.
    /// </summary>
    [Fact]
    public void What_the_program_remembers_is_kept()
    {
        var em = new Emitter();
        var cell = em.AllocateUnitSlot();
        var wanted = em.UnitRead(cell);

        em.UnitWrite(cell, em.Add(em.Unary(OpCode.Sin, em.Load(OpCode.LoadT)), 0.5f));

        var program = em.ToProgram(wanted);

        program.Length.ShouldBe(em.ToProgram().Length);
    }

    /// <summary>
    /// Delay lines and phase cells are handed out in the order their ops run, and
    /// <see cref="StateOwners"/> names their modules in that order — so one left
    /// out would give every one after it its neighbor's past.
    /// </summary>
    [Fact]
    public void An_op_that_owns_memory_by_position_is_kept_unread()
    {
        var em = new Emitter();
        var t = em.Load(OpCode.LoadT);
        var half = em.Constant(0.5f);

        em.DelayLine(OpCode.Delay, t, half, half, 1f);
        em.Phase(t, half, half);

        var wanted = em.DelayLine(OpCode.Delay, t, half, half, 2f);

        var program = new CompiledPatch(em.ToProgram(wanted), em.RegisterCount, wanted.Base, 1);

        program.DelayLengths.ShouldBe([1f, 2f]);
        program.PhaseCount.ShouldBe(1);
        em.Owners.Delays.Count.ShouldBe(program.DelayLengths.Count);
    }

    /// <summary>
    /// Where it matters: Coordinates measures a radius and an angle for every
    /// patch that reads x from it.
    /// </summary>
    [Fact]
    public void An_output_no_wire_takes_costs_the_patch_nothing()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add("coord", 0, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0);

        b.Wire(coord, 0, sink, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        program.Ops.ShouldNotContain(op => op.Code == OpCode.Hypot || op.Code == OpCode.Atan2);
    }
}
