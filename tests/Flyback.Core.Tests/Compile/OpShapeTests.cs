using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// The shape table every backend and every pass reads: an opcode added without a
/// row fails here, not as a register read out of range somewhere else.
/// </summary>
public class OpShapeTests
{
    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_has_a_shape(OpCode code)
    {
        OpShape.Inputs(code).ShouldBeInRange(0, 3);
        OpShape.Outputs(code).ShouldBeInRange(0, 3);
    }

    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void An_op_reads_as_many_registers_as_its_shape_says(OpCode code)
    {
        var op = new Op(code, 9, a: 1, b: 2, c: 3);

        OpShape.Reads(op).ShouldBe(new[] { 1, 2, 3 }.Take(OpShape.Inputs(code)));
    }

    [Fact]
    public void An_opcode_outside_the_table_is_refused_rather_than_guessed()
    {
        var unnamed = (OpCode)255;

        Should.Throw<ArgumentOutOfRangeException>(() => OpShape.Inputs(unnamed));
        Should.Throw<ArgumentOutOfRangeException>(() => OpShape.Outputs(unnamed));
    }

    /// <summary>A line or a cell is owned by position, so an op that owns one stays in the program whatever reads it.</summary>
    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void An_op_that_owns_memory_is_kept(OpCode code)
    {
        if (OpShape.Owns(code) != OpMemory.None) OpShape.Kept(code).ShouldBeTrue();
        if (OpShape.Outputs(code) == 0) OpShape.Kept(code).ShouldBeTrue();
    }

    [Fact]
    public void Lines_and_cells_belong_to_the_ops_that_take_them()
    {
        OpShape.Owns(OpCode.Delay).ShouldBe(OpMemory.Line);
        OpShape.Owns(OpCode.Allpass).ShouldBe(OpMemory.Line);
        OpShape.Owns(OpCode.Phase).ShouldBe(OpMemory.Cell);
        OpShape.Owns(OpCode.Add).ShouldBe(OpMemory.None);
    }
}
