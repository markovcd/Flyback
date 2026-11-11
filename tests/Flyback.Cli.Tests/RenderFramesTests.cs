using Flyback.Cli.Commands;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Gpu;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>What draws a render's frames, and where they are smoothed.</summary>
public class RenderFramesTests
{
    private sealed class Gpu : IHeadlessRenderer
    {
        public string Description => "fake";

        public Smoothing Smoothing { get; set; }

        public string? Prepare(CompiledPatch patch) => null;

        public void Reset()
        {
        }

        public void Render(CompiledPatch patch, double time, int width, int height, Span<byte> destination, int stride, LiveValues? live = null)
        {
        }

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(Smoothing.Off)]
    [InlineData(Smoothing.Fast)]
    [InlineData(Smoothing.Best)]
    public void The_GPU_smooths_its_own_frames_as_the_preview_does(Smoothing smoothing)
    {
        var gpu = new Gpu();

        RenderCommand.Frames(gpu, smoothing).ShouldBeSameAs(gpu);
        gpu.Smoothing.ShouldBe(smoothing);
    }

    [Fact]
    public void The_processor_s_frames_are_smoothed_after_they_are_drawn() =>
        RenderCommand.Frames(null, Smoothing.Fast).ShouldBeOfType<SmoothedFrames>();
}
