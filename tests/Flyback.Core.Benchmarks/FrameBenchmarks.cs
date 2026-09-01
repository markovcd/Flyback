using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Benchmarks;

/// <summary>A whole frame through the renderer, which is what a preview costs.</summary>
[MemoryDiagnoser]
public class FrameBenchmarks
{
    private const int Width = 960;
    private const int Height = 540;

    private readonly SynthRenderer renderer = new();
    private byte[] destination = null!;
    private CompiledPatch patch = null!;

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

        destination = new byte[Width * Height * 4];
    }

    [Benchmark]
    public void Frame() => renderer.Render(patch, 1.5d, Width, Height, destination, Width * 4);
}
