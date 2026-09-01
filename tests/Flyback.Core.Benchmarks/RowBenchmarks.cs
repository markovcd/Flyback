using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Benchmarks;

/// <summary>
/// One scanline of the renderer's inner loop, single-threaded: the interpreter
/// plus the clamping, the frame history and the BGRA write that surround it.
/// </summary>
/// <remarks>
/// Single-threaded and in one process on purpose. The frame benchmark runs
/// nineteen workers and its absolute numbers move by a sixth between runs of the
/// same binary as the machine's clocks drift, so the only comparison worth
/// making there is one BenchmarkDotNet can put a ratio on — which means both
/// arms in the same run. This is that, at the cost of leaving out the parallel
/// loop, which neither arm changes.
/// </remarks>
[MemoryDiagnoser]
public class RowBenchmarks
{
    private const int Width = 960;
    private const double Aspect = 960d / 540d;
    private const double Y = 0.25d;
    private const double T = 1.5d;

    private CompiledPatch patch = null!;
    private double[] registers = null!;
    private float[] history = null!;
    private byte[] row = null!;

    [Params("Plasma", "Nebula", "FeedbackTunnel", "WholeBand")]
    public string Preset { get; set; } = "Plasma";

    [GlobalSetup]
    public void Setup()
    {
        patch = Patches.Video(Preset switch
        {
            "Nebula" => Presets.Nebula,
            "FeedbackTunnel" => Presets.FeedbackTunnel,
            "WholeBand" => Presets.WholeBand,
            _ => Presets.Plasma,
        });

        registers = patch.AllocateRegisters();
        history = new float[Width * 3];
        row = new byte[Width * 4];
    }

    [Benchmark(Baseline = true)]
    public void Whole()
    {
        var feedback = default(FeedbackFrame);

        for (var x = 0; x < Width; x++)
        {
            patch.Evaluate(At(x), Y, T, registers, feedback, aspect: Aspect);
            Store(x);
        }
    }

    [Benchmark]
    public void Staged()
    {
        var feedback = default(FeedbackFrame);

        patch.EvaluateStage(EvaluationStage.Frame, 0d, Y, T, registers, feedback, Aspect);
        patch.EvaluateStage(EvaluationStage.Row, 0d, Y, T, registers, feedback, Aspect);

        for (var x = 0; x < Width; x++)
        {
            patch.EvaluateStage(EvaluationStage.Pixel, At(x), Y, T, registers, feedback, Aspect);
            Store(x);
        }
    }

    private static double At(int x) => (2d * (x + 0.5d) / Width - 1d) * Aspect;

    /// <summary>What SynthRenderer does with a pixel once the program has produced it.</summary>
    private void Store(int x)
    {
        var r = Saturate(registers[patch.OutputBase + 0]);
        var g = Saturate(registers[patch.OutputBase + 1]);
        var b = Saturate(registers[patch.OutputBase + 2]);

        history[x * 3 + 0] = (float)r;
        history[x * 3 + 1] = (float)g;
        history[x * 3 + 2] = (float)b;

        row[x * 4 + 0] = ToByte(b);
        row[x * 4 + 1] = ToByte(g);
        row[x * 4 + 2] = ToByte(r);
        row[x * 4 + 3] = 255;
    }

    private static double Saturate(double v) => double.IsFinite(v) ? Math.Clamp(v, 0d, 1d) : 0d;

    private static byte ToByte(double v) => (byte)(v * 255d + 0.5d);
}
