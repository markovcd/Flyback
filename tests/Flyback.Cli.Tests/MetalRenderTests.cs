using Flyback.Gpu;
using Flyback.Tests;

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
}
