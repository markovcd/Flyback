using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// The sound's ops that follow from the constants alone, run once a buffer by the
/// interpreter: which ops those are, and that the sound comes out the same to the bit.
/// </summary>
public class SoundPlanTests
{
    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void A_presets_sound_is_the_same_with_its_fixed_part_lifted_out(string name) =>
        SoundPlanParity.Sound(Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn).CompileForAudio(NodeCatalog.BuiltIn).Program, 4_800, name);

    /// <summary>2 × 3 is fixed, and so is its sine; times the clock is not.</summary>
    [Fact]
    public void What_follows_from_the_constants_alone_is_fixed()
    {
        var plan = Plan(
            new Op(OpCode.Const, 0, k: 2f),
            new Op(OpCode.Const, 1, k: 3f),
            new Op(OpCode.Mul, 2, 0, 1),
            new Op(OpCode.Sin, 3, 2),
            new Op(OpCode.LoadT, 4),
            new Op(OpCode.Mul, 5, 3, 4));

        plan.Fixed.Select(op => op.Out).ShouldBe([0, 1, 2, 3]);
        plan.Varying.Select(op => op.Out).ShouldBe([4, 5]);
    }

    /// <summary>A key or a knob played in may change inside a buffer, and an op with memory changes every sample.</summary>
    [Theory]
    [InlineData(OpCode.LoadLive)]
    [InlineData(OpCode.Delay)]
    [InlineData(OpCode.Phase)]
    [InlineData(OpCode.UnitRead)]
    [InlineData(OpCode.Table)]
    public void What_the_player_or_memory_decides_varies(OpCode code)
    {
        var plan = Plan(
            new Op(OpCode.Const, 0, k: 0.5f),
            new Op(code, 1, 0, 0, 0, 1f),
            new Op(OpCode.Add, 2, 1, 0));

        plan.Varying.Select(op => op.Code).ShouldBe([code, OpCode.Add]);
    }

    [Fact]
    public void A_program_that_writes_a_register_twice_runs_whole() =>
        SoundPlan.For([new Op(OpCode.Const, 0, k: 1f), new Op(OpCode.Const, 0, k: 2f)], 1).ShouldBeNull();

    [Fact]
    public void A_program_with_nothing_fixed_runs_whole() =>
        SoundPlan.For([new Op(OpCode.LoadT, 0), new Op(OpCode.Sin, 1, 0)], 2).ShouldBeNull();

    private static SoundPlan Plan(params Op[] ops) => SoundPlan.For(ops, ops.Max(op => op.Out) + 1).ShouldNotBeNull();
}
