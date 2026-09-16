using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
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
    private const double Aspect = 16d / 9d;

    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    /// <summary>
    /// Every preset's picture, whole and staged, over a grid of coordinates and
    /// clocks and against a previous frame that is not empty, so the feedback read
    /// is exercised too.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void The_picture_is_the_interpreters_to_the_bit(string name)
    {
        var program = Build(name).CompileForVideo().Program;
        var il = IlProgram.Compile(program);

        var feedback = Stripes(48, 27);
        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        foreach (var t in (double[])[0d, 0.5d, 37.25d, 3600.125d])
        {
            for (var i = 0; i < 9; i++)
            {
                var y = 1d - 2d * (i + 0.5d) / 9d;

                program.EvaluateStage(EvaluationStage.Frame, 0d, y, t, expected, feedback, Aspect);
                program.EvaluateStage(EvaluationStage.Row, 0d, y, t, expected, feedback, Aspect);
                il.EvaluateStage(EvaluationStage.Frame, 0d, y, t, actual, feedback, Aspect);
                il.EvaluateStage(EvaluationStage.Row, 0d, y, t, actual, feedback, Aspect);

                for (var j = 0; j < 17; j++)
                {
                    var x = (2d * (j + 0.5d) / 17d - 1d) * Aspect;

                    program.EvaluateStage(EvaluationStage.Pixel, x, y, t, expected, feedback, Aspect);
                    il.EvaluateStage(EvaluationStage.Pixel, x, y, t, actual, feedback, Aspect);
                    ShouldMatch(program, expected, actual, $"{name} staged at ({x}, {y}, {t})");

                    program.Evaluate(x, y, t, expected, feedback, aspect: Aspect);
                    il.Evaluate(x, y, t, actual, feedback, aspect: Aspect);
                    ShouldMatch(program, expected, actual, $"{name} whole at ({x}, {y}, {t})");
                }
            }
        }
    }

    /// <summary>
    /// Every preset's sound, each side keeping memory of its own, stepped
    /// together — so a delay line, an accumulator or a cell that the IL numbered
    /// differently shows up as every sample after it disagreeing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void The_sound_is_the_interpreters_to_the_bit(string name)
    {
        var program = Build(name).CompileForAudio().Program;
        var il = IlProgram.Compile(program);

        var renderer = new AudioRenderer();
        var expectedMemory = renderer.DelayMemoryFor(program);
        var actualMemory = renderer.DelayMemoryFor(program);

        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();
        var step = 1d / (renderer.SampleRate * renderer.Oversample);

        for (var i = 0; i < 12_000; i++)
        {
            var t = i * step;

            program.Evaluate(0d, 0d, t, expected, default, expectedMemory);
            il.Evaluate(0d, 0d, t, actual, default, actualMemory);
            ShouldMatch(program, expected, actual, $"{name} at sample {i}");
        }
    }

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

        program.Evaluate(0.3d, -0.2d, 1.25d, expected, Stripes(8, 8));
        il.Evaluate(0.3d, -0.2d, 1.25d, actual, Stripes(8, 8));

        // The outputs only: the operands are locals of the emitted method, and the
        // IL writes back to the bank only what something outside it reads.
        ShouldMatch(program, expected, actual, code.ToString());
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

        il.Parts.ShouldBe(parts);

        var feedback = Stripes(16, 9);
        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        foreach (var stage in Enum.GetValues<EvaluationStage>())
        {
            program.EvaluateStage(stage, 0.4d, -0.3d, 2.5d, expected, feedback, Aspect);
            il.EvaluateStage(stage, 0.4d, -0.3d, 2.5d, actual, feedback, Aspect);
        }

        ShouldMatch(program, expected, actual, $"{parts}, staged");

        program.Evaluate(0.4d, -0.3d, 2.5d, expected, feedback, aspect: Aspect);
        il.Evaluate(0.4d, -0.3d, 2.5d, actual, feedback, aspect: Aspect);

        ShouldMatch(program, expected, actual, $"{parts}, whole");
    }

    [Fact]
    public void An_unknown_opcode_is_refused_rather_than_skipped() =>
        Should.Throw<NotSupportedException>(() => IlProgram.Compile(OneOp((OpCode)200)));

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

    [Fact]
    public void Code_bound_to_one_patch_cannot_be_attached_to_another()
    {
        var one = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;
        var other = Presets.Plasma(NodeCatalog.Current).CompileForVideo().Program;

        Should.Throw<ArgumentException>(() => other.Attach(IlProgram.Compile(one)));
    }

    private static Patch Build(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.Current);

    private static FeedbackFrame Stripes(int width, int height)
    {
        var pixels = new float[width * height * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = i * 37 % 101 / 100f;

        return new FeedbackFrame(pixels, width, height);
    }

    private static void ShouldMatch(CompiledPatch program, double[] expected, double[] actual, string where)
    {
        for (var c = 0; c < program.OutputWidth; c++)
        {
            var at = program.OutputBase + c;
            BitConverter.DoubleToInt64Bits(actual[at])
                .ShouldBe(BitConverter.DoubleToInt64Bits(expected[at]), $"{where}, output {c}: {actual[at]:R} against {expected[at]:R}");
        }
    }

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
