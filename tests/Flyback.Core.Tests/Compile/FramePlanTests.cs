using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="FramePlan"/> — the program sorted by how often a frame has to run
/// each op, so a pixel pays only for what a pixel changes.
/// </summary>
/// <remarks>
/// The whole of the claim is that running the three stages is the same as
/// running the program, and "the same" here means the same bits rather than the
/// same to a tolerance. A picture is compared against a previous frame of itself
/// in every feedback patch there is, so a plan that was merely close would drift
/// — and there is no reason for it to be close rather than exact, because the
/// ops are the program's own and only their order has moved.
/// </remarks>
public class FramePlanTests
{
    /// <summary>
    /// Every shipped preset, evaluated both ways over a grid of coordinates and
    /// clocks, has to give the identical double out of every output register.
    /// </summary>
    /// <remarks>
    /// The presets rather than a hand-built program, because what has to hold is
    /// that no module in the catalogue lowers to something the sort gets wrong —
    /// and between them they reach every op the compiler emits.
    /// </remarks>
    [Fact]
    public void Staged_evaluation_matches_the_whole_program()
    {
        foreach (var preset in Presets.All)
        {
            var program = preset.Build(NodeCatalog.Current).CompileForVideo().Program;

            var whole = program.AllocateRegisters();
            var staged = program.AllocateRegisters();

            for (var i = 0; i < 16; i++)
            {
                var t = i * 0.137d;
                var y = 1d - 2d * (i + 0.5d) / 16d;

                program.EvaluateStage(EvaluationStage.Frame, 0d, y, t, staged, default, Aspect);
                program.EvaluateStage(EvaluationStage.Row, 0d, y, t, staged, default, Aspect);

                for (var j = 0; j < 16; j++)
                {
                    var x = (2d * (j + 0.5d) / 16d - 1d) * Aspect;

                    program.Evaluate(x, y, t, whole, default, aspect: Aspect);
                    program.EvaluateStage(EvaluationStage.Pixel, x, y, t, staged, default, Aspect);

                    for (var c = 0; c < program.OutputWidth; c++)
                        staged[program.OutputBase + c]
                            .ShouldBe(whole[program.OutputBase + c], $"{preset.Name} at ({x}, {y}, {t})");
                }
            }
        }
    }

    /// <summary>
    /// A stage holds every op that reaches it and no more, so the three ranges
    /// partition the program rather than overlapping or losing any of it.
    /// </summary>
    [Fact]
    public void The_three_stages_partition_the_program()
    {
        var program = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var plan = program.Plan.ShouldNotBeNull();

        plan.Ops.Length.ShouldBe(program.Ops.Length);

        var (_, frameTo) = plan.Range(EvaluationStage.Frame);
        var (rowFrom, rowTo) = plan.Range(EvaluationStage.Row);
        var (pixelFrom, pixelTo) = plan.Range(EvaluationStage.Pixel);

        frameTo.ShouldBe(rowFrom);
        rowTo.ShouldBe(pixelFrom);
        pixelTo.ShouldBe(plan.Ops.Length);

        // The sort moves ops, it does not add or drop them.
        plan.Ops.OrderBy(o => o.ToString()).ShouldBe(program.Ops.OrderBy(o => o.ToString()));
    }

    /// <summary>
    /// Nothing that a coordinate reaches is left in the frame's stage, which is
    /// what the saving rests on — a plan that put everything in the pixel stage
    /// would be correct and worth nothing.
    /// </summary>
    [Fact]
    public void The_clock_and_the_literals_come_out_of_the_pixel_loop()
    {
        var program = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var plan = program.Plan.ShouldNotBeNull();

        var (_, frameTo) = plan.Range(EvaluationStage.Frame);

        plan.Ops[..frameTo].ShouldContain(o => o.Code == OpCode.LoadT);
        plan.Ops[..frameTo].Count(o => o.Code is OpCode.Const)
            .ShouldBe(program.Ops.Count(o => o.Code is OpCode.Const));

        var (pixelFrom, _) = plan.Range(EvaluationStage.Pixel);
        plan.Ops[pixelFrom..].Length.ShouldBeLessThan(program.Ops.Length);
    }

    /// <summary>
    /// A program that writes a register twice gets no plan, because a stage is
    /// read back off the register an input came from and that only means
    /// something while a register is written once.
    /// </summary>
    [Fact]
    public void A_program_that_is_not_single_assignment_gets_no_plan()
    {
        Op[] ops =
        [
            new(OpCode.LoadX, 0),
            new(OpCode.LoadT, 0),
            new(OpCode.Copy, 1, 0),
        ];

        new CompiledPatch(ops, 2, 1, 1).Plan.ShouldBeNull();
    }

    /// <summary>
    /// And a program with no plan still draws: the pixel stage runs the whole of
    /// it and the other two do nothing, so a renderer staging its loops is right
    /// either way.
    /// </summary>
    [Fact]
    public void A_program_with_no_plan_runs_whole_at_the_pixel_stage()
    {
        Op[] ops =
        [
            new(OpCode.LoadX, 0),
            new(OpCode.LoadT, 0),
            new(OpCode.Copy, 1, 0),
        ];

        var program = new CompiledPatch(ops, 2, 1, 1);
        var registers = program.AllocateRegisters();

        program.EvaluateStage(EvaluationStage.Frame, 0.5d, 0d, 7d, registers, default);
        registers[1].ShouldBe(0d);

        program.EvaluateStage(EvaluationStage.Pixel, 0.5d, 0d, 7d, registers, default);
        registers[1].ShouldBe(7d);
    }

    private const double Aspect = 16d / 9d;
}
