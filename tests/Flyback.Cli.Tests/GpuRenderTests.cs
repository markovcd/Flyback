using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Gpu;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// The GPU's picture against the processor's, drawn headless the way
/// <c>flyback-cli render</c> draws it.
/// </summary>
/// <remarks>
/// CI draws these on Mesa's llvmpipe through EGL with no surface. A machine with no
/// OpenGL at all skips them and says why. The bounds are ADR-0035's: the two may
/// differ in their last bits, and a frame upside down, a channel swapped or a
/// history a frame behind differs by far more than that.
/// </remarks>
public class GpuRenderTests
{
    private const int Width = 160;
    private const int Height = 90;

    /// <summary>Plain, two kinds of feedback, a picture, a Meter's reading, the widest spread and the largest shader.</summary>
    public static TheoryData<string> Drawn() =>
        ["Plasma", "Feedback tunnel", "Trails", "Picture in", "Heard", "Kaleidoscope", "Whole band"];

    [Theory]
    [MemberData(nameof(Drawn))]
    public void The_GPU_draws_a_preset_as_the_processor_does(string name) =>
        Agrees(Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn));

    /// <summary>A value carried round per pixel, which on the GPU is a float target ping-ponged beside the history.</summary>
    [Fact]
    public void The_GPU_carries_a_plane_as_the_processor_does()
    {
        var patch = Presets.All.Single(p => p.Name == "Empty").Build(NodeCatalog.BuiltIn);
        var add = NodeInstance.Create(NodeCatalog.Require("math.add"), 0, 0);
        add.InputValues[1] = 0.25f;

        patch.Nodes.Add(add);
        patch.Connect(add.Id, 0, add.Id, 0);
        patch.Connect(add.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        Agrees(patch);
    }

    /// <summary>Four frames a thirtieth apart on each, so a loop has three frames behind it to disagree about.</summary>
    private static void Agrees(Patch patch)
    {
        using var gpu = HeadlessRenderer.Open(out var why);
        Assert.SkipWhen(gpu is null, $"No GPU here. {why}");

        var program = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        IlCompiler.CompileOnce(program, IlParts.Staged);

        gpu.Prepare(program).ShouldBeNull();

        IFrameRenderer processor = new SynthRenderer();
        var drawn = new byte[Width * Height * 4];
        var expected = new byte[Width * Height * 4];

        for (var frame = 0; frame < 4; frame++)
        {
            var time = 1d + frame / 30d;
            gpu.Render(program, time, Width, Height, drawn, Width * 4);
            processor.Render(program, time, Width, Height, expected, Width * 4);
        }

        long sum = 0;
        var worst = 0;

        for (var i = 0; i < drawn.Length; i++)
        {
            var difference = Math.Abs(drawn[i] - expected[i]);
            sum += difference;
            worst = Math.Max(worst, difference);
        }

        var mean = sum / (double)drawn.Length;

        worst.ShouldBeLessThanOrEqualTo(8, $"on {gpu.Description}");
        mean.ShouldBeLessThanOrEqualTo(0.5d, $"on {gpu.Description}");
    }
}
