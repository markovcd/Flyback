using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Benchmarks;

/// <summary>One audio callback's worth of samples, oversampled as the engine runs it.</summary>
[MemoryDiagnoser]
public class AudioBenchmarks
{
    private const int Frames = 1024;

    private AudioRenderer renderer = null!;
    private CompiledPatch patch = null!;
    private float[] buffer = null!;

    [Params("Drone", "FourVoices")]
    public string Preset { get; set; } = "Drone";

    [GlobalSetup]
    public void Setup()
    {
        patch = Patches.Audio(Preset == "FourVoices" ? Presets.FourVoices : Presets.Drone);
        renderer = new AudioRenderer();
        renderer.Prepare(patch);
        buffer = new float[Frames * 2];
    }

    [Benchmark]
    public void Callback() => renderer.Render(patch, buffer, AudioScan.TimeDriven);
}
