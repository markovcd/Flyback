using Flyback.Core.Compile;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// A program and its <see cref="IlProgram"/> run side by side and compared to the bit,
/// for the engine's presets here and the plugins' in Flyback.Plugins.Tests.
/// </summary>
internal static class IlParity
{
    public const double Aspect = 16d / 9d;

    /// <summary>
    /// The picture, whole and staged, over a grid of coordinates and clocks and against
    /// a previous frame that is not empty, so the feedback read is exercised too.
    /// </summary>
    public static void Picture(CompiledPatch program, string name)
    {
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
    /// <paramref name="samples"/> of the sound at the renderer's inner rate, each side
    /// keeping memory of its own, stepped together.
    /// </summary>
    public static void Sound(CompiledPatch program, int samples, string name)
    {
        var il = IlProgram.Compile(program);

        var renderer = new AudioRenderer();
        var expectedMemory = renderer.DelayMemoryFor(program);
        var actualMemory = renderer.DelayMemoryFor(program);

        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();
        var step = 1d / (renderer.SampleRate * renderer.Oversample);

        for (var i = 0; i < samples; i++)
        {
            var t = i * step;

            program.Evaluate(0d, 0d, t, expected, default, expectedMemory);
            il.Evaluate(0d, 0d, t, actual, default, actualMemory);
            ShouldMatch(program, expected, actual, $"{name} at sample {i}");
        }
    }

    public static FeedbackFrame Stripes(int width, int height)
    {
        var pixels = new float[width * height * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = i * 37 % 101 / 100f;

        return new FeedbackFrame(pixels, width, height, 1d / 30d);
    }

    public static void ShouldMatch(CompiledPatch program, double[] expected, double[] actual, string where)
    {
        for (var c = 0; c < program.OutputWidth; c++)
        {
            var at = program.OutputBase + c;
            BitConverter.DoubleToInt64Bits(actual[at])
                .ShouldBe(BitConverter.DoubleToInt64Bits(expected[at]), $"{where}, output {c}: {actual[at]:R} against {expected[at]:R}");
        }
    }
}
