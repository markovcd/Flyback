using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Benchmarks;

/// <summary>
/// The interpreter's inner loop on its own: one program over a row of pixel
/// coordinates, walked whole and walked in stages.
/// </summary>
[MemoryDiagnoser]
public class EvaluateBenchmarks
{
    private const int Pixels = 4096;
    private const double Aspect = 1.777d;
    private const double Y = 0.25d;
    private const double T = 1.5d;

    private CompiledPatch patch = null!;
    private double[] registers = null!;

    [Params("Plasma", "Kaleidoscope", "Nebula", "FeedbackTunnel", "WholeBand")]
    public string Preset { get; set; } = "Plasma";

    [GlobalSetup]
    public void Setup()
    {
        patch = Patches.Video(Preset switch
        {
            "Kaleidoscope" => Presets.Kaleidoscope,
            "Nebula" => Presets.Nebula,
            "FeedbackTunnel" => Presets.FeedbackTunnel,
            "WholeBand" => Presets.WholeBand,
            _ => Presets.Plasma,
        });

        registers = patch.AllocateRegisters();
    }

    [Benchmark(Baseline = true)]
    public double Whole()
    {
        var feedback = default(FeedbackFrame);
        var total = 0d;

        for (var i = 0; i < Pixels; i++)
        {
            patch.Evaluate(At(i), Y, T, registers, feedback, aspect: Aspect);
            total += registers[patch.OutputBase];
        }

        return total;
    }

    [Benchmark]
    public double Staged()
    {
        var feedback = default(FeedbackFrame);
        var total = 0d;

        patch.EvaluateStage(EvaluationStage.Frame, 0d, Y, T, registers, feedback, Aspect);
        patch.EvaluateStage(EvaluationStage.Row, 0d, Y, T, registers, feedback, Aspect);

        for (var i = 0; i < Pixels; i++)
        {
            patch.EvaluateStage(EvaluationStage.Pixel, At(i), Y, T, registers, feedback, Aspect);
            total += registers[patch.OutputBase];
        }

        return total;
    }

    private static double At(int pixel) => (2d * (pixel + 0.5d) / Pixels - 1d) * Aspect;
}
