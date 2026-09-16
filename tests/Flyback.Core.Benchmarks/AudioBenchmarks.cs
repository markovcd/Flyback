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
    private AudioRenderer compiledRenderer = null!;
    private CompiledPatch patch = null!;
    private CompiledPatch compiled = null!;
    private float[] buffer = null!;

    [Params("Drone", "FourVoices")]
    public string Preset { get; set; } = "Drone";

    [GlobalSetup]
    public void Setup()
    {
        Func<ModuleCatalog, Patch> preset = Preset == "FourVoices" ? Presets.FourVoices : Presets.Drone;

        patch = Patches.Audio(preset);
        renderer = new AudioRenderer();
        renderer.Prepare(patch);

        // Its own program and its own renderer, so the two arms keep separate
        // memory and neither picks up the other's IL.
        compiled = Patches.Audio(preset);
        compiled.Attach(IlProgram.Compile(compiled));
        compiledRenderer = new AudioRenderer();
        compiledRenderer.Prepare(compiled);

        buffer = new float[Frames * 2];
    }

    [Benchmark(Baseline = true)]
    public void Callback() => renderer.Render(patch, buffer);

    [Benchmark]
    public void CallbackCompiled() => compiledRenderer.Render(compiled, buffer);
}
