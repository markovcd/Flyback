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

        // Whole code attached at once would queue no build and stay whole. Whether
        // the staged build has landed straight after Submit is a race, so not asked.
        compiler.Submit(seen, IlLane.Picture);

        await compiler.Settled();
        heard.Il.ShouldNotBeNull().Parts.ShouldBe(IlParts.Whole);
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

    /// <summary>A gallery tile's still is drawn from IL built before its first frame, not after.</summary>
    [Fact]
    public void A_program_compiled_on_the_spot_has_its_il_when_the_call_returns()
    {
        using var compiler = new IlCompiler();
        var program = Plasma(0.5f);

        compiler.Compile(program, IlLane.AuditionPicture);

        program.Il.ShouldNotBeNull().Source.ShouldBeSameAs(program);
    }

    [Fact]
    public void Nothing_is_compiled_on_the_spot_while_it_is_off()
    {
        using var compiler = new IlCompiler { Enabled = false };
        var program = Plasma(0.5f);

        compiler.Compile(program, IlLane.AuditionPicture);

        program.Il.ShouldBeNull();
    }

    /// <summary>An offline render builds only the part its renderer calls.</summary>
    [Theory]
    [InlineData(IlParts.Staged)]
    [InlineData(IlParts.Whole)]
    public void A_program_compiled_once_has_its_il_when_the_call_returns(IlParts parts)
    {
        var program = Plasma(0.5f);

        IlCompiler.CompileOnce(program, parts).ShouldBeNull();

        var il = program.Il.ShouldNotBeNull();
        il.Source.ShouldBeSameAs(program);
        il.Parts.ShouldBe(parts);
    }

    /// <summary>A preset tried in the gallery does not take the patch's own lanes from it.</summary>
    [Fact]
    public async Task An_audition_does_not_replace_the_patch_in_its_lanes()
    {
        using var compiler = new IlCompiler();
        var patch = Presets.Drone(NodeCatalog.Current).CompileForAudio().Program;
        var tried = Presets.Plasma(NodeCatalog.Current).CompileForAudio().Program;

        compiler.Submit(patch, IlLane.Sound);
        compiler.Submit(tried, IlLane.AuditionSound);
        await compiler.Settled();

        patch.Il.ShouldNotBeNull();
        tried.Il.ShouldNotBeNull();
    }

    /// <summary>An opened patch is never left for the interpreter: it waits until its IL is there.</summary>
    [Fact]
    public async Task A_held_program_waits_until_its_il_arrives()
    {
        using var compiler = new IlCompiler();
        var program = Plasma(0.5f);

        compiler.Submit(program, IlLane.Picture, held: true);

        // Read in this order: a hold is let go only after the IL is attached.
        (program.Waiting || program.Il is not null).ShouldBeTrue();

        await compiler.Settled();

        program.Waiting.ShouldBeFalse();
        program.Il.ShouldNotBeNull();
    }

    [Fact]
    public void An_edited_program_never_waits()
    {
        using var compiler = new IlCompiler();
        var program = Plasma(0.5f);

        compiler.Submit(program, IlLane.Picture);

        program.Waiting.ShouldBeFalse();
    }

    [Fact]
    public async Task A_held_program_whose_shape_is_built_does_not_wait()
    {
        using var compiler = new IlCompiler();

        compiler.Submit(Plasma(0.5f), IlLane.Picture);
        await compiler.Settled();

        var opened = Plasma(0.9f);
        compiler.Submit(opened, IlLane.Picture, held: true);

        opened.Waiting.ShouldBeFalse();
        opened.Il.ShouldNotBeNull();
    }

    [Fact]
    public void A_held_program_is_let_go_when_an_edit_replaces_it()
    {
        using var compiler = new IlCompiler();
        var opened = Presets.WholeBand(NodeCatalog.Current).CompileForVideo().Program;

        compiler.Submit(opened, IlLane.Picture, held: true);
        compiler.Submit(Plasma(0.5f), IlLane.Picture);

        opened.Waiting.ShouldBeFalse();
    }

    /// <summary>A preset picked from the text view is read into text the moment it opens, which is an edit.</summary>
    [Fact]
    public async Task An_edit_to_a_patch_that_has_not_started_waits_with_it()
    {
        using var compiler = new IlCompiler();
        var opened = Presets.WholeBand(NodeCatalog.Current).CompileForVideo().Program;
        var edited = Presets.WholeBand(NodeCatalog.Current).CompileForVideo().Program;

        compiler.Submit(opened, IlLane.Picture, held: true);
        var stillWaiting = opened.Waiting;
        compiler.Submit(edited, IlLane.Picture);

        opened.Waiting.ShouldBeFalse();
        if (stillWaiting) (edited.Waiting || edited.Il is not null).ShouldBeTrue();

        await compiler.Settled();
        edited.Waiting.ShouldBeFalse();
    }

    [Fact]
    public void A_held_program_is_let_go_when_compiling_is_turned_off()
    {
        using var compiler = new IlCompiler();
        var opened = Presets.WholeBand(NodeCatalog.Current).CompileForVideo().Program;

        compiler.Submit(opened, IlLane.Picture, held: true);
        compiler.Enabled = false;

        opened.Waiting.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_is_held_while_it_is_off()
    {
        using var compiler = new IlCompiler { Enabled = false };
        var opened = Plasma(0.5f);

        compiler.Submit(opened, IlLane.Picture, held: true);

        opened.Waiting.ShouldBeFalse();
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
