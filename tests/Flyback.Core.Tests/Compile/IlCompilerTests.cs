using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="IlCompiler"/> — what puts IL under a program that is already playing.
/// </summary>
public class IlCompilerTests
{
    [Fact]
    public async Task A_submitted_program_is_interpreted_until_its_il_arrives()
    {
        using var compiler = new IlCompiler();
        var program = Plasma(0.5f);

        compiler.Submit(program, IlLane.Picture);
        await compiler.Settled();

        program.Il.ShouldNotBeNull().Source.ShouldBeSameAs(program);
    }

    /// <summary>
    /// The knob case: a program that differs only in a constant finds its code
    /// built and is attached before <see cref="IlCompiler.Submit"/> returns — and
    /// reads its own constant, not the one the code was built with.
    /// </summary>
    [Fact]
    public async Task A_turned_knob_is_attached_at_once_and_reads_its_own_value()
    {
        using var compiler = new IlCompiler();
        var before = Plasma(0.5f);

        compiler.Submit(before, IlLane.Picture);
        await compiler.Settled();

        var after = Plasma(0.9f);
        compiler.Submit(after, IlLane.Picture);

        var il = after.Il.ShouldNotBeNull();
        il.Source.ShouldBeSameAs(after);

        var expected = after.AllocateRegisters();
        var actual = after.AllocateRegisters();

        foreach (var stage in Enum.GetValues<EvaluationStage>())
        {
            after.EvaluateStage(stage, 0.3d, 0.4d, 2d, expected, default);
            il.EvaluateStage(stage, 0.3d, 0.4d, 2d, actual, default);
        }

        actual[after.OutputBase].ShouldBe(expected[after.OutputBase]);
    }

    /// <summary>
    /// Each lane builds what its renderer calls and nothing else, since every part
    /// is its own trip through the JIT.
    /// </summary>
    [Fact]
    public async Task The_picture_is_built_in_stages_and_the_sound_whole()
    {
        using var compiler = new IlCompiler();
        var picture = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var sound = Presets.Drone(NodeCatalog.Current).CompileForAudio().Program;

        compiler.Submit(picture, IlLane.Picture);
        compiler.Submit(sound, IlLane.Sound);
        await compiler.Settled();

        picture.Il.ShouldNotBeNull().Parts.ShouldBe(IlParts.Staged);
        sound.Il.ShouldNotBeNull().Parts.ShouldBe(IlParts.Whole);
    }

    /// <summary>
    /// One program in both lanes is two builds, not one build shared: code built
    /// whole cannot run a stage, and handing it to the picture would only move the
    /// picture back onto the interpreter.
    /// </summary>
    [Fact]
    public async Task One_shape_in_both_lanes_is_built_for_each()
    {
        using var compiler = new IlCompiler();
        var heard = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var seen = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;

        compiler.Submit(heard, IlLane.Sound);
        await compiler.Settled();

        compiler.Submit(seen, IlLane.Picture);
        seen.Il.ShouldBeNull("nothing built in stages yet, so nothing to attach at once");

        await compiler.Settled();
        seen.Il.ShouldNotBeNull().Parts.ShouldBe(IlParts.Staged);
    }

    [Fact]
    public async Task Turning_it_off_puts_the_interpreter_back_and_on_brings_the_il_back()
    {
        using var compiler = new IlCompiler();
        var program = Plasma(0.5f);

        compiler.Submit(program, IlLane.Picture);
        await compiler.Settled();

        compiler.Enabled = false;
        program.Il.ShouldBeNull();

        compiler.Enabled = true;
        await compiler.Settled();
        program.Il.ShouldNotBeNull();
    }

    [Fact]
    public async Task Nothing_is_built_while_it_is_off()
    {
        using var compiler = new IlCompiler { Enabled = false };
        var program = Plasma(0.5f);

        compiler.Submit(program, IlLane.Sound);
        await compiler.Settled();

        program.Il.ShouldBeNull();
    }

    /// <summary>
    /// Only what is playing now is built. A program replaced before the compiler
    /// reached it is never given IL, however long it waited.
    /// </summary>
    [Fact]
    public async Task The_latest_program_in_a_lane_is_the_one_that_ends_up_compiled()
    {
        using var compiler = new IlCompiler();

        var programs = Enumerable.Range(0, 8)
            .Select(i => Presets.All[i % Presets.All.Count].Build(NodeCatalog.Current).CompileForVideo().Program)
            .ToList();

        foreach (var program in programs) compiler.Submit(program, IlLane.Picture);
        await compiler.Settled();

        programs[^1].Il.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_two_lanes_do_not_replace_each_other()
    {
        using var compiler = new IlCompiler();
        var picture = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var sound = Presets.Drone(NodeCatalog.Current).CompileForAudio().Program;

        compiler.Submit(picture, IlLane.Picture);
        compiler.Submit(sound, IlLane.Sound);
        await compiler.Settled();

        picture.Il.ShouldNotBeNull();
        sound.Il.ShouldNotBeNull();
    }

    /// <summary>Plasma with one of its knobs at <paramref name="speed"/>, which changes a constant and nothing else.</summary>
    private static CompiledPatch Plasma(float speed)
    {
        var program = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;

        var ops = program.Ops.ToArray();
        var at = Array.FindLastIndex(ops, o => o.Code is OpCode.Const);
        ops[at] = new Op(OpCode.Const, ops[at].Out, k: 1000f + speed);

        return new CompiledPatch(ops, program.RegisterCount, program.OutputBase, program.OutputWidth);
    }
}
