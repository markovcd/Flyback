using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="IlProgram"/> — the program as machine code, which has to be the
/// interpreter by another route.
/// </summary>
/// <remarks>
/// "The same" means the same bits, for the reason it does in
/// <see cref="FramePlanTests"/>, and for one more: the IL takes over from the
/// interpreter while a patch is playing, and anything short of identical would be
/// a click or a jump at the moment it did.
/// </remarks>
public class IlProgramTests
{
    private const double Aspect = IlParity.Aspect;

    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    /// <summary>
    /// Every preset's picture, whole and staged, over a grid of coordinates and
    /// clocks and against a previous frame that is not empty, so the feedback read
    /// is exercised too.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void The_picture_is_the_interpreters_to_the_bit(string name) =>
        IlParity.Picture(Build(name).CompileForVideo().Program, name);

    /// <summary>
    /// Every preset's sound, each side keeping memory of its own, stepped
    /// together — so a delay line, an accumulator or a cell that the IL numbered
    /// differently shows up as every sample after it disagreeing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void The_sound_is_the_interpreters_to_the_bit(string name) =>
        IlParity.Sound(Build(name).CompileForAudio().Program, 12_000, name);

    /// <summary>
    /// Fails the day an opcode is added and the IL backend is not told about it —
    /// and checks, on the way, that what it was told gives the interpreter's answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_lowers_to_the_interpreters_answer(OpCode code)
    {
        var program = OneOp(code);
        var il = IlProgram.Compile(program);

        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        program.Evaluate(0.3d, -0.2d, 1.25d, expected, IlParity.Stripes(8, 8));
        il.Evaluate(0.3d, -0.2d, 1.25d, actual, IlParity.Stripes(8, 8));

        // The outputs only: the operands are locals of the emitted method, and the
        // IL writes back to the bank only what something outside it reads.
        IlParity.ShouldMatch(program, expected, actual, code.ToString());
    }

    /// <summary>
    /// A part that was not built is not an error: it is the interpreter, and gives
    /// the interpreter's answer, so a program built for one renderer is still safe
    /// in the other.
    /// </summary>
    [Theory]
    [InlineData(IlParts.Whole)]
    [InlineData(IlParts.Staged)]
    public void What_was_not_built_is_interpreted(IlParts parts)
    {
        var program = Presets.FeedbackTunnel(NodeCatalog.Current).CompileForVideo().Program;
        var il = IlProgram.Compile(program, parts);

        il.Methods.Parts.ShouldBe(parts);

        var feedback = IlParity.Stripes(16, 9);
        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        foreach (var stage in Enum.GetValues<EvaluationStage>())
        {
            program.EvaluateStage(stage, 0.4d, -0.3d, 2.5d, expected, feedback, Aspect);
            il.EvaluateStage(stage, 0.4d, -0.3d, 2.5d, actual, feedback, Aspect);
        }

        IlParity.ShouldMatch(program, expected, actual, $"{parts}, staged");

        program.Evaluate(0.4d, -0.3d, 2.5d, expected, feedback, aspect: Aspect);
        il.Evaluate(0.4d, -0.3d, 2.5d, actual, feedback, aspect: Aspect);

        IlParity.ShouldMatch(program, expected, actual, $"{parts}, whole");
    }

    /// <summary>An opcode nothing names never reaches the IL: the program refuses it as it is built.</summary>
    [Fact]
    public void An_unknown_opcode_is_refused_before_it_reaches_the_il() =>
        Should.Throw<ArgumentOutOfRangeException>(() => OneOp((OpCode)200));

    /// <summary>
    /// A frame drawn by the renderer with the IL attached is the frame it draws
    /// without, byte for byte — twice, so the second frame reads the first
    /// through feedback.
    /// </summary>
    [Fact]
    public void A_renderer_draws_the_same_frames_either_way()
    {
        const int width = 64, height = 36;

        var interpreted = Presets.FeedbackTunnel(NodeCatalog.Current).CompileForVideo().Program;
        var compiled = Presets.FeedbackTunnel(NodeCatalog.Current).CompileForVideo().Program;
        compiled.Attach(IlProgram.Compile(compiled));

        var one = new SynthRenderer();
        var other = new SynthRenderer();

        for (var frame = 0; frame < 2; frame++)
        {
            var expected = new byte[width * height * 4];
            var actual = new byte[width * height * 4];

            one.Render(interpreted, frame / 30d, width, height, expected, width * 4);
            other.Render(compiled, frame / 30d, width, height, actual, width * 4);

            actual.ShouldBe(expected, $"frame {frame}");
        }
    }

    [Fact]
    public void A_renderer_plays_the_same_samples_either_way()
    {
        var interpreted = Presets.FourVoices(NodeCatalog.Current).CompileForAudio().Program;
        var compiled = Presets.FourVoices(NodeCatalog.Current).CompileForAudio().Program;
        compiled.Attach(IlProgram.Compile(compiled));

        var expected = new float[4_096];
        var actual = new float[4_096];

        new AudioRenderer().Render(interpreted, expected);
        new AudioRenderer().Render(compiled, actual);

        actual.ShouldBe(expected);
    }

    /// <summary>
    /// A program longer than one method is several, and each numbers its delay
    /// lines and accumulators from where the one before it stopped.
    /// </summary>
    [Fact]
    public void A_program_split_across_methods_keeps_its_memory_in_order()
    {
        var ops = new List<Op> { new(OpCode.LoadT, 0), new(OpCode.Const, 1, k: 0.5f) };
        var last = 0;

        for (var i = 0; i < 400; i++)
        {
            var next = ops.Count + 1;
            // A frequency for an accumulator, or a delay of a few samples.
            ops.Add(new Op(OpCode.Const, next, k: i % 2 == 0 ? 1f + i % 7 : (1f + i % 5) * 1e-5f));
            ops.Add(i % 2 == 0
                ? new Op(OpCode.Phase, next + 1, 0, next, last)
                : new Op(OpCode.Delay, next + 1, last, 1, next, 1e-4f * (1 + i % 3)));
            last = next + 1;
        }

        var program = new CompiledPatch([.. ops, new Op(OpCode.Copy, ops.Count + 1, last), new Op(OpCode.Sin, ops.Count + 2, last)], ops.Count + 3, ops.Count + 1, 2);
        program.Ops.Length.ShouldBeGreaterThan(800);

        var il = IlProgram.Compile(program, IlParts.Whole);
        var renderer = new AudioRenderer();
        var expectedMemory = renderer.DelayMemoryFor(program);
        var actualMemory = renderer.DelayMemoryFor(program);
        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        for (var i = 0; i < 2_000; i++)
        {
            program.Evaluate(0d, 0d, i / 192_000d, expected, default, expectedMemory);
            il.Evaluate(0d, 0d, i / 192_000d, actual, default, actualMemory);
            IlParity.ShouldMatch(program, expected, actual, $"sample {i}");
        }
    }

    [Fact]
    public void Code_bound_to_one_patch_cannot_be_attached_to_another()
    {
        var one = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var other = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;

        Should.Throw<ArgumentException>(() => other.Attach(IlProgram.Compile(one)));
    }

    private static Patch Build(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.Current);

    /// <summary>Three operands and the op under test writing r3, as the GLSL tests build it.</summary>
    private static CompiledPatch OneOp(OpCode code) =>
        new(
            [
                new Op(OpCode.Const, 0, k: 0.25f),
                new Op(OpCode.Const, 1, k: 0.5f),
                new Op(OpCode.Const, 2, k: 0.75f),
                new Op(code, 3, 0, 1, 2, 1f),
            ],
            6,
            3);
}
