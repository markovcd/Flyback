using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Benchmarks;

/// <summary>
/// The evaluations behind one audio callback — 1024 frames at four times
/// oversampling — without the decimation filter around them, interpreted and
/// as IL.
/// </summary>
/// <remarks>
/// The audio path runs the whole program in order with its memory, so there is
/// no staging to share the work with, and this is where a cost per op is paid
/// most often. Each arm keeps its own state, which moves on between invocations
/// exactly as a playing patch's does.
/// </remarks>
[MemoryDiagnoser]
public class AudioEvaluateBenchmarks
{
    private const int Evaluations = 1024 * 4;
    private const double Step = 1d / 192_000d;

    private CompiledPatch patch = null!;
    private IlProgram il = null!;
    private double[] registers = null!;
    private DelayState? interpretedState;
    private DelayState? ilState;
    private double time;

    [Params("Drone", "FourVoices", "WholeBand")]
    public string Preset { get; set; } = "Drone";

    [GlobalSetup]
    public void Setup()
    {
        patch = Patches.Audio(Preset switch
        {
            "FourVoices" => Presets.FourVoices,
            "WholeBand" => Presets.WholeBand,
            _ => Presets.Drone,
        });

        il = IlProgram.Compile(patch);
        registers = patch.AllocateRegisters();

        var renderer = new AudioRenderer();
        interpretedState = renderer.DelayMemoryFor(patch);
        ilState = renderer.DelayMemoryFor(patch);
    }

    [Benchmark(Baseline = true)]
    public double Interpreted()
    {
        var total = 0d;

        for (var i = 0; i < Evaluations; i++)
        {
            patch.Evaluate(0d, 0d, time + i * Step, registers, default, interpretedState);
            total += registers[patch.OutputBase];
        }

        time += Evaluations * Step;
        return total;
    }

    [Benchmark]
    public double Il()
    {
        var total = 0d;

        for (var i = 0; i < Evaluations; i++)
        {
            il.Evaluate(0d, 0d, time + i * Step, registers, default, ilState);
            total += registers[patch.OutputBase];
        }

        time += Evaluations * Step;
        return total;
    }
}
