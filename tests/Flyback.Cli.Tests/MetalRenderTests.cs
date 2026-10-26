using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Gpu;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// The picture through Metal against the processor's, every case
/// <see cref="GpuRenderTests"/> draws on OpenGL, to the same bounds.
/// </summary>
/// <remarks>
/// A Mac draws these on its own GPU, and GitHub's on Apple's paravirtual one. A
/// machine with no Metal skips them and says why.
/// </remarks>
[TestCategory(TestCategory.Gpu)]
public class MetalRenderTests : GpuRenderTests
{
    private protected override IHeadlessRenderer? Open(out string? why) => MetalHeadlessRenderer.Open(out why);

    /// <summary>
    /// Overlapped, as the preview draws, each frame hands back the one asked for before it,
    /// and the first is handed back twice.
    /// </summary>
    [Fact]
    public void An_overlapped_frame_is_the_one_asked_for_before_it()
    {
        using var late = MetalHeadlessRenderer.Open(out var why, overlapped: true);
        TestCategory.Gpu.Require(late is not null, $"No Metal here. {why}");
        using var now = MetalHeadlessRenderer.Open(out _)!;

        var program = Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;
        var times = new[] { 1d, 2d, 3d };
        var drawn = times.Select(time => Frame(now, program, time)).ToArray();
        var handed = times.Select(time => Frame(late, program, time)).ToArray();

        drawn[0].ShouldNotBe(drawn[1]);
        handed[0].ShouldBe(drawn[0]);
        handed[1].ShouldBe(drawn[0]);
        handed[2].ShouldBe(drawn[1]);
    }

    private static byte[] Frame(IHeadlessRenderer renderer, CompiledPatch program, double time)
    {
        var frame = new byte[64 * 36 * 4];
        renderer.Render(program, time, 64, 36, frame, 64 * 4);

        return frame;
    }
}
