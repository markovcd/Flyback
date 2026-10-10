using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// What <see cref="JsEmitter"/> writes, run under Node on a heap of its own: the
/// interpreter's sound by another route, which is what a browser plays.
/// </summary>
/// <remarks>
/// Compared bit for bit, save a hair where the script rounds a <c>Math</c> call differently (<see cref="ScriptRun.Inexact"/>). Skipped where there is no Node.
/// </remarks>
public class JsProgramTests
{
    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    /// <summary>
    /// Every preset's sound, each side keeping memory of its own, for three thousand
    /// frames, so a delay line or accumulator numbered differently shows up as every
    /// sample after it disagreeing.
    /// </summary>
    [Theory]
    [TestCategory(TestCategory.Node)]
    [MemberData(nameof(AllPresets))]
    public void The_sound_is_the_interpreters(string name)
    {
        TestCategory.Node.Require(NodeJs.Path is not null, "no Node on this machine");

        var program = Presets.All.Single(p => p.Name == name).Build(NodeCatalog.Current).CompileForAudio().Program;

        ScriptRun.ShouldMatch(ScriptRun.Interpret(program, 3_000), ScriptRun.Script(program, 3_000), name, ScriptRun.HairFor(program));
    }

    /// <summary>A knob turned is a new value in the layout and the same script, so the engine keeps what it optimized.</summary>
    [Fact]
    public void A_knob_turned_leaves_the_script_as_it_was()
    {
        var (before, after) = SineTurned();

        JsEmitter.Emit(after).ShouldBe(JsEmitter.Emit(before));
        JsEmitter.Constants(after).ShouldNotBe(JsEmitter.Constants(before));
        JsEmitter.Constants(after).ShouldContain(331d);
    }

    /// <summary>A script handed a turned knob's constants as it plays sounds as the interpreter playing the turned program on.</summary>
    [Fact]
    [TestCategory(TestCategory.Node)]
    public void A_script_retuned_as_it_plays_is_the_turned_program_played_on()
    {
        TestCategory.Node.Require(NodeJs.Path is not null, "no Node on this machine");

        var (before, after) = SineTurned();

        ScriptRun.ShouldMatch(ScriptRun.Interpret(before, 1_000, (after, 400)), ScriptRun.Script(before, 1_000, (after, 400)), "retuned", ScriptRun.HairFor(before));
    }

    /// <summary>A Line In's two inputs are written into the script once a frame, from what the renderer hears, as the interpreter's renderer writes them.</summary>
    [Fact]
    [TestCategory(TestCategory.Node)]
    public void A_line_in_is_heard_a_frame_at_a_time()
    {
        TestCategory.Node.Require(NodeJs.Path is not null, "no Node on this machine");

        var builder = new PatchBuilder();
        var line = builder.Add(NodeCatalog.LineInTypeId, 0, 0, (0, 0.8f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(line, 0, sink, NodeCatalog.OutputLeftPort).Wire(line, 1, sink, NodeCatalog.OutputRightPort);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var heard = new float[2_000 * 2];

        for (var i = 0; i < 2_000; i++)
        {
            heard[i * 2] = 0.5f * MathF.Sin(i * 0.05f);
            heard[i * 2 + 1] = 0.25f * MathF.Cos(i * 0.031f);
        }

        var interpreted = ScriptRun.Interpret(program, 1_000, input: heard);

        interpreted.ShouldContain(sample => Math.Abs(sample) > 0.1f, "the interpreter heard nothing, so agreeing with it proves nothing");
        ScriptRun.ShouldMatch(interpreted, ScriptRun.Script(program, 1_000, input: heard), "line in", ScriptRun.HairFor(program));
    }

    /// <summary>A sine heard on the left, before and after its frequency is turned from 220 to 331.</summary>
    private static (CompiledPatch Before, CompiledPatch After) SineTurned()
    {
        var b = new PatchBuilder(NodeCatalog.Current);
        var sine = b.Add("osc.sine", (1, 220f));
        var output = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(sine, 0, output, NodeCatalog.OutputLeftPort);

        var before = b.Patch.CompileForAudio().Program;
        sine.InputValues[1] = 331f;

        return (before, b.Patch.CompileForAudio().Program);
    }

    /// <summary>Fails the day an opcode is added and the emitter is not told about it.</summary>
    [Theory]
    [TestCategory(TestCategory.Node)]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_gives_the_interpreters_answer(OpCode code)
    {
        TestCategory.Node.Require(NodeJs.Path is not null, "no Node on this machine");

        var program = OneOp(code);

        ScriptRun.ShouldMatch(ScriptRun.Interpret(program, 8), ScriptRun.Script(program, 8), code.ToString(), ScriptRun.HairFor(program));
    }

    /// <summary>
    /// A program longer than one function is several, and each numbers its delay lines
    /// and accumulators from where the one before it stopped, and hands its registers on.
    /// </summary>
    [Fact]
    [TestCategory(TestCategory.Node)]
    public void A_program_split_across_functions_keeps_its_memory_in_order()
    {
        TestCategory.Node.Require(NodeJs.Path is not null, "no Node on this machine");

        var ops = new List<Op> { new(OpCode.LoadT, 0), new(OpCode.Const, 1, k: 0.5f) };
        var last = 0;

        for (var i = 0; i < 400; i++)
        {
            var next = ops.Count + 1;
            ops.Add(new Op(OpCode.Const, next, k: i % 2 == 0 ? 1f + i % 7 : (1f + i % 5) * 1e-5f));
            ops.Add(i % 2 == 0
                ? new Op(OpCode.Phase, next + 1, 0, next, last)
                : new Op(OpCode.Delay, next + 1, last, 1, next, 1e-4f * (1 + i % 3)));
            last = next + 1;
        }

        var program = new CompiledPatch(
            [.. ops, new Op(OpCode.Copy, ops.Count + 1, last), new Op(OpCode.Sin, ops.Count + 2, last)],
            ops.Count + 3,
            ops.Count + 1,
            2);

        program.Ops.Length.ShouldBeGreaterThan(3 * JsEmitter.ChunkSize);

        ScriptRun.ShouldMatch(ScriptRun.Interpret(program, 500), ScriptRun.Script(program, 500), "split", ScriptRun.HairFor(program));
    }

    [Fact]
    public void A_program_that_reads_a_picture_is_left_to_the_interpreter()
    {
        var picture = new LoadedImage(new float[2 * 2 * 3], 2, 2);
        var program = new CompiledPatch(
            [new Op(OpCode.Const, 0), new Op(OpCode.SamplePicture, 1, 0, 0, k: 0)],
            4,
            1,
            2,
            pictures: [picture]);

        JsEmitter.Emit(program).ShouldBeNull();
    }

    /// <summary>Three operands and the op under test writing r3, with r3 and r4 heard as left and right.</summary>
    private static CompiledPatch OneOp(OpCode code) =>
        new(
            [
                new Op(OpCode.Const, 0, k: 0.25f),
                new Op(OpCode.Const, 1, k: 0.5f),
                new Op(OpCode.Const, 2, k: 0.75f),
                new Op(code, 3, 0, 1, 2, 1f),
            ],
            6,
            3,
            2);
}
